using Jornada.Aplicacao.Portas;

namespace Jornada.Persistencia.Testes.Contrato;

/// <summary>
/// As portas de persistência como uma requisição as enxerga: um conjunto por
/// empresa e por unidade de trabalho. Abrir uma sessão nova equivale a uma
/// requisição nova. No Cosmos é um repositório novo, então o que ela lê veio do
/// banco, não do mapa de identidade da sessão anterior.
/// </summary>
public sealed class Sessao(
    IRepositorioDeAtivos ativos,
    IRepositorioDeRelacoes relacoes,
    IRepositorioDeValidacoes validacoes,
    IUnidadeDeTrabalho unidade,
    IRepositorioDeRevisoes revisoes,
    ITrilhaDeAuditoria trilha,
    IAsyncDisposable? recursos = null) : IAsyncDisposable
{
    public IRepositorioDeAtivos Ativos { get; } = ativos;
    public IRepositorioDeRelacoes Relacoes { get; } = relacoes;
    public IRepositorioDeValidacoes Validacoes { get; } = validacoes;
    public IUnidadeDeTrabalho Unidade { get; } = unidade;
    public IRepositorioDeRevisoes Revisoes { get; } = revisoes;
    public ITrilhaDeAuditoria Trilha { get; } = trilha;

    public ValueTask DisposeAsync() => recursos?.DisposeAsync() ?? ValueTask.CompletedTask;
}
