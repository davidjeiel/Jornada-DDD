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
public sealed partial class RepositorioCosmos : IRepositorioDePessoas, IRepositorioDePleitos
{
    private readonly Dictionary<Guid, Rastreado<Pessoa>> _pessoas = [];
    private readonly Dictionary<Guid, Rastreado<PleitoDeAcesso>> _pleitos = [];

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
