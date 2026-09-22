namespace Jornada.Dominio.Comum;

/// <summary>
/// O tempo entra no domínio por aqui, nunca por <c>DateTimeOffset.UtcNow</c>.
///
/// No painel-ddd o <c>datetime('now')</c> está espalhado pelo SQL, o que torna
/// impossível testar SLA e vigência de forma determinística — exatamente o que
/// <c>governanca.sla_restante</c> e o comando <c>vigiar-sla</c> precisam.
/// Com a porta, o teste escolhe "hoje" e a regra fica verificável.
/// </summary>
public interface IRelogio
{
    public DateTimeOffset Agora { get; }

    public DateOnly Hoje => DateOnly.FromDateTime(Agora.UtcDateTime);
}

/// <summary>Relógio de produção. O único que olha o relógio do sistema.</summary>
public sealed class RelogioDoSistema : IRelogio
{
    public DateTimeOffset Agora => DateTimeOffset.UtcNow;

    public DateOnly Hoje => DateOnly.FromDateTime(Agora.UtcDateTime);
}

/// <summary>Relógio de teste: o tempo é um parâmetro, não um acidente.</summary>
public sealed class RelogioFixo(DateTimeOffset instante) : IRelogio
{
    public DateTimeOffset Agora { get; private set; } = instante;

    public DateOnly Hoje => DateOnly.FromDateTime(Agora.UtcDateTime);

    public void Avancar(TimeSpan quanto) => Agora = Agora.Add(quanto);

    public static RelogioFixo Em(int ano, int mes, int dia) =>
        new(new DateTimeOffset(ano, mes, dia, 12, 0, 0, TimeSpan.Zero));
}
