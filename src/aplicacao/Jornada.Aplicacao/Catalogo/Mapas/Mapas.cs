using Jornada.Aplicacao.Portas;
using Jornada.Dominio.Catalogo;

namespace Jornada.Aplicacao.Catalogo.Mapas;

/// <summary>
/// A vizinhança de um ativo no grafo de relações, com teto explícito. Portado
/// de <c>servicos.vizinhanca</c>.
///
/// As árvores mostram a hierarquia; isto mostra o que elas não alcançam: as
/// relações transversais, que cruzam domínios e sistemas. A expansão trata as
/// arestas como não direcionadas (quem consome importa tanto quanto quem é
/// consumido), mas cada aresta devolvida preserva a direção real.
///
/// <see cref="Saida.Truncado"/> faz parte do contrato: um recorte silencioso
/// faria a tela mentir sobre o alcance da mudança.
/// </summary>
public sealed class ConsultarVizinhanca(IRepositorioDeAtivos ativos, IRepositorioDeRelacoes relacoes)
{
    public const int TetoPadrao = 150;
    public const int SaltosMaximos = 3;

    public sealed record No(Ativo Ativo, int Salto);

    public sealed record Saida(IdDeAtivo Centro, int Saltos, IReadOnlyList<No> Nos, IReadOnlyList<RelacaoResumo> Arestas, bool Truncado, int Teto);

    public async Task<Resultado<Saida>> ExecutarAsync(
        IdDeAtivo id, int saltos = 1, IReadOnlyCollection<string>? tiposDeRelacao = null, int teto = TetoPadrao,
        CancellationToken ct = default)
    {
        var centro = await ativos.ObterAsync(id, ct);
        if (centro is null)
            return Resultado<Saida>.Recusado("ATIVO_NAO_ENCONTRADO", "Ativo não encontrado.");

        saltos = Math.Clamp(saltos, 1, SaltosMaximos);
        teto = Math.Max(teto, 1);
        bool Passa(RelacaoResumo r) => tiposDeRelacao is not { Count: > 0 } || tiposDeRelacao.Contains(r.Tipo);

        var distancias = new Dictionary<IdDeAtivo, int> { [id] = 0 };
        var arestas = new Dictionary<(IdDeAtivo, IdDeAtivo, string), RelacaoResumo>();
        var fronteira = new List<IdDeAtivo> { id };
        var truncado = false;

        for (var salto = 1; salto <= saltos && fronteira.Count > 0 && !truncado; salto++)
        {
            var proxima = new List<IdDeAtivo>();
            foreach (var atual in fronteira)
            {
                foreach (var r in (await relacoes.ListarRelacoesAsync(atual, ct)).Where(Passa))
                {
                    arestas.TryAdd((r.Origem, r.Destino, r.Tipo), r);
                    var outro = r.Origem == atual ? r.Destino : r.Origem;
                    if (distancias.ContainsKey(outro)) continue;
                    if (distancias.Count >= teto) { truncado = true; break; }
                    distancias[outro] = salto;
                    proxima.Add(outro);
                }
                if (truncado) break;
            }
            fronteira = proxima;
        }

        var nos = new List<No>();
        foreach (var (idNo, salto) in distancias)
            if (await ativos.ObterAsync(idNo, ct) is { } ativo)
                nos.Add(new No(ativo, salto));

        var presentes = nos.Select(n => n.Ativo.Id).ToHashSet();
        return Resultado<Saida>.Ok(new Saida(id, saltos,
            nos.OrderBy(n => n.Salto).ThenBy(n => n.Ativo.Nome, StringComparer.CurrentCultureIgnoreCase).ToArray(),
            arestas.Values.Where(a => presentes.Contains(a.Origem) && presentes.Contains(a.Destino)).ToArray(),
            truncado, teto));
    }
}

/// <summary>
/// A hierarquia inteira a partir de um tipo de raiz (<c>servicos.arvore</c>):
/// Domínio → Capacidade e Sistema → Endpoint usam a mesma mecânica, porque a
/// hierarquia é sempre o pai. Arquivados ficam de fora; cada capacidade traz
/// quantos ativos a implementam.
/// </summary>
public sealed class MontarArvore(IRepositorioDeAtivos ativos, IRepositorioDeRelacoes relacoes)
{
    public sealed record No(Ativo Ativo, int Implementacoes, IReadOnlyList<No> Filhos);

    public async Task<IReadOnlyList<No>> ExecutarAsync(string tipoRaiz, CancellationToken ct = default)
    {
        var todos = (await ativos.BuscarAsync(new FiltroDoCatalogo(), Consultas.BuscarNoCatalogo.Teto, ct))
            .Where(a => a.Status != StatusCicloVida.Arquivado)
            .ToArray();

        var implementacoes = new Dictionary<IdDeAtivo, int>();
        foreach (var capacidade in todos.Where(a => a.TipoItem == "capacidade"))
            implementacoes[capacidade.Id] = await relacoes.ContarImplementacoesAsync(capacidade.Id, ct);

        var filhos = todos.Where(a => a.IdPai is not null).ToLookup(a => a.IdPai!.Value);
        var nome = StringComparer.CurrentCultureIgnoreCase;
        No Montar(Ativo a, int profundidade) => new(a, implementacoes.GetValueOrDefault(a.Id),
            profundidade > 12 ? [] : filhos[a.Id].OrderBy(f => f.Nome, nome).Select(f => Montar(f, profundidade + 1)).ToArray());

        return todos.Where(a => a.TipoItem.Equals(tipoRaiz, StringComparison.OrdinalIgnoreCase))
            .OrderBy(a => a.Nome, nome)
            .Select(a => Montar(a, 0))
            .ToArray();
    }
}

