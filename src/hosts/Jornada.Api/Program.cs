using Jornada.Adaptadores.Memoria;
using Jornada.Api;
using Jornada.Api.Contrato;
using Jornada.Api.Demonstracao;
using Jornada.Aplicacao.Acesso;
using Jornada.Aplicacao.Catalogo.Comandos;
using Jornada.Aplicacao.Catalogo.Consultas;
using Jornada.Aplicacao.Governanca;
using Jornada.Aplicacao.Portas;
using Jornada.Dominio.Acesso;
using Jornada.Dominio.Catalogo;
using Jornada.Dominio.Comum;
using Jornada.Dominio.Governanca;
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
builder.Services.AddScoped<IAtorAtual, AtorDaRequisicao>();

// Papel concedido, não escolhido (ADR-0018). A escolha livre pelo X-Papel só
// com Acesso:ModoDemonstracao, que o ambiente local liga (local/compose.yaml).
var opcoesDeAcesso = builder.Configuration.GetSection("Acesso").Get<OpcoesDeAcesso>() ?? new OpcoesDeAcesso();
builder.Services.AddSingleton(opcoesDeAcesso);
builder.Services.AddSingleton(new RegrasDeAcesso(
    opcoesDeAcesso.AdministradoresIniciais.Select(m => m.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase)));

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
    builder.Services.AddScoped<IRepositorioDeRevisoes>(sp => sp.GetRequiredService<RepositorioCosmos>());
    builder.Services.AddScoped<ITrilhaDeAuditoria>(sp => sp.GetRequiredService<RepositorioCosmos>());
    builder.Services.AddScoped<IRepositorioDePessoas>(sp => sp.GetRequiredService<RepositorioCosmos>());
    builder.Services.AddScoped<IRepositorioDePleitos>(sp => sp.GetRequiredService<RepositorioCosmos>());
    builder.Services.AddScoped<IRepositorioDeSquads>(sp => sp.GetRequiredService<RepositorioCosmos>());
    builder.Services.AddScoped<IConsultaDeVinculos>(sp => sp.GetRequiredService<RepositorioCosmos>());
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
    builder.Services.AddScoped<IRepositorioDeRevisoes>(sp => sp.GetRequiredService<RepositorioDeAtivosEmMemoria>());
    builder.Services.AddScoped<ITrilhaDeAuditoria>(sp => sp.GetRequiredService<RepositorioDeAtivosEmMemoria>());
    builder.Services.AddScoped<RepositorioDeAcessoEmMemoria>();
    builder.Services.AddScoped<IRepositorioDePessoas>(sp => sp.GetRequiredService<RepositorioDeAcessoEmMemoria>());
    builder.Services.AddScoped<IRepositorioDePleitos>(sp => sp.GetRequiredService<RepositorioDeAcessoEmMemoria>());
    builder.Services.AddScoped<IRepositorioDeSquads>(sp => sp.GetRequiredService<RepositorioDeAcessoEmMemoria>());
    builder.Services.AddScoped<IConsultaDeVinculos>(sp => sp.GetRequiredService<RepositorioDeAtivosEmMemoria>());
}

builder.Services.AddSingleton<IMetamodelo, MetamodeloPadrao>();
builder.Services.AddSingleton<IPoliticasDeGovernanca, PoliticasPadrao>();
builder.Services.AddSingleton<IPublicadorDeEventos, PublicadorEmMemoria>();

builder.Services.AddScoped<CadastrarAtivo>();
builder.Services.AddScoped<EditarAtivo>();
builder.Services.AddScoped<AtribuirResponsavel>();
builder.Services.AddScoped<AnexarEvidencia>();
builder.Services.AddScoped<RelacionarAtivos>();
builder.Services.AddScoped<RelacionarEmLote>();
builder.Services.AddScoped<ConsultarFila>();
builder.Services.AddScoped<AtribuirValidacao>();
builder.Services.AddScoped<PainelDoValidador>();
builder.Services.AddScoped<MinhaMesa>();
builder.Services.AddScoped<SubmeterAtivo>();
builder.Services.AddScoped<AbrirRevisao>();
builder.Services.AddScoped<DecidirValidacao>();
builder.Services.AddScoped<DescontinuarAtivo>();
builder.Services.AddScoped<ArquivarAtivo>();
builder.Services.AddScoped<AnalisarImpacto>();
builder.Services.AddScoped<ConsultarHistorico>();
builder.Services.AddScoped<CompararRevisoes>();
builder.Services.AddScoped<AvaliarPreCheck>();
builder.Services.AddScoped<ObterVisaoDoAtivo>();
builder.Services.AddScoped<BuscarNoCatalogo>();
builder.Services.AddScoped<SolicitarAcesso>();
builder.Services.AddScoped<DecidirPleito>();
builder.Services.AddScoped<ListarPleitos>();
builder.Services.AddScoped<ConsultarPerfil>();
builder.Services.AddScoped<ListarPessoas>();
builder.Services.AddScoped<GestaoDeAcessos>();
builder.Services.AddScoped<CadastroDeSquads>();
builder.Services.AddScoped<ConsultarCartilha>();

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

