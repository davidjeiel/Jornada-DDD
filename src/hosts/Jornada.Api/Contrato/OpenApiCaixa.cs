using System.Reflection;
using System.Text.Json.Nodes;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Jornada.Api.Contrato;

/// <summary>
/// Metadados exigidos pelo guia CAIXA (§4) que não são do código: equipe,
/// gestor, classificação, RTC, servidores. Vêm de configuração (seção
/// <c>OpenApi</c>) porque são definidos pela governança, não pelo time.
/// O valor padrão "a definir" deixa a lacuna visível no contrato em vez de
/// inventar um dado corporativo.
/// </summary>
public sealed class MetadadosDoContrato
{
    public const string ADefinir = "a definir com a governança";

    public string Titulo { get; set; } = "API Catálogo Corporativo DDD";
    public string Versao { get; set; } = "0.1.0";
    public string Descricao { get; set; } =
        "Catálogo governado de ativos de negócio (domínio, subdomínio, contexto, capacidade) " +
        "e técnicos (sistema, aplicação, API, endpoint, dados), com ciclo de vida, pré-check, " +
        "validação em etapas e score de qualidade cadastral.";
    public string EquipeResponsavel { get; set; } = ADefinir;
    public string EmailDaEquipe { get; set; } = ADefinir;
    public string GestorNegocial { get; set; } = ADefinir;
    public string ClassificacaoDaInformacao { get; set; } = ADefinir;
    public string IdentificacaoRtc { get; set; } = ADefinir;
    public string TimeoutGateway { get; set; } = ADefinir;
    public string TimeoutBackend { get; set; } = ADefinir;

    /// <summary>Servidores por ambiente. Vazio = mesmo host que serve o documento.</summary>
    public List<ServidorDoContrato> Servidores { get; set; } = [];

    /// <summary>
    /// Prefixo das rotas de negócio. O guia pede /&lt;dominio&gt;/&lt;area&gt;/&lt;produto&gt;/v1,
    /// com valores aprovados pela governança; até lá, /v1.
    /// </summary>
    public string PrefixoDasRotas { get; set; } = "/v1";

    /// <summary>Swagger UI fora de Development só com aprovação (guia CAIXA §12).</summary>
    public bool HabilitarUi { get; set; }
}

public sealed class ServidorDoContrato
{
    public string Url { get; set; } = "";
    public string Descricao { get; set; } = "";
}

public static class OpenApiCaixa
{
    public const string NomeDoDocumento = "v1";

    // Grupos de operações. A descrição aparece no contrato e no Swagger UI.
    public const string TagCatalogo = "Catálogo";
    public const string TagGovernanca = "Governança";
    public const string TagOperacao = "Operação";

    // Esquemas de segurança.
    public const string EsquemaBearer = "Bearer";
    public static readonly string[] CabecalhosDeDesenvolvimento = ["X-Empresa", "X-Papel", "X-Pessoa", "X-Unidade"];

    public static IServiceCollection AdicionarContratoOpenApi(
        this IServiceCollection servicos, MetadadosDoContrato meta)
    {
        servicos.AddEndpointsApiExplorer();
        servicos.AddSwaggerGen(o =>
        {
            o.SwaggerDoc(NomeDoDocumento, new OpenApiInfo
            {
                Title = meta.Titulo,
                Version = meta.Versao,
                Description = meta.Descricao,
                Contact = new OpenApiContact { Name = meta.EquipeResponsavel, Email = EmailOuNulo(meta.EmailDaEquipe) },
                Extensions = new Dictionary<string, IOpenApiExtension>
                {
                    ["x-equipe-responsavel"] = Texto(meta.EquipeResponsavel),
                    ["x-gestor-negocial"] = Texto(meta.GestorNegocial),
                    ["x-classificacao-informacao"] = Texto(meta.ClassificacaoDaInformacao),
                    ["x-identificacao-rtc"] = Texto(meta.IdentificacaoRtc),
                    ["x-timeout-gateway"] = Texto(meta.TimeoutGateway),
                    ["x-timeout-backend"] = Texto(meta.TimeoutBackend),
                },
            });

            var xml = Path.Combine(AppContext.BaseDirectory,
                $"{Assembly.GetExecutingAssembly().GetName().Name}.xml");
            if (File.Exists(xml)) o.IncludeXmlComments(xml, includeControllerXmlComments: false);

            o.AddSecurityDefinition(EsquemaBearer, new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                Description =
                    "ALVO de produção: token JWT do provedor de identidade corporativo, com a empresa " +
                    "no claim tid_empresa. AINDA NÃO VALIDADO por esta versão da API — enquanto a " +
                    "identidade não for ligada, use os cabeçalhos de desenvolvimento.",
            });

            foreach (var cabecalho in CabecalhosDeDesenvolvimento)
            {
                o.AddSecurityDefinition(cabecalho, new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.ApiKey,
                    In = ParameterLocation.Header,
                    Name = cabecalho,
                    Description = DescricaoDoCabecalho(cabecalho),
                });
            }

            o.DocumentFilter<FiltroDoDocumento>();
            o.OperationFilter<FiltroDeOperacao>();
            o.SchemaFilter<FiltroDeRespostas>();
            o.SupportNonNullableReferenceTypes();
        });
        servicos.AddSingleton(meta);
        return servicos;
    }

    private static string DescricaoDoCabecalho(string cabecalho) => cabecalho switch
    {
        "X-Empresa" => "SOMENTE DESENVOLVIMENTO. Id da empresa (UUID). Em produção vem do token.",
        "X-Papel" => "SOMENTE DESENVOLVIMENTO. admin, curador, arquiteto, techlead, negocio ou consulta.",
        "X-Pessoa" => "SOMENTE DESENVOLVIMENTO. Id da pessoa que age (UUID).",
        "X-Unidade" => "SOMENTE DESENVOLVIMENTO. Id da unidade da pessoa (UUID), escopo de autorização.",
        _ => "SOMENTE DESENVOLVIMENTO.",
    };

    private static JsonNodeExtension Texto(string valor) => new(JsonValue.Create(valor));

    private static string? EmailOuNulo(string email) => email.Contains('@') ? email : null;
}

