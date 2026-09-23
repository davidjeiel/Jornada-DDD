using Jornada.Adaptadores.Memoria;
using Jornada.Api;
using Jornada.Aplicacao;
using Jornada.Aplicacao.Catalogo.Comandos;
using Jornada.Aplicacao.Catalogo.Consultas;
using Jornada.Aplicacao.Portas;
using Jornada.Dominio.Catalogo;
using Jornada.Dominio.Comum;
using Jornada.Dominio.Governanca;
using Jornada.Dominio.Tenancy;
using Jornada.Persistencia.Postgres;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

// ValidateScopes/ValidateOnBuild fazem o container RECUSAR a montagem quando um
// singleton depende de algo scoped. Sem isso, a primeira versão deste arquivo
// registrou o repositório como singleton e ele capturou o contexto de tenant da
// PRIMEIRA requisição — a dependência cativa que vaza dado entre empresas.
// Ligado sempre, não só em Development: o custo é uma vez na partida.
builder.Host.UseDefaultServiceProvider(opcoes =>
{
    opcoes.ValidateScopes = true;
    opcoes.ValidateOnBuild = true;
});

// ── Composition root: o ÚNICO lugar que conhece domínio e infraestrutura juntos.
builder.Services.AddSingleton<IRelogio, RelogioDoSistema>();

builder.Services.AddScoped<ContextoDaRequisicao>();
builder.Services.AddScoped<IContextoDeTenantAtual>(sp => sp.GetRequiredService<ContextoDaRequisicao>());
builder.Services.AddScoped<IAtorAtual>(sp => sp.GetRequiredService<ContextoDaRequisicao>());

// Adaptador de persistência escolhido em RUNTIME, por configuração — nunca por
// rebuild de imagem (Grupo 1 do plano de containers: um container só é
// reconfigurável de verdade se isso for verdade). Zero-config continua sendo
// "memoria" (appsettings.json); container/pod liga Postgres via variável de
// ambiente Persistencia__Provedor (ver local/compose.yaml).
var provedorDePersistencia = builder.Configuration.GetValue("Persistencia:Provedor", "memoria")!;
var usaPostgres = string.Equals(provedorDePersistencia, "postgres", StringComparison.OrdinalIgnoreCase);

if (usaPostgres)
{
    var stringDeConexao = builder.Configuration.GetConnectionString("Catalogo")
        ?? throw new InvalidOperationException(
            "ConnectionStrings:Catalogo é obrigatória quando Persistencia:Provedor=postgres.");

    // EnableDynamicJson: sem isto, o Npgsql 8+ recusa serializar
    // Dictionary<string,string> (o campo `atributos`) para jsonb — é opt-in de
    // propósito, para não pagar reflexão em cenários AOT que não precisam.
    builder.Services.AddSingleton(_ =>
        new NpgsqlDataSourceBuilder(stringDeConexao).EnableDynamicJson().Build());

    // O DbContext é scoped (padrão do AddDbContext): carrega o contexto de
    // tenant da requisição, do mesmo jeito que o repositório em memória já
    // fazia — inverter isso reabriria a dependência cativa do ADR-0003.
    builder.Services.AddDbContext<ContextoDeDados>((sp, o) =>
        o.UseNpgsql(sp.GetRequiredService<NpgsqlDataSource>()));

    builder.Services.AddScoped<RepositorioDeAtivosPostgres>();
    builder.Services.AddScoped<IRepositorioDeAtivos>(sp => sp.GetRequiredService<RepositorioDeAtivosPostgres>());
    builder.Services.AddScoped<IRepositorioDeRelacoes>(sp => sp.GetRequiredService<RepositorioDeAtivosPostgres>());
    builder.Services.AddScoped<IRepositorioDeValidacoes>(sp => sp.GetRequiredService<RepositorioDeAtivosPostgres>());
    builder.Services.AddScoped<IUnidadeDeTrabalho, TransacaoComTenant>();
}
else
{
    // O ARMAZÉM é singleton (são os dados, como o banco); o REPOSITÓRIO é scoped
    // (carrega o contexto de tenant da requisição). Inverter isso é o bug de
    // dependência cativa descrito em RepositorioDeAtivosEmMemoria.
    builder.Services.AddSingleton<ArmazemEmMemoria>();
    builder.Services.AddScoped<RepositorioDeAtivosEmMemoria>();
    builder.Services.AddScoped<IRepositorioDeAtivos>(sp => sp.GetRequiredService<RepositorioDeAtivosEmMemoria>());
    builder.Services.AddScoped<IRepositorioDeRelacoes>(sp => sp.GetRequiredService<RepositorioDeAtivosEmMemoria>());
    builder.Services.AddScoped<IUnidadeDeTrabalho>(sp => sp.GetRequiredService<RepositorioDeAtivosEmMemoria>());
    builder.Services.AddScoped<IRepositorioDeValidacoes>(sp => sp.GetRequiredService<RepositorioDeAtivosEmMemoria>());
}