// O cenário piloto do painel-ddd nas empresas de teste, pelos próprios casos de
// uso. Desligado por padrão; o ambiente local liga (local/compose.yaml).
if (builder.Configuration.GetValue("Demonstracao:Carregar", false))
{
    await ActivatorUtilities.CreateInstance<CargaDeDemonstracao>(app.Services).CarregarAsync();
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
    explicacao = "Use estes cabeçalhos para exercitar a API sem autenticação real. Os papéis são os concedidos " +
                 "à pessoa do X-Pessoa (veja GET /v1/pessoas); X-Papel só vale com Acesso:ModoDemonstracao.",
    modoDemonstracao = opcoesDeAcesso.ModoDemonstracao,
    cabecalhos = new
    {
        X_Empresa = "11111111-1111-1111-1111-111111111111",
        X_Pessoa = "20000000-0000-0000-0000-000000000001",
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

// O catálogo com recorte, ordem e página (servicos.buscar_pagina). /ativos continua
// sendo a lista simples, que os formulários usam para escolher pai e destino.
catalogo.MapGet("/catalogo", async Task<Results<Ok<PaginaDoCatalogoResponse>, JsonHttpResult<ApiError>>> (
    HttpContext http, BuscarNoCatalogo caso, IRelogio relogio, CancellationToken ct,
    string? termo = null, string? tipo = null, string? status = null, Guid? squad = null, string? criticidade = null,
    bool semOwner = false, string? origem = null, string? ordenar = null, bool desc = false, int pagina = 1, int porPagina = 50) =>
{
    StatusCicloVida? st = null;
    if (!string.IsNullOrWhiteSpace(status))
    {
        if (!Enum.TryParse<StatusCicloVida>(status.Replace("_", ""), ignoreCase: true, out var s))
            return Erro(http, CodigosDeErro.RequisicaoInvalida, "status deve ser rascunho, em_validacao, publicado, em_revisao, descontinuado ou arquivado.");
        st = s;
    }
    Criticidade? crit = null;
    if (!string.IsNullOrWhiteSpace(criticidade))
    {
        if (criticidade.ToLowerInvariant() is not ("baixa" or "media" or "alta" or "critica"))
            return Erro(http, CodigosDeErro.RequisicaoInvalida, "criticidade deve ser baixa, media, alta ou critica.");
        crit = CriticidadeExtensoes.De(criticidade);
    }
    OrigemDoAtivo? org = (origem ?? "").ToLowerInvariant() switch
    {
        "" => null,
        "manual" => OrigemDoAtivo.Manual,
        "automatica" => OrigemDoAtivo.Automatica,
        _ => (OrigemDoAtivo)(-1),
    };
    if (org == (OrigemDoAtivo)(-1)) return Erro(http, CodigosDeErro.RequisicaoInvalida, "origem deve ser manual ou automatica.");

    var filtro = new FiltroDoCatalogo(termo, tipo, st, squad, crit, semOwner, org, relogio.Hoje);
    var r = await caso.ExecutarAsync(filtro, ordenar, desc, pagina, porPagina, ct);
    return TypedResults.Ok(PaginaDoCatalogoResponse.De(r, relogio.Hoje));
})
    .WithName("buscarNoCatalogo")
    .WithSummary("Busca no catálogo, com filtros, ordem e página")
    .WithDescription("termo procura em nome, código e descrição. Filtros: tipo, status, squad, criticidade, semOwner (sem responsável vigente) e origem (manual ou automatica). ordenar: tipo (padrão), nome, status, criticidade, atualizado ou codigo; desconhecida vira tipo. pagina fora do intervalo devolve a última; porPagina vai de 1 a 200. total é o do recorte inteiro.")
    .ComErros(StatusCodes.Status400BadRequest);

catalogo.MapGet("/busca/sugestoes", async (BuscarNoCatalogo caso, CancellationToken ct, string? termo = null) =>
{
    var achados = await caso.SugerirAsync(termo, ct);
    return TypedResults.Ok(new SugestoesResponse((termo ?? "").Trim(), achados.Select(a => new SugestaoResponse(
        a.Id.Valor, a.Codigo.Valor, a.Nome, a.TipoItem, MaquinaDeCicloDeVida.Rotulo(a.Status), $"/ativos/{a.Id.Valor}")).ToArray()));
})
    .WithName("sugerirAtivos")
    .WithSummary("Sugestões para a busca do topo")
    .WithDescription("Até 8 ativos cujo nome, código ou descrição contém o termo, os que começam pelo termo primeiro. Termo com menos de 2 letras devolve lista vazia.");

catalogo.MapPost("/ativos", async Task<Results<Created<AtivoCriadoResponse>, JsonHttpResult<ApiError>>> (
    HttpContext http, CadastrarAtivo caso, CadastrarAtivoRequest corpo, CancellationToken ct) =>
{
    var r = await caso.ExecutarAsync(new ComandoCadastrarAtivo(
        corpo.Tipo, corpo.Nome, corpo.Descricao ?? "",
        CriticidadeExtensoes.De(corpo.Criticidade),
        corpo.IdPai is { } p ? new IdDeAtivo(p) : null,
        corpo.Atributos, corpo.IdSquad), ct);

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
        CriticidadeExtensoes.De(corpo.Criticidade), corpo.Atributos, corpo.IdSquad), ct);

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

catalogo.MapPost("/ativos/{id:guid}/relacoes", async Task<Results<Ok<RelacaoRegistradaResponse>, JsonHttpResult<ApiError>>> (
    HttpContext http, Guid id, RelacionarAtivosRequest corpo, RelacionarAtivos caso, CancellationToken ct) =>
{
    var r = await caso.ExecutarAsync(new ComandoRelacionarAtivos(new IdDeAtivo(id), new IdDeAtivo(corpo.IdDestino), corpo.Tipo,
        CriticidadeExtensoes.De(corpo.Criticidade), corpo.Mecanismo), ct);
    return r.Sucesso ? TypedResults.Ok(new RelacaoRegistradaResponse(r.Valor)) : Erro(http, r.Codigo!, r.Mensagem!);
})
    .WithName("relacionarAtivos")
    .WithSummary("Relaciona este ativo a outro")
    .WithDescription("Cria uma aresta do grafo do catálogo, com este ativo como origem. A criticidade é a da dependência; alta ou crítica faz a descontinuação do destino exigir confirmação. A mesma relação (destino e tipo) não se grava duas vezes: criada=false. Uma depende_de que fecharia um ciclo devolve 422 CICLO_DE_DEPENDENCIA.")
    .ComErros(StatusCodes.Status400BadRequest, StatusCodes.Status403Forbidden, StatusCodes.Status404NotFound, StatusCodes.Status422UnprocessableEntity);

catalogo.MapPost("/ativos/{id:guid}/relacoes/lote", async Task<Results<Ok<LoteDeRelacoesResponse>, JsonHttpResult<ApiError>>> (
    HttpContext http, Guid id, RelacionarEmLoteRequest corpo, RelacionarEmLote caso, CancellationToken ct) =>
{
    var r = await caso.ExecutarAsync(new ComandoRelacionarEmLote(new IdDeAtivo(id),
        corpo.Destinos.Select(d => new IdDeAtivo(d)).ToArray(), corpo.Tipo, CriticidadeExtensoes.De(corpo.Criticidade), corpo.Mecanismo), ct);
    return r.Sucesso
        ? TypedResults.Ok(new LoteDeRelacoesResponse(r.Valor!.Criadas, r.Valor.Ignoradas,
            r.Valor.Erros.Select(e => new ErroDoLoteResponse(e.Destino.Valor, e.Codigo, e.Mensagem)).ToArray()))
        : Erro(http, r.Codigo!, r.Mensagem!);
})
    .WithName("relacionarEmLote")
    .WithSummary("Relaciona este ativo a vários de uma vez")
    .WithDescription("Mesmo tipo, criticidade e mecanismo para todos os destinos. O próprio ativo e as relações que já existem são ignorados; um destino recusado (ciclo, inexistente) vai para erros sem impedir os outros.")
    .ComErros(StatusCodes.Status400BadRequest, StatusCodes.Status403Forbidden, StatusCodes.Status404NotFound);

catalogo.MapGet("/metamodelo", async (IMetamodelo metamodelo, CancellationToken ct) =>
{
    var tipos = await metamodelo.ObterAsync(ct);
    return TypedResults.Ok(new MetamodeloResponse(
        tipos.Todos.Select(t => new TipoDeAtivoResponse(t.Chave, t.Rotulo, t.Bloco, t.Pai,
            t.CamposDoTipo.Select(c => new CampoDoTipoResponse(c.Nome, c.Rotulo, c.Tipo, c.Obrigatorio, c.Opcoes ?? [])).ToArray())).ToArray(),
        CatalogoDeRelacoes.Tipos.Select(r => new TipoDeRelacaoResponse(r.Chave, r.Rotulo, r.DestinosSugeridos)).ToArray(),
        CatalogoDeRelacoes.Mecanismos.Select(m => new MecanismoResponse(m.Chave, m.Rotulo)).ToArray()));
})
    .WithName("consultarMetamodelo")
    .WithSummary("Tipos de ativo, tipos de relação e mecanismos da empresa")
    .WithDescription("O que o assistente de cadastro e o formulário de relação leem: os campos de cada tipo, os destinos sugeridos para cada relação e os mecanismos.");

// O rito antes de cadastrar: o assistente mostra o que o tipo e a criticidade vão exigir.
v1.MapGroup("").WithTags(OpenApiCaixa.TagGovernanca).MapGet("/ritos", async Task<Results<Ok<RitoResponse>, JsonHttpResult<ApiError>>> (
    HttpContext http, IPoliticasDeGovernanca politicas, IMetamodelo metamodelo, string tipo, CancellationToken ct, string? criticidade = null) =>
{
    var tipos = await metamodelo.ObterAsync(ct);
    if (!tipos.Existe(tipo)) return Erro(http, "TIPO_DESCONHECIDO", $"Tipo de ativo desconhecido: {tipo}");
    var crit = CriticidadeExtensoes.De(criticidade);
    var politica = (await politicas.ObterAsync(ct)).Para(tipo, crit);
    return TypedResults.Ok(new RitoResponse(tipo, tipos.Obter(tipo).Rotulo, politica.Criticidade,
        politica.Etapas.Select(e => e.ToString().ToLowerInvariant()).ToArray(),
        politica.EvidenciaMinima, politica.ScoreMinimo, politica.SlaHoras, politica.PeriodicidadeRevisaoDias));
})
    .WithName("consultarRito")
    .WithSummary("O rito de publicação de um tipo, numa criticidade")
    .WithDescription("Etapas de validação, evidências, score mínimo, prazo e periodicidade de revisão que a política aplica ao tipo e à criticidade (ausente vale media). criticidade na resposta é a da política aplicada: * quando a linha vale para qualquer criticidade.")
    .ComErros(StatusCodes.Status400BadRequest);

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

governanca.MapGet("/ativos/{id:guid}/impacto", async Task<Results<Ok<ImpactoResponse>, JsonHttpResult<ApiError>>> (
    HttpContext http, Guid id, AnalisarImpacto caso, CancellationToken ct) =>
{
    var r = await caso.ExecutarAsync(new IdDeAtivo(id), ct);
    return r.Sucesso ? TypedResults.Ok(ImpactoResponse.De(r.Valor!)) : Erro(http, r.Codigo!, r.Mensagem!);
})
    .WithName("analisarImpacto")
    .WithSummary("Lista quem depende do ativo")
    .WithDescription("Consumidores em vigor (publicados ou em revisão) que apontam para este ativo, mais críticos primeiro, com contagem por criticidade da dependência e por tipo. Não altera nada.")
    .ComErros(StatusCodes.Status404NotFound);

// Com consumidor de alta criticidade, a confirmação é o código do ativo: quem
// descontinua prova que leu a análise de impacto (painel-ddd, rota descontinuar).
governanca.MapPost("/ativos/{id:guid}/descontinuacao", async Task<Results<Ok<DescontinuacaoResponse>, JsonHttpResult<ApiError>>> (
    HttpContext http, Guid id, DescontinuarAtivoRequest corpo, DescontinuarAtivo caso, CancellationToken ct) =>
{
    var r = await caso.ExecutarAsync(new ComandoDescontinuarAtivo(new IdDeAtivo(id), corpo.Motivo, corpo.Confirmacao), ct);
    return r.Sucesso ? TypedResults.Ok(new DescontinuacaoResponse(r.Valor!.Afetados)) : Erro(http, r.Codigo!, r.Mensagem!);
})
    .WithName("descontinuarAtivo")
    .WithSummary("Descontinua um ativo publicado ou em revisão")
    .WithDescription("Tira o ativo de vigência, com motivo obrigatório. Se algum consumidor depende dele com criticidade alta ou crítica, exige confirmacao igual ao código do ativo; sem ela devolve 422 CONFIRMACAO_NECESSARIA. Outro estado devolve 409 TRANSICAO_INVALIDA.")
    .ComErros(StatusCodes.Status400BadRequest, StatusCodes.Status403Forbidden, StatusCodes.Status404NotFound,
              StatusCodes.Status409Conflict, StatusCodes.Status422UnprocessableEntity);

governanca.MapPost("/ativos/{id:guid}/arquivamento", async Task<Results<NoContent, JsonHttpResult<ApiError>>> (
    HttpContext http, Guid id, ArquivarAtivo caso, CancellationToken ct) =>
{
    var r = await caso.ExecutarAsync(new ComandoArquivarAtivo(new IdDeAtivo(id)), ct);
    return r.Sucesso ? TypedResults.NoContent() : Erro(http, r.Codigo!, r.Mensagem!);
})
    .WithName("arquivarAtivo")
    .WithSummary("Arquiva um rascunho ou um ativo descontinuado")
    .WithDescription("Estado terminal. Publicado precisa ser descontinuado antes; outro estado devolve 409 TRANSICAO_INVALIDA.")
    .ComErros(StatusCodes.Status403Forbidden, StatusCodes.Status404NotFound, StatusCodes.Status409Conflict);

// O histórico de um ativo: retratos das revisões (com hash) e trilha de
// auditoria. A aba Histórico da visão 360° do painel (ADR-0017).
governanca.MapGet("/ativos/{id:guid}/historico", async Task<Results<Ok<HistoricoResponse>, JsonHttpResult<ApiError>>> (
    HttpContext http, Guid id, ConsultarHistorico caso, CancellationToken ct, int limite = 30) =>
{
    var r = await caso.ExecutarAsync(new IdDeAtivo(id), Math.Clamp(limite, 1, 200), ct);
    return r.Sucesso ? TypedResults.Ok(HistoricoResponse.De(r.Valor!)) : Erro(http, r.Codigo!, r.Mensagem!);
})
    .WithName("consultarHistorico")
    .WithSummary("Revisões submetidas e trilha de auditoria do ativo")
    .WithDescription("Revisões da mais recente para a mais antiga, cada uma com o hash SHA-256 do retrato e se ele ainda confere (integra). Auditoria da mais recente para a mais antiga, até limite registros (padrão 30, máximo 200).")
    .ComErros(StatusCodes.Status404NotFound);

governanca.MapGet("/ativos/{id:guid}/revisoes/comparacao", async Task<Results<Ok<ComparacaoResponse>, JsonHttpResult<ApiError>>> (
    HttpContext http, Guid id, CompararRevisoes caso, CancellationToken ct, int? a = null, int? b = null) =>
{
    var r = await caso.ExecutarAsync(new IdDeAtivo(id), a, b, ct);
    return r.Sucesso ? TypedResults.Ok(ComparacaoResponse.De(r.Valor!)) : Erro(http, r.Codigo!, r.Mensagem!);
})
    .WithName("compararRevisoes")
    .WithSummary("Compara duas revisões do ativo")
    .WithDescription("Campos que mudaram da revisão a para a revisão b, já resumidos em texto. Sem parâmetros, compara a última revisão com a anterior. Revisão sem retrato devolve 404 REVISAO_NAO_ENCONTRADA.")
    .ComErros(StatusCodes.Status404NotFound);

governanca.MapGet("/validacoes", async (IRepositorioDeValidacoes repo, Guid? ativo, CancellationToken ct) =>
{
    var validacoes = await repo.ListarAsync(ativo is { } id ? new IdDeAtivo(id) : null, ct);
    return TypedResults.Ok(new ListaDeValidacoesResponse(
        validacoes.Count, validacoes.Select(ValidacaoResponse.De).ToArray()));
})
    .WithName("listarValidacoes")
    .WithSummary("Lista as validações da empresa")
    .WithDescription("Abertas primeiro. O parâmetro ativo restringe a um ativo.");

governanca.MapGet("/validacoes/fila", async Task<Results<Ok<FilaDeValidacaoResponse>, JsonHttpResult<ApiError>>> (
    HttpContext http, ConsultarFila caso, CancellationToken ct, string? etapa = null, bool minhas = false, bool livres = false) =>
{
    EtapaValidacao? filtro = null;
    if (!string.IsNullOrWhiteSpace(etapa))
    {
        if (!Enum.TryParse<EtapaValidacao>(etapa, ignoreCase: true, out var e))
            return Erro(http, CodigosDeErro.RequisicaoInvalida, "etapa deve ser negocial, tecnica ou arquitetural.");
        filtro = e;
    }
    var r = await caso.ExecutarAsync(filtro, minhas, livres, ct);
    return TypedResults.Ok(new FilaDeValidacaoResponse(r.Itens.Select(ItemDaFilaResponse.De).ToArray(), r.Contagem));
})
    .WithName("consultarFilaDeValidacao")
    .WithSummary("Fila de validação, a mais crítica primeiro")
    .WithDescription("Só etapas abertas, ordenadas por criticidade do ativo e prazo. etapa filtra pela aba; minhas=true só as que você assumiu; livres=true só as que ninguém assumiu. contagem traz o total aberto por etapa e o das minhas.");

governanca.MapPost("/validacoes/{id:guid}/atribuicao", async Task<Results<Ok<ValidacaoResponse>, JsonHttpResult<ApiError>>> (
    HttpContext http, Guid id, AtribuirValidacao caso, CancellationToken ct) =>
{
    var r = await caso.AssumirAsync(id, ct);
    return r.Sucesso ? TypedResults.Ok(ValidacaoResponse.De(r.Valor!)) : Erro(http, r.Codigo!, r.Mensagem!);
})
    .WithName("assumirValidacao")
    .WithSummary("Assume a análise de uma etapa")
    .WithDescription("Marca você como quem está analisando, para dois validadores não duplicarem trabalho. Já assumida por outra pessoa: 409 VALIDACAO_JA_ASSUMIDA.")
    .ComErros(StatusCodes.Status403Forbidden, StatusCodes.Status404NotFound, StatusCodes.Status409Conflict);

governanca.MapDelete("/validacoes/{id:guid}/atribuicao", async Task<Results<Ok<ValidacaoResponse>, JsonHttpResult<ApiError>>> (
    HttpContext http, Guid id, AtribuirValidacao caso, CancellationToken ct) =>
{
    var r = await caso.LiberarAsync(id, ct);
    return r.Sucesso ? TypedResults.Ok(ValidacaoResponse.De(r.Valor!)) : Erro(http, r.Codigo!, r.Mensagem!);
})
    .WithName("liberarValidacao")
    .WithSummary("Devolve a etapa à fila livre")
    .WithDescription("Só quem assumiu libera; de outra pessoa, 409 VALIDACAO_JA_ASSUMIDA.")
    .ComErros(StatusCodes.Status403Forbidden, StatusCodes.Status404NotFound, StatusCodes.Status409Conflict);

governanca.MapGet("/validacoes/{id:guid}/painel", async Task<Results<Ok<PainelDoValidadorResponse>, JsonHttpResult<ApiError>>> (
    HttpContext http, Guid id, PainelDoValidador caso, CancellationToken ct) =>
{
    var r = await caso.ExecutarAsync(id, ct);
    return r.Sucesso ? TypedResults.Ok(PainelDoValidadorResponse.De(r.Valor!)) : Erro(http, r.Codigo!, r.Mensagem!);
})
    .WithName("consultarPainelDoValidador")
    .WithSummary("Tudo o que o validador precisa para decidir a etapa")
    .WithDescription("A etapa, a ficha do ativo (evidências, responsáveis, trilha), o pré-check e o que mudou desde a revisão anterior.")
    .ComErros(StatusCodes.Status404NotFound);

governanca.MapGet("/mesa", async (MinhaMesa caso, CancellationToken ct) =>
    TypedResults.Ok(MesaResponse.De(await caso.ExecutarAsync(ct))))
    .WithName("consultarMesa")
    .WithSummary("Minha mesa: o que é meu")
    .WithDescription("As etapas que você assumiu, a fila livre, o que você cadastrou e ainda está com você (rascunho ou em revisão), e o que você cadastrou e aguarda decisão.");

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

// ── acesso ──────────────────────────────────────────────────────────────────

var acesso = v1.MapGroup("").WithTags(OpenApiCaixa.TagAcesso);

acesso.MapGet("/perfil", async (ConsultarPerfil caso, IRelogio relogio, CancellationToken ct) =>
    TypedResults.Ok(PerfilResponse.De(await caso.ExecutarAsync(ct), opcoesDeAcesso.ModoDemonstracao, relogio.Hoje)))
    .WithName("consultarPerfil")
    .WithSummary("Quem é você para a API e o que pode fazer")
    .WithDescription("A pessoa do X-Pessoa, os papéis que valem agora (com escopo e blocos de tipos), os pleitos dela e o que ela pode fazer. Sem pessoa, identificado é false e tudo é consulta.");

acesso.MapGet("/pessoas", async (ListarPessoas caso, IRelogio relogio, CancellationToken ct) =>
{
    var pessoas = await caso.ExecutarAsync(ct);
    return TypedResults.Ok(new ListaDePessoasResponse(pessoas.Count, pessoas.Select(p => PessoaResponse.De(p, relogio.Hoje)).ToArray()));
})
    .WithName("listarPessoas")
    .WithSummary("Lista as pessoas ativas da empresa")
    .WithDescription("Por nome. É o \"quem é você\" do perfil enquanto não há identidade corporativa (ADR-0007).");

acesso.MapPost("/pleitos", async Task<Results<Created<PleitoAbertoResponse>, JsonHttpResult<ApiError>>> (
    HttpContext http, SolicitarAcessoRequest corpo, SolicitarAcesso caso, CancellationToken ct) =>
{
    var r = await caso.ExecutarAsync(new ComandoSolicitarAcesso(
        corpo.Matricula, corpo.Nome, corpo.Email, corpo.Unidade, corpo.PapelPleiteado, corpo.Justificativa), ct);
    if (!r.Sucesso) return Erro(http, r.Codigo!, r.Mensagem!);

    return TypedResults.Created($"{metadados.PrefixoDasRotas}/pleitos/{r.Valor!.Pleito.Id}",
        new PleitoAbertoResponse(r.Valor.Pessoa.Id, r.Valor.Pleito.Id, PleitoResponse.Rotulo(r.Valor.Pleito.Status)));
})
    .WithName("solicitarAcesso")
    .WithSummary("Cadastra a pessoa e pede um papel")
    .WithDescription("Aberta a quem ainda não tem papel: não exige X-Pessoa. O cadastro não concede nada; a pessoa consulta o catálogo até um curador ou admin decidir. Matrícula já cadastrada atualiza o cadastro, sem duplicar; com pleito pendente, devolve 409 PLEITO_PENDENTE.")
    .ComErros(StatusCodes.Status409Conflict, StatusCodes.Status422UnprocessableEntity);

acesso.MapGet("/pleitos", async Task<Results<Ok<FilaDePleitosResponse>, JsonHttpResult<ApiError>>> (
    HttpContext http, ListarPleitos caso, IRelogio relogio, CancellationToken ct, string? status = null) =>
{
    StatusDoPleito? filtro = status?.ToLowerInvariant() switch
    {
        null or "" => null,
        "pendente" => StatusDoPleito.Pendente,
        "aprovado" => StatusDoPleito.Aprovado,
        "negado" => StatusDoPleito.Negado,
        _ => (StatusDoPleito)(-1),
    };
    if (filtro is (StatusDoPleito)(-1))
        return Erro(http, CodigosDeErro.RequisicaoInvalida, "status deve ser pendente, aprovado ou negado.");

    var r = await caso.ExecutarAsync(filtro, ct);
    return r.Sucesso ? TypedResults.Ok(FilaDePleitosResponse.De(r.Valor!, relogio.Hoje)) : Erro(http, r.Codigo!, r.Mensagem!);
})
    .WithName("listarPleitos")
    .WithSummary("Fila de pleitos de acesso")
    .WithDescription("Só para quem concede (curador ou admin). Pendentes primeiro e, entre eles, quem espera há mais tempo. O parâmetro status filtra: pendente, aprovado ou negado.")
    .ComErros(StatusCodes.Status403Forbidden);

acesso.MapPost("/pleitos/{id:guid}/decisoes", async Task<Results<Ok<PleitoResponse>, JsonHttpResult<ApiError>>> (
    HttpContext http, Guid id, DecidirPleitoRequest corpo, DecidirPleito caso, IRelogio relogio, CancellationToken ct) =>
{
    var r = await caso.ExecutarAsync(new ComandoDecidirPleito(
        id, corpo.Aprovar, corpo.Resposta, corpo.PapelConcedido, corpo.EscopoTipo, corpo.EscopoId), ct);
    return r.Sucesso ? TypedResults.Ok(PleitoResponse.De(r.Valor!, relogio.Hoje)) : Erro(http, r.Codigo!, r.Mensagem!);
})
    .WithName("decidirPleito")
    .WithSummary("Concede ou nega um pleito de acesso")
    .WithDescription("Curador concede todo papel menos admin; só admin concede admin. Ninguém decide o próprio pleito (403 SEGREGACAO_DE_FUNCAO). Negar, ou conceder papel diferente do pleiteado, exige resposta (422 RESPOSTA_OBRIGATORIA). Pleito já decidido devolve 409 PLEITO_JA_DECIDIDO.")
    .ComErros(StatusCodes.Status403Forbidden, StatusCodes.Status404NotFound,
              StatusCodes.Status409Conflict, StatusCodes.Status422UnprocessableEntity);

// A cartilha: leitura aberta, inclusive para quem ainda não se identificou.
acesso.MapGet("/cartilha", async (ConsultarCartilha caso, CancellationToken ct) =>
    TypedResults.Ok(CartilhaResponse.De(await caso.ExecutarAsync(ct))))
    .WithName("consultarCartilha")
    .WithSummary("Tabelas de referência da cartilha")
    .WithDescription("Quem pode fazer o quê, as etapas que cada papel decide, os blocos em que cada papel escreve e o rito de cada tipo de ativo. Tudo lido das políticas que a API aplica, não escrito à mão. Não exige X-Pessoa.");

// ── gestão de acessos (só admin) ──

var gestao = v1.MapGroup("/gestao").WithTags(OpenApiCaixa.TagAcesso);

gestao.MapGet("/pessoas", async Task<Results<Ok<ListaDeCadastrosResponse>, JsonHttpResult<ApiError>>> (
    HttpContext http, GestaoDeAcessos caso, IRelogio relogio, CancellationToken ct, string? busca = null) =>
{
    var r = await caso.ListarAsync(busca, ct);
    return r.Sucesso
        ? TypedResults.Ok(new ListaDeCadastrosResponse(r.Valor!.Count, r.Valor.Select(c => CadastroResponse.De(c, relogio.Hoje)).ToArray()))
        : Erro(http, r.Codigo!, r.Mensagem!);
})
    .WithName("listarCadastros")
    .WithSummary("Lista todos os cadastros, inclusive os inativos")
    .WithDescription("Só admin. Ativos primeiro, por nome. O parâmetro busca filtra por nome, matrícula, e-mail, unidade ou squad.")
    .ComErros(StatusCodes.Status403Forbidden);

gestao.MapGet("/pessoas/{id:guid}/vinculos", async Task<Results<Ok<VinculosResponse>, JsonHttpResult<ApiError>>> (
    HttpContext http, Guid id, GestaoDeAcessos caso, CancellationToken ct) =>
{
    var r = await caso.VinculosAsync(id, ct);
    return r.Sucesso ? TypedResults.Ok(new VinculosResponse(r.Valor!.Count == 0, r.Valor)) : Erro(http, r.Codigo!, r.Mensagem!);
})
    .WithName("consultarVinculos")
    .WithSummary("O que amarra o cadastro ao histórico")
    .WithDescription("Só admin. Papéis, pleitos, responsabilidades, revisões, validações e auditoria. Sem vínculo, o cadastro pode ser excluído.")
    .ComErros(StatusCodes.Status403Forbidden, StatusCodes.Status404NotFound);

gestao.MapPut("/pessoas/{id:guid}", async Task<Results<Ok<CadastroResponse>, JsonHttpResult<ApiError>>> (
    HttpContext http, Guid id, CorrigirCadastroRequest corpo, GestaoDeAcessos caso, IRelogio relogio, CancellationToken ct) =>
{
    var r = await caso.CorrigirAsync(id, corpo.Nome, corpo.Email, corpo.Unidade, corpo.IdSquad, corpo.Ativa, ct);
    return r.Sucesso ? TypedResults.Ok(CadastroResponse.De(r.Valor!, relogio.Hoje)) : Erro(http, r.Codigo!, r.Mensagem!);
})
    .WithName("corrigirCadastro")
    .WithSummary("Corrige o cadastro e o situa como ativo ou inativo")
    .WithDescription("Só admin. Nome, e-mail, unidade e squad; a matrícula não muda. ativa=false encerra todos os papéis; o administrador conectado não se inativa (403 PROPRIO_CADASTRO).")
    .ComErros(StatusCodes.Status400BadRequest, StatusCodes.Status403Forbidden, StatusCodes.Status404NotFound);

gestao.MapPost("/pessoas/{id:guid}/papeis", async Task<Results<Ok<CadastroResponse>, JsonHttpResult<ApiError>>> (
    HttpContext http, Guid id, PapelNoEscopoRequest corpo, GestaoDeAcessos caso, IRelogio relogio, CancellationToken ct) =>
{
    var r = await caso.ConcederAsync(id, corpo.Papel, corpo.EscopoTipo, corpo.EscopoId, ct);
    return r.Sucesso ? TypedResults.Ok(CadastroResponse.De(r.Valor!, relogio.Hoje)) : Erro(http, r.Codigo!, r.Mensagem!);
})
    .WithName("concederPapel")
    .WithSummary("Concede um papel direto, fora de pleito")
    .WithDescription("Só admin. Ninguém concede a si mesmo (403 SEGREGACAO_DE_FUNCAO). Papel repetido no mesmo escopo devolve 409 PAPEL_JA_CONCEDIDO.")
    .ComErros(StatusCodes.Status400BadRequest, StatusCodes.Status403Forbidden, StatusCodes.Status404NotFound, StatusCodes.Status409Conflict);

gestao.MapPost("/pessoas/{id:guid}/revogacoes", async Task<Results<Ok<CadastroResponse>, JsonHttpResult<ApiError>>> (
    HttpContext http, Guid id, PapelNoEscopoRequest corpo, GestaoDeAcessos caso, IRelogio relogio, CancellationToken ct) =>
{
    var r = await caso.RevogarAsync(id, corpo.Papel, corpo.EscopoTipo, corpo.EscopoId, ct);
    return r.Sucesso ? TypedResults.Ok(CadastroResponse.De(r.Valor!, relogio.Hoje)) : Erro(http, r.Codigo!, r.Mensagem!);
})
    .WithName("revogarPapel")
    .WithSummary("Revoga um papel vigente")
    .WithDescription("Só admin. A atribuição deixa de valer na hora e fica no histórico, encerrada. O administrador conectado não revoga os próprios papéis (403 PROPRIO_CADASTRO).")
    .ComErros(StatusCodes.Status400BadRequest, StatusCodes.Status403Forbidden, StatusCodes.Status404NotFound, StatusCodes.Status409Conflict);

gestao.MapPost("/inativacoes", async Task<Results<Ok<InativacaoEmLoteResponse>, JsonHttpResult<ApiError>>> (
    HttpContext http, InativarEmLoteRequest corpo, GestaoDeAcessos caso, CancellationToken ct) =>
{
    var r = await caso.InativarEmLoteAsync(corpo.Ids, ct);
    return r.Sucesso ? TypedResults.Ok(new InativacaoEmLoteResponse(r.Valor)) : Erro(http, r.Codigo!, r.Mensagem!);
})
    .WithName("inativarEmLote")
    .WithSummary("Inativa vários cadastros")
    .WithDescription("Só admin. Encerra os papéis de cada um. O cadastro do administrador conectado e os já inativos ficam de fora.")
    .ComErros(StatusCodes.Status403Forbidden);

gestao.MapDelete("/pessoas/{id:guid}", async Task<Results<NoContent, JsonHttpResult<ApiError>>> (
    HttpContext http, Guid id, GestaoDeAcessos caso, CancellationToken ct) =>
{
    var r = await caso.ExcluirAsync(id, ct);
    return r.Sucesso ? TypedResults.NoContent() : Erro(http, r.Codigo!, r.Mensagem!);
})
    .WithName("excluirCadastro")
    .WithSummary("Exclui um cadastro sem vínculo")
    .WithDescription("Só admin. Com qualquer vínculo (papel, pleito, responsabilidade, revisão, validação, auditoria) devolve 409 CADASTRO_COM_VINCULOS: inative em vez de excluir.")
    .ComErros(StatusCodes.Status403Forbidden, StatusCodes.Status404NotFound, StatusCodes.Status409Conflict);

// ── squads ──

acesso.MapGet("/squads", async (CadastroDeSquads caso, CancellationToken ct, string? busca = null) =>
{
    var squads = await caso.ListarAsync(busca, ct);
    return TypedResults.Ok(new ListaDeSquadsResponse(squads.Count, squads.Select(SquadResponse.De).ToArray()));
})
    .WithName("listarSquads")
    .WithSummary("Lista os squads da empresa")
    .WithDescription("Ativos primeiro, por título. O parâmetro busca filtra por título, sigla ou descrição.");

acesso.MapPost("/squads", async Task<Results<Created<SquadResponse>, JsonHttpResult<ApiError>>> (
    HttpContext http, SquadRequest corpo, CadastroDeSquads caso, CancellationToken ct) =>
{
    var r = await caso.CriarAsync(corpo.Sigla, corpo.Titulo, corpo.Descricao, ct);
    return r.Sucesso
        ? TypedResults.Created($"{metadados.PrefixoDasRotas}/squads/{r.Valor!.Id}", SquadResponse.De(r.Valor))
        : Erro(http, r.Codigo!, r.Mensagem!);
})
    .WithName("cadastrarSquad")
    .WithSummary("Cadastra um squad")
    .WithDescription("Curador ou admin. Sigla repetida na empresa devolve 409 SIGLA_DUPLICADA.")
    .ComErros(StatusCodes.Status400BadRequest, StatusCodes.Status403Forbidden, StatusCodes.Status409Conflict);

acesso.MapPut("/squads/{id:guid}", async Task<Results<Ok<SquadResponse>, JsonHttpResult<ApiError>>> (
    HttpContext http, Guid id, SquadRequest corpo, CadastroDeSquads caso, CancellationToken ct) =>
{
    var r = await caso.AlterarAsync(id, corpo.Sigla, corpo.Titulo, corpo.Descricao, ct);
    return r.Sucesso ? TypedResults.Ok(SquadResponse.De(r.Valor!)) : Erro(http, r.Codigo!, r.Mensagem!);
})
    .WithName("alterarSquad")
    .WithSummary("Altera título, sigla e descrição do squad")
    .WithDescription("Curador ou admin.")
    .ComErros(StatusCodes.Status400BadRequest, StatusCodes.Status403Forbidden, StatusCodes.Status404NotFound, StatusCodes.Status409Conflict);

acesso.MapPost("/squads/{id:guid}/inativacao", async Task<Results<Ok<SquadResponse>, JsonHttpResult<ApiError>>> (
    HttpContext http, Guid id, CadastroDeSquads caso, CancellationToken ct) =>
{
    var r = await caso.InativarAsync(id, ct);
    return r.Sucesso ? TypedResults.Ok(SquadResponse.De(r.Valor!)) : Erro(http, r.Codigo!, r.Mensagem!);
})
    .WithName("inativarSquad")
    .WithSummary("Inativa um squad")
    .WithDescription("Curador ou admin. Os vínculos existentes são preservados.")
    .ComErros(StatusCodes.Status403Forbidden, StatusCodes.Status404NotFound);

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
