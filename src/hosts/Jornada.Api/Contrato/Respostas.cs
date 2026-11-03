using Jornada.Aplicacao.Acesso;
using Jornada.Aplicacao.Catalogo.Consultas;
using Jornada.Aplicacao.Portas;
using Jornada.Dominio.Acesso;
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

/// <summary>Retrato de uma revisão submetida (ADR-0017).</summary>
/// <param name="Numero">Número da revisão.</param>
/// <param name="Motivo">O motivo informado na submissão.</param>
/// <param name="Autor">Quem submeteu.</param>
/// <param name="CriadaEm">Quando foi submetida.</param>
/// <param name="PublicadaEm">Quando foi publicada; nulo enquanto em validação ou se foi reprovada.</param>
/// <param name="Hash">SHA-256 do retrato, em hexadecimal.</param>
/// <param name="Integra">O hash ainda confere com o conteúdo gravado. Falso indica adulteração.</param>
public sealed record RevisaoResponse(
    int Numero, string Motivo, Guid Autor, DateTimeOffset CriadaEm, DateTimeOffset? PublicadaEm, string Hash, bool Integra);

/// <summary>Uma linha da trilha de auditoria.</summary>
/// <param name="Acao">criar, alterar, definir_responsavel, relacionar, anexar_evidencia, submeter, validar, publicar, abrir_revisao, descontinuar ou arquivar.</param>
/// <param name="Pessoa">Quem agiu.</param>
/// <param name="Em">Quando.</param>
/// <param name="Antes">Valores anteriores, quando a ação altera algo.</param>
/// <param name="Depois">Valores novos ou detalhes da ação.</param>
public sealed record AuditoriaResponse(
    string Acao, Guid Pessoa, DateTimeOffset Em,
    IReadOnlyDictionary<string, string>? Antes, IReadOnlyDictionary<string, string>? Depois);

/// <summary>Histórico do ativo: revisões e auditoria, das mais recentes para as mais antigas.</summary>
public sealed record HistoricoResponse(IReadOnlyList<RevisaoResponse> Revisoes, IReadOnlyList<AuditoriaResponse> Auditoria)
{
    public static HistoricoResponse De(ConsultarHistorico.Saida s) => new(
        s.Revisoes.Select(r => new RevisaoResponse(r.Numero, r.Motivo, r.IdAutor, r.CriadaEm, r.PublicadaEm, r.Hash, r.Integra)).ToArray(),
        s.Auditoria.Select(a => new AuditoriaResponse(a.Acao, a.IdPessoa, a.Em, a.Antes, a.Depois)).ToArray());
}

/// <summary>Um campo que mudou entre as duas revisões.</summary>
/// <param name="Campo">Chave do retrato (nome, descricao, atributos, responsaveis, relacoes...).</param>
/// <param name="Rotulo">Nome do campo para pessoas.</param>
/// <param name="Antes">Valor na revisão a, resumido.</param>
/// <param name="Depois">Valor na revisão b, resumido.</param>
public sealed record DiferencaResponse(string Campo, string Rotulo, string Antes, string Depois);

/// <summary>Revisão a contra revisão b.</summary>
public sealed record ComparacaoResponse(int A, int B, IReadOnlyList<DiferencaResponse> Diferencas)
{
    public static ComparacaoResponse De(CompararRevisoes.Saida s) => new(
        s.A, s.B, s.Diferencas.Select(d => new DiferencaResponse(d.Campo, d.Rotulo, d.Antes, d.Depois)).ToArray());
}

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

/// <summary>Uma pessoa cadastrada.</summary>
/// <param name="Id">Identificador da pessoa: é o que vai no cabeçalho X-Pessoa.</param>
/// <param name="Matricula">Uma letra seguida de seis números.</param>
/// <param name="Nome">Nome completo.</param>
/// <param name="Email">E-mail informado no cadastro.</param>
/// <param name="Unidade">Código de quatro números da unidade.</param>
/// <param name="Ativa">Cadastro inativo não tem papel nenhum.</param>
/// <param name="Papeis">Chaves dos papéis vigentes, em qualquer escopo.</param>
public sealed record PessoaResponse(
    Guid Id, string Matricula, string Nome, string Email, string Unidade, bool Ativa, IReadOnlyList<string> Papeis)
{
    public static PessoaResponse De(Pessoa p, DateOnly hoje) => new(
        p.Id, p.Matricula.Valor, p.Nome, p.Email, p.Unidade.Valor, p.Ativa,
        p.Vigentes(hoje).Select(a => a.Papel.Chave()).Distinct().Order().ToArray());
}

/// <summary>As pessoas ativas da empresa, por nome.</summary>
public sealed record ListaDePessoasResponse(int Total, IReadOnlyList<PessoaResponse> Pessoas);

/// <summary>Um papel que vale agora, como o perfil mostra.</summary>
/// <param name="Papel">admin, curador, arquiteto, techlead, negocio ou consulta.</param>
/// <param name="Rotulo">Nome do papel para pessoas.</param>
/// <param name="EscopoTipo">empresa ou dominio.</param>
/// <param name="EscopoId">Id do domínio, quando o escopo é dominio.</param>
/// <param name="EscopoRotulo">"todo o catálogo" ou "domínio Crédito".</param>
/// <param name="Blocos">Blocos de tipos em que o papel escreve. Vazio: só consulta.</param>
/// <param name="Desde">Início da vigência.</param>
public sealed record PapelResponse(
    string Papel, string Rotulo, string EscopoTipo, Guid? EscopoId, string EscopoRotulo,
    IReadOnlyList<string> Blocos, DateOnly Desde)
{
    public static PapelResponse De(PapelLegivel p) => new(
        p.Atribuicao.Papel.Chave(), p.Atribuicao.Papel.Rotulo(), p.Atribuicao.EscopoTipo.ToString().ToLowerInvariant(),
        p.Atribuicao.EscopoId, p.EscopoRotulo, p.Blocos, p.Atribuicao.InicioVigencia);
}

