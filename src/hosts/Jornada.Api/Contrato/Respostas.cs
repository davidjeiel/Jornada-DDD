using Jornada.Aplicacao.Catalogo.Consultas;
using Jornada.Aplicacao.Portas;
using Jornada.Dominio.Catalogo;
using Jornada.Dominio.Governanca;

namespace Jornada.Api.Contrato;

// Corpos de resposta. São o CONTRATO público: tipos nomeados (e não objetos
// anônimos) para que o OpenAPI descreva cada campo, e para que uma mudança no
// domínio não altere a resposta sem passar por aqui.
//
// Identificadores saem como texto. Antes a ficha devolvia "id": {"valor": "..."}
// (o IdDeAtivo serializado por dentro), e o frontend, que espera texto,
// chamava /v1/ativos/[object Object].

/// <summary>Ativo recém-cadastrado.</summary>
/// <param name="Id">Identificador público do ativo.</param>
public sealed record AtivoCriadoResponse(Guid Id);

/// <summary>Linha da listagem do catálogo.</summary>
public sealed record AtivoResumoResponse(
    Guid Id, string Codigo, string Nome, string Tipo, string Status, string Criticidade)
{
    public static AtivoResumoResponse De(Ativo a) => new(
        a.Id.Valor, a.Codigo.Valor, a.Nome, a.TipoItem,
        MaquinaDeCicloDeVida.Rotulo(a.Status), a.Criticidade.Chave());
}

/// <summary>Listagem do catálogo da empresa.</summary>
public sealed record ListaDeAtivosResponse(int Total, IReadOnlyList<AtivoResumoResponse> Ativos);

public sealed record EvidenciaResponse(Guid Id, string Tipo, string Titulo, string? Url);

public sealed record ResponsavelResponse(
    Guid IdPessoa, string Papel, DateOnly InicioVigencia, DateOnly? FimVigencia, bool Vigente);

/// <summary>Relação vista a partir deste ativo.</summary>
/// <param name="Direcao">saida (este ativo é a origem) ou entrada (é o destino).</param>
/// <param name="AtivoId">O ativo do outro lado.</param>
/// <param name="Codigo">Código do ativo do outro lado.</param>
/// <param name="Nome">Nome do ativo do outro lado.</param>
/// <param name="Tipo">Tipo da relação.</param>
/// <param name="Criticidade">Quanto a origem depende do destino: baixa, media, alta ou critica.</param>
public sealed record RelacaoResponse(string Direcao, Guid AtivoId, string Codigo, string Nome, string Tipo, string Criticidade);

/// <summary>Ficha completa do ativo (visão 360°).</summary>
public sealed record FichaDoAtivoResponse(
    Guid Id,
    string Codigo,
    string Nome,
    string TipoItem,
    string Status,
    string Criticidade,
    string Descricao,
    int RevisaoAtual,
    IReadOnlyList<string> Trilha,
    IReadOnlyDictionary<string, string> Atributos,
    IReadOnlyList<EvidenciaResponse> Evidencias,
    IReadOnlyList<ResponsavelResponse> Responsaveis,
    IReadOnlyList<RelacaoResponse> Relacoes)
{
    public static FichaDoAtivoResponse De(ObterVisaoDoAtivo.Saida s) => new(
        s.Id.Valor, s.Codigo, s.Nome, s.TipoItem, s.Status, s.Criticidade, s.Descricao,
        s.RevisaoAtual, s.Trilha, s.Atributos,
        s.Evidencias.Select(e => new EvidenciaResponse(e.Id, e.Tipo, e.Titulo, e.Url)).ToArray(),
        s.Responsaveis.Select(r => new ResponsavelResponse(
            r.IdPessoa, r.Papel, r.InicioVigencia, r.FimVigencia, r.Vigente)).ToArray(),
        s.Relacoes.Select(r => new RelacaoResponse(
            r.Direcao, Guid.Parse(r.AtivoId), r.Codigo, r.Nome, r.Tipo, r.Criticidade)).ToArray());
}

/// <summary>Ativo que aponta para o analisado e ainda vale como referência (publicado ou em revisão).</summary>
/// <param name="Id">O consumidor.</param>
/// <param name="Codigo">Código do consumidor.</param>
/// <param name="Nome">Nome do consumidor.</param>
/// <param name="TipoItem">Tipo do consumidor no metamodelo.</param>
/// <param name="TipoRelacao">Como ele depende do analisado (implementa, consome, depende_de...).</param>
/// <param name="Criticidade">Criticidade da dependência: baixa, media, alta ou critica.</param>
public sealed record ConsumidorResponse(
    Guid Id, string Codigo, string Nome, string TipoItem, string TipoRelacao, string Criticidade);

