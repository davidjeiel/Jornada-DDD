using Jornada.Dominio.Acesso;
using Jornada.Dominio.Catalogo;
using Jornada.Dominio.Comum;
using Jornada.Dominio.Governanca;
using Jornada.Dominio.Tenancy;

namespace Jornada.Aplicacao.Portas;

/// <summary>
/// De onde vem o contexto de tenant. Repare que NÃO há como DEFINIR a empresa:
/// ela é resolvida no middleware a partir do token e validada contra o control
/// plane. Nenhum caso de uso a recebe de quem chama (ADR-0003).
/// </summary>
public interface IContextoDeTenantAtual
{
    public ContextoDeTenant Atual { get; }
}

/// <summary>Quem está agindo agora, com os papéis já resolvidos.</summary>
public interface IAtorAtual
{
    public Task<Ator> ObterAsync(CancellationToken ct = default);
}

public interface IRepositorioDeAtivos
{
    public Task<Ativo?> ObterAsync(IdDeAtivo id, CancellationToken ct = default);

    public Task<Ativo?> ObterPorCodigoAsync(CodigoDeAtivo codigo, CancellationToken ct = default);

    public Task<IReadOnlyList<Ativo>> ListarAsync(
        string? tipoItem = null, StatusCicloVida? status = null,
        int limite = 200, CancellationToken ct = default);

    public Task AdicionarAsync(Ativo ativo, CancellationToken ct = default);

    /// <summary>A trilha hierárquica até a raiz. Equivale a <c>servicos.trilha</c>.</summary>
    public Task<IReadOnlyList<Ativo>> TrilhaAsync(IdDeAtivo id, CancellationToken ct = default);

    /// <summary>Existe outro ativo do mesmo tipo e nome NESTA empresa?</summary>
    public Task<bool> ExisteNomeDuplicadoAsync(
        string tipoItem, string nome, IdDeAtivo? exceto = null, CancellationToken ct = default);

    /// <summary>Próximo sequencial para o código, por tipo. De <c>servicos.gerar_codigo</c>.</summary>
    public Task<int> ProximoSequencialAsync(string tipoItem, CancellationToken ct = default);
}

/// <summary>Consultas sobre o grafo de relações (ADR-0004: CTE recursiva no Postgres).</summary>
public interface IRepositorioDeRelacoes
{
    public Task<bool> TemDependenciaCircularAsync(IdDeAtivo id, CancellationToken ct = default);

    public Task<IReadOnlyList<string>> NomesDeDestinosForaDeVigenciaAsync(
        IdDeAtivo id, CancellationToken ct = default);

    public Task<int> ContarImplementacoesAsync(IdDeAtivo idCapacidade, CancellationToken ct = default);
}

/// <summary>A taxonomia desta empresa, na versão vigente (ADR-0005).</summary>
public interface IMetamodelo
{
    public Task<CatalogoDeTipos> ObterAsync(CancellationToken ct = default);
}

/// <summary>As políticas de governança desta empresa.</summary>
public interface IPoliticasDeGovernanca
{
    public Task<PoliticasDeGovernanca> ObterAsync(CancellationToken ct = default);
}

/// <summary>
/// Transação. O <c>ConfirmarAsync</c> é o ponto — e o único ponto — em que os
/// eventos dos agregados vão para a outbox, na MESMA transação (ADR-0008).
/// </summary>
public interface IUnidadeDeTrabalho
{
    public Task<int> ConfirmarAsync(CancellationToken ct = default);
}

/// <summary>Saída de eventos. Em dev é barramento em memória; em produção, Service Bus.</summary>
public interface IPublicadorDeEventos
{
    public Task PublicarAsync(IEnumerable<EventoDeDominio> eventos, CancellationToken ct = default);
}