builder.Services.AddSingleton<IMetamodelo, MetamodeloPadrao>();
builder.Services.AddSingleton<IPoliticasDeGovernanca, PoliticasPadrao>();
builder.Services.AddSingleton<IPublicadorDeEventos, PublicadorEmMemoria>();

builder.Services.AddScoped<CadastrarAtivo>();
builder.Services.AddScoped<AtribuirResponsavel>();
builder.Services.AddScoped<SubmeterAtivo>();
builder.Services.AddScoped<DecidirValidacao>();
builder.Services.AddScoped<AvaliarPreCheck>();
builder.Services.AddScoped<ObterVisaoDoAtivo>();

// Probe de prontidão real: só existe verificação quando há algo externo para
// verificar. Com "memoria" não há tag "pronto" nenhuma, e o middleware de
// health checks responde saudável por padrão quando não há checagem alguma —
// o que é o comportamento certo nesse caso (não há dependência a aguardar).
var checagensDeSaude = builder.Services.AddHealthChecks();
if (usaPostgres)
{
    checagensDeSaude.AddCheck<VerificacaoDePostgres>("postgres", tags: ["pronto"]);
}

var app = builder.Build();

// Migração + Esquema.sql (RLS) ANTES de aceitar tráfego. Roda com a conexão do
// DONO do schema (catalogo_migrador via ConnectionStrings:Migracoes) — nunca
// com a conexão de requisição (catalogo_app), que nem tem privilégio de DDL
// (ADR-0003). Guardado por flag porque, com mais de uma réplica do pod, só UMA
// deveria aplicar schema; hoje o produto ainda não tem esse segundo processo
// (ver README > Limitações), então o próprio host faz o papel de migrador.
if (usaPostgres && builder.Configuration.GetValue("Persistencia:AplicarEsquemaNaInicializacao", true))
{
    await AplicarEsquemaDoBancoAsync(app.Configuration, app.Logger);
}

app.UseMiddleware<MiddlewareDeTenant>();

// Traduz exceção de domínio em Problem Details. A regra decide; o adaptador formata.
app.Use(async (http, proximo) =>
{
    try { await proximo(); }
    catch (SemPermissao e)
    {
        await MiddlewareDeTenant.EscreverProblema(http, StatusCodes.Status403Forbidden,
            e.Codigo, "Ação não permitida", e.Message);
    }
    catch (ErroDeDominio e)
    {
        await MiddlewareDeTenant.EscreverProblema(http, StatusCodes.Status422UnprocessableEntity,
            e.Codigo, "Regra de negócio violada", e.Message);
    }
});

// ═══════════════════════════════════════════════════════════════ rotas

app.MapGet("/", () => Results.Ok(new
{
    produto = "Jornada DDD",
    versao = "0.1.0",
    situacao = "primeira versão — estrutura hexagonal + domínio portado",
    provedor = provedorDePersistencia,
    documentacao = "docs/ARQUITETURA.md",
    dica = "toda rota /v1 exige o cabeçalho X-Empresa. Veja /demo para um exemplo pronto.",
}));

