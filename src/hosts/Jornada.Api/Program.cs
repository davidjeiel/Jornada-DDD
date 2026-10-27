using Jornada.Adaptadores.Memoria;
using Jornada.Api;
using Jornada.Api.Contrato;
using Jornada.Aplicacao.Catalogo.Comandos;
using Jornada.Aplicacao.Catalogo.Consultas;
using Jornada.Aplicacao.Portas;
using Jornada.Dominio.Catalogo;
using Jornada.Dominio.Comum;
using Jornada.Dominio.Tenancy;
using Jornada.Persistencia.Cosmos;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.OpenApi;

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
// "memoria" (appsettings.json); container/pod liga o Cosmos DB via variável de
// ambiente Persistencia__Provedor (ver local/compose.yaml). O Postgres saiu do
// produto (ADR-0016): um valor desconhecido derruba a partida, em vez de cair
// em silêncio na memória e perder os dados a cada reinício.
var provedorDePersistencia = builder.Configuration.GetValue("Persistencia:Provedor", "memoria")!;
var usaCosmos = string.Equals(provedorDePersistencia, "cosmos", StringComparison.OrdinalIgnoreCase);
if (!usaCosmos && !string.Equals(provedorDePersistencia, "memoria", StringComparison.OrdinalIgnoreCase))
    throw new InvalidOperationException(
        $"Persistencia:Provedor='{provedorDePersistencia}' não existe. Use 'cosmos' ou 'memoria'.");

