namespace Jornada.Dominio.Qualidade;

/// <summary>
/// Score de qualidade cadastral. Cinco dimensões de 0 a 100, combinadas por
/// peso. Portado de <c>qualidade.PESOS</c>:
///
/// <code>
/// completude 0.30 · consistencia 0.20 · ownership 0.20
/// evidencia  0.15 · temporalidade 0.15
/// </code>
/// </summary>
public sealed record ScoreDeQualidade(
    int Completude,
    int Consistencia,
    int Ownership,
    int Evidencia,
    int Temporalidade,
    IReadOnlyList<string> Pendencias)
{
    public const double PesoCompletude = 0.30;
    public const double PesoConsistencia = 0.20;
    public const double PesoOwnership = 0.20;
    public const double PesoEvidencia = 0.15;
    public const double PesoTemporalidade = 0.15;

    /// <summary>
    /// Total ponderado, arredondado como o Python faz. Atenção: <c>round()</c>
    /// do Python usa arredondamento bancário (metade para o par), e
    /// <c>Math.Round</c> do .NET também — por isso a paridade é preservada sem
    /// truque. Usar <c>MidpointRounding.AwayFromZero</c> aqui divergiria do
    /// painel-ddd em valores terminados em .5.
    /// </summary>
    public int Total => (int)Math.Round(
        Completude * PesoCompletude +
        Consistencia * PesoConsistencia +
        Ownership * PesoOwnership +
        Evidencia * PesoEvidencia +
        Temporalidade * PesoTemporalidade,
        MidpointRounding.ToEven);

    public bool AtingeMinimo(int minimo) => Total >= minimo;

    public override string ToString() =>
        $"{Total}% (compl {Completude}, consist {Consistencia}, own {Ownership}, " +
        $"evid {Evidencia}, temp {Temporalidade})";
}
