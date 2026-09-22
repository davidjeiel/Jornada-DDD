using Jornada.Dominio.Catalogo;

namespace Jornada.Dominio.Governanca;

/// <summary>Etapa de validação. De <c>validacao.etapa</c>.</summary>
public enum EtapaValidacao { Negocial, Tecnica, Arquitetural }

/// <summary>
/// Política aplicável a um (tipo, criticidade). Corresponde a
/// <c>politica_governanca</c> e é semeada por <c>POLITICAS_PADRAO</c>.
/// </summary>
public sealed record PoliticaDeGovernanca(
    string TipoItem,
    string Criticidade,                       // "*" = vale para qualquer criticidade
    IReadOnlyList<EtapaValidacao> Etapas,
    int EvidenciaMinima,
    int ScoreMinimo,
    int SlaHoras,
    int PeriodicidadeRevisaoDias)
{
    /// <summary>Fallback de <c>governanca.politica</c> quando nada casa.</summary>
    public static PoliticaDeGovernanca Padrao(string tipoItem) =>
        new(tipoItem, "*", [EtapaValidacao.Tecnica], 0, 60, 48, 180);
}

/// <summary>
/// Conjunto de políticas de uma empresa, com a mesma resolução do
/// <c>governanca.politica</c>: casa (tipo, criticidade) e, se não achar,
/// cai para (tipo, "*"); se ainda assim não achar, usa o padrão.
/// </summary>
public sealed class PoliticasDeGovernanca(IEnumerable<PoliticaDeGovernanca> politicas)
{
    private readonly PoliticaDeGovernanca[] _politicas = politicas.ToArray();

    public PoliticaDeGovernanca Para(string tipoItem, Criticidade criticidade)
    {
        var chave = criticidade.Chave();

        var exata = _politicas.FirstOrDefault(p =>
            p.TipoItem.Equals(tipoItem, StringComparison.OrdinalIgnoreCase) &&
            p.Criticidade.Equals(chave, StringComparison.OrdinalIgnoreCase));
        if (exata is not null) return exata;

        var curinga = _politicas.FirstOrDefault(p =>
            p.TipoItem.Equals(tipoItem, StringComparison.OrdinalIgnoreCase) &&
            p.Criticidade == "*");

        return curinga ?? PoliticaDeGovernanca.Padrao(tipoItem);
    }

    /// <summary>
    /// Portado de <c>governanca.POLITICAS_PADRAO</c>, linha por linha:
    /// tipo, criticidade, etapas, evidência mínima, score mínimo, SLA h, revisão dias.
    /// </summary>
    public static PoliticasDeGovernanca Padrao() => new(
    [
        new("dominio",     "*",       [EtapaValidacao.Negocial, EtapaValidacao.Arquitetural],                            1, 70, 72, 365),
        new("subdominio",  "*",       [EtapaValidacao.Negocial],                                                          0, 65, 72, 365),
        new("contexto",    "*",       [EtapaValidacao.Negocial, EtapaValidacao.Tecnica, EtapaValidacao.Arquitetural],     1, 75, 48, 180),
        new("capacidade",  "*",       [EtapaValidacao.Negocial, EtapaValidacao.Tecnica],                                  1, 70, 48, 180),
        new("capacidade",  "critica", [EtapaValidacao.Negocial, EtapaValidacao.Tecnica, EtapaValidacao.Arquitetural],     2, 80, 24,  90),
        new("sistema",     "*",       [EtapaValidacao.Tecnica],                                                           0, 60, 72, 365),
        new("aplicacao",   "*",       [EtapaValidacao.Tecnica],                                                           1, 65, 48, 180),
        new("repositorio", "*",       [EtapaValidacao.Tecnica],                                                           1, 60, 72, 180),
        new("api",         "*",       [EtapaValidacao.Tecnica, EtapaValidacao.Arquitetural],                              1, 70, 48, 120),
        new("api",         "critica", [EtapaValidacao.Negocial, EtapaValidacao.Tecnica, EtapaValidacao.Arquitetural],     2, 80, 24,  90),
        new("endpoint",    "*",       [EtapaValidacao.Tecnica],                                                           0, 55, 72, 180),
        new("base_dados",  "*",       [EtapaValidacao.Tecnica],                                                           0, 60, 72, 365),
        new("objeto_dado", "*",       [EtapaValidacao.Tecnica],                                                           0, 55, 72, 365),
        new("evento",      "*",       [EtapaValidacao.Tecnica, EtapaValidacao.Arquitetural],                              1, 70, 48, 120),
    ]);
}
