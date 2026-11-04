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

    /// <summary>Como a relação acontece: sincrono, assincrono, batch, arquivo ou biblioteca. Opcional.</summary>
    /// <example>assincrono</example>
    public string? Mecanismo { get; init; }
}

/// <summary>Várias relações do mesmo tipo, com a mesma criticidade e o mesmo mecanismo.</summary>
public sealed record RelacionarEmLoteRequest
{
    public required IReadOnlyList<Guid> Destinos { get; init; }

    /// <example>expoe</example>
    public required string Tipo { get; init; }

    /// <example>media</example>
    public string? Criticidade { get; init; }

    /// <example>sincrono</example>
    public string? Mecanismo { get; init; }
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

/// <summary>Cadastro da pessoa e pedido de papel. Aberto a quem ainda não tem papel nenhum.</summary>
public sealed record SolicitarAcessoRequest
{
    /// <summary>Uma letra seguida de seis números. Minúscula vira maiúscula.</summary>
    /// <example>C123456</example>
    public required string Matricula { get; init; }

    /// <summary>Nome completo: pelo menos nome e sobrenome.</summary>
    /// <example>Davi Souza</example>
    public required string Nome { get; init; }

    /// <example>davi@empresa.com</example>
    public required string Email { get; init; }

    /// <summary>Código da unidade, com quatro números.</summary>
    /// <example>0427</example>
    public required string Unidade { get; init; }

    /// <summary>admin, curador, arquiteto, techlead, negocio ou consulta.</summary>
    /// <example>techlead</example>
    public required string PapelPleiteado { get; init; }

    /// <example>Vou cadastrar as APIs do meu time.</example>
    public string? Justificativa { get; init; }
}

/// <summary>Decisão sobre um pleito de acesso.</summary>
public sealed record DecidirPleitoRequest
{
    /// <summary>true concede; false nega, e então a resposta é obrigatória.</summary>
    /// <example>true</example>
    public required bool Aprovar { get; init; }

    /// <summary>Resposta a quem pediu. Obrigatória ao negar e ao conceder papel diferente do pleiteado.</summary>
    /// <example>Bem-vindo.</example>
    public string? Resposta { get; init; }

    /// <summary>O papel concedido, quando diferente do pleiteado. Só admin concede admin.</summary>
    /// <example>techlead</example>
    public string? PapelConcedido { get; init; }

    /// <summary>empresa (padrão) ou dominio.</summary>
    /// <example>empresa</example>
    public string? EscopoTipo { get; init; }

    /// <summary>Id do domínio, quando o escopo é dominio.</summary>
    public Guid? EscopoId { get; init; }
}

/// <summary>Correção do cadastro pela gestão de acessos. A matrícula não muda.</summary>
public sealed record CorrigirCadastroRequest
{
    /// <example>Davi Souza Lima</example>
    public required string Nome { get; init; }

    /// <example>davi.lima@empresa.com</example>
    public required string Email { get; init; }

    /// <example>0500</example>
    public required string Unidade { get; init; }

    /// <summary>O squad da pessoa; nulo tira do squad.</summary>
    public Guid? IdSquad { get; init; }

    /// <summary>false inativa o cadastro e encerra todos os papéis; true reativa, sem devolver papel.</summary>
    /// <example>true</example>
    public required bool Ativa { get; init; }
}

/// <summary>Um papel num escopo, para conceder ou revogar direto.</summary>
public sealed record PapelNoEscopoRequest
{
    /// <summary>admin, curador, arquiteto, techlead, negocio ou consulta.</summary>
    /// <example>arquiteto</example>
    public required string Papel { get; init; }

    /// <summary>empresa (padrão) ou dominio.</summary>
    /// <example>empresa</example>
    public string? EscopoTipo { get; init; }

    /// <summary>Id do domínio, quando o escopo é dominio.</summary>
    public Guid? EscopoId { get; init; }
}

/// <summary>Cadastros a inativar de uma vez. O do administrador conectado é ignorado.</summary>
public sealed record InativarEmLoteRequest
{
    public required IReadOnlyList<Guid> Ids { get; init; }
}

/// <summary>Um squad: título, sigla e descrição, os três obrigatórios.</summary>
public sealed record SquadRequest
{
    /// <summary>Até 20 letras; guardada em maiúsculas e única na empresa.</summary>
    /// <example>CRED</example>
    public required string Sigla { get; init; }

    /// <example>Squad Crédito</example>
    public required string Titulo { get; init; }

    /// <example>Originação e manutenção de contratos de crédito.</example>
    public required string Descricao { get; init; }
}
