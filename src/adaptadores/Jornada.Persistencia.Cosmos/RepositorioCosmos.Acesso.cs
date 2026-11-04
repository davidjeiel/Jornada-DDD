using Jornada.Aplicacao.Portas;
using Jornada.Dominio.Acesso;
using Jornada.Dominio.Comum;

namespace Jornada.Persistencia.Cosmos;

/// <summary>
/// Pessoas e pleitos, na partição <c>[empresa, "acesso"]</c> (ADR-0018). Mesmo
/// rastreamento dos ativos: o que muda vai no lote da partição de acesso, com
/// ETag. Decidir um pleito grava o pleito e a pessoa com o papel novo juntos.
///
/// As portas repetem a assinatura <c>ObterAsync(Guid)</c> da de validações,
/// por isso a implementação é explícita.
/// </summary>
public sealed partial class RepositorioCosmos : IRepositorioDePessoas, IRepositorioDePleitos, IRepositorioDeSquads, IConsultaDeVinculos
{
    private readonly Dictionary<Guid, Rastreado<Pessoa>> _pessoas = [];
    private readonly Dictionary<Guid, Rastreado<PleitoDeAcesso>> _pleitos = [];
    private readonly Dictionary<Guid, Rastreado<Squad>> _squads = [];

    /// <summary>A sigla de cada squad como está no banco: mudar a sigla troca a reserva no mesmo lote.</summary>
    private readonly Dictionary<Guid, string> _siglasGravadas = [];

    private readonly List<Pessoa> _pessoasRemovidas = [];

    // ───────────────────────────────────────────────── IRepositorioDePessoas

    async Task<Pessoa?> IRepositorioDePessoas.ObterAsync(Guid id, CancellationToken ct)
    {
        if (_pessoas.TryGetValue(id, out var rastreada)) return rastreada.Entidade;
        var documento = await _acesso.LerAsync<DocumentoDePessoa>(AcessoDoTenant.ParticaoDeAcesso, Mapeamento.Texto(id), ct);
        return documento is null ? null : Rastrear(documento);
    }

    async Task<Pessoa?> IRepositorioDePessoas.ObterPorMatriculaAsync(Matricula matricula, CancellationToken ct)
    {
        var nova = _pessoas.Values.FirstOrDefault(p => p.EhNovo && p.Entidade.Matricula == matricula);
        if (nova is not null) return nova.Entidade;

        var reserva = await _acesso.LerAsync<DocumentoDeMatricula>(AcessoDoTenant.ParticaoDeAcesso, Mapeamento.IdDaMatricula(matricula), ct);
        return reserva is null ? null : await ((IRepositorioDePessoas)this).ObterAsync(Guid.Parse(reserva.IdPessoa), ct);
    }

    async Task<IReadOnlyList<Pessoa>> IRepositorioDePessoas.ListarAsync(bool incluirInativas, CancellationToken ct)
    {
        var documentos = await _acesso.ConsultarAsync<DocumentoDePessoa>(TiposDeDocumento.Pessoa,
            incluirInativas ? "true" : "c.ativa = true", ct: ct);
        return documentos.Select(Rastrear)
            .Concat(_pessoas.Values.Where(p => p.EhNovo).Select(p => p.Entidade))
            .Where(p => incluirInativas || p.Ativa)
            .DistinctBy(p => p.Id)
            .OrderBy(p => p.Nome, StringComparer.CurrentCulture)
            .ToArray();
    }

    Task IRepositorioDePessoas.AdicionarAsync(Pessoa pessoa, CancellationToken ct)
    {
        if (pessoa.Empresa != _acesso.Empresa)
            throw new ErroDeDominio("TENANT_INVALIDO", "Tentativa de gravar pessoa de outra empresa.");
        _pessoas[pessoa.Id] = Rastreado<Pessoa>.Novo(pessoa);
        return Task.CompletedTask;
    }

    Task IRepositorioDePessoas.RemoverAsync(Pessoa pessoa, CancellationToken ct)
    {
        if (pessoa.Empresa != _acesso.Empresa)
            throw new ErroDeDominio("TENANT_INVALIDO", "Tentativa de excluir pessoa de outra empresa.");
        if (_pessoas.Remove(pessoa.Id, out var rastreada) && !rastreada.EhNovo)
            _pessoasRemovidas.Add(pessoa);
        return Task.CompletedTask;
    }

    // ───────────────────────────────────────────────── IRepositorioDeSquads

