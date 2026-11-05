using Jornada.Aplicacao.Portas;
using Jornada.Dominio.Catalogo;
using Jornada.Dominio.Notificacoes;

namespace Jornada.Persistencia.Cosmos;

/// <summary>
/// A caixa de notificações. O aviso vai no lote da partição do fato (o ativo,
/// ou a de acesso), e por isso entra junto com ele ou não entra. Ler, marcar
/// como lida e marcar o envio usam o mesmo rastreamento dos ativos, com ETag.
/// </summary>
public sealed partial class RepositorioCosmos
{
    private readonly Dictionary<Guid, Rastreado<Notificacao>> _notificacoes = [];

    private static string ParticaoDa(Notificacao n) =>
        n.Ativo is { } ativo ? Mapeamento.Texto(ativo.Valor) : AcessoDoTenant.ParticaoDeAcesso;

    private const string NaoLida = "(NOT IS_DEFINED(c.lidaEm) OR IS_NULL(c.lidaEm))";
    private const string NaoEnviada = "(NOT IS_DEFINED(c.enviadaEm) OR IS_NULL(c.enviadaEm))";

    Task IRepositorioDeNotificacoes.AdicionarAsync(Notificacao notificacao, CancellationToken ct)
    {
        if (notificacao.Empresa != _acesso.Empresa)
            throw new Dominio.Comum.ErroDeDominio("TENANT_INVALIDO", "Tentativa de gravar notificação de outra empresa.");
        _notificacoes[notificacao.Id] = Rastreado<Notificacao>.Novo(notificacao);
        return Task.CompletedTask;
    }

    async Task<Notificacao?> IRepositorioDeNotificacoes.ObterAsync(Guid id, CancellationToken ct)
    {
        if (_notificacoes.TryGetValue(id, out var rastreada)) return rastreada.Entidade;
        var documentos = await _acesso.ConsultarAsync<DocumentoDeNotificacao>(TiposDeDocumento.Notificacao,
            "c.id = @id", parametros: new Dictionary<string, object?> { ["@id"] = Mapeamento.Texto(id) }, ct: ct);
        return documentos.Count == 0 ? null : Rastrear(documentos[0]);
    }

    async Task<bool> IRepositorioDeNotificacoes.ExisteChaveAsync(string chaveUnica, CancellationToken ct) =>
        _notificacoes.Values.Any(n => n.Entidade.ChaveUnica == chaveUnica)
        || await _acesso.ContarAsync(TiposDeDocumento.Notificacao, "c.chaveUnica = @chave",
            new Dictionary<string, object?> { ["@chave"] = chaveUnica }, ct) > 0;

    async Task<IReadOnlyList<Notificacao>> IRepositorioDeNotificacoes.DaPessoaAsync(
        Guid idPessoa, bool apenasNaoLidas, int limite, CancellationToken ct)
    {
        var documentos = await _acesso.ConsultarAsync<DocumentoDeNotificacao>(TiposDeDocumento.Notificacao,
            "c.idPessoa = @pessoa" + (apenasNaoLidas ? $" AND {NaoLida}" : ""),
            $"ORDER BY c.criadaEm DESC OFFSET 0 LIMIT {Math.Max(limite, 0)}",
            new Dictionary<string, object?> { ["@pessoa"] = Mapeamento.Texto(idPessoa) }, ct);
        return documentos.Select(Rastrear)
            .Concat(_notificacoes.Values.Where(n => n.EhNovo).Select(n => n.Entidade).Where(n => n.IdPessoa == idPessoa))
            .Where(n => !apenasNaoLidas || !n.Lida)
            .DistinctBy(n => n.Id)
            .OrderByDescending(n => n.CriadaEm).ThenByDescending(n => n.Id)
            .Take(limite)
            .ToArray();
    }

    Task<int> IRepositorioDeNotificacoes.ContarNaoLidasAsync(Guid idPessoa, CancellationToken ct) =>
        _acesso.ContarAsync(TiposDeDocumento.Notificacao, $"c.idPessoa = @pessoa AND {NaoLida}",
            new Dictionary<string, object?> { ["@pessoa"] = Mapeamento.Texto(idPessoa) }, ct);

