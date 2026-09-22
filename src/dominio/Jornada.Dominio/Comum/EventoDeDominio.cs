namespace Jornada.Dominio.Comum;

/// <summary>
/// Fato consumado, no passado. O agregado acumula; a unidade de trabalho grava
/// na outbox DENTRO da transação do fato gerador (ADR-0008).
/// </summary>
public abstract record EventoDeDominio
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public DateTimeOffset OcorridoEm { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Chave que torna o consumo idempotente. Generaliza o <c>chave_unica</c> de
    /// <c>notificacoes.py</c>, que já evitava alerta repetido do mesmo fato.
    /// </summary>
    public abstract string ChaveDeIdempotencia { get; }
}
