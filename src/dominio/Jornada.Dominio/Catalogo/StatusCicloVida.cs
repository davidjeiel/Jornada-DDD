using Jornada.Dominio.Comum;

namespace Jornada.Dominio.Catalogo;

/// <summary>Estados do ciclo de vida. Portado de <c>governanca.TRANSICOES</c>.</summary>
public enum StatusCicloVida
{
    Rascunho,
    EmValidacao,
    Publicado,
    EmRevisao,
    Descontinuado,
    Arquivado,
}

/// <summary>
/// Portado literalmente de <c>governanca.TRANSICOES</c>:
///
/// <code>
/// "rascunho":      {"em_validacao", "arquivado"},
/// "em_validacao":  {"rascunho", "publicado"},
/// "publicado":     {"em_revisao", "descontinuado"},
/// "em_revisao":    {"publicado", "descontinuado"},
/// "descontinuado": {"arquivado"},
/// "arquivado":     set(),
/// </code>
///
/// Vira uma máquina PURA: nenhum banco, nenhuma injeção. A suíte que cobre isto
/// (<c>tests/test_governanca.py</c>) hoje precisa de <c>create_app</c>,
/// <c>seed</c> e <c>tmp_path</c>; aqui roda em microssegundos (ADR-0001).
/// </summary>
public static class MaquinaDeCicloDeVida
{
    private static readonly Dictionary<StatusCicloVida, StatusCicloVida[]> Transicoes = new()
    {
        [StatusCicloVida.Rascunho]      = [StatusCicloVida.EmValidacao, StatusCicloVida.Arquivado],
        [StatusCicloVida.EmValidacao]   = [StatusCicloVida.Rascunho, StatusCicloVida.Publicado],
        [StatusCicloVida.Publicado]     = [StatusCicloVida.EmRevisao, StatusCicloVida.Descontinuado],
        [StatusCicloVida.EmRevisao]     = [StatusCicloVida.Publicado, StatusCicloVida.Descontinuado],
        [StatusCicloVida.Descontinuado] = [StatusCicloVida.Arquivado],
        [StatusCicloVida.Arquivado]     = [],
    };

    public static IReadOnlyList<StatusCicloVida> DestinosDe(StatusCicloVida origem) =>
        Transicoes.TryGetValue(origem, out var d) ? d : [];

    public static bool Permite(StatusCicloVida origem, StatusCicloVida destino) =>
        DestinosDe(origem).Contains(destino);

    /// <summary>Levanta <see cref="TransicaoInvalida"/> nomeando os destinos válidos.</summary>
    public static void Exigir(StatusCicloVida origem, StatusCicloVida destino)
    {
        if (Permite(origem, destino)) return;

        var validos = DestinosDe(origem);
        var lista = validos.Count == 0
            ? "nenhum — é um estado terminal"
            : string.Join(", ", validos.Select(Rotulo));

        throw new TransicaoInvalida(
            $"Não é possível ir de '{Rotulo(origem)}' para '{Rotulo(destino)}'. " +
            $"Destinos válidos: {lista}.");
    }

    public static string Rotulo(StatusCicloVida s) => s switch
    {
        StatusCicloVida.Rascunho => "rascunho",
        StatusCicloVida.EmValidacao => "em_validacao",
        StatusCicloVida.Publicado => "publicado",
        StatusCicloVida.EmRevisao => "em_revisao",
        StatusCicloVida.Descontinuado => "descontinuado",
        StatusCicloVida.Arquivado => "arquivado",
        _ => s.ToString().ToLowerInvariant(),
    };

    /// <summary>Estados em que o ativo não vale mais como referência.</summary>
    public static bool ForaDeVigencia(StatusCicloVida s) =>
        s is StatusCicloVida.Descontinuado or StatusCicloVida.Arquivado;
}
