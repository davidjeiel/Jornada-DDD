namespace Jornada.Api.Contrato;

// Corpos de requisição. Ficam no adaptador HTTP: o domínio não conhece JSON.
// Repare no que NENHUM deles tem: `usuario` e `tenant_id`. Quem age e por qual
// empresa vem da autenticação, nunca do corpo (ADR-0003, ADR-0009).
//
// `required` faz o System.Text.Json recusar o corpo sem o campo — a recusa
// vira 400 REQUISICAO_INVALIDA, e o OpenAPI marca o campo como obrigatório.

/// <summary>Cadastro de um ativo novo. Ele nasce em rascunho.</summary>
public sealed record CadastrarAtivoRequest
{
    /// <summary>Chave do tipo no metamodelo (dominio, subdominio, contexto, capacidade, sistema, aplicacao, repositorio, api, endpoint, base_dados, objeto_dado, evento).</summary>
    /// <example>sistema</example>
    public required string Tipo { get; init; }

    /// <summary>Nome do ativo. Único por tipo dentro da empresa.</summary>
    /// <example>Core Bancário</example>
    public required string Nome { get; init; }

    /// <summary>Para que serve o ativo. Obrigatória para publicar, não para cadastrar.</summary>
    /// <example>Sistema central de contas e lançamentos.</example>
    public string? Descricao { get; init; }

    /// <summary>baixa, media, alta ou critica. Ausente vale media.</summary>
    /// <example>alta</example>
    public string? Criticidade { get; init; }

    /// <summary>Id do item pai, quando o tipo exige hierarquia (ex.: subdomínio exige domínio).</summary>
    public Guid? IdPai { get; init; }

    /// <summary>Campos específicos do tipo, declarados no metamodelo.</summary>
    /// <example>{"plataforma": "mainframe"}</example>
    public Dictionary<string, string>? Atributos { get; init; }
}

/// <summary>Estado completo dos campos editáveis (PUT substitui, não mescla).</summary>
public sealed record EditarAtivoRequest
{
    /// <example>Core Bancário</example>
    public required string Nome { get; init; }

    /// <example>Sistema central de contas e lançamentos.</example>
    public string? Descricao { get; init; }

    /// <example>alta</example>
    public string? Criticidade { get; init; }

    /// <summary>Atributos informados são gravados; os ausentes ficam como estão.</summary>
    /// <example>{"plataforma": "mainframe"}</example>
    public Dictionary<string, string>? Atributos { get; init; }
}

/// <summary>Define quem responde pelo ativo.</summary>
public sealed record AtribuirResponsavelRequest
{
    /// <example>44444444-4444-4444-4444-444444444444</example>
    public required Guid IdPessoa { get; init; }

    /// <summary>OwnerNegocial, OwnerTecnico, Arquiteto ou Mantenedor.</summary>
    /// <example>OwnerTecnico</example>
    public required string Papel { get; init; }
}

/// <summary>Evidência que sustenta o cadastro (ADR, contrato, documento, link).</summary>
public sealed record AnexarEvidenciaRequest
{
    /// <example>openapi</example>
    public required string Tipo { get; init; }

    /// <example>Contrato OpenAPI v2</example>
    public required string Titulo { get; init; }

    /// <example>https://repositorio.exemplo/core-bancario/openapi.yaml</example>
    public string? Url { get; init; }
}

/// <summary>Relação deste ativo (origem) com outro (destino).</summary>
public sealed record RelacionarAtivosRequest
{
    public required Guid IdDestino { get; init; }

    /// <summary>implementa, expoe, consome, produz, depende_de ou persiste_em.</summary>
    /// <example>depende_de</example>
    public required string Tipo { get; init; }

    /// <summary>Quanto a origem depende do destino: baixa, media, alta ou critica. Ausente vale media. Alta ou crítica faz a descontinuação do destino exigir confirmação.</summary>
    /// <example>critica</example>
    public string? Criticidade { get; init; }
}

/// <summary>Pedido para tirar o ativo de vigência.</summary>
public sealed record DescontinuarAtivoRequest
{
    /// <example>Substituído pelo Core Bancário 2.</example>
    public required string Motivo { get; init; }

    /// <summary>O código do ativo (ex.: CAP-0001). Obrigatório quando há consumidor de alta criticidade; nos demais casos é ignorado.</summary>
    /// <example>CAP-0001</example>
    public string? Confirmacao { get; init; }
}

/// <summary>Envio do ativo para validação.</summary>
public sealed record SubmeterAtivoRequest
{
    /// <example>Primeira versão</example>
    public string? Motivo { get; init; }
}

/// <summary>Decisão sobre uma etapa de validação.</summary>
public sealed record DecidirValidacaoRequest
{
    /// <summary>true aprova a etapa; false devolve o ativo ao rascunho.</summary>
    /// <example>true</example>
    public required bool Aprovada { get; init; }

    /// <example>Owner e evidência conferidos.</example>
    public string? Motivo { get; init; }
}