/// <summary>Um pleito de acesso.</summary>
/// <param name="Id">Identificador do pleito.</param>
/// <param name="Status">pendente, aprovado ou negado.</param>
/// <param name="PapelPleiteado">O papel pedido.</param>
/// <param name="Justificativa">Por que a pessoa pediu.</param>
/// <param name="CriadoEm">Quando pediu.</param>
/// <param name="Pessoa">Quem pediu.</param>
/// <param name="PapelConcedido">O papel concedido, quando aprovado.</param>
/// <param name="EscopoTipo">empresa ou dominio, quando aprovado.</param>
/// <param name="EscopoRotulo">O escopo pelo nome, quando aprovado.</param>
/// <param name="DecididoPor">Quem decidiu. Nulo no primeiro administrador, concedido pela configuração.</param>
/// <param name="DecididoEm">Quando foi decidido.</param>
/// <param name="Resposta">A resposta a quem pediu.</param>
public sealed record PleitoResponse(
    Guid Id, string Status, string PapelPleiteado, string Justificativa, DateTimeOffset CriadoEm, PessoaResponse? Pessoa,
    string? PapelConcedido, string? EscopoTipo, string? EscopoRotulo, Guid? DecididoPor, DateTimeOffset? DecididoEm, string Resposta)
{
    public static PleitoResponse De(PleitoLegivel l, DateOnly hoje) => new(
        l.Pleito.Id, Rotulo(l.Pleito.Status), l.Pleito.PapelPleiteado.Chave(), l.Pleito.Justificativa, l.Pleito.CriadoEm,
        l.Pessoa is { } p ? PessoaResponse.De(p, hoje) : null,
        l.Pleito.PapelConcedido?.Chave(), l.Pleito.EscopoTipo?.ToString().ToLowerInvariant(), l.EscopoRotulo,
        l.Pleito.DecididoPor, l.Pleito.DecididoEm, l.Pleito.Resposta);

    public static string Rotulo(StatusDoPleito s) => s switch
    {
        StatusDoPleito.Aprovado => "aprovado",
        StatusDoPleito.Negado => "negado",
        _ => "pendente",
    };
}

/// <summary>A fila de pleitos, pendentes primeiro e, entre eles, quem espera há mais tempo.</summary>
/// <param name="Total">Pleitos na lista.</param>
/// <param name="Pendentes">Quantos aguardam decisão.</param>
/// <param name="PodeConcederAdmin">Quem consulta pode conceder o papel de admin (só outro admin).</param>
/// <param name="Pleitos">Os pleitos.</param>
public sealed record FilaDePleitosResponse(int Total, int Pendentes, bool PodeConcederAdmin, IReadOnlyList<PleitoResponse> Pleitos)
{
    public static FilaDePleitosResponse De(ListarPleitos.Saida s, DateOnly hoje) => new(
        s.Pleitos.Count, s.Pendentes, s.PodeConcederAdmin, s.Pleitos.Select(p => PleitoResponse.De(p, hoje)).ToArray());
}

/// <summary>Cadastro registrado e pleito aberto.</summary>
/// <param name="IdPessoa">Quem se cadastrou: vai no cabeçalho X-Pessoa daqui em diante.</param>
/// <param name="IdPleito">O pleito aberto.</param>
/// <param name="Status">pendente; aprovado só no primeiro administrador da empresa.</param>
public sealed record PleitoAbertoResponse(Guid IdPessoa, Guid IdPleito, string Status);

/// <summary>
/// Seu perfil: quem você é para a API nesta requisição, e o que pode fazer.
/// </summary>
/// <param name="Identificado">Há pessoa na requisição. Sem pessoa, tudo é consulta.</param>
/// <param name="ModoLeitura">A requisição veio com X-Modo: leitura.</param>
/// <param name="ModoDemonstracao">A API aceita X-Papel no lugar dos papéis concedidos.</param>
/// <param name="Pessoa">O cadastro, quando a pessoa existe.</param>
/// <param name="Papeis">Os papéis que valem agora.</param>
/// <param name="Pleitos">Os pleitos da pessoa, do mais recente ao mais antigo.</param>
/// <param name="BlocosQueEscreve">Blocos de tipos em que a pessoa cadastra agora. Vazio: só consulta.</param>
/// <param name="PodeConceder">Despacha pleitos (curador ou admin).</param>
/// <param name="PodeConcederAdmin">Concede o papel de admin (só admin).</param>
/// <param name="PodeAdministrar">Administra a ferramenta (admin).</param>
public sealed record PerfilResponse(
    bool Identificado, bool ModoLeitura, bool ModoDemonstracao, PessoaResponse? Pessoa,
    IReadOnlyList<PapelResponse> Papeis, IReadOnlyList<PleitoResponse> Pleitos, IReadOnlyList<string> BlocosQueEscreve,
    bool PodeConceder, bool PodeConcederAdmin, bool PodeAdministrar)
{
    public static PerfilResponse De(ConsultarPerfil.Saida s, bool modoDemonstracao, DateOnly hoje) => new(
        s.Ator.Identificado, s.Ator.ModoLeitura, modoDemonstracao,
        s.Pessoa is { } p ? PessoaResponse.De(p, hoje) : null,
        s.Papeis.Select(PapelResponse.De).ToArray(),
        s.Pleitos.Select(l => PleitoResponse.De(l, hoje)).ToArray(),
        s.BlocosQueEscreve.Order().ToArray(),
        s.PodeConceder, s.PodeConcederAdmin, s.PodeAdministrar);
}
