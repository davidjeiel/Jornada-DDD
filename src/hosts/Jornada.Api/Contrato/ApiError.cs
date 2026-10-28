namespace Jornada.Api.Contrato;

// Modelo de erro padrão CAIXA (guia "API .NET com OpenAPI 3.0", §9). Toda
// resposta 4xx/5xx desta API sai neste formato, inclusive as que o próprio
// framework produziria (JSON malformado, rota sem empresa, exceção inesperada).
// Substitui o Problem Details (RFC 9457) do ADR-0009 — ver ADR-0014.

/// <summary>Erro no padrão CAIXA. Toda resposta 4xx e 5xx desta API usa este formato.</summary>
public sealed class ApiError
{
    /// <summary>Código estável, para o cliente tratar o erro sem interpretar texto.</summary>
    /// <example>ATIVO_NAO_ENCONTRADO</example>
    public required string Codigo { get; init; }

    /// <summary>Mensagem para pessoas. Nunca carrega stack trace nem dado interno.</summary>
    /// <example>Ativo não encontrado.</example>
    public required string Mensagem { get; init; }

    /// <summary>O mesmo valor devolvido no cabeçalho <c>x-correlation-id</c>.</summary>
    /// <example>7df2662f-1b66-43f5-98d4-bfe3c3600011</example>
    public required string CorrelationId { get; init; }

    /// <summary>Instante do erro, com fuso.</summary>
    /// <example>2026-10-09T10:37:00-03:00</example>
    public required DateTimeOffset DataHora { get; init; }
}

/// <summary>
/// Código de erro → status HTTP, num ponto só. Antes cada rota escolhia o
/// status (quase tudo virava 422, até "não encontrado"); agora segue a tabela
/// do guia CAIXA (§5): 400 entrada inválida, 404 não encontrado, 409 conflito
/// de estado, 422 regra de negócio.
/// </summary>
public static class CodigosDeErro
{
    public const string RequisicaoInvalida = "REQUISICAO_INVALIDA";
    public const string EmpresaNaoIdentificada = "EMPRESA_NAO_IDENTIFICADA";
    public const string SemPermissao = "SEM_PERMISSAO";
    public const string ErroInterno = "ERRO_INTERNO";

    private static readonly Dictionary<string, int> Status = new(StringComparer.Ordinal)
    {
        [RequisicaoInvalida] = StatusCodes.Status400BadRequest,
        ["TIPO_DESCONHECIDO"] = StatusCodes.Status400BadRequest,
        ["TIPO_OBRIGATORIO"] = StatusCodes.Status400BadRequest,
        ["TITULO_OBRIGATORIO"] = StatusCodes.Status400BadRequest,
        ["NOME_OBRIGATORIO"] = StatusCodes.Status400BadRequest,
        ["MOTIVO_OBRIGATORIO"] = StatusCodes.Status400BadRequest,
        ["CODIGO_INVALIDO"] = StatusCodes.Status400BadRequest,
        ["PAPEL_INVALIDO"] = StatusCodes.Status400BadRequest,
        ["MATRICULA_INVALIDA"] = StatusCodes.Status400BadRequest,
        ["UNIDADE_INVALIDA"] = StatusCodes.Status400BadRequest,

        [EmpresaNaoIdentificada] = StatusCodes.Status401Unauthorized,
        [SemPermissao] = StatusCodes.Status403Forbidden,

        ["ATIVO_NAO_ENCONTRADO"] = StatusCodes.Status404NotFound,
        ["VALIDACAO_NAO_ENCONTRADA"] = StatusCodes.Status404NotFound,

        ["TRANSICAO_INVALIDA"] = StatusCodes.Status409Conflict,
        ["VALIDACAO_JA_DECIDIDA"] = StatusCodes.Status409Conflict,
        ["VALIDACAO_OBSOLETA"] = StatusCodes.Status409Conflict,
        ["ATIVO_NAO_EDITAVEL"] = StatusCodes.Status409Conflict,
        ["CONFLITO_DE_EDICAO"] = StatusCodes.Status409Conflict,

        [ErroInterno] = StatusCodes.Status500InternalServerError,
    };

    /// <summary>Código sem entrada na tabela é regra de negócio: 422.</summary>
    public static int StatusPara(string codigo) =>
        Status.TryGetValue(codigo, out var status) ? status : StatusCodes.Status422UnprocessableEntity;
}