    async Task<Squad?> IRepositorioDeSquads.ObterAsync(Guid id, CancellationToken ct)
    {
        if (_squads.TryGetValue(id, out var rastreado)) return rastreado.Entidade;
        var documento = await _acesso.LerAsync<DocumentoDeSquad>(AcessoDoTenant.ParticaoDeAcesso, Mapeamento.Texto(id), ct);
        return documento is null ? null : Rastrear(documento);
    }

    async Task<Squad?> IRepositorioDeSquads.ObterPorSiglaAsync(string sigla, CancellationToken ct)
    {
        var normalizada = Squad.NormalizarSigla(sigla);
        var nesta = _squads.Values.FirstOrDefault(s => s.Entidade.Sigla == normalizada);
        if (nesta is not null) return nesta.Entidade;

        var reserva = await _acesso.LerAsync<DocumentoDeSigla>(AcessoDoTenant.ParticaoDeAcesso, Mapeamento.IdDaSigla(normalizada), ct);
        if (reserva is null) return null;
        var squad = await ((IRepositorioDeSquads)this).ObterAsync(Guid.Parse(reserva.IdSquad), ct);
        return squad?.Sigla == normalizada ? squad : null;
    }

    async Task<IReadOnlyList<Squad>> IRepositorioDeSquads.ListarAsync(CancellationToken ct)
    {
        var documentos = await _acesso.ConsultarAsync<DocumentoDeSquad>(TiposDeDocumento.Squad, ct: ct);
        return documentos.Select(Rastrear)
            .Concat(_squads.Values.Where(s => s.EhNovo).Select(s => s.Entidade))
            .DistinctBy(s => s.Id)
            .OrderBy(s => s.Ativa ? 0 : 1).ThenBy(s => s.Titulo, StringComparer.CurrentCulture)
            .ToArray();
    }

    Task IRepositorioDeSquads.AdicionarAsync(Squad squad, CancellationToken ct)
    {
        if (squad.Empresa != _acesso.Empresa)
            throw new ErroDeDominio("TENANT_INVALIDO", "Tentativa de gravar squad de outra empresa.");
        _squads[squad.Id] = Rastreado<Squad>.Novo(squad);
        return Task.CompletedTask;
    }

    // ─────────────────────────────────────────────────── IConsultaDeVinculos

    async Task<IReadOnlyList<string>> IConsultaDeVinculos.DaPessoaAsync(Guid idPessoa, CancellationToken ct)
    {
        var p = new Dictionary<string, object?> { ["@p"] = Mapeamento.Texto(idPessoa) };
        var consultas = new (string Nome, string Tipo, string Filtro)[]
        {
            ("responsabilidades em ativos", TiposDeDocumento.Ativo, "EXISTS(SELECT VALUE r FROM r IN c.responsaveis WHERE r.idPessoa = @p)"),
            ("revisões criadas", TiposDeDocumento.Revisao, "c.idAutor = @p"),
            ("validações submetidas ou decididas", TiposDeDocumento.Validacao, "c.idSubmissor = @p OR c.idDecisor = @p"),
            ("eventos de auditoria", TiposDeDocumento.Auditoria, "c.idPessoa = @p"),
        };
        var vinculos = new List<string>();
        foreach (var (nome, tipo, filtro) in consultas)
            if (await _acesso.ContarAsync(tipo, filtro, p, ct) > 0)
                vinculos.Add(nome);
        return vinculos;
    }

    // ───────────────────────────────────────────────── IRepositorioDePleitos

    Task IRepositorioDePleitos.AdicionarAsync(PleitoDeAcesso pleito, CancellationToken ct)
    {
        if (pleito.Empresa != _acesso.Empresa)
            throw new ErroDeDominio("TENANT_INVALIDO", "Tentativa de gravar pleito de outra empresa.");
        _pleitos[pleito.Id] = Rastreado<PleitoDeAcesso>.Novo(pleito);
        return Task.CompletedTask;
    }

    async Task<PleitoDeAcesso?> IRepositorioDePleitos.ObterAsync(Guid id, CancellationToken ct)
    {
        if (_pleitos.TryGetValue(id, out var rastreado)) return rastreado.Entidade;
        var documento = await _acesso.LerAsync<DocumentoDePleito>(AcessoDoTenant.ParticaoDeAcesso, Mapeamento.Texto(id), ct);
        return documento is null ? null : Rastrear(documento);
    }