/// <summary>Servidores e descrição dos grupos de operação.</summary>
internal sealed class FiltroDoDocumento(MetadadosDoContrato meta) : IDocumentFilter
{
    public void Apply(OpenApiDocument documento, DocumentFilterContext contexto)
    {
        if (meta.Servidores.Count > 0)
        {
            documento.Servers = meta.Servidores
                .Select(s => new OpenApiServer { Url = s.Url, Description = s.Descricao })
                .ToList();
        }

        documento.Tags = new HashSet<OpenApiTag>
        {
            new() { Name = OpenApiCaixa.TagCatalogo, Description = "Cadastro e consulta de ativos, responsáveis, evidências e relações." },
            new() { Name = OpenApiCaixa.TagGovernanca, Description = "Pré-check, submissão e decisão das etapas de validação." },
            new() { Name = OpenApiCaixa.TagOperacao, Description = "Verificações de saúde para o orquestrador." },
        };
    }
}

/// <summary>
/// Respostas: todo campo que a API sempre envia é marcado obrigatório (o
/// consumidor não precisa tratar ausência), e cada tipo leva um exemplo
/// realista (guia CAIXA §4: "exemplos de requisição e resposta"). Os exemplos
/// de requisição ficam nos próprios tipos, em &lt;example&gt;.
/// </summary>
internal sealed class FiltroDeRespostas : ISchemaFilter
{
    private const string IdAtivo = "01a12128-8221-716c-89ac-50ca6aad15a3";
    private const string IdValidacao = "01a12128-8277-7ab1-9c35-1b2e4f9d0c11";
    private const string IdPessoa = "40000000-0000-0000-0000-000000000009";

    private static readonly Dictionary<Type, string> Exemplos = new()
    {
        [typeof(AtivoCriadoResponse)] = $$"""{"id":"{{IdAtivo}}"}""",
        [typeof(AtivoResumoResponse)] = $$"""{"id":"{{IdAtivo}}","codigo":"SIS-0001","nome":"Core Bancário","tipo":"sistema","status":"rascunho","criticidade":"alta"}""",
        [typeof(FichaDoAtivoResponse)] = $$"""
            {"id":"{{IdAtivo}}","codigo":"SIS-0001","nome":"Core Bancário","tipoItem":"sistema",
             "status":"publicado","criticidade":"alta","descricao":"Sistema central de contas e lançamentos.",
             "revisaoAtual":1,"trilha":["Core Bancário"],"atributos":{"plataforma":"mainframe"},
             "evidencias":[{"id":"01a12128-8290-7d4e-a1f0-0b6a3c2d9e10","tipo":"openapi","titulo":"Contrato OpenAPI v2","url":"https://repositorio.exemplo/core-bancario/openapi.yaml"}],
             "responsaveis":[{"idPessoa":"{{IdPessoa}}","papel":"OwnerTecnico","inicioVigencia":"2026-10-09","fimVigencia":null,"vigente":true}],
             "relacoes":[]}
            """,
        [typeof(PreCheckResponse)] = """
            {"aprovado":false,"score":76,
             "dimensoes":{"completude":100,"consistencia":100,"ownership":0,"evidencia":80,"temporalidade":100},
             "bloqueios":[{"codigo":"OWNER_NAO_DEFINIDO","mensagem":"Owner técnico não definido"}],
             "alertas":["Definir owner tecnico"],
             "politica":{"scoreMinimo":60,"evidenciaMinima":0,"slaHoras":72,"etapas":["tecnica"]},
             "caminho":[{"passo":"responsaveis","rotulo":"Definir responsáveis","concluido":false,"impedimentos":["OWNER_NAO_DEFINIDO"]}]}
            """,
        [typeof(SubmissaoResponse)] = """{"revisao":1,"score":97,"etapasAbertas":["tecnica"]}""",
        [typeof(ValidacaoResponse)] = $$"""
            {"id":"{{IdValidacao}}","ativo":"{{IdAtivo}}","revisao":1,"etapa":"tecnica","status":"aberta",
             "submetidaPor":"20000000-0000-0000-0000-000000000001","decididaPor":null,"motivo":null}
            """,
        [typeof(SaudeResponse)] = """{"situacao":"ok"}""",
    };