/// <summary>Quem seria afetado se o ativo fosse descontinuado.</summary>
/// <param name="Id">O ativo analisado.</param>
/// <param name="Codigo">Código do ativo analisado: é o que se digita para confirmar a descontinuação.</param>
/// <param name="Total">Quantos consumidores em vigor.</param>
/// <param name="Criticos">Quantos de alta criticidade (alta ou critica). Acima de zero, descontinuar exige a confirmação.</param>
/// <param name="ExigeConfirmacao">Verdadeiro quando há consumidor de alta criticidade.</param>
/// <param name="Consumidores">Os consumidores, mais críticos primeiro.</param>
/// <param name="PorCriticidade">Contagem por criticidade da dependência.</param>
/// <param name="PorTipo">Contagem por tipo do consumidor.</param>
public sealed record ImpactoResponse(
    Guid Id,
    string Codigo,
    int Total,
    int Criticos,
    bool ExigeConfirmacao,
    IReadOnlyList<ConsumidorResponse> Consumidores,
    IReadOnlyDictionary<string, int> PorCriticidade,
    IReadOnlyDictionary<string, int> PorTipo)
{
    public static ImpactoResponse De(AnalisarImpacto.Saida s) => new(
        s.Id.Valor, s.Codigo, s.Total, s.Criticos.Count, s.Criticos.Count > 0,
        s.Consumidores.Select(c => new ConsumidorResponse(
            c.Id.Valor, c.Codigo, c.Nome, c.TipoItem, c.TipoRelacao, c.Criticidade.Chave())).ToArray(),
        s.PorCriticidade, s.PorTipo);
}

/// <summary>Descontinuação aceita.</summary>
/// <param name="Afetados">Consumidores em vigor no momento da descontinuação.</param>
public sealed record DescontinuacaoResponse(int Afetados);

/// <summary>As cinco dimensões do score de qualidade, de 0 a 100.</summary>
public sealed record DimensoesDoScoreResponse(
    int Completude, int Consistencia, int Ownership, int Evidencia, int Temporalidade);

/// <summary>Impedimento de publicação.</summary>
/// <param name="Codigo">Chave estável (ex.: OWNER_NAO_DEFINIDO), para o cliente automatizar a correção.</param>
/// <param name="Mensagem">Texto para pessoas.</param>
public sealed record BloqueioResponse(string Codigo, string Mensagem);

/// <summary>Política de governança que vale para este ativo (tipo × criticidade).</summary>
public sealed record PoliticaResponse(int ScoreMinimo, int EvidenciaMinima, int SlaHoras, IReadOnlyList<string> Etapas);

/// <summary>Passo do caminho até a publicação.</summary>
public sealed record PassoResponse(string Passo, string Rotulo, bool Concluido, IReadOnlyList<string> Impedimentos);

/// <summary>Resultado do pré-check: o que falta para o ativo poder ser submetido.</summary>
public sealed record PreCheckResponse(
    bool Aprovado,
    int Score,
    DimensoesDoScoreResponse Dimensoes,
    IReadOnlyList<BloqueioResponse> Bloqueios,
    IReadOnlyList<string> Alertas,
    PoliticaResponse Politica,
    IReadOnlyList<PassoResponse> Caminho)
{
    public static PreCheckResponse De(AvaliarPreCheck.Saida s) => new(
        s.Checagem.Aprovado,
        s.Score.Total,
        new DimensoesDoScoreResponse(s.Score.Completude, s.Score.Consistencia,
            s.Score.Ownership, s.Score.Evidencia, s.Score.Temporalidade),
        s.Checagem.Bloqueios.Select(b => new BloqueioResponse(b.Codigo.Chave(), b.Mensagem)).ToArray(),
        s.Checagem.Alertas,
        new PoliticaResponse(s.Checagem.Politica.ScoreMinimo, s.Checagem.Politica.EvidenciaMinima,
            s.Checagem.Politica.SlaHoras,
            s.Checagem.Politica.Etapas.Select(e => e.ToString().ToLowerInvariant()).ToArray()),
        s.Caminho.Select(p => new PassoResponse(p.Chave, p.Rotulo, p.Concluido,
            p.Impedimentos.Select(b => b.Codigo.Chave()).ToArray())).ToArray());
}

/// <summary>Submissão aceita: a revisão foi aberta e as etapas exigidas pela política aguardam decisão.</summary>
public sealed record SubmissaoResponse(int Revisao, int Score, IReadOnlyList<string> EtapasAbertas);

/// <summary>Etapa de validação de uma revisão.</summary>
/// <param name="Id">Identificador da validação.</param>
/// <param name="Ativo">Ativo validado.</param>
/// <param name="Revisao">Número da revisão submetida.</param>
/// <param name="Etapa">negocial, tecnica ou arquitetural.</param>
/// <param name="Status">aberta, aprovada, reprovada ou cancelada (outra etapa da mesma revisão foi reprovada).</param>
/// <param name="SubmetidaPor">Quem submeteu a revisão.</param>
/// <param name="DecididaPor">Quem decidiu a etapa, se já decidida.</param>
/// <param name="Motivo">Justificativa da decisão.</param>
public sealed record ValidacaoResponse(
    Guid Id, Guid Ativo, int Revisao, string Etapa, string Status,
    Guid SubmetidaPor, Guid? DecididaPor, string? Motivo)
{
    public static ValidacaoResponse De(ValidacaoEmProcesso v) => new(
        v.Id, v.Ativo.Valor, v.Revisao, v.Etapa.ToString().ToLowerInvariant(),
        v.Status.ToString().ToLowerInvariant(), v.IdSubmissor, v.IdDecisor, v.Motivo);
}

/// <summary>Fila de validações da empresa.</summary>
public sealed record ListaDeValidacoesResponse(int Total, IReadOnlyList<ValidacaoResponse> Validacoes);

/// <summary>Resposta da verificação de vida do processo.</summary>
/// <example>ok</example>
public sealed record SaudeResponse(string Situacao);