// Liveness: "o processo responde?". Nunca toca banco — um Postgres lento não
// deve fazer o orquestrador matar um pod que está saudável.
app.MapGet("/saude", () => Results.Ok(new { situacao = "ok" }));

// Readiness: "este pod pode receber tráfego agora?". Só aqui é honesto tocar
// a dependência externa — é exatamente a pergunta que a probe de prontidão de
// um orquestrador de containers faz antes de rotear uma requisição (Grupo 1).
app.MapHealthChecks("/saude/pronto", new HealthCheckOptions
{
    Predicate = c => c.Tags.Contains("pronto"),
});

app.MapGet("/demo", () => Results.Ok(new
{
    explicacao = "Use estes cabeçalhos para exercitar a API sem autenticação real.",
    cabecalhos = new
    {
        X_Empresa = "11111111-1111-1111-1111-111111111111",
        X_Papel = "curador",
        X_Pessoa = "33333333-3333-3333-3333-333333333333",
    },
    exemplo = "curl -H 'X-Empresa: 11111111-1111-1111-1111-111111111111' http://localhost:5080/v1/ativos",
}));

var v1 = app.MapGroup("/v1");

// ── catálogo ────────────────────────────────────────────────────────────────

v1.MapGet("/ativos", async (IRepositorioDeAtivos repo, string? tipo, CancellationToken ct) =>
{
    var ativos = await repo.ListarAsync(tipo, ct: ct);
    return Results.Ok(new
    {
        total = ativos.Count,
        ativos = ativos.Select(a => new
        {
            id = a.Id.Valor,
            codigo = a.Codigo.Valor,
            nome = a.Nome,
            tipo = a.TipoItem,
            status = MaquinaDeCicloDeVida.Rotulo(a.Status),
            criticidade = a.Criticidade.Chave(),
        }),
    });
});

v1.MapPost("/ativos", async (CadastrarAtivo caso, CorpoCadastrarAtivo corpo, CancellationToken ct) =>
{
    // Note o que o corpo NÃO tem: `usuario` e `tenant_id`. No api.py atual,
    // POST /itens lê corpo["usuario"] — quem chama declara quem é (ADR-0009).
    var r = await caso.ExecutarAsync(new ComandoCadastrarAtivo(
        corpo.Tipo, corpo.Nome, corpo.Descricao ?? "",
        CriticidadeExtensoes.De(corpo.Criticidade),
        corpo.IdPai is { } p ? new IdDeAtivo(p) : null,
        corpo.Atributos), ct);

    return r.Sucesso
        ? Results.Created($"/v1/ativos/{r.Valor.Valor}", new { id = r.Valor.Valor })
        : Problema(r.Codigo!, r.Mensagem!);
});

v1.MapGet("/ativos/{id:guid}", async (Guid id, ObterVisaoDoAtivo caso, CancellationToken ct) =>
{
    var r = await caso.ExecutarAsync(new IdDeAtivo(id), ct);
    return r.Sucesso ? Results.Ok(r.Valor) : Problema(r.Codigo!, r.Mensagem!, 404);
});

v1.MapPost("/ativos/{id:guid}/responsaveis",
    async (Guid id, AtribuirResponsavel caso, CorpoResponsavel corpo, CancellationToken ct) =>
{
    if (!Enum.TryParse<PapelDeOwnership>(corpo.Papel, ignoreCase: true, out var papel))
        return Problema("PAPEL_INVALIDO",
            $"Papel '{corpo.Papel}' inválido. Use: " +
            string.Join(", ", Enum.GetNames<PapelDeOwnership>()));

    var r = await caso.ExecutarAsync(
        new ComandoAtribuirResponsavel(new IdDeAtivo(id), corpo.IdPessoa, papel), ct);

    return r.Sucesso ? Results.NoContent() : Problema(r.Codigo!, r.Mensagem!);
});

// ── governança ──────────────────────────────────────────────────────────────