if (usaCosmos)
{
    // Cosmos DB, API NoSQL (ADR-0016). O banco (cliente + containers) é
    // singleton; o repositório é scoped porque carrega o contexto de tenant,
    // pelo mesmo motivo de dependência cativa do adaptador em memória.
    var stringDoCosmos = builder.Configuration.GetConnectionString("Cosmos")
        ?? throw new InvalidOperationException(
            "ConnectionStrings:Cosmos é obrigatória quando Persistencia:Provedor=cosmos.");

    builder.Services.AddSingleton(_ => BancoDoCatalogo.Conectar(
        stringDoCosmos,
        builder.Configuration.GetValue("Cosmos:Banco", "jornada")!,
        emulador: builder.Configuration.GetValue("Cosmos:Emulador", false)));

    builder.Services.AddScoped<RepositorioCosmos>();
    builder.Services.AddScoped<IRepositorioDeAtivos>(sp => sp.GetRequiredService<RepositorioCosmos>());
    builder.Services.AddScoped<IRepositorioDeRelacoes>(sp => sp.GetRequiredService<RepositorioCosmos>());
    builder.Services.AddScoped<IRepositorioDeValidacoes>(sp => sp.GetRequiredService<RepositorioCosmos>());
    builder.Services.AddScoped<IUnidadeDeTrabalho>(sp => sp.GetRequiredService<RepositorioCosmos>());
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
builder.Services.AddScoped<EditarAtivo>();
builder.Services.AddScoped<AtribuirResponsavel>();
builder.Services.AddScoped<AnexarEvidencia>();
builder.Services.AddScoped<RelacionarAtivos>();
builder.Services.AddScoped<SubmeterAtivo>();
builder.Services.AddScoped<AbrirRevisao>();
builder.Services.AddScoped<DecidirValidacao>();
builder.Services.AddScoped<AvaliarPreCheck>();
builder.Services.AddScoped<ObterVisaoDoAtivo>();

// Probe de prontidão real: só existe verificação quando há algo externo para
// verificar. Com "memoria" não há tag "pronto" nenhuma, e o middleware de
// health checks responde saudável por padrão quando não há checagem alguma —
// o que é o comportamento certo nesse caso (não há dependência a aguardar).
var checagensDeSaude = builder.Services.AddHealthChecks();
if (usaCosmos)
{
    checagensDeSaude.AddCheck<VerificacaoDeCosmos>("cosmos", tags: ["pronto"]);
}

// ── Contrato OpenAPI no padrão CAIXA (ver ADR-0014 e docs/swagger.pdf).
var metadados = builder.Configuration.GetSection("OpenApi").Get<MetadadosDoContrato>() ?? new MetadadosDoContrato();
builder.Services.AdicionarContratoOpenApi(metadados);

// Corpo malformado ou sem campo obrigatório vira exceção (e não um 400 de corpo
// vazio, que é o padrão fora de Development): só assim ele sai como ApiError.
builder.Services.Configure<RouteHandlerOptions>(o => o.ThrowOnBadRequest = true);

var app = builder.Build();

// Database e containers do Cosmos, se não existirem, ANTES de aceitar tráfego.
// Guardado por flag porque, com mais de uma réplica do pod, só UMA deveria
// provisionar; hoje o produto ainda não tem esse segundo processo.
if (usaCosmos && builder.Configuration.GetValue("Persistencia:AplicarEsquemaNaInicializacao", true))
{
    app.Logger.LogInformation("Provisionando database e containers do Cosmos DB...");
    await app.Services.GetRequiredService<BancoDoCatalogo>().ProvisionarAsync();
}

// ── Pipeline. A ORDEM importa: a correlação vem antes de tudo, para que até o
// erro do middleware de tenant (401) e a exceção inesperada (500) carreguem o
// mesmo x-correlation-id que o consumidor recebe.
app.UseMiddleware<MiddlewareDeCorrelacao>();

app.Use(async (http, proximo) =>
{
    try { await proximo(); }
    catch (BadHttpRequestException) when (!http.Response.HasStarted)
    {
        // O texto do framework cita tipo e parâmetro internos: não vai para fora.
        await RespostaDeErro.EscreverAsync(http, CodigosDeErro.RequisicaoInvalida,
            "Corpo ou parâmetro da requisição inválido. Confira os campos obrigatórios e os formatos no contrato OpenAPI.");
    }
    catch (ErroDeDominio e) when (!http.Response.HasStarted)
    {
        // SemPermissao é ErroDeDominio com código SEM_PERMISSAO: sai 403 pela tabela.
        await RespostaDeErro.EscreverAsync(http, e.Codigo, e.Message);
    }
    catch (Exception e) when (!http.Response.HasStarted)
    {
        // Sem stack trace para o consumidor (guia CAIXA §16): o detalhe fica no
        // log, achável pelo x-correlation-id que vai na resposta.
        app.Logger.LogError(e, "Erro não tratado em {Metodo} {Caminho}", http.Request.Method, http.Request.Path);
        await RespostaDeErro.EscreverAsync(http, CodigosDeErro.ErroInterno,
            "Erro interno não esperado. Informe o correlationId ao suporte.");
    }
});

// 404/405 do roteamento saem sem corpo; aqui ganham o mesmo formato de erro.
app.UseStatusCodePages(async contexto =>
{
    var http = contexto.HttpContext;
    if (http.Response.ContentLength is > 0 || http.Response.ContentType is not null) return;

    var (codigo, mensagem) = http.Response.StatusCode switch
    {
        StatusCodes.Status404NotFound => ("RECURSO_NAO_ENCONTRADO", "Recurso não encontrado."),
        StatusCodes.Status405MethodNotAllowed => ("METODO_NAO_PERMITIDO", "Método HTTP não permitido para esta rota."),
        _ => ((string?)null, ""),
    };
    if (codigo is null) return;
    await http.Response.WriteAsJsonAsync(RespostaDeErro.Montar(http, codigo, mensagem));
});

// Documento OpenAPI 3.0 (o guia CAIXA pede 3.0.x). Em JSON e YAML:
// /swagger/v1/swagger.json e /swagger/v1/swagger.yaml.
app.UseSwagger(o => o.OpenApiVersion = OpenApiSpecVersion.OpenApi3_0);

// A interface visual só em Development ou com aprovação explícita
// (OpenApi:HabilitarUi) — guia CAIXA §12.
if (app.Environment.IsDevelopment() || metadados.HabilitarUi)
{
    app.UseSwaggerUI(o =>
    {
        o.SwaggerEndpoint($"/swagger/{OpenApiCaixa.NomeDoDocumento}/swagger.json", metadados.Titulo);
        o.DocumentTitle = metadados.Titulo;
    });
}

app.UseMiddleware<MiddlewareDeTenant>();

// ═══════════════════════════════════════════════════════════════ rotas

app.MapGet("/", () => Results.Ok(new
{
    produto = "Jornada DDD",
    versao = metadados.Versao,
    provedor = provedorDePersistencia,
    contrato = $"/swagger/{OpenApiCaixa.NomeDoDocumento}/swagger.json",
    dica = $"toda rota {metadados.PrefixoDasRotas} exige o cabeçalho X-Empresa. Veja /demo para um exemplo pronto.",
})).ExcludeFromDescription();

// Liveness: "o processo responde?". Nunca toca banco — um banco lento não
// deve fazer o orquestrador matar um pod que está saudável.
app.MapGet("/saude", () => TypedResults.Ok(new SaudeResponse("ok")))
    .WithName("verificarVida")
    .WithTags(OpenApiCaixa.TagOperacao)
    .WithSummary("Verifica se o processo responde")
    .WithDescription("Liveness. Não consulta dependências.");

// Readiness: "este pod pode receber tráfego agora?". Só aqui é honesto tocar
// a dependência externa — é exatamente a pergunta que a probe de prontidão de
// um orquestrador de containers faz antes de rotear uma requisição (Grupo 1).
// Rota comum (e não MapHealthChecks) para entrar no contrato OpenAPI; o
// orquestrador só olha o status: 200 pronto, 503 não pronto.
app.MapGet("/saude/pronto", async Task<Results<Ok<SaudeResponse>, JsonHttpResult<SaudeResponse>>> (
    HealthCheckService saude, CancellationToken ct) =>
{
    var relatorio = await saude.CheckHealthAsync(c => c.Tags.Contains("pronto"), ct);
    var corpo = new SaudeResponse(relatorio.Status.ToString().ToLowerInvariant());
    return relatorio.Status == HealthStatus.Unhealthy
        ? TypedResults.Json(corpo, statusCode: StatusCodes.Status503ServiceUnavailable)
        : TypedResults.Ok(corpo);
})
    .WithName("verificarProntidao")
    .WithTags(OpenApiCaixa.TagOperacao)
    .WithSummary("Verifica se a API pode receber tráfego")
    .WithDescription("Readiness. Com persistência Cosmos DB, testa o acesso ao container do catálogo. situacao: healthy, degraded ou unhealthy.")
    .Produces<SaudeResponse>(StatusCodes.Status503ServiceUnavailable);

app.MapGet("/demo", () => Results.Ok(new
{
    explicacao = "Use estes cabeçalhos para exercitar a API sem autenticação real.",
    cabecalhos = new
    {
        X_Empresa = "11111111-1111-1111-1111-111111111111",
        X_Papel = "curador",
        X_Pessoa = "33333333-3333-3333-3333-333333333333",
    },
    exemplo = $"curl -H 'X-Empresa: 11111111-1111-1111-1111-111111111111' http://localhost:5080{metadados.PrefixoDasRotas}/ativos",
})).ExcludeFromDescription();

var v1 = app.MapGroup(metadados.PrefixoDasRotas);

// ── catálogo ────────────────────────────────────────────────────────────────

var catalogo = v1.MapGroup("").WithTags(OpenApiCaixa.TagCatalogo);

catalogo.MapGet("/ativos", async (IRepositorioDeAtivos repo, string? tipo, CancellationToken ct) =>
{
    var ativos = await repo.ListarAsync(tipo, ct: ct);
    return TypedResults.Ok(new ListaDeAtivosResponse(
        ativos.Count, ativos.Select(AtivoResumoResponse.De).ToArray()));
})
    .WithName("listarAtivos")
    .WithSummary("Lista os ativos da empresa")
    .WithDescription("Até 200 ativos, ordenados por código. O parâmetro tipo filtra pela chave do metamodelo (ex.: sistema).");

catalogo.MapPost("/ativos", async Task<Results<Created<AtivoCriadoResponse>, JsonHttpResult<ApiError>>> (
    HttpContext http, CadastrarAtivo caso, CadastrarAtivoRequest corpo, CancellationToken ct) =>
{
    var r = await caso.ExecutarAsync(new ComandoCadastrarAtivo(
        corpo.Tipo, corpo.Nome, corpo.Descricao ?? "",
        CriticidadeExtensoes.De(corpo.Criticidade),
        corpo.IdPai is { } p ? new IdDeAtivo(p) : null,
        corpo.Atributos), ct);

    return r.Sucesso
        ? TypedResults.Created($"{metadados.PrefixoDasRotas}/ativos/{r.Valor.Valor}", new AtivoCriadoResponse(r.Valor.Valor))
        : Erro(http, r.Codigo!, r.Mensagem!);
})
    .WithName("cadastrarAtivo")
    .WithSummary("Cadastra um ativo")
    .WithDescription("O ativo nasce em rascunho, com código gerado pelo tipo (ex.: SIS-0001). O papel precisa escrever no bloco do tipo: negócio cadastra a estrutura DDD, tech lead os ativos técnicos.")
    .ComErros(StatusCodes.Status403Forbidden, StatusCodes.Status422UnprocessableEntity);

catalogo.MapGet("/ativos/{id:guid}", async Task<Results<Ok<FichaDoAtivoResponse>, JsonHttpResult<ApiError>>> (
    HttpContext http, Guid id, ObterVisaoDoAtivo caso, CancellationToken ct) =>
{
    var r = await caso.ExecutarAsync(new IdDeAtivo(id), ct);
    return r.Sucesso ? TypedResults.Ok(FichaDoAtivoResponse.De(r.Valor!)) : Erro(http, r.Codigo!, r.Mensagem!);
})
    .WithName("consultarAtivo")
    .WithSummary("Consulta a ficha completa de um ativo")
    .WithDescription("Atributos, trilha hierárquica, responsáveis, evidências e relações nos dois sentidos.")
    .ComErros(StatusCodes.Status404NotFound);

// Recurso inteiro, não PATCH parcial: o formulário de edição já manda o
// estado completo dos campos editáveis — mais simples de raciocinar e de
// testar do que merge de patch parcial (ADR-0009 mantém isso simples).
catalogo.MapPut("/ativos/{id:guid}", async Task<Results<NoContent, JsonHttpResult<ApiError>>> (
    HttpContext http, Guid id, EditarAtivoRequest corpo, EditarAtivo caso, CancellationToken ct) =>
{
    var r = await caso.ExecutarAsync(new ComandoEditarAtivo(
        new IdDeAtivo(id), corpo.Nome, corpo.Descricao ?? "",
        CriticidadeExtensoes.De(corpo.Criticidade), corpo.Atributos), ct);

    return r.Sucesso ? TypedResults.NoContent() : Erro(http, r.Codigo!, r.Mensagem!);
})
    .WithName("editarAtivo")
    .WithSummary("Altera os campos editáveis de um ativo")
    .WithDescription("Substitui nome, descrição e criticidade; grava os atributos informados. Só em rascunho ou com revisão aberta: publicado devolve 409 ATIVO_NAO_EDITAVEL.")
    .ComErros(StatusCodes.Status403Forbidden, StatusCodes.Status404NotFound,
              StatusCodes.Status409Conflict, StatusCodes.Status422UnprocessableEntity);

catalogo.MapPost("/ativos/{id:guid}/responsaveis", async Task<Results<NoContent, JsonHttpResult<ApiError>>> (
    HttpContext http, Guid id, AtribuirResponsavel caso, AtribuirResponsavelRequest corpo, CancellationToken ct) =>
{
    if (!Enum.TryParse<PapelDeOwnership>(corpo.Papel, ignoreCase: true, out var papel))
        return Erro(http, "PAPEL_INVALIDO",
            $"Papel '{corpo.Papel}' inválido. Use: {string.Join(", ", Enum.GetNames<PapelDeOwnership>())}.");

    var r = await caso.ExecutarAsync(new ComandoAtribuirResponsavel(new IdDeAtivo(id), corpo.IdPessoa, papel), ct);
    return r.Sucesso ? TypedResults.NoContent() : Erro(http, r.Codigo!, r.Mensagem!);
})
    .WithName("atribuirResponsavel")
    .WithSummary("Define um responsável pelo ativo")
    .WithDescription("Sem os owners que o tipo exige, o pré-check bloqueia a submissão. Só em rascunho ou com revisão aberta.")
    .ComErros(StatusCodes.Status403Forbidden, StatusCodes.Status404NotFound, StatusCodes.Status409Conflict);

catalogo.MapPost("/ativos/{id:guid}/evidencias", async Task<Results<NoContent, JsonHttpResult<ApiError>>> (
    HttpContext http, Guid id, AnexarEvidenciaRequest corpo, AnexarEvidencia caso, CancellationToken ct) =>
{
    var r = await caso.ExecutarAsync(new ComandoAnexarEvidencia(new IdDeAtivo(id), corpo.Tipo, corpo.Titulo, corpo.Url), ct);
    return r.Sucesso ? TypedResults.NoContent() : Erro(http, r.Codigo!, r.Mensagem!);
})
    .WithName("anexarEvidencia")
    .WithSummary("Anexa uma evidência ao ativo")
    .WithDescription("A política do tipo define quantas evidências são exigidas para publicar. Só em rascunho ou com revisão aberta.")
    .ComErros(StatusCodes.Status403Forbidden, StatusCodes.Status404NotFound, StatusCodes.Status409Conflict);

catalogo.MapPost("/ativos/{id:guid}/relacoes", async Task<Results<NoContent, JsonHttpResult<ApiError>>> (
    HttpContext http, Guid id, RelacionarAtivosRequest corpo, RelacionarAtivos caso, CancellationToken ct) =>
{
    var r = await caso.ExecutarAsync(new ComandoRelacionarAtivos(new IdDeAtivo(id), new IdDeAtivo(corpo.IdDestino), corpo.Tipo), ct);
    return r.Sucesso ? TypedResults.NoContent() : Erro(http, r.Codigo!, r.Mensagem!);
})
    .WithName("relacionarAtivos")
    .WithSummary("Relaciona este ativo a outro")
    .WithDescription("Cria uma aresta do grafo do catálogo, com este ativo como origem.")
    .ComErros(StatusCodes.Status403Forbidden, StatusCodes.Status404NotFound, StatusCodes.Status422UnprocessableEntity);

// ── governança ──────────────────────────────────────────────────────────────

var governanca = v1.MapGroup("").WithTags(OpenApiCaixa.TagGovernanca);

governanca.MapGet("/ativos/{id:guid}/pre-check", async Task<Results<Ok<PreCheckResponse>, JsonHttpResult<ApiError>>> (
    HttpContext http, Guid id, AvaliarPreCheck caso, CancellationToken ct) =>
{
    var r = await caso.ExecutarAsync(new IdDeAtivo(id), ct);
    // Os códigos de bloqueio são contrato público: o cliente automatiza a
    // correção em vez de casar mensagens de erro (ADR-0009).
    return r.Sucesso ? TypedResults.Ok(PreCheckResponse.De(r.Valor!)) : Erro(http, r.Codigo!, r.Mensagem!);
})
    .WithName("avaliarPreCheck")
    .WithSummary("Avalia se o ativo pode ser submetido")
    .WithDescription("Devolve score de qualidade, bloqueios com código estável, alertas, a política aplicável e o caminho até a publicação. Não altera nada.")
    .ComErros(StatusCodes.Status404NotFound);

// Publicado é imutável: alterar exige abrir revisão. A revisão volta ao rito
// pela submissão; rejeitada, volta para em_revisao (não para rascunho).
governanca.MapPost("/ativos/{id:guid}/revisoes", async Task<Results<NoContent, JsonHttpResult<ApiError>>> (
    HttpContext http, Guid id, AbrirRevisao caso, CancellationToken ct) =>
{
    var r = await caso.ExecutarAsync(new ComandoAbrirRevisao(new IdDeAtivo(id)), ct);
    return r.Sucesso ? TypedResults.NoContent() : Erro(http, r.Codigo!, r.Mensagem!);
})
    .WithName("abrirRevisao")
    .WithSummary("Abre revisão de um ativo publicado")
    .WithDescription("Único caminho para alterar um ativo publicado: o ativo vai para em_revisao, aceita edição e volta à validação pela submissão. Só ativo publicado; outro estado devolve 409 TRANSICAO_INVALIDA.")
    .ComErros(StatusCodes.Status403Forbidden, StatusCodes.Status404NotFound, StatusCodes.Status409Conflict);

// Recurso, não procedimento: POST /submissoes em vez de POST /submeter (ADR-0009).
governanca.MapPost("/ativos/{id:guid}/submissoes", async Task<Results<Created<SubmissaoResponse>, JsonHttpResult<ApiError>>> (
    HttpContext http, Guid id, SubmeterAtivo caso, SubmeterAtivoRequest? corpo, CancellationToken ct) =>
{
    var r = await caso.ExecutarAsync(new ComandoSubmeterAtivo(new IdDeAtivo(id), corpo?.Motivo ?? ""), ct);
    if (!r.Sucesso) return Erro(http, r.Codigo!, r.Mensagem!);

    var s = r.Valor!;
    return TypedResults.Created($"{metadados.PrefixoDasRotas}/ativos/{id}/submissoes/{s.Revisao}",
        new SubmissaoResponse(s.Revisao, s.Score, s.EtapasAbertas.Select(e => e.ToString().ToLowerInvariant()).ToArray()));
})
    .WithName("submeterAtivo")
    .WithSummary("Submete o ativo para validação")
    .WithDescription("Roda o pré-check; se aprovado, abre uma nova revisão e as etapas de validação exigidas pela política. Reprovado no pré-check: 422 PRECHECK_REPROVADO.")
    .ComErros(StatusCodes.Status403Forbidden, StatusCodes.Status404NotFound,
              StatusCodes.Status409Conflict, StatusCodes.Status422UnprocessableEntity);

governanca.MapGet("/validacoes", async (IRepositorioDeValidacoes repo, Guid? ativo, CancellationToken ct) =>
{
    var validacoes = await repo.ListarAsync(ativo is { } id ? new IdDeAtivo(id) : null, ct);
    return TypedResults.Ok(new ListaDeValidacoesResponse(
        validacoes.Count, validacoes.Select(ValidacaoResponse.De).ToArray()));
})
    .WithName("listarValidacoes")
    .WithSummary("Lista as validações da empresa")
    .WithDescription("Abertas primeiro. O parâmetro ativo restringe a um ativo.");

governanca.MapPost("/validacoes/{id:guid}/decisoes", async Task<Results<NoContent, JsonHttpResult<ApiError>>> (
    HttpContext http, Guid id, DecidirValidacaoRequest corpo, DecidirValidacao caso, CancellationToken ct) =>
{
    var r = await caso.ExecutarAsync(new ComandoDecidirValidacao(id, corpo.Aprovada, corpo.Motivo), ct);
    return r.Sucesso ? TypedResults.NoContent() : Erro(http, r.Codigo!, r.Mensagem!);
})
    .WithName("decidirValidacao")
    .WithSummary("Aprova ou reprova uma etapa de validação")
    .WithDescription("Quem submeteu a revisão não decide sobre ela, nem sendo admin. Cada etapa exige o papel correspondente (negocial: negócio; técnica: tech lead; arquitetural: arquiteto) ou ser o owner do ativo. Reprovar uma etapa cancela as outras etapas abertas da mesma revisão e devolve o ativo para ajuste; aprovar a última etapa da revisão publica o ativo.")
    .ComErros(StatusCodes.Status403Forbidden, StatusCodes.Status404NotFound, StatusCodes.Status409Conflict);

app.Run();
return;

// O status sai do código do erro (CodigosDeErro), nunca da rota: assim nenhuma
// rota devolve 422 para uma recusa de autorização, ou para um "não encontrado".
static JsonHttpResult<ApiError> Erro(HttpContext http, string codigo, string mensagem) =>
    TypedResults.Json(RespostaDeErro.Montar(http, codigo, mensagem), statusCode: CodigosDeErro.StatusPara(codigo));

/// <summary>Readiness com Cosmos DB: o container do catálogo responde?</summary>
internal sealed class VerificacaoDeCosmos(BancoDoCatalogo banco) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext contexto, CancellationToken ct = default) =>
        await banco.VerificarAsync(ct)
            ? HealthCheckResult.Healthy("Cosmos DB alcançável.")
            : HealthCheckResult.Unhealthy("Cosmos DB não respondeu.");
}

/// <summary>Documenta, numa linha por rota, os erros específicos dela no formato ApiError.</summary>
internal static class DocumentacaoDeErros
{
    public static RouteHandlerBuilder ComErros(this RouteHandlerBuilder rota, params int[] status)
    {
        foreach (var s in status) rota.Produces<ApiError>(s);
        return rota;
    }
}