    public void Apply(IOpenApiSchema schema, SchemaFilterContext contexto)
    {
        if (schema is not OpenApiSchema concreto || !contexto.Type.Name.EndsWith("Response", StringComparison.Ordinal))
            return;

        if (concreto.Properties is { Count: > 0 } propriedades)
        {
            concreto.Required = new HashSet<string>(propriedades
                .Where(p => p.Value.Type is not { } tipo || !tipo.HasFlag(JsonSchemaType.Null))
                .Select(p => p.Key));
        }

        if (Exemplos.TryGetValue(contexto.Type, out var exemplo))
            concreto.Example = JsonNode.Parse(exemplo);
    }
}

/// <summary>
/// O que vale para TODA operação e por isso não deve ser repetido em cada rota:
/// o cabeçalho x-correlation-id (entrada e saída), a segurança das rotas de
/// negócio, os erros que qualquer rota pode devolver e as descrições das
/// respostas em português.
/// </summary>
internal sealed class FiltroDeOperacao : IOperationFilter
{
    private static readonly Dictionary<string, string> Descricoes = new()
    {
        ["200"] = "Operação concluída com sucesso.",
        ["201"] = "Recurso criado.",
        ["204"] = "Operação concluída, sem conteúdo de resposta.",
        ["400"] = "Requisição inválida: corpo malformado, campo obrigatório ausente ou valor fora do domínio.",
        ["401"] = "Empresa não identificada (token ou, em desenvolvimento, cabeçalho X-Empresa).",
        ["403"] = "O papel da pessoa não permite esta ação neste ativo.",
        ["404"] = "Recurso não encontrado nesta empresa.",
        ["409"] = "Conflito com o estado atual do recurso.",
        ["422"] = "Regra de negócio não atendida.",
        ["500"] = "Erro interno não esperado. O detalhe fica no log, sob o x-correlation-id.",
        ["503"] = "Dependência indisponível.",
    };

    public void Apply(OpenApiOperation operacao, OperationFilterContext contexto)
    {
        operacao.Parameters ??= [];
        operacao.Parameters.Add(new OpenApiParameter
        {
            Name = MiddlewareDeCorrelacao.Cabecalho,
            In = ParameterLocation.Header,
            Required = false,
            Description = "Identificador de rastreabilidade. Se ausente ou inválido, a API gera um e o devolve no cabeçalho de mesmo nome.",
            Schema = new OpenApiSchema { Type = JsonSchemaType.String, Format = "uuid" },
            Example = JsonValue.Create("1cf88bdf-48c6-4421-a40b-79fb835a6c4d"),
        });

        var ehOperacao = operacao.Tags?.Any(t => t.Name == OpenApiCaixa.TagOperacao) == true;
        if (!ehOperacao)
        {
            // Duas alternativas (OU): o token de produção, ou os cabeçalhos de
            // desenvolvimento juntos (E). X-Unidade fica fora: é opcional.
            operacao.Security =
            [
                new OpenApiSecurityRequirement
                {
                    [new OpenApiSecuritySchemeReference(OpenApiCaixa.EsquemaBearer, contexto.Document)] = [],
                },
                new OpenApiSecurityRequirement
                {
                    [new OpenApiSecuritySchemeReference("X-Empresa", contexto.Document)] = [],
                    [new OpenApiSecuritySchemeReference("X-Papel", contexto.Document)] = [],
                    [new OpenApiSecuritySchemeReference("X-Pessoa", contexto.Document)] = [],
                },
            ];

            var erro = contexto.SchemaGenerator.GenerateSchema(typeof(ApiError), contexto.SchemaRepository);
            operacao.Responses ??= [];
            foreach (var status in new[] { "400", "401", "500" })
            {
                if (operacao.Responses.ContainsKey(status)) continue;
                operacao.Responses[status] = new OpenApiResponse
                {
                    Content = new Dictionary<string, OpenApiMediaType>
                    {
                        ["application/json"] = new() { Schema = erro },
                    },
                };
            }
        }

        if (operacao.Responses is null) return;
        foreach (var (status, resposta) in operacao.Responses)
        {
            if (resposta is not OpenApiResponse concreta) continue;
            if (Descricoes.TryGetValue(status, out var texto)) concreta.Description = texto;

            concreta.Headers ??= new Dictionary<string, IOpenApiHeader>();
            concreta.Headers[MiddlewareDeCorrelacao.Cabecalho] = new OpenApiHeader
            {
                Description = "O identificador de rastreabilidade desta requisição.",
                Schema = new OpenApiSchema { Type = JsonSchemaType.String, Format = "uuid" },
            };
        }
    }
}