v1.MapGet("/ativos/{id:guid}/pre-check", async (Guid id, AvaliarPreCheck caso, CancellationToken ct) =>
{
    var r = await caso.ExecutarAsync(new IdDeAtivo(id), ct);
    if (!r.Sucesso) return Problema(r.Codigo!, r.Mensagem!, 404);

    var s = r.Valor!;
    return Results.Ok(new
    {
        aprovado = s.Checagem.Aprovado,
        score = s.Score.Total,
        dimensoes = new
        {
            completude = s.Score.Completude,
            consistencia = s.Score.Consistencia,
            ownership = s.Score.Ownership,
            evidencia = s.Score.Evidencia,
            temporalidade = s.Score.Temporalidade,
        },
        // Os códigos são contrato público: o cliente automatiza a correção
        // em vez de casar mensagens de erro (ADR-0009).
        bloqueios = s.Checagem.Bloqueios.Select(b => new { codigo = b.Codigo.Chave(), mensagem = b.Mensagem }),
        alertas = s.Checagem.Alertas,
        politica = new
        {
            scoreMinimo = s.Checagem.Politica.ScoreMinimo,
            evidenciaMinima = s.Checagem.Politica.EvidenciaMinima,
            slaHoras = s.Checagem.Politica.SlaHoras,
            etapas = s.Checagem.Politica.Etapas.Select(e => e.ToString().ToLowerInvariant()),
        },
        caminho = s.Caminho.Select(p => new
        {
            passo = p.Chave,
            rotulo = p.Rotulo,
            concluido = p.Concluido,
            impedimentos = p.Impedimentos.Select(b => b.Codigo.Chave()),
        }),
    });
});

// Recurso, não procedimento: POST /submissoes em vez de POST /submeter (ADR-0009).
v1.MapPost("/ativos/{id:guid}/submissoes",
    async (Guid id, SubmeterAtivo caso, CorpoSubmeter? corpo, CancellationToken ct) =>
{
    var r = await caso.ExecutarAsync(new ComandoSubmeterAtivo(new IdDeAtivo(id), corpo?.Motivo ?? ""), ct);

    return r.Sucesso
        ? Results.Created($"/v1/ativos/{id}/submissoes/{r.Valor!.Revisao}", new
        {
            revisao = r.Valor.Revisao,
            score = r.Valor.Score,
            etapasAbertas = r.Valor.EtapasAbertas.Select(e => e.ToString().ToLowerInvariant()),
        })
        : Problema(r.Codigo!, r.Mensagem!);
});

v1.MapGet("/validacoes", async (IRepositorioDeValidacoes repo, string? ativo, CancellationToken ct) =>
{
    IdDeAtivo? idAtivo = Guid.TryParse(ativo, out var id) ? new IdDeAtivo(id) : null;
    var validacoes = await repo.ListarAsync(idAtivo, ct);
    return Results.Ok(new
    {
        total = validacoes.Count,
        validacoes = validacoes.Select(v => new
        {
            id = v.Id,
            ativo = v.Ativo.Valor,
            revisao = v.Revisao,
            etapa = v.Etapa.ToString().ToLowerInvariant(),
            status = v.Status.ToString().ToLowerInvariant(),
            submetidaPor = v.IdSubmissor,
            decididaPor = v.IdDecisor,
            motivo = v.Motivo,
        }),
    });
});

v1.MapPost("/validacoes/{id:guid}/decisoes",
    async (Guid id, CorpoDecisaoValidacao corpo, DecidirValidacao caso, CancellationToken ct) =>
{
    var r = await caso.ExecutarAsync(
        new ComandoDecidirValidacao(id, corpo.Aprovada, corpo.Motivo), ct);
    return r.Sucesso ? Results.NoContent() : Problema(r.Codigo!, r.Mensagem!);
});

app.Run();
return;

