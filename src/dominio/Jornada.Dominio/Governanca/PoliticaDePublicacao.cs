using Jornada.Dominio.Catalogo;
using Jornada.Dominio.Comum;
using Jornada.Dominio.Qualidade;

namespace Jornada.Dominio.Governanca;

/// <summary>
/// Fatos sobre o resto do catálogo que o pré-check precisa e o agregado não tem
/// como saber: se existe outro ativo do mesmo tipo com o mesmo nome, se há ciclo
/// de dependência, e quais relações apontam para ativos fora de vigência.
///
/// O caso de uso os obtém pelas portas e entrega prontos — é o que permite a
/// <see cref="PoliticaDePublicacao"/> ser uma função pura.
/// </summary>
public sealed record ContextoDePreCheck(
    bool PaiInformado,
    bool NomeDuplicadoNaEmpresa,
    bool TemDependenciaCircular,
    IReadOnlyList<string> RelacoesForaDeVigencia)
{
    public static ContextoDePreCheck Vazio(bool paiInformado = true) =>
        new(paiInformado, false, false, []);
}

/// <summary>Resultado do pré-check: aprovado, bloqueios e alertas.</summary>
public sealed record ResultadoPreCheck(
    bool Aprovado,
    IReadOnlyList<Bloqueio> Bloqueios,
    IReadOnlyList<string> Alertas,
    int Score,
    PoliticaDeGovernanca Politica)
{
    public IEnumerable<CodigoDeBloqueio> Codigos => Bloqueios.Select(b => b.Codigo);

    public bool Bloqueado(CodigoDeBloqueio codigo) => Codigos.Contains(codigo);
}

/// <summary>
/// ═══════════════════════════════════════════════════════════════════════════
///  O CORAÇÃO DESTE PROJETO, e o melhor exemplo do que o ADR-0001 compra.
///
///  No painel-ddd, <c>governanca.pre_check(con, id_item)</c> mistura três
///  coisas: abrir consultas SQL, aplicar as regras e formatar bloqueios. Ela só
///  roda com um banco montado — por isso <c>tests/test_governanca.py</c>
///  precisa de <c>create_app</c>, <c>seed</c> e <c>tmp_path</c> para verificar
///  o que, em essência, é uma função de (ativo, política, score) → bloqueios.
///
///  Aqui ela é PURA. Nenhum `con`, nenhuma injeção, nenhum I/O. A suíte que a
///  cobre roda em microssegundos, com tabela de casos.
/// ═══════════════════════════════════════════════════════════════════════════
/// </summary>
public static class PoliticaDePublicacao
{
    public static ResultadoPreCheck Avaliar(
        Ativo ativo,
        TipoDeAtivo tipo,
        PoliticaDeGovernanca politica,
        ScoreDeQualidade score,
        ContextoDePreCheck contexto,
        IRelogio relogio)
    {
        var bloqueios = new List<Bloqueio>();
        var alertas = new List<string>();
        var hoje = relogio.Hoje;

        // ── campos obrigatórios do tipo ──────────────────────────────────────
        foreach (var campo in tipo.CamposObrigatorios)
        {
            var valor = ativo.Atributos.GetValueOrDefault(campo.Nome);
            if (string.IsNullOrWhiteSpace(valor))
                bloqueios.Add(new Bloqueio(CodigoDeBloqueio.Campo,
                    $"Campo obrigatório vazio: {campo.Rotulo}"));
        }

        // ── descrição ────────────────────────────────────────────────────────
        if (string.IsNullOrWhiteSpace(ativo.Descricao))
            bloqueios.Add(new Bloqueio(CodigoDeBloqueio.Descricao,
                "Descrição é obrigatória para publicar"));

        // ── hierarquia ───────────────────────────────────────────────────────
        if (tipo.Pai is not null && !contexto.PaiInformado)
            bloqueios.Add(new Bloqueio(CodigoDeBloqueio.Hierarquia,
                $"Hierarquia incompleta: informe o {tipo.Pai}"));

        // ── ownership ────────────────────────────────────────────────────────
        // Mesma fonte que a dimensão de ownership do score, de propósito: no
        // painel-ddd o pre_check reaproveita `qualidade._papeis` justamente para
        // a regra não dessincronizar da pontuação.
        if (tipo.ExigeOwnerNegocial && !ativo.TemOwner(PapelDeOwnership.OwnerNegocial, hoje))
            bloqueios.Add(new Bloqueio(CodigoDeBloqueio.Owner, "Owner negocial não definido"));

        if (tipo.ExigeOwnerTecnico && !ativo.TemOwner(PapelDeOwnership.OwnerTecnico, hoje))
            bloqueios.Add(new Bloqueio(CodigoDeBloqueio.Owner, "Owner técnico não definido"));

        // ── duplicidade ──────────────────────────────────────────────────────
        // "Mesmo tipo, mesmo nome" — agora POR EMPRESA (ADR-0003). Duas empresas
        // podem ter, cada uma, o seu "Cadastro de Clientes".
        if (contexto.NomeDuplicadoNaEmpresa)
            bloqueios.Add(new Bloqueio(CodigoDeBloqueio.Duplicidade,
                "Já existe ativo do mesmo tipo com esse nome"));

        // ── ciclo em depende_de ──────────────────────────────────────────────
        if (contexto.TemDependenciaCircular)
            bloqueios.Add(new Bloqueio(CodigoDeBloqueio.Ciclo,
                "Relação circular de dependência detectada"));

        // ── evidência mínima ─────────────────────────────────────────────────
        var totalEvidencias = ativo.Evidencias.Count;
        if (totalEvidencias < politica.EvidenciaMinima)
            bloqueios.Add(new Bloqueio(CodigoDeBloqueio.Evidencia,
                $"Evidência mínima não atendida ({totalEvidencias}/{politica.EvidenciaMinima})"));

        // ── relações para itens fora de vigência: ALERTA, não bloqueio ───────
        foreach (var nome in contexto.RelacoesForaDeVigencia)
            alertas.Add($"Relação com ativo fora de vigência: {nome}");

        // ── score mínimo ─────────────────────────────────────────────────────
        if (score.Total < politica.ScoreMinimo)
            bloqueios.Add(new Bloqueio(CodigoDeBloqueio.Score,
                $"Score de qualidade abaixo do mínimo ({score.Total}% < {politica.ScoreMinimo}%)"));

        // As pendências do score entram como alerta, igual ao painel-ddd.
        alertas.AddRange(score.Pendencias);

        return new ResultadoPreCheck(
            Aprovado: bloqueios.Count == 0,
            Bloqueios: bloqueios,
            Alertas: alertas,
            Score: score.Total,
            Politica: politica);
    }
}
