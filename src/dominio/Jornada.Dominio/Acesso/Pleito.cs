using Jornada.Dominio.Comum;
using Jornada.Dominio.Tenancy;

namespace Jornada.Dominio.Acesso;

public enum StatusDoPleito { Pendente, Aprovado, Negado }

/// <summary>
/// O pedido de um papel. Corresponde a <c>solicitacao_acesso</c> e ao rito de
/// <c>acesso.solicitar_acesso</c> / <c>decidir_solicitacao</c> do painel-ddd.
///
/// O ponto do rito: ninguém ganha papel por se cadastrar. O papel só nasce de
/// uma decisão de quem tem atribuição para decidir, registrada com autor, data
/// e resposta a quem pediu.
/// </summary>
public sealed class PleitoDeAcesso
{
    public Guid Id { get; }
    public IdDeEmpresa Empresa { get; }
    public Guid IdPessoa { get; }
    public Papel PapelPleiteado { get; }
    public string Justificativa { get; }
    public DateTimeOffset CriadoEm { get; }

    public StatusDoPleito Status { get; private set; } = StatusDoPleito.Pendente;
    public Guid? DecididoPor { get; private set; }
    public DateTimeOffset? DecididoEm { get; private set; }
    public Papel? PapelConcedido { get; private set; }
    public TipoDeEscopo? EscopoTipo { get; private set; }
    public Guid? EscopoId { get; private set; }
    public string Resposta { get; private set; } = "";

    private PleitoDeAcesso(Guid id, IdDeEmpresa empresa, Guid idPessoa, Papel papel, string justificativa, DateTimeOffset criadoEm)
    {
        Id = id;
        Empresa = empresa;
        IdPessoa = idPessoa;
        PapelPleiteado = papel;
        Justificativa = justificativa;
        CriadoEm = criadoEm;
    }

    public static PleitoDeAcesso Abrir(Pessoa pessoa, Papel papel, string? justificativa, IRelogio relogio) =>
        new(Guid.CreateVersion7(), pessoa.Empresa, pessoa.Id, papel, (justificativa ?? "").Trim(), relogio.Agora);

    /// <summary>
    /// Concede ou nega, sempre com resposta a quem pediu. As verificações vão
    /// na ordem do painel: já decidido, segregação de função, alçada de quem
    /// decide, e por último a resposta.
    ///
    /// O aprovador pode conceder papel diferente do pleiteado (é o caso comum
    /// de quem pede mais do que precisa). Quando isso acontece, ou quando o
    /// pleito é negado, a resposta é obrigatória: a pessoa tem direito de saber
    /// por que o que recebeu não é o que pediu.
    /// </summary>
    /// <returns>A atribuição concedida, ou nulo quando negado.</returns>
    public AtribuicaoDePapel? Decidir(
        Ator decisor, bool aprovar, string? resposta, IRelogio relogio,
        Papel? papelConcedido = null, TipoDeEscopo escopoTipo = TipoDeEscopo.Empresa, Guid? escopoId = null)
    {
        if (Status != StatusDoPleito.Pendente)
            throw new ErroDeDominio("PLEITO_JA_DECIDIDO",
                $"Este pleito já foi {(Status == StatusDoPleito.Aprovado ? "aprovado" : "negado")} em {DecididoEm:dd/MM/yyyy HH:mm}.");
        if (decisor.IdPessoa == IdPessoa)
            throw new ErroDeDominio("SEGREGACAO_DE_FUNCAO", "Segregação de função: ninguém concede acesso a si mesmo.");

        var papel = aprovar ? papelConcedido ?? PapelPleiteado : (Papel?)null;
        var alcada = PoliticaDeAutorizacao.PodeConceder(decisor, papel, relogio.Hoje);
        if (!alcada.Permitido)
            throw new SemPermissao(alcada.Motivo);

        var texto = (resposta ?? "").Trim();
        if (!aprovar && texto.Length == 0)
            throw new ErroDeDominio("RESPOSTA_OBRIGATORIA", "Negar um pleito exige dizer por quê.");
        if (aprovar && papel != PapelPleiteado && texto.Length == 0)
            throw new ErroDeDominio("RESPOSTA_OBRIGATORIA", "Conceder papel diferente do pleiteado exige explicar a troca.");
        if (aprovar && escopoTipo != TipoDeEscopo.Empresa && escopoId is null)
            throw new ErroDeDominio("ESCOPO_SEM_ALVO", $"O escopo {escopoTipo.ToString().ToLowerInvariant()} exige um alvo.");

        Status = aprovar ? StatusDoPleito.Aprovado : StatusDoPleito.Negado;
        DecididoPor = decisor.IdPessoa;
        DecididoEm = relogio.Agora;
        Resposta = texto;
        if (!aprovar) return null;

        PapelConcedido = papel;
        EscopoTipo = escopoTipo;
        EscopoId = escopoTipo == TipoDeEscopo.Empresa ? null : escopoId;
        return new AtribuicaoDePapel(papel!.Value, escopoTipo, EscopoId, relogio.Hoje, ConcedidoPor: decisor.IdPessoa);
    }

    /// <summary>
    /// O primeiro administrador da empresa: sem admin nenhum, não há quem
    /// decida o pleito dele. A configuração responde por essa decisão
    /// (<c>Acesso:AdministradoresIniciais</c>, ADR-0018), e o pleito fica
    /// aprovado sem decisor, com a explicação na resposta.
    /// </summary>
    public AtribuicaoDePapel AprovarPelaConfiguracao(IRelogio relogio)
    {
        if (Status != StatusDoPleito.Pendente)
            throw new ErroDeDominio("PLEITO_JA_DECIDIDO", "Este pleito já foi decidido.");
        Status = StatusDoPleito.Aprovado;
        DecididoEm = relogio.Agora;
        PapelConcedido = Papel.Admin;
        EscopoTipo = TipoDeEscopo.Empresa;
        Resposta = "Primeiro administrador da empresa, concedido pela configuração da plataforma.";
        return AtribuicaoDePapel.NaEmpresa(Papel.Admin, relogio.Hoje);
    }

    /// <summary>Reidratação pela persistência. Não usar em regra de negócio.</summary>
    public static PleitoDeAcesso Reidratar(
        Guid id, IdDeEmpresa empresa, Guid idPessoa, Papel papel, string justificativa, DateTimeOffset criadoEm,
        StatusDoPleito status, Guid? decididoPor, DateTimeOffset? decididoEm, Papel? papelConcedido,
        TipoDeEscopo? escopoTipo, Guid? escopoId, string resposta) =>
        new(id, empresa, idPessoa, papel, justificativa, criadoEm)
        {
            Status = status,
            DecididoPor = decididoPor,
            DecididoEm = decididoEm,
            PapelConcedido = papelConcedido,
            EscopoTipo = escopoTipo,
            EscopoId = escopoId,
            Resposta = resposta,
        };
}
