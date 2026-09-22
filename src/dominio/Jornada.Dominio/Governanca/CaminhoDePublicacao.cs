namespace Jornada.Dominio.Governanca;

/// <summary>Um passo do checklist até a publicação, com o estado real.</summary>
public sealed record PassoDePublicacao(
    string Chave,
    string Rotulo,
    bool Concluido,
    IReadOnlyList<Bloqueio> Impedimentos);

/// <summary>
/// Os cinco passos até publicar. Portado de <c>governanca.ETAPAS_PUBLICACAO</c>.
///
/// A ideia que o painel-ddd já teve, e que vale preservar: o checklist NÃO
/// reimplementa nenhuma regra — ele agrupa os códigos de bloqueio que o
/// pré-check já produziu. Uma regra, duas apresentações.
/// </summary>
public static class CaminhoDePublicacao
{
    private static readonly (string Chave, string Rotulo, CodigoDeBloqueio[] Impedem)[] Passos =
    [
        ("descrever",    "Descrever o ativo",       [CodigoDeBloqueio.Descricao, CodigoDeBloqueio.Campo, CodigoDeBloqueio.Duplicidade]),
        ("responsaveis", "Definir responsáveis",    [CodigoDeBloqueio.Owner]),
        ("conectar",     "Conectar ao catálogo",    [CodigoDeBloqueio.Hierarquia, CodigoDeBloqueio.Ciclo]),
        ("evidenciar",   "Anexar evidências",       [CodigoDeBloqueio.Evidencia]),
        ("qualidade",    "Atingir o score mínimo",  [CodigoDeBloqueio.Score]),
    ];

    public static IReadOnlyList<PassoDePublicacao> De(ResultadoPreCheck checagem) =>
        Passos.Select(p =>
        {
            var impedimentos = checagem.Bloqueios
                .Where(b => p.Impedem.Contains(b.Codigo))
                .ToArray();

            return new PassoDePublicacao(p.Chave, p.Rotulo, impedimentos.Length == 0, impedimentos);
        }).ToArray();
}