    async Task<IReadOnlyList<PleitoDeAcesso>> IRepositorioDePleitos.ListarAsync(
        StatusDoPleito? status, Guid? idPessoa, CancellationToken ct)
    {
        var filtros = new List<string> { "true" };
        var parametros = new Dictionary<string, object?>();
        if (status is { } s)
        {
            filtros.Add("c.status = @status");
            parametros["@status"] = s.ToString().ToLowerInvariant();
        }
        if (idPessoa is { } id)
        {
            filtros.Add("c.idPessoa = @pessoa");
            parametros["@pessoa"] = Mapeamento.Texto(id);
        }

        var documentos = await _acesso.ConsultarAsync<DocumentoDePleito>(TiposDeDocumento.Pleito,
            string.Join(" AND ", filtros), parametros: parametros, ct: ct);
        return documentos.Select(Rastrear)
            .Concat(_pleitos.Values.Where(p => p.EhNovo).Select(p => p.Entidade))
            .Where(p => (status is null || p.Status == status) && (idPessoa is null || p.IdPessoa == idPessoa))
            .DistinctBy(p => p.Id)
            .OrderBy(p => p.Status == StatusDoPleito.Pendente ? 0 : 1).ThenBy(p => p.CriadoEm).ThenBy(p => p.Id)
            .ToArray();
    }

    // ───────────────────────────────────────────────────────────── confirmação

    /// <summary>Pessoa nova leva a reserva da matrícula no mesmo lote: a matrícula repetida derruba o lote inteiro.</summary>
    private void AgendarAcesso(AcessoDoTenant.Lote lote)
    {
        foreach (var rastreada in _pessoas.Values)
        {
            if (rastreada.EhNovo) lote.Criar(Mapeamento.Reserva(rastreada.Entidade));
            Agendar(lote, rastreada, Mapeamento.ParaDocumento(rastreada.Entidade));
        }
        foreach (var rastreado in _pleitos.Values)
            Agendar(lote, rastreado, Mapeamento.ParaDocumento(rastreado.Entidade));

        foreach (var pessoa in _pessoasRemovidas)
        {
            lote.Remover(Mapeamento.Texto(pessoa.Id));
            lote.Remover(Mapeamento.IdDaMatricula(pessoa.Matricula));
        }

        foreach (var (id, rastreado) in _squads)
        {
            var squad = rastreado.Entidade;
            var gravada = _siglasGravadas.GetValueOrDefault(id);
            if (gravada != squad.Sigla)
            {
                if (gravada is not null) lote.Remover(Mapeamento.IdDaSigla(gravada));
                lote.Criar(Mapeamento.Reserva(squad), _ => _siglasGravadas[id] = squad.Sigla);
            }
            Agendar(lote, rastreado, Mapeamento.ParaDocumento(squad));
        }
    }

    /// <summary>Depois de um lote confirmado, a exclusão já foi feita: não se repete.</summary>
    private void AcessoConfirmado() => _pessoasRemovidas.Clear();

    private Squad Rastrear(DocumentoDeSquad documento)
    {
        var id = Guid.Parse(documento.Id);
        if (_squads.TryGetValue(id, out var existente)) return existente.Entidade;

        var squad = Mapeamento.ParaDominio(documento);
        _squads[id] = Rastreado<Squad>.Lido(squad, documento.ETag, Retrato(Mapeamento.ParaDocumento(squad)));
        _siglasGravadas[id] = squad.Sigla;
        return squad;
    }

    private Pessoa Rastrear(DocumentoDePessoa documento)
    {
        var id = Guid.Parse(documento.Id);
        if (_pessoas.TryGetValue(id, out var existente)) return existente.Entidade;

        var pessoa = Mapeamento.ParaDominio(documento);
        _pessoas[id] = Rastreado<Pessoa>.Lido(pessoa, documento.ETag, Retrato(Mapeamento.ParaDocumento(pessoa)));
        return pessoa;
    }

    private PleitoDeAcesso Rastrear(DocumentoDePleito documento)
    {
        var id = Guid.Parse(documento.Id);
        if (_pleitos.TryGetValue(id, out var existente)) return existente.Entidade;

        var pleito = Mapeamento.ParaDominio(documento);
        _pleitos[id] = Rastreado<PleitoDeAcesso>.Lido(pleito, documento.ETag, Retrato(Mapeamento.ParaDocumento(pleito)));
        return pleito;
    }
}
