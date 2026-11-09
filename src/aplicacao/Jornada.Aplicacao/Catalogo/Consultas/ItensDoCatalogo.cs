using Jornada.Aplicacao.Portas;
using Jornada.Dominio.Catalogo;
using Jornada.Dominio.Comum;
using Jornada.Dominio.Governanca;
using Jornada.Dominio.Qualidade;

namespace Jornada.Aplicacao.Catalogo.Consultas;

/// <summary>
/// O que as listas do painel mostram em cada linha além do próprio ativo: o pai
/// (a coluna "Contexto/pai") e a qualidade (o score). O painel lê o score
/// gravado em <c>qualidade_catalogo</c>; aqui ele é calculado com a mesma
/// calculadora do pré-check.
///
/// Para uma página inteira, as relações vêm numa consulta só, e os ativos que o
/// cálculo precisa (pais e destinos das relações) por leitura pontual, cada um
/// uma vez.
/// </summary>
public sealed class ItensDoCatalogo(
    IRepositorioDeAtivos ativos,
    IRepositorioDeRelacoes relacoes,
    IMetamodelo metamodelo,
    IPoliticasDeGovernanca politicas,
    IRelogio relogio)
{
    public sealed record Item(Ativo Ativo, Ativo? Pai, int? Score);

    public async Task<IReadOnlyList<Item>> DetalharAsync(IReadOnlyList<Ativo> lista, CancellationToken ct = default)
    {
        if (lista.Count == 0) return [];
        var tipos = await metamodelo.ObterAsync(ct);
        var regras = await politicas.ObterAsync(ct);
        var arestas = await relacoes.ListarArestasAsync(ct: ct);
        var saidas = arestas.ToLookup(r => r.Origem);
        var implementacoes = Implementacoes(arestas);

        var conhecidos = lista.ToDictionary(a => a.Id);
        var faltam = lista.Select(a => a.IdPai).OfType<IdDeAtivo>()
            .Concat(lista.SelectMany(a => saidas[a.Id]).Select(r => r.Destino))
            .Where(id => !conhecidos.ContainsKey(id))
            .Distinct()
            .ToArray();
        foreach (var id in faltam)
            if (await ativos.ObterAsync(id, ct) is { } ativo) conhecidos[id] = ativo;

        Ativo? PorId(IdDeAtivo id) => conhecidos.GetValueOrDefault(id);
        return lista.Select(a => new Item(a, a.IdPai is { } p ? PorId(p) : null,
            Score(a, tipos, regras, PorId, saidas[a.Id], implementacoes, relogio))).ToArray();
    }

    /// <summary>Quantos ativos implementam cada capacidade.</summary>
    public static IReadOnlyDictionary<IdDeAtivo, int> Implementacoes(IEnumerable<RelacaoResumo> arestas) =>
        arestas.Where(r => r.Tipo == "implementa").GroupBy(r => r.Destino).ToDictionary(g => g.Key, g => g.Count());

    /// <summary>
    /// O score do ativo, com o contexto montado de dados já carregados (o que o
    /// pré-check busca ativo a ativo). Nulo para um tipo que o metamodelo não conhece.
    /// </summary>
    public static int? Score(
        Ativo ativo, CatalogoDeTipos tipos, PoliticasDeGovernanca regras, Func<IdDeAtivo, Ativo?> porId,
        IEnumerable<RelacaoResumo> saidasDoAtivo, IReadOnlyDictionary<IdDeAtivo, int> implementacoes, IRelogio relogio)
    {
        if (!tipos.Existe(ativo.TipoItem)) return null;
        var pai = ativo.IdPai is { } idPai ? porId(idPai) : null;
        var contexto = new ContextoDeQualidade(
            PaiExiste: pai is not null,
            PaiForaDeVigencia: pai is not null && MaquinaDeCicloDeVida.ForaDeVigencia(pai.Status),
            RelacoesParaAtivosForaDeVigencia: saidasDoAtivo.Count(r => porId(r.Destino) is { } d && MaquinaDeCicloDeVida.ForaDeVigencia(d.Status)),
            ImplementacoesDaCapacidade: ativo.TipoItem == "capacidade" ? implementacoes.GetValueOrDefault(ativo.Id) : 0);
        return CalculadoraDeQualidade.Avaliar(ativo, tipos.Obter(ativo.TipoItem), regras.Para(ativo.TipoItem, ativo.Criticidade), contexto, relogio).Total;
    }
}
