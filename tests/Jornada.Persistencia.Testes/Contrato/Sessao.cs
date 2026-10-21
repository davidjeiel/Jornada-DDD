using Jornada.Aplicacao.Portas;

namespace Jornada.Persistencia.Testes.Contrato;

/// <summary>
/// As portas de persistência como uma requisição as enxerga: um conjunto por
/// empresa e por unidade de trabalho. Abrir uma sessão nova equivale a uma
/// requisição nova. No Postgres é um <c>DbContext</c> novo, então o que ela lê
/// veio do banco, não do change tracker da sessão anterior.
/// </summary>
public sealed class Sessao(
    IRepositorioDeAtivos ativos,
    IRepositorioDeRelacoes relacoes,
    IRepositorioDeValidacoes validacoes,
    IUnidadeDeTrabalho unidade,
    IAsyncDisposable? recursos = null) : IAsyncDisposable
{
    public IRepositorioDeAtivos Ativos { get; } = ativos;
    public IRepositorioDeRelacoes Relacoes { get; } = relacoes;
    public IRepositorioDeValidacoes Validacoes { get; } = validacoes;
    public IUnidadeDeTrabalho Unidade { get; } = unidade;

    public ValueTask DisposeAsync() => recursos?.DisposeAsync() ?? ValueTask.CompletedTask;
}
