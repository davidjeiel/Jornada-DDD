using Jornada.Aplicacao.Portas;
using Jornada.Dominio.Catalogo;

namespace Jornada.Aplicacao.Catalogo.Consultas;

/// <summary>
/// Quem depende deste ativo: os consumidores em vigor que seriam afetados se
/// ele saísse do catálogo. Equivale a <c>servicos.analise_impacto</c> (que usa
/// <c>governanca.impacto_descontinuacao</c>).
///
/// Consumidor é quem APONTA para o ativo (ele é o destino da relação) e ainda
/// vale como referência: publicado ou em revisão. Rascunho não conta, porque
/// nunca foi oficial; descontinuado e arquivado também não, porque já saíram.
/// </summary>
public sealed class AnalisarImpacto(IRepositorioDeAtivos ativos, IRepositorioDeRelacoes relacoes)
{
    public sealed record Consumidor(
        IdDeAtivo Id, string Codigo, string Nome, string TipoItem, string TipoRelacao, Criticidade Criticidade);

    public sealed record Saida(
        IdDeAtivo Id,
        string Codigo,
        IReadOnlyList<Consumidor> Consumidores,
        IReadOnlyDictionary<string, int> PorCriticidade,
        IReadOnlyDictionary<string, int> PorTipo)
    {
        public int Total => Consumidores.Count;

        /// <summary>Os de alta criticidade (alta ou crítica): são eles que exigem confirmação para descontinuar.</summary>
        public IReadOnlyList<Consumidor> Criticos =>
            Consumidores.Where(c => EhCritica(c.Criticidade)).ToArray();
    }

    public static bool EhCritica(Criticidade c) => c is Criticidade.Alta or Criticidade.Critica;

    public async Task<Resultado<Saida>> ExecutarAsync(IdDeAtivo id, CancellationToken ct = default)
    {
        var ativo = await ativos.ObterAsync(id, ct);
        if (ativo is null)
            return Resultado<Saida>.Recusado("ATIVO_NAO_ENCONTRADO", "Ativo não encontrado.");

        var consumidores = new List<Consumidor>();
        foreach (var aresta in (await relacoes.ListarRelacoesAsync(id, ct)).Where(r => r.Destino == id))
        {
            var origem = await ativos.ObterAsync(aresta.Origem, ct);
            if (origem is null || origem.Status is not (StatusCicloVida.Publicado or StatusCicloVida.EmRevisao))
                continue;

            consumidores.Add(new Consumidor(origem.Id, origem.Codigo.Valor, origem.Nome, origem.TipoItem,
                aresta.Tipo, aresta.Criticidade));
        }

        // Mais crítico primeiro: é o que quem vai descontinuar precisa ver antes.
        var ordenados = consumidores
            .OrderByDescending(c => c.Criticidade)
            .ThenBy(c => c.Codigo, StringComparer.Ordinal)
            .ToArray();

        return Resultado<Saida>.Ok(new Saida(
            ativo.Id,
            ativo.Codigo.Valor,
            ordenados,
            Contar(ordenados, c => c.Criticidade.Chave()),
            Contar(ordenados, c => c.TipoItem)));
    }

    private static Dictionary<string, int> Contar(IEnumerable<Consumidor> consumidores, Func<Consumidor, string> chave) =>
        consumidores.GroupBy(chave).ToDictionary(g => g.Key, g => g.Count());
}