/// <summary>
/// O grafo do catálogo inteiro, ou de um recorte da hierarquia, para a visão
/// executiva. Portado de <c>servicos.grafo_catalogo</c>.
///
/// Sem recorte: todos os ativos não arquivados, as arestas pai → filho e as
/// relações entre eles. Com recorte (domínio, subdomínio ou um item), só os
/// descendentes do recorte e só a hierarquia, como o painel faz desde
/// "Restringe grafo filtrado a hierarquia": dentro de um ramo, o que importa é
/// a composição. Domínio e subdomínio juntos são a interseção dos dois ramos.
///
/// O painel desenha tudo; aqui há um teto, e <see cref="Saida.Truncado"/>
/// avisa quando ele cortou (a mesma regra da vizinhança).
/// </summary>
public sealed class MontarGrafoDoCatalogo(IRepositorioDeAtivos ativos, IRepositorioDeRelacoes relacoes)
{
    public const int TetoPadrao = 300;
    public const string Hierarquia = "hierarquia";

    public sealed record Recorte(IdDeAtivo? Dominio = null, IdDeAtivo? Subdominio = null, IdDeAtivo? Item = null)
    {
        public bool Filtrado => Dominio is not null || Subdominio is not null || Item is not null;
    }

    public sealed record Saida(
        IReadOnlyList<Ativo> Nos, IReadOnlyList<RelacaoResumo> Arestas, bool Filtrado, bool Truncado, int Teto,
        IReadOnlyList<Ativo> Dominios, IReadOnlyList<Ativo> Subdominios);

    public async Task<Saida> ExecutarAsync(Recorte recorte, int teto = TetoPadrao, CancellationToken ct = default)
    {
        var todos = (await ativos.BuscarAsync(new FiltroDoCatalogo(), Consultas.BuscarNoCatalogo.Teto, ct))
            .Where(a => a.Status != StatusCicloVida.Arquivado)
            .ToArray();
        var nome = StringComparer.CurrentCultureIgnoreCase;
        var filhos = todos.Where(a => a.IdPai is not null).ToLookup(a => a.IdPai!.Value);

        HashSet<IdDeAtivo> Descendentes(IdDeAtivo raiz)
        {
            var achados = new HashSet<IdDeAtivo>();
            var pilha = new Stack<IdDeAtivo>();
            if (todos.Any(a => a.Id == raiz)) pilha.Push(raiz);
            while (pilha.Count > 0)
            {
                var atual = pilha.Pop();
                if (!achados.Add(atual)) continue;
                foreach (var f in filhos[atual]) pilha.Push(f.Id);
            }
            return achados;
        }

        // O item clicado vence o domínio (id_item or id_dominio, como no painel).
        var raizes = new[] { recorte.Item ?? recorte.Dominio, recorte.Subdominio }.OfType<IdDeAtivo>().ToArray();
        var escopo = raizes.Length == 0 ? null
            : raizes.Select(Descendentes).Aggregate((a, b) => { a.IntersectWith(b); return a; });

        var candidatos = todos.Where(a => escopo is null || escopo.Contains(a.Id))
            .OrderBy(a => a.TipoItem, StringComparer.Ordinal).ThenBy(a => a.Nome, nome)
            .ToArray();
        teto = Math.Max(teto, 1);
        var nos = candidatos.Take(teto).ToArray();
        var presentes = nos.Select(a => a.Id).ToHashSet();

        var arestas = nos.Where(a => a.IdPai is { } p && presentes.Contains(p))
            .Select(a => new RelacaoResumo(a.IdPai!.Value, a.Id, Hierarquia))
            .ToList();
        if (!recorte.Filtrado)
            arestas.AddRange((await relacoes.ListarArestasAsync(ct: ct))
                .Where(r => presentes.Contains(r.Origem) && presentes.Contains(r.Destino))
                .OrderBy(r => r.Tipo, StringComparer.Ordinal));

        var dominios = todos.Where(a => a.TipoItem == "dominio").OrderBy(a => a.Nome, nome).ToArray();
        // Todos os subdomínios, cada um com o seu domínio: a tela filtra o seletor sem ir ao servidor.
        var subdominios = todos.Where(a => a.TipoItem == "subdominio").OrderBy(a => a.Nome, nome).ToArray();

        return new Saida(nos, arestas, recorte.Filtrado, candidatos.Length > nos.Length, teto, dominios, subdominios);
    }
}
