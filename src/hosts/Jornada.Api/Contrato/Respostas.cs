using Jornada.Aplicacao.Acesso;
using Jornada.Aplicacao.Catalogo.Consultas;
using Jornada.Aplicacao.Catalogo.Descobertas;
using Jornada.Aplicacao.Catalogo.Mapas;
using Jornada.Aplicacao.Governanca;
using Jornada.Aplicacao.Notificacoes;
using Jornada.Aplicacao.Portas;
using Jornada.Dominio.Acesso;
using Jornada.Dominio.Catalogo;
using Jornada.Dominio.Governanca;
using Jornada.Dominio.Notificacoes;

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
/// <param name="Mecanismo">Como a relação acontece (sincrono, assincrono, batch, arquivo, biblioteca); nulo se não informado.</param>
/// <param name="OrigemDaEvidencia">De onde veio a evidência: manual, git, openapi, cmdb ou cicd.</param>
public sealed record RelacaoResponse(string Direcao, Guid AtivoId, string Codigo, string Nome, string Tipo, string Criticidade, string? Mecanismo, string OrigemDaEvidencia);

/// <summary>Um degrau da trilha hierárquica.</summary>
public sealed record AncestralResponse(Guid Id, string Codigo, string Nome, string TipoItem);

/// <summary>Ficha completa do ativo (visão 360°).</summary>
/// <param name="Id">Identificador do ativo.</param>
/// <param name="Codigo">Código gerado pelo tipo (ex.: SIS-0001).</param>
/// <param name="Nome">Nome do ativo.</param>
/// <param name="TipoItem">Chave do tipo no metamodelo.</param>
/// <param name="Status">Estado do ciclo de vida.</param>
/// <param name="Criticidade">baixa, media, alta ou critica.</param>
/// <param name="Descricao">Para que serve o ativo.</param>
/// <param name="RevisaoAtual">Número da revisão corrente.</param>
/// <param name="IdSquad">O squad que cuida do ativo; nulo sem squad.</param>
/// <param name="Trilha">Da raiz até o pai direto, sem o próprio ativo. Vazia num ativo raiz.</param>
/// <param name="Atributos">Campos específicos do tipo.</param>
/// <param name="Evidencias">Evidências anexadas.</param>
/// <param name="Responsaveis">Responsáveis, vigentes e encerrados.</param>
/// <param name="Relacoes">Relações nos dois sentidos.</param>
/// <param name="Filhos">Os filhos diretos na hierarquia, sem os arquivados.</param>
public sealed record FichaDoAtivoResponse(
    Guid Id,
    string Codigo,
    string Nome,
    string TipoItem,
    string Status,
    string Criticidade,
    string Descricao,
    int RevisaoAtual,
    Guid? IdSquad,
    IReadOnlyList<AncestralResponse> Trilha,
    IReadOnlyDictionary<string, string> Atributos,
    IReadOnlyList<EvidenciaResponse> Evidencias,
    IReadOnlyList<ResponsavelResponse> Responsaveis,
    IReadOnlyList<RelacaoResponse> Relacoes,
    IReadOnlyList<AtivoResumoResponse> Filhos)
{
    public static FichaDoAtivoResponse De(ObterVisaoDoAtivo.Saida s) => new(
        s.Id.Valor, s.Codigo, s.Nome, s.TipoItem, s.Status, s.Criticidade, s.Descricao,
        s.RevisaoAtual, s.IdSquad, s.Trilha.Select(a => new AncestralResponse(a.Id.Valor, a.Codigo, a.Nome, a.TipoItem)).ToArray(), s.Atributos,
        s.Evidencias.Select(e => new EvidenciaResponse(e.Id, e.Tipo, e.Titulo, e.Url)).ToArray(),
        s.Responsaveis.Select(r => new ResponsavelResponse(
            r.IdPessoa, r.Papel, r.InicioVigencia, r.FimVigencia, r.Vigente)).ToArray(),
        s.Relacoes.Select(r => new RelacaoResponse(
            r.Direcao, Guid.Parse(r.AtivoId), r.Codigo, r.Nome, r.Tipo, r.Criticidade, r.Mecanismo, r.OrigemDaEvidencia)).ToArray(),
        s.Filhos.Select(AtivoResumoResponse.De).ToArray());
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
/// <param name="ScoreMinimo">Score de qualidade mínimo para publicar.</param>
/// <param name="EvidenciaMinima">Quantas evidências o rito exige.</param>
/// <param name="SlaHoras">Prazo de cada etapa de validação, em horas.</param>
/// <param name="Etapas">As etapas do rito, na ordem.</param>
/// <param name="RevisarACadaDias">De quantos em quantos dias o ativo deve ser revisto.</param>
public sealed record PoliticaResponse(int ScoreMinimo, int EvidenciaMinima, int SlaHoras, IReadOnlyList<string> Etapas, int RevisarACadaDias);

/// <summary>Passo do caminho até a publicação.</summary>
/// <param name="Passo">descrever, responsaveis, conectar, evidenciar ou qualidade.</param>
/// <param name="Rotulo">O passo para pessoas.</param>
/// <param name="Concluido">Nenhum bloqueio do pré-check cai neste passo.</param>
/// <param name="Impedimentos">Códigos dos bloqueios que impedem o passo.</param>
/// <param name="Pendencias">As mensagens desses bloqueios, para a tela.</param>
public sealed record PassoResponse(string Passo, string Rotulo, bool Concluido, IReadOnlyList<string> Impedimentos, IReadOnlyList<string> Pendencias);

/// <summary>Um campo específico do tipo, declarado no metamodelo.</summary>
/// <param name="Nome">Chave do atributo.</param>
/// <param name="Rotulo">O campo para pessoas.</param>
/// <param name="Tipo">texto, area, selecao ou url.</param>
/// <param name="Obrigatorio">Exigido para publicar.</param>
/// <param name="Opcoes">As opções, quando o tipo é selecao.</param>
public sealed record CampoDoTipoResponse(string Nome, string Rotulo, string Tipo, bool Obrigatorio, IReadOnlyList<string> Opcoes);

/// <summary>Um tipo de ativo do metamodelo da empresa.</summary>
/// <param name="Chave">Chave do tipo (dominio, api...).</param>
/// <param name="Rotulo">O tipo para pessoas.</param>
/// <param name="Bloco">Estrutura DDD ou Ativos técnicos.</param>
/// <param name="Pai">A chave do tipo pai, quando o tipo exige hierarquia.</param>
/// <param name="Campos">Os campos específicos do tipo.</param>
/// <param name="ExigeOwnerNegocial">Publicar exige owner negocial vigente.</param>
/// <param name="ExigeOwnerTecnico">Publicar exige owner técnico vigente.</param>
public sealed record TipoDeAtivoResponse(string Chave, string Rotulo, string Bloco, string? Pai, IReadOnlyList<CampoDoTipoResponse> Campos,
    bool ExigeOwnerNegocial, bool ExigeOwnerTecnico);

/// <summary>Um tipo de relação e os tipos de destino que fazem sentido para ele.</summary>
public sealed record TipoDeRelacaoResponse(string Chave, string Rotulo, IReadOnlyList<string> DestinosSugeridos);

/// <summary>Um mecanismo de relação.</summary>
public sealed record MecanismoResponse(string Chave, string Rotulo);

/// <summary>O metamodelo da empresa: tipos de ativo, tipos de relação e mecanismos.</summary>
public sealed record MetamodeloResponse(
    IReadOnlyList<TipoDeAtivoResponse> Tipos, IReadOnlyList<TipoDeRelacaoResponse> Relacoes, IReadOnlyList<MecanismoResponse> Mecanismos);

/// <summary>Relação registrada, ou ignorada porque já existia.</summary>
public sealed record RelacaoRegistradaResponse(bool Criada);

/// <summary>O que o lote fez.</summary>
/// <param name="Criadas">Relações novas.</param>
/// <param name="Ignoradas">O próprio ativo e as que já existiam.</param>
/// <param name="Erros">Destinos recusados, com o motivo.</param>
public sealed record LoteDeRelacoesResponse(int Criadas, int Ignoradas, IReadOnlyList<ErroDoLoteResponse> Erros);

/// <summary>Um destino que o lote recusou.</summary>
public sealed record ErroDoLoteResponse(Guid Destino, string Codigo, string Mensagem);

/// <summary>Resultado do pré-check: o que falta para o ativo poder ser submetido.</summary>
public sealed record PreCheckResponse(
    bool Aprovado,
    int Score,
    DimensoesDoScoreResponse Dimensoes,
    IReadOnlyList<BloqueioResponse> Bloqueios,
    IReadOnlyList<string> Alertas,
    PoliticaResponse Politica,
    IReadOnlyList<PassoResponse> Caminho,
    DateOnly ProximaRevisao)
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
            s.Checagem.Politica.Etapas.Select(e => e.ToString().ToLowerInvariant()).ToArray(),
            s.Checagem.Politica.PeriodicidadeRevisaoDias),
        s.Caminho.Select(p => new PassoResponse(p.Chave, p.Rotulo, p.Concluido,
            p.Impedimentos.Select(b => b.Codigo.Chave()).ToArray(),
            p.Impedimentos.Select(b => b.Mensagem).ToArray())).ToArray(),
        s.ProximaRevisao);
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
/// <param name="AbertaEm">Quando a etapa abriu.</param>
/// <param name="Prazo">Até quando decidir: abertura mais o SLA da política.</param>
/// <param name="AtribuidaA">Quem assumiu a análise; nulo na fila livre.</param>
public sealed record ValidacaoResponse(
    Guid Id, Guid Ativo, int Revisao, string Etapa, string Status,
    Guid SubmetidaPor, Guid? DecididaPor, string? Motivo,
    DateTimeOffset? AbertaEm, DateTimeOffset? Prazo, Guid? AtribuidaA)
{
    public static ValidacaoResponse De(ValidacaoEmProcesso v) => new(
        v.Id, v.Ativo.Valor, v.Revisao, v.Etapa.ToString().ToLowerInvariant(),
        v.Status.ToString().ToLowerInvariant(), v.IdSubmissor, v.IdDecisor, v.Motivo,
        v.AbertaEm, v.Prazo, v.AtribuidaA);
}