static IResult Problema(string codigo, string mensagem, int status = 422)
{
    // Falta de papel é 403 em qualquer rota — não deixe cada endpoint decidir,
    // senão uma delas esquece e devolve 422 para uma recusa de autorização.
    if (codigo == "SEM_PERMISSAO") status = 403;

    return Results.Problem(
        type: $"https://jornada-ddd.exemplo/erros/{codigo.ToLowerInvariant().Replace('_', '-')}",
        title: status == 403 ? "Ação não permitida" : "Requisição não processada",
        detail: mensagem,
        statusCode: status,
        extensions: new Dictionary<string, object?> { ["codigo"] = codigo });
}

/// <summary>
/// Aplica migrations do EF Core e depois <c>Esquema.sql</c> (RLS, outbox),
/// nessa ordem — a mesma que o comentário do arquivo exige: toda tabela com
/// <c>tenant_id</c> precisa existir ANTES do bloco que liga RLS.
///
/// Usa <c>ConnectionStrings:Migracoes</c> (papel <c>catalogo_migrador</c>, dono
/// do schema), nunca a conexão de requisição — essa nem tem privilégio de DDL,
/// de propósito (ADR-0003).
/// </summary>
static async Task AplicarEsquemaDoBancoAsync(IConfiguration configuracao, ILogger logger)
{
    var stringDeMigracao = configuracao.GetConnectionString("Migracoes")
        ?? throw new InvalidOperationException(
            "ConnectionStrings:Migracoes é obrigatória para aplicar migrations/RLS na inicialização.");

    // catalogo_migrador não tem USAGE em `public` (REVOKE ALL ... FROM PUBLIC em
    // 01-papeis.sql, de propósito — ADR-0006 §2) e não tem search_path próprio.
    // Sem apontar o schema explicitamente, o Postgres recusa criar
    // __EFMigrationsHistory com "no schema has been selected to create in".
    var opcoes = new DbContextOptionsBuilder<ContextoDeDados>()
        .UseNpgsql(stringDeMigracao, o => o.MigrationsHistoryTable("__ef_migrations_historico", "catalogo"))
        .Options;

    // Tenant fixo: migração é DDL puro, nunca consulta filtrada por empresa —
    // o valor não importa, só precisa satisfazer o construtor do DbContext.
    await using var db = new ContextoDeDados(opcoes, ContextoDeTenantFixo.Para(IdDeEmpresa.Novo()));

    logger.LogInformation("Aplicando migrations do EF Core (catalogo_migrador)...");
    await db.Database.MigrateAsync();

    var caminhoDoEsquema = Path.Combine(AppContext.BaseDirectory, "Esquema.sql");
    logger.LogInformation("Aplicando {Arquivo} (RLS + outbox)...", caminhoDoEsquema);
    var esquemaSql = await File.ReadAllTextAsync(caminhoDoEsquema);
    await db.Database.ExecuteSqlRawAsync(esquemaSql);

    logger.LogInformation("Schema do Postgres pronto — RLS ligada em toda tabela com tenant_id.");
}

/// <summary>
/// Readiness real (Grupo 1): pergunta ao Postgres, pela MESMA conexão de
/// requisição que a API usa (catalogo_app, via PgBouncer). Um orquestrador de
/// containers só deveria rotear tráfego para este pod depois disto responder
/// saudável.
/// </summary>
internal sealed class VerificacaoDePostgres(ContextoDeDados db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext contexto, CancellationToken ct = default)
    {
        try
        {
            return await db.Database.CanConnectAsync(ct)
                ? HealthCheckResult.Healthy("Postgres alcançável.")
                : HealthCheckResult.Unhealthy("Postgres não respondeu.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Falha ao conectar no Postgres.", ex);
        }
    }
}

// Contratos de entrada. Ficam aqui, no adaptador: o domínio não conhece JSON.
internal sealed record CorpoCadastrarAtivo(
    string Tipo,
    string Nome,
    string? Descricao = null,
    string? Criticidade = null,
    Guid? IdPai = null,
    Dictionary<string, string>? Atributos = null);

internal sealed record CorpoSubmeter(string? Motivo);

internal sealed record CorpoResponsavel(Guid IdPessoa, string Papel);

internal sealed record CorpoDecisaoValidacao(bool Aprovada, string? Motivo = null);
