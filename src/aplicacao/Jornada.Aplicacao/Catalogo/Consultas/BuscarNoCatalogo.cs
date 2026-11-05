using Jornada.Aplicacao.Portas;
using Jornada.Dominio.Catalogo;

namespace Jornada.Aplicacao.Catalogo.Consultas;

/// <summary>
/// A busca do catálogo (<c>servicos.buscar_pagina</c>): o recorte, a ordenação e
/// a página, com o total real. O rodapé do painel deixava de dizer a verdade a
/// partir do teto fixo de 200 linhas; aqui o total é o do recorte inteiro.
///
/// O filtro roda no banco; a ordenação e a página, aqui. Assim a mesma regra de
/// ordem (criticidade, por exemplo, que não é ordem alfabética) vale nos dois
/// adaptadores. O preço é ler o recorte inteiro: até <see cref="Teto"/> ativos.
/// Acima disso, <see cref="Saida.Truncado"/> avisa.
/// </summary>
public sealed class BuscarNoCatalogo(IRepositorioDeAtivos ativos)
{
    public const int Teto = 5000;
    public const int PorPaginaPadrao = 50;
    public const int MaximoPorPagina = 200;
    public const string OrdemPadrao = "tipo";

    /// <summary>As ordenações aceitas (<c>servicos.ORDENACOES</c>, menos score, que o Jornada não guarda).</summary>
    public static readonly IReadOnlyList<string> Ordenacoes = ["tipo", "nome", "status", "criticidade", "atualizado", "codigo"];

    public sealed record Saida(
        IReadOnlyList<Ativo> Itens, int Total, int Pagina, int Paginas, int PorPagina, int Primeiro, int Ultimo,
        string Ordem, bool Descendente, bool Truncado);

    public async Task<Saida> ExecutarAsync(
        FiltroDoCatalogo filtro, string? ordenar = null, bool descendente = false,
        int pagina = 1, int porPagina = PorPaginaPadrao, CancellationToken ct = default)
    {
        var recorte = await ativos.BuscarAsync(filtro, Teto + 1, ct);
        var truncado = recorte.Count > Teto;
        var todos = truncado ? recorte.Take(Teto).ToArray() : recorte;

        // Ordenação desconhecida cai no padrão, em vez de virar erro (test_ordenacao_desconhecida_cai_no_padrao).
        var ordem = Ordenacoes.Contains((ordenar ?? "").ToLowerInvariant()) ? ordenar!.ToLowerInvariant() : OrdemPadrao;
        var ordenados = Ordenar(todos, ordem, descendente);

        porPagina = Math.Clamp(porPagina, 1, MaximoPorPagina);
        var total = ordenados.Count;
        var paginas = Math.Max(1, (total + porPagina - 1) / porPagina);
        pagina = Math.Clamp(pagina, 1, paginas);   // fora do intervalo volta para a última
        var itens = ordenados.Skip((pagina - 1) * porPagina).Take(porPagina).ToArray();

        return new Saida(itens, total, pagina, paginas, porPagina,
            total == 0 ? 0 : (pagina - 1) * porPagina + 1, Math.Min(pagina * porPagina, total), ordem, descendente, truncado);
    }

    /// <summary>
    /// Sugestões da busca do topo (<c>/busca/sugestoes</c>): até 8 ativos, e
    /// nada com menos de 2 letras, que casaria com o catálogo inteiro.
    /// </summary>
    public async Task<IReadOnlyList<Ativo>> SugerirAsync(string? termo, CancellationToken ct = default)
    {
        var limpo = (termo ?? "").Trim();
        if (limpo.Length < 2) return [];
        var achados = await ativos.BuscarAsync(new FiltroDoCatalogo(Termo: limpo), Teto, ct);
        // Primeiro o que começa pelo termo, depois o resto, por nome.
        return achados
            .OrderBy(a => a.Nome.StartsWith(limpo, StringComparison.OrdinalIgnoreCase) || a.Codigo.Valor.StartsWith(limpo, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(a => a.Nome, StringComparer.CurrentCultureIgnoreCase)
            .Take(8)
            .ToArray();
    }

    private static IReadOnlyList<Ativo> Ordenar(IReadOnlyList<Ativo> ativos, string ordem, bool descendente)
    {
        var nome = StringComparer.CurrentCultureIgnoreCase;
        IOrderedEnumerable<Ativo> ordenado = ordem switch
        {
            "nome" => ativos.OrderBy(a => a.Nome, nome),
            "status" => ativos.OrderBy(a => a.Status).ThenBy(a => a.Nome, nome),
            // Crítica primeiro, como a fila de validação.
            "criticidade" => ativos.OrderByDescending(a => a.Criticidade).ThenBy(a => a.Nome, nome),
            "atualizado" => ativos.OrderByDescending(a => a.AtualizadoEm),
            "codigo" => ativos.OrderBy(a => a.Codigo.Valor, StringComparer.Ordinal),
            _ => ativos.OrderBy(a => a.TipoItem, StringComparer.Ordinal).ThenBy(a => a.Nome, nome),
        };
        var lista = ordenado.ThenBy(a => a.Codigo.Valor, StringComparer.Ordinal).ToList();
        if (descendente) lista.Reverse();
        return lista;
    }
}