/// <summary>Uma etapa aberta na fila, com o ativo e o tempo que falta.</summary>
/// <param name="Validacao">A etapa.</param>
/// <param name="Ativo">O ativo validado.</param>
/// <param name="SlaRestante">"vencido", horas (abaixo de 48) ou dias.</param>
public sealed record ItemDaFilaResponse(ValidacaoResponse Validacao, AtivoResumoResponse Ativo, string SlaRestante)
{
    public static ItemDaFilaResponse De(ItemDaFila i) =>
        new(ValidacaoResponse.De(i.Validacao), AtivoResumoResponse.De(i.Ativo), i.SlaRestante);
}

/// <summary>A fila de validação: a mais crítica primeiro, e, na mesma criticidade, o prazo mais próximo.</summary>
/// <param name="Itens">As etapas abertas, com os filtros pedidos.</param>
/// <param name="Contagem">Etapas abertas por etapa (negocial, tecnica, arquitetural) e as "minhas".</param>
public sealed record FilaDeValidacaoResponse(IReadOnlyList<ItemDaFilaResponse> Itens, IReadOnlyDictionary<string, int> Contagem);

/// <summary>O painel do validador.</summary>
/// <param name="Validacao">A etapa em análise.</param>
/// <param name="Ficha">A ficha do ativo: trilha, responsáveis, evidências, relações.</param>
/// <param name="PreCheck">O pré-check de agora.</param>
/// <param name="Mudancas">O que mudou desde a revisão anterior; nulo na primeira ou sem retrato anterior.</param>
/// <param name="PrimeiraRevisao">É a primeira revisão do ativo: não há o que comparar.</param>
public sealed record PainelDoValidadorResponse(
    ValidacaoResponse Validacao, FichaDoAtivoResponse Ficha, PreCheckResponse PreCheck, ComparacaoResponse? Mudancas, bool PrimeiraRevisao)
{
    public static PainelDoValidadorResponse De(PainelDoValidador.Saida s) => new(
        ValidacaoResponse.De(s.Validacao), FichaDoAtivoResponse.De(s.Ficha), PreCheckResponse.De(s.PreCheck),
        s.Mudancas is { } m ? ComparacaoResponse.De(m) : null, s.PrimeiraRevisao);
}