    async Task<IReadOnlyList<Notificacao>> IRepositorioDeNotificacoes.PendentesDeEnvioAsync(int limite, CancellationToken ct)
    {
        var documentos = await _acesso.ConsultarAsync<DocumentoDeNotificacao>(TiposDeDocumento.Notificacao,
            NaoEnviada, $"ORDER BY c.criadaEm OFFSET 0 LIMIT {Math.Max(limite, 0)}", ct: ct);
        return documentos.Select(Rastrear).Where(n => n.EnviadaEm is null).ToArray();
    }

    private Notificacao Rastrear(DocumentoDeNotificacao documento)
    {
        var id = Guid.Parse(documento.Id);
        if (_notificacoes.TryGetValue(id, out var existente)) return existente.Entidade;

        var notificacao = ParaDominio(documento);
        _notificacoes[id] = Rastreado<Notificacao>.Lido(notificacao, documento.ETag, Retrato(ParaDocumento(notificacao)));
        return notificacao;
    }

    private void AgendarNotificacoes(Func<string, AcessoDoTenant.Lote> loteNa)
    {
        foreach (var rastreada in _notificacoes.Values)
            Agendar(loteNa(ParticaoDa(rastreada.Entidade)), rastreada, ParaDocumento(rastreada.Entidade));
    }

    private static DocumentoDeNotificacao ParaDocumento(Notificacao n) => new()
    {
        Id = Mapeamento.Texto(n.Id),
        Empresa = Mapeamento.Texto(n.Empresa.Valor),
        IdAtivo = ParticaoDa(n),
        IdPessoa = Mapeamento.Texto(n.IdPessoa),
        TipoNotificacao = n.Tipo,
        Titulo = n.Titulo,
        Corpo = n.Corpo,
        Url = n.Url,
        Ativo = n.Ativo is { } a ? Mapeamento.Texto(a.Valor) : null,
        IdValidacao = n.IdValidacao is { } v ? Mapeamento.Texto(v) : null,
        ChaveUnica = n.ChaveUnica,
        Canal = n.Canal,
        CriadaEm = n.CriadaEm,
        LidaEm = n.LidaEm,
        EnviadaEm = n.EnviadaEm,
    };

    private static Notificacao ParaDominio(DocumentoDeNotificacao d) => Notificacao.Reidratar(
        Guid.Parse(d.Id), new Dominio.Tenancy.IdDeEmpresa(Guid.Parse(d.Empresa)), Guid.Parse(d.IdPessoa), d.TipoNotificacao,
        d.Titulo, d.Corpo, d.Url, d.Ativo is null ? null : new IdDeAtivo(Guid.Parse(d.Ativo)),
        d.IdValidacao is null ? null : Guid.Parse(d.IdValidacao), d.ChaveUnica, d.Canal, d.CriadaEm, d.LidaEm, d.EnviadaEm);

    // ───────────────────────────────────────────── IRepositorioDePreferencias

    private static string IdDasPreferencias(Guid idPessoa) => $"preferencias-{Mapeamento.Texto(idPessoa)}";

    async Task<PreferenciasDeNotificacao> IRepositorioDePreferencias.ObterAsync(Guid idPessoa, CancellationToken ct) =>
        await _acesso.LerAsync<DocumentoDePreferencias>(AcessoDoTenant.ParticaoDeAcesso, IdDasPreferencias(idPessoa), ct) is { } d
            ? new PreferenciasDeNotificacao(idPessoa, d.Ativas.ToHashSet())
            : PreferenciasDeNotificacao.Padrao(idPessoa);

    async Task IRepositorioDePreferencias.SalvarAsync(PreferenciasDeNotificacao preferencias, CancellationToken ct) =>
        await _acesso.GravarAsync(new DocumentoDePreferencias
        {
            Id = IdDasPreferencias(preferencias.IdPessoa),
            Empresa = Mapeamento.Texto(_acesso.Empresa.Valor),
            IdAtivo = AcessoDoTenant.ParticaoDeAcesso,
            IdPessoa = Mapeamento.Texto(preferencias.IdPessoa),
            Ativas = preferencias.Ativas.Order(StringComparer.Ordinal).ToList(),
        }, ct);
}

/// <summary>As empresas da plataforma, para as tarefas de fundo.</summary>
public sealed class EmpresasNoCosmos(BancoDoCatalogo banco) : IEmpresasDaPlataforma
{
    public async Task<IReadOnlyList<Dominio.Tenancy.IdDeEmpresa>> ListarAsync(CancellationToken ct = default) =>
        (await banco.EmpresasAsync(ct)).Select(e => new Dominio.Tenancy.IdDeEmpresa(Guid.Parse(e))).ToArray();
}
