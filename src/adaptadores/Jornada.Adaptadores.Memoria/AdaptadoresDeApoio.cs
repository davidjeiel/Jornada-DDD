using Jornada.Aplicacao.Portas;
using Jornada.Dominio.Acesso;
using Jornada.Dominio.Catalogo;
using Jornada.Dominio.Comum;
using Jornada.Dominio.Governanca;
using Jornada.Dominio.Tenancy;

namespace Jornada.Adaptadores.Memoria;

/// <summary>
/// Contexto de tenant fixo. Em produção, quem implementa esta porta é o
/// middleware que lê o token e valida contra o control plane (ADR-0003/0007).
/// </summary>
public sealed class ContextoDeTenantFixo(ContextoDeTenant contexto) : IContextoDeTenantAtual
{
    public ContextoDeTenant Atual { get; } = contexto;

    public static ContextoDeTenantFixo Para(IdDeEmpresa empresa, IdDeUnidade? unidade = null) =>
        new(new ContextoDeTenant(empresa, unidade));
}

public sealed class AtorFixo(Ator ator) : IAtorAtual
{
    public Task<Ator> ObterAsync(CancellationToken ct = default) => Task.FromResult(ator);
}

/// <summary>Metamodelo padrão. Em produção vem do banco, versionado por empresa (ADR-0005).</summary>
public sealed class MetamodeloPadrao : IMetamodelo
{
    private readonly CatalogoDeTipos _tipos = CatalogoDeTipos.Padrao();

    public Task<CatalogoDeTipos> ObterAsync(CancellationToken ct = default) =>
        Task.FromResult(_tipos);
}

public sealed class PoliticasPadrao : IPoliticasDeGovernanca
{
    private readonly PoliticasDeGovernanca _politicas = PoliticasDeGovernanca.Padrao();

    public Task<PoliticasDeGovernanca> ObterAsync(CancellationToken ct = default) =>
        Task.FromResult(_politicas);
}

/// <summary>
/// Barramento em memória: os módulos conversam por evento dentro do mesmo
/// processo. Trocar por Service Bus é implementar esta porta — o caso de uso
/// não muda uma linha (ADR-0006, ADR-0008).
/// </summary>
public sealed class PublicadorEmMemoria : IPublicadorDeEventos
{
    private readonly List<EventoDeDominio> _publicados = [];

    public IReadOnlyList<EventoDeDominio> Publicados => _publicados;

    public Task PublicarAsync(IEnumerable<EventoDeDominio> eventos, CancellationToken ct = default)
    {
        _publicados.AddRange(eventos);
        return Task.CompletedTask;
    }
}