/// <summary>A minha mesa.</summary>
/// <param name="MinhasValidacoes">Etapas que eu assumi.</param>
/// <param name="FilaLivre">Etapas que ninguém assumiu.</param>
/// <param name="MeusRascunhos">O que cadastrei e ainda está comigo: rascunho ou em revisão, com qualidade e atualização.</param>
/// <param name="AguardandoDecisao">O que cadastrei e está em validação.</param>
public sealed record MesaResponse(
    IReadOnlyList<ItemDaFilaResponse> MinhasValidacoes, IReadOnlyList<ItemDaFilaResponse> FilaLivre,
    IReadOnlyList<ItemDoCatalogoResponse> MeusRascunhos, IReadOnlyList<ItemDoCatalogoResponse> AguardandoDecisao)
{
    public static MesaResponse De(MinhaMesa.Saida s, IReadOnlyList<ItensDoCatalogo.Item> itens, DateOnly hoje)
    {
        var porId = itens.ToDictionary(i => i.Ativo.Id);
        ItemDoCatalogoResponse[] Itens(IEnumerable<Ativo> ativos) => ativos.Select(a => ItemDoCatalogoResponse.De(porId[a.Id], hoje)).ToArray();
        return new(
            s.MinhasValidacoes.Select(ItemDaFilaResponse.De).ToArray(), s.FilaLivre.Select(ItemDaFilaResponse.De).ToArray(),
            Itens(s.MeusRascunhos), Itens(s.AguardandoDecisao));
    }
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
/// <param name="IdSquad">O squad da pessoa, quando tem.</param>
public sealed record PessoaResponse(
    Guid Id, string Matricula, string Nome, string Email, string Unidade, bool Ativa, IReadOnlyList<string> Papeis, Guid? IdSquad)
{
    public static PessoaResponse De(Pessoa p, DateOnly hoje) => new(
        p.Id, p.Matricula.Valor, p.Nome, p.Email, p.Unidade.Valor, p.Ativa,
        p.Vigentes(hoje).Select(a => a.Papel.Chave()).Distinct().Order().ToArray(), p.IdSquad);
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

/// <summary>Um squad.</summary>
/// <param name="Id">Identificador do squad.</param>
/// <param name="Sigla">Única na empresa, em maiúsculas.</param>
/// <param name="Titulo">Nome do squad.</param>
/// <param name="Descricao">Do que o squad cuida.</param>
/// <param name="Ativa">Inativo não aparece para novos vínculos; os existentes ficam.</param>
public sealed record SquadResponse(Guid Id, string Sigla, string Titulo, string Descricao, bool Ativa)
{
    public static SquadResponse De(Squad s) => new(s.Id, s.Sigla, s.Titulo, s.Descricao, s.Ativa);
}

/// <summary>Os squads da empresa, ativos primeiro.</summary>
public sealed record ListaDeSquadsResponse(int Total, IReadOnlyList<SquadResponse> Squads);

/// <summary>Um cadastro na gestão de acessos.</summary>
/// <param name="Pessoa">O cadastro.</param>
/// <param name="Squad">O squad da pessoa, quando tem.</param>
/// <param name="Papeis">Os papéis vigentes, com escopo e blocos.</param>
public sealed record CadastroResponse(PessoaResponse Pessoa, SquadResponse? Squad, IReadOnlyList<PapelResponse> Papeis)
{
    public static CadastroResponse De(CadastroLegivel c, DateOnly hoje) => new(
        PessoaResponse.De(c.Pessoa, hoje), c.Squad is { } s ? SquadResponse.De(s) : null,
        c.Papeis.Select(PapelResponse.De).ToArray());
}

/// <summary>Os cadastros da empresa, ativos primeiro, por nome.</summary>
public sealed record ListaDeCadastrosResponse(int Total, IReadOnlyList<CadastroResponse> Cadastros);

/// <summary>O que amarra o cadastro ao histórico.</summary>
/// <param name="PodeExcluir">Nenhum vínculo: o cadastro pode ser excluído. Com vínculo, inativa-se.</param>
/// <param name="Vinculos">"papéis atribuídos", "pleitos de acesso", "responsabilidades em ativos"...</param>
public sealed record VinculosResponse(bool PodeExcluir, IReadOnlyList<string> Vinculos);

/// <summary>Quantos cadastros foram inativados.</summary>
public sealed record InativacaoEmLoteResponse(int Inativados);

/// <summary>Uma linha da matriz "quem pode fazer o quê".</summary>
/// <param name="Acao">A ação, em minúsculas (cadastrar, decidir, conceder...).</param>
/// <param name="Rotulo">A ação para pessoas.</param>
/// <param name="Papeis">Papel → pode. Lido da política de autorização, não escrito à mão.</param>
public sealed record LinhaDaMatrizResponse(string Acao, string Rotulo, IReadOnlyDictionary<string, bool> Papeis);

/// <summary>O rito de validação de um tipo (e criticidade).</summary>
/// <param name="TipoItem">Chave do tipo.</param>
/// <param name="Rotulo">Nome do tipo.</param>
/// <param name="Criticidade">"*" vale para qualquer criticidade.</param>
/// <param name="Etapas">Etapas de validação, na ordem.</param>
/// <param name="EvidenciaMinima">Evidências exigidas para publicar.</param>
/// <param name="ScoreMinimo">Score de qualidade mínimo, de 0 a 100.</param>
/// <param name="SlaHoras">Prazo de cada etapa.</param>
/// <param name="RevisarACadaDias">Periodicidade de revisão do ativo publicado.</param>
public sealed record RitoResponse(
    string TipoItem, string Rotulo, string Criticidade, IReadOnlyList<string> Etapas,
    int EvidenciaMinima, int ScoreMinimo, int SlaHoras, int RevisarACadaDias);

/// <summary>As tabelas de referência da cartilha, lidas do código.</summary>
/// <param name="Papeis">As colunas da matriz, na ordem da tela.</param>
/// <param name="Matriz">Quem pode fazer o quê.</param>
/// <param name="EtapasPorPapel">Papel → etapas de validação que ele decide.</param>
/// <param name="BlocosPorPapel">Papel → blocos de tipos em que escreve.</param>
/// <param name="Ritos">O rito de cada tipo de ativo.</param>
/// <param name="MatrizDeMudanca">Por tipo de mudança, se cada etapa é obrigatoria, condicional, opcional ou nao.</param>
public sealed record CartilhaResponse(
    IReadOnlyList<string> Papeis, IReadOnlyList<LinhaDaMatrizResponse> Matriz,
    IReadOnlyDictionary<string, IReadOnlyList<string>> EtapasPorPapel,
    IReadOnlyDictionary<string, IReadOnlyList<string>> BlocosPorPapel,
    IReadOnlyList<RitoResponse> Ritos,
    IReadOnlyList<MudancaResponse> MatrizDeMudanca)
{
    public static CartilhaResponse De(ConsultarCartilha.Saida s) => new(
        ConsultarCartilha.OrdemDosPapeis.Select(p => p.Chave()).ToArray(),
        s.Matriz.Select(l => new LinhaDaMatrizResponse(l.Acao.ToString().ToLowerInvariant(), l.Rotulo,
            l.Papeis.ToDictionary(kv => kv.Key.Chave(), kv => kv.Value))).ToArray(),
        s.EtapasPorPapel.ToDictionary(kv => kv.Key.Chave(),
            kv => (IReadOnlyList<string>)kv.Value.Select(e => e.ToString().ToLowerInvariant()).ToArray()),
        s.BlocosPorPapel.ToDictionary(kv => kv.Key.Chave(), kv => kv.Value),
        s.Ritos.Select(r => new RitoResponse(r.TipoItem, r.Rotulo, r.Politica.Criticidade,
            r.Politica.Etapas.Select(e => e.ToString().ToLowerInvariant()).ToArray(),
            r.Politica.EvidenciaMinima, r.Politica.ScoreMinimo, r.Politica.SlaHoras, r.Politica.PeriodicidadeRevisaoDias)).ToArray(),
        s.MatrizDeMudanca.Select(l => new MudancaResponse(l.Mudanca, l.Rotulo, l.Negocial, l.Tecnica, l.Arquitetural)).ToArray());
}

/// <summary>Uma linha da matriz mínima de aprovação por tipo de mudança.</summary>
/// <param name="Mudanca">texto, escopo, contrato, dependencia ou descontinuacao.</param>
/// <param name="Rotulo">A mudança, por extenso.</param>
/// <param name="Negocial">obrigatoria, condicional, opcional ou nao.</param>
/// <param name="Tecnica">obrigatoria, condicional, opcional ou nao.</param>
/// <param name="Arquitetural">obrigatoria, condicional, opcional ou nao.</param>
public sealed record MudancaResponse(string Mudanca, string Rotulo, string Negocial, string Tecnica, string Arquitetural);

/// <summary>Um ativo na lista do catálogo.</summary>
/// <param name="Ativo">Código, nome, tipo, status e criticidade.</param>
/// <param name="Origem">manual ou automatica (descoberta automática).</param>
/// <param name="IdSquad">O squad que cuida do ativo.</param>
/// <param name="TemResponsavel">Há responsável vigente hoje.</param>
/// <param name="AtualizadoEm">A última alteração.</param>
/// <param name="Pai">O ativo pai na hierarquia; nulo num ativo raiz.</param>
/// <param name="Score">O score de qualidade, de 0 a 100, com a mesma conta do pré-check.</param>
/// <param name="CriadoPor">Quem cadastrou (ou importou) o ativo; nulo na carga.</param>
public sealed record ItemDoCatalogoResponse(
    AtivoResumoResponse Ativo, string Origem, Guid? IdSquad, bool TemResponsavel, DateTimeOffset AtualizadoEm,
    AtivoResumoResponse? Pai, int? Score, Guid? CriadoPor)
{
    public static ItemDoCatalogoResponse De(ItensDoCatalogo.Item i, DateOnly hoje) => new(
        AtivoResumoResponse.De(i.Ativo), i.Ativo.Origem == OrigemDoAtivo.Automatica ? "automatica" : "manual", i.Ativo.IdSquad,
        i.Ativo.Responsaveis.Any(r => r.Vigente(hoje)), i.Ativo.AtualizadoEm,
        i.Pai is null ? null : AtivoResumoResponse.De(i.Pai), i.Score, i.Ativo.CriadoPor);
}

/// <summary>Uma página do catálogo, com o total real do recorte.</summary>
/// <param name="Itens">Os ativos desta página.</param>
/// <param name="Total">Ativos no recorte inteiro, não só nesta página.</param>
/// <param name="Pagina">A página devolvida; fora do intervalo, a última.</param>
/// <param name="Paginas">Quantas páginas o recorte tem.</param>
/// <param name="PorPagina">Tamanho da página.</param>
/// <param name="Primeiro">Posição do primeiro item da página (1 em diante; 0 sem itens).</param>
/// <param name="Ultimo">Posição do último item da página.</param>
/// <param name="Ordem">A ordenação aplicada: tipo, nome, status, criticidade, atualizado ou codigo. Desconhecida vira tipo.</param>
/// <param name="Descendente">Ordem invertida.</param>
/// <param name="Truncado">O recorte passou do teto de leitura; refine os filtros.</param>
public sealed record PaginaDoCatalogoResponse(
    IReadOnlyList<ItemDoCatalogoResponse> Itens, int Total, int Pagina, int Paginas, int PorPagina, int Primeiro, int Ultimo,
    string Ordem, bool Descendente, bool Truncado)
{
    public static PaginaDoCatalogoResponse De(BuscarNoCatalogo.Saida s, IReadOnlyList<ItensDoCatalogo.Item> itens, DateOnly hoje) => new(
        itens.Select(i => ItemDoCatalogoResponse.De(i, hoje)).ToArray(), s.Total, s.Pagina, s.Paginas, s.PorPagina,
        s.Primeiro, s.Ultimo, s.Ordem, s.Descendente, s.Truncado);
}

/// <summary>Uma sugestão da busca do topo.</summary>
/// <param name="Id">O ativo.</param>
/// <param name="Codigo">Código do ativo.</param>
/// <param name="Nome">Nome do ativo.</param>
/// <param name="Tipo">Chave do tipo.</param>
/// <param name="Status">Estado do ciclo de vida.</param>
/// <param name="Url">O caminho da página do ativo na interface.</param>
public sealed record SugestaoResponse(Guid Id, string Codigo, string Nome, string Tipo, string Status, string Url);

/// <summary>As sugestões para o termo: até 8, nenhuma com menos de 2 letras.</summary>
public sealed record SugestoesResponse(string Termo, IReadOnlyList<SugestaoResponse> Itens);

/// <summary>Um nó da vizinhança.</summary>
/// <param name="Ativo">O ativo.</param>
/// <param name="Salto">A distância até o centro, em relações (0 é o centro).</param>
public sealed record NoDaVizinhancaResponse(AtivoResumoResponse Ativo, int Salto);

/// <summary>Uma aresta da vizinhança, na direção real.</summary>
/// <param name="Origem">Quem aponta.</param>
/// <param name="Destino">Para quem aponta.</param>
/// <param name="Tipo">implementa, expoe, consome, produz, depende_de ou persiste_em; no grafo do catálogo, também hierarquia (pai → filho).</param>
/// <param name="Criticidade">A criticidade da dependência.</param>
/// <param name="Mecanismo">Como a relação acontece, quando informado.</param>
public sealed record ArestaResponse(Guid Origem, Guid Destino, string Tipo, string Criticidade, string? Mecanismo);

/// <summary>A vizinhança de um ativo no grafo de relações.</summary>
/// <param name="Centro">O ativo pedido.</param>
/// <param name="Saltos">A profundidade aplicada (1 a 3).</param>
/// <param name="Nos">Os ativos alcançados, do centro para fora.</param>
/// <param name="Arestas">As relações entre os nós devolvidos.</param>
/// <param name="Truncado">A expansão parou no teto: há mais ativos do que os mostrados.</param>
/// <param name="Teto">O máximo de nós.</param>
public sealed record VizinhancaResponse(Guid Centro, int Saltos, IReadOnlyList<NoDaVizinhancaResponse> Nos, IReadOnlyList<ArestaResponse> Arestas, bool Truncado, int Teto)
{
    public static VizinhancaResponse De(ConsultarVizinhanca.Saida s) => new(
        s.Centro.Valor, s.Saltos,
        s.Nos.Select(n => new NoDaVizinhancaResponse(AtivoResumoResponse.De(n.Ativo), n.Salto)).ToArray(),
        s.Arestas.Select(a => new ArestaResponse(a.Origem.Valor, a.Destino.Valor, a.Tipo, a.Criticidade.Chave(), a.Mecanismo)).ToArray(),
        s.Truncado, s.Teto);
}

/// <summary>Um nó da árvore do catálogo.</summary>
/// <param name="Ativo">O ativo.</param>
/// <param name="Implementacoes">Quantos ativos implementam esta capacidade (0 nos demais tipos).</param>
/// <param name="Filhos">Os filhos, por nome.</param>
public sealed record NoDaArvoreResponse(AtivoResumoResponse Ativo, int Implementacoes, IReadOnlyList<NoDaArvoreResponse> Filhos)
{
    public static NoDaArvoreResponse De(MontarArvore.No n) =>
        new(AtivoResumoResponse.De(n.Ativo), n.Implementacoes, n.Filhos.Select(De).ToArray());
}

/// <summary>Uma árvore do mapa.</summary>
/// <param name="Visao">negocio (a partir do domínio) ou tecnica (a partir do sistema).</param>
/// <param name="Raizes">Os nós de raiz, cada um com os descendentes.</param>
public sealed record ArvoreResponse(string Visao, IReadOnlyList<NoDaArvoreResponse> Raizes);

/// <summary>A fotografia dos indicadores numa competência.</summary>
/// <param name="Competencia">O mês, AAAA-MM.</param>
/// <param name="Valores">Indicador → valor naquele mês.</param>
/// <param name="GeradaEm">Quando a fotografia foi tirada.</param>
public sealed record FotografiaResponse(string Competencia, IReadOnlyDictionary<string, double> Valores, DateTimeOffset GeradaEm)
{
    public static FotografiaResponse De(FotografiaDosIndicadores f) => new(f.Competencia, f.Valores, f.GeradaEm);
}

/// <summary>A cobertura de capacidades de um domínio.</summary>
/// <param name="Id">O domínio.</param>
/// <param name="Nome">O nome do domínio.</param>
/// <param name="Total">Capacidades abaixo dele, em qualquer nível.</param>
/// <param name="Cobertas">Quantas têm pelo menos um ativo que as implementa.</param>
/// <param name="Percentual">Cobertas sobre o total, de 0 a 100.</param>
public sealed record CoberturaDoDominioResponse(Guid Id, string Nome, int Total, int Cobertas, int Percentual);

/// <summary>Uma pendência prioritária e o recorte que a explica.</summary>
/// <param name="Chave">capacidade_sem_owner, api_sem_capacidade, repositorio_sem_mantenedor, sla_vencido ou dependencia_critica_sem_evidencia.</param>
/// <param name="Titulo">O que a tela mostra.</param>
/// <param name="Total">Quantos casos há agora.</param>
/// <param name="TipoItem">O tipo que filtra o catálogo, quando a pendência é de ativos.</param>
/// <param name="SemResponsavel">O recorte também filtra os ativos sem responsável vigente.</param>
/// <param name="Tela">Outra tela que explica a pendência (validacoes), quando não é o catálogo.</param>
public sealed record PendenciaPrioritariaResponse(string Chave, string Titulo, int Total, string? TipoItem, bool SemResponsavel, string? Tela);

/// <summary>Os indicadores da visão executiva.</summary>
/// <param name="Atuais">Indicador → valor agora. Cobertura, APIs governadas e qualidade média são porcentagens.</param>
/// <param name="Variacao">Indicador → diferença entre as duas últimas fotografias; ausente com menos de duas.</param>
/// <param name="QuantoMenosMelhor">Os indicadores em que cair é melhora (sem responsável, pendências, SLA vencido, dependências críticas).</param>
/// <param name="Serie">As fotografias, da mais antiga para a mais recente.</param>
/// <param name="CoberturaPorDominio">Por domínio, as capacidades e quantas têm implementação.</param>
/// <param name="Pendencias">As pendências prioritárias, na ordem do painel.</param>
public sealed record IndicadoresResponse(
    IReadOnlyDictionary<string, double> Atuais, IReadOnlyDictionary<string, double> Variacao,
    IReadOnlyList<string> QuantoMenosMelhor, IReadOnlyList<FotografiaResponse> Serie,
    IReadOnlyList<CoberturaDoDominioResponse> CoberturaPorDominio, IReadOnlyList<PendenciaPrioritariaResponse> Pendencias)
{
    public static IndicadoresResponse De(Indicadores.Saida s) => new(
        s.Atuais, s.Variacao, Indicadores.QuantoMenosMelhor.Order().ToArray(), s.Serie.Select(FotografiaResponse.De).ToArray(),
        s.CoberturaPorDominio.Select(c => new CoberturaDoDominioResponse(c.Id.Valor, c.Nome, c.Total, c.Cobertas, c.Percentual)).ToArray(),
        s.Pendencias.Select(p => new PendenciaPrioritariaResponse(p.Chave, p.Titulo, p.Total, p.TipoItem, p.SemResponsavel, p.Tela)).ToArray());
}

/// <summary>Um domínio ou subdomínio oferecido como recorte do grafo.</summary>
/// <param name="Id">O ativo.</param>
/// <param name="Nome">O nome.</param>
/// <param name="IdPai">O domínio do subdomínio; nulo no domínio.</param>
public sealed record OpcaoDeRecorteResponse(Guid Id, string Nome, Guid? IdPai);

/// <summary>O grafo do catálogo, inteiro ou recortado pela hierarquia.</summary>
/// <param name="Nos">Os ativos, por tipo e nome.</param>
/// <param name="Arestas">Pai → filho (tipo hierarquia) e, sem recorte, as relações entre os nós.</param>
/// <param name="Filtrado">Há recorte: só a hierarquia é desenhada.</param>
/// <param name="Truncado">O catálogo passou do teto e o grafo foi cortado.</param>
/// <param name="Teto">O máximo de nós.</param>
/// <param name="Dominios">Os domínios, para o seletor.</param>
/// <param name="Subdominios">Todos os subdomínios, cada um com o seu domínio em idPai.</param>
public sealed record GrafoDoCatalogoResponse(
    IReadOnlyList<AtivoResumoResponse> Nos, IReadOnlyList<ArestaResponse> Arestas, bool Filtrado, bool Truncado, int Teto,
    IReadOnlyList<OpcaoDeRecorteResponse> Dominios, IReadOnlyList<OpcaoDeRecorteResponse> Subdominios)
{
    public static GrafoDoCatalogoResponse De(MontarGrafoDoCatalogo.Saida s) => new(
        s.Nos.Select(AtivoResumoResponse.De).ToArray(),
        s.Arestas.Select(a => new ArestaResponse(a.Origem.Valor, a.Destino.Valor, a.Tipo, a.Criticidade.Chave(), a.Mecanismo)).ToArray(),
        s.Filtrado, s.Truncado, s.Teto,
        s.Dominios.Select(d => new OpcaoDeRecorteResponse(d.Id.Valor, d.Nome, null)).ToArray(),
        s.Subdominios.Select(d => new OpcaoDeRecorteResponse(d.Id.Valor, d.Nome, d.IdPai?.Valor)).ToArray());
}

/// <summary>Um aviso da caixa.</summary>
/// <param name="Id">Identificador do aviso.</param>
/// <param name="Tipo">revisao_submetida, sla_vencendo, sla_vencido, revisao_rejeitada, ativo_publicado, acesso_solicitado ou acesso_decidido.</param>
/// <param name="Rotulo">O tipo, por extenso.</param>
/// <param name="Urgente">Se o tipo pede ação.</param>
/// <param name="Titulo">A primeira linha do aviso.</param>
/// <param name="Corpo">O detalhe: o parecer, o prazo, a justificativa.</param>
/// <param name="Url">Para onde o aviso leva, na interface.</param>
/// <param name="IdAtivo">O ativo do aviso, se houver.</param>
/// <param name="CriadaEm">Quando o aviso foi gerado.</param>
/// <param name="LidaEm">Quando foi lido; nulo enquanto não.</param>
public sealed record NotificacaoResponse(
    Guid Id, string Tipo, string Rotulo, bool Urgente, string Titulo, string Corpo, string? Url,
    Guid? IdAtivo, DateTimeOffset CriadaEm, DateTimeOffset? LidaEm)
{
    public static NotificacaoResponse De(Notificacao n)
    {
        var tipo = TiposDeNotificacao.Obter(n.Tipo);
        return new(n.Id, n.Tipo, tipo.Rotulo, tipo.Urgente, n.Titulo, n.Corpo, n.Url, n.Ativo?.Valor, n.CriadaEm, n.LidaEm);
    }
}

/// <summary>A caixa de avisos.</summary>
/// <param name="NaoLidas">Quantos avisos ainda não lidos (a bolha do sino).</param>
/// <param name="Itens">Os avisos, do mais recente para o mais antigo.</param>
public sealed record CaixaResponse(int NaoLidas, IReadOnlyList<NotificacaoResponse> Itens);

/// <summary>A bolha do sino.</summary>
/// <param name="NaoLidas">Avisos não lidos; zero sem pessoa identificada.</param>
public sealed record ContagemDeNotificacoesResponse(int NaoLidas);

/// <summary>Quantos avisos foram marcados como lidos.</summary>
/// <param name="Marcadas">Quantos mudaram agora.</param>
public sealed record LeituraResponse(int Marcadas);

/// <summary>Um tipo de aviso e os canais em que a pessoa o recebe.</summary>
/// <param name="Tipo">A chave do tipo.</param>
/// <param name="Rotulo">O tipo, por extenso.</param>
/// <param name="Urgente">Se o tipo pede ação.</param>
/// <param name="Canais">Canal → ligado.</param>
public sealed record PreferenciaDoTipoResponse(string Tipo, string Rotulo, bool Urgente, IReadOnlyDictionary<string, bool> Canais);

/// <summary>Um canal de entrega.</summary>
/// <param name="Chave">app, email ou teams.</param>
/// <param name="Rotulo">O canal, por extenso.</param>
/// <param name="Disponivel">Se já entrega. Hoje só o app.</param>
public sealed record CanalResponse(string Chave, string Rotulo, bool Disponivel);

/// <summary>As preferências de notificação.</summary>
/// <param name="Canais">Os canais, e quais já entregam.</param>
/// <param name="Tipos">Cada tipo de aviso e os canais ligados.</param>
public sealed record PreferenciasResponse(IReadOnlyList<CanalResponse> Canais, IReadOnlyList<PreferenciaDoTipoResponse> Tipos)
{
    public static PreferenciasResponse De(PreferenciasDeNotificacao p) => new(
        CanaisDeNotificacao.Todos.Select(c => new CanalResponse(c.Chave, c.Rotulo, CanaisDeNotificacao.Disponiveis.Contains(c.Chave))).ToArray(),
        TiposDeNotificacao.Todos.Select(t => new PreferenciaDoTipoResponse(t.Chave, t.Rotulo, t.Urgente,
            CanaisDeNotificacao.Todos.ToDictionary(c => c.Chave, c => p.QuerReceber(t.Chave, c.Chave)))).ToArray());
}

/// <summary>O resultado de um despacho da outbox.</summary>
/// <param name="Despachadas">Quantas saíram agora.</param>
/// <param name="PorCanal">Canal → quantas.</param>
/// <param name="SemCanalDisponivel">Quantas esperam um canal que ainda não entrega.</param>
public sealed record DespachoResponse(int Despachadas, IReadOnlyDictionary<string, int> PorCanal, int SemCanalDisponivel)
{
    public static DespachoResponse De(DespacharNotificacoes.Saida s) => new(s.Despachadas, s.PorCanal, s.SemCanalDisponivel);
}

/// <summary>O resultado de uma rodada do vigia de SLA.</summary>
/// <param name="SlaVencendo">Avisos de prazo vencendo em menos de 24h.</param>
/// <param name="SlaVencido">Avisos de prazo vencido.</param>
public sealed record VigiaDeSlaResponse(int SlaVencendo, int SlaVencido);

/// <summary>Uma fonte de descoberta.</summary>
/// <param name="Chave">openapi ou git.</param>
/// <param name="Rotulo">A fonte, por extenso.</param>
/// <param name="Ajuda">O formato esperado.</param>
/// <param name="TipoDoPai">O tipo do ativo que recebe o que for descoberto.</param>
public sealed record FonteDeDescobertaResponse(string Chave, string Rotulo, string Ajuda, string TipoDoPai);

/// <summary>A tela de descobertas: as fontes e a bandeja.</summary>
/// <param name="Fontes">De onde se importa.</param>
/// <param name="Bandeja">O que a importação trouxe e ninguém triou: origem automática, em rascunho, o mais recente primeiro.</param>
public sealed record DescobertasResponse(IReadOnlyList<FonteDeDescobertaResponse> Fontes, IReadOnlyList<ItemDoCatalogoResponse> Bandeja);

/// <summary>Uma linha da prévia.</summary>
/// <param name="Acao">criar, manter (já existe) ou atualizar (repositório existente recebe os atributos).</param>
/// <param name="Tipo">O tipo, por extenso.</param>
/// <param name="Nome">O nome que o ativo terá.</param>
/// <param name="Detalhe">Versão, resumo da operação ou linguagem.</param>
public sealed record AcaoDoPlanoResponse(string Acao, string Tipo, string Nome, string Detalhe);

/// <summary>A prévia: o que a importação faria. Nada foi gravado.</summary>
/// <param name="Fonte">openapi ou git.</param>
/// <param name="Titulo">O título do contrato, ou quantos repositórios.</param>
/// <param name="Versao">A versão do contrato; vazia no inventário Git.</param>
/// <param name="Criar">Quantos ativos seriam criados.</param>
/// <param name="Manter">Quantos já existem.</param>
/// <param name="Acoes">Uma linha por ativo.</param>
/// <param name="IdPai">A aplicação escolhida.</param>
public sealed record PlanoDeImportacaoResponse(string Fonte, string Titulo, string Versao, int Criar, int Manter, IReadOnlyList<AcaoDoPlanoResponse> Acoes, Guid? IdPai)
{
    public static PlanoDeImportacaoResponse De(ImportarDescobertas.Plano p) => new(
        p.Fonte, p.Titulo, p.Versao, p.Criar, p.Manter, p.Acoes.Select(a => new AcaoDoPlanoResponse(a.Acao, a.Tipo, a.Nome, a.Detalhe)).ToArray(), p.IdPai?.Valor);
}

/// <summary>O resultado da importação.</summary>
/// <param name="Criados">Ativos novos, em rascunho, com origem automática: estão na bandeja.</param>
/// <param name="Atualizados">Repositórios existentes que receberam os atributos novos.</param>
/// <param name="Mantidos">Os que já existiam e ficaram como estavam.</param>
/// <param name="Novos">Os ids dos criados.</param>
public sealed record ImportacaoResponse(int Criados, int Atualizados, int Mantidos, IReadOnlyList<Guid> Novos)
{
    public static ImportacaoResponse De(ImportarDescobertas.Saida s) => new(s.Criados, s.Atualizados, s.Mantidos, s.Novos.Select(n => n.Valor).ToArray());
}

/// <summary>O resultado da triagem.</summary>
/// <param name="Acao">aceitar ou descartar.</param>
/// <param name="Tratados">Quantos saíram da bandeja.</param>
/// <param name="Ignorados">Os que não eram descoberta pendente (cadastro humano, já triado) ou que a pessoa não pode triar.</param>
public sealed record TriagemResponse(string Acao, int Tratados, int Ignorados);
