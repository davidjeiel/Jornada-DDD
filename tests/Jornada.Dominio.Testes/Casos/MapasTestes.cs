using Jornada.Aplicacao.Catalogo.Consultas;
using Jornada.Aplicacao.Catalogo.Mapas;
using Jornada.Aplicacao.Portas;
using Jornada.Dominio.Catalogo;
using Jornada.Dominio.Testes.Harness;

namespace Jornada.Dominio.Testes.Casos;

/// <summary>
/// Portado de <c>tests/test_acesso.py</c> (vizinhança no grafo) e
/// <c>tests/test_escala.py</c> (árvores e análise de impacto).
/// </summary>
[Suite("Mapas: vizinhança no grafo, árvores e análise de impacto")]
public sealed class MapasTestes
{
    [Teste("vizinhança de um salto traz só os vizinhos diretos")]
    public void VizinhancaDeUmSalto()
    {
        var m = new MundoDaFicha();
        var (_, _, _, cap) = m.Capacidade();
        var app = m.Aplicacao();
        m.Exigir(m.Relacionar(app, cap, "implementa"));

        var um = Vizinhanca(m, cap, 1);
        Verificar.Igual(Ids(cap, app), Ids(um.Nos.Select(n => n.Ativo.Id).ToArray()));
        Verificar.Igual("implementa", um.Arestas.Single().Tipo);
        Verificar.Falso(um.Truncado);
        Verificar.Igual(0, um.Nos.Single(n => n.Ativo.Id == cap).Salto);
    }

    [Teste("vizinhança de dois saltos não repete nós e marca a distância")]
    public void VizinhancaDeDoisSaltosNaoRepeteNos()
    {
        var m = new MundoDaFicha();
        var (_, _, ctx, cap) = m.Capacidade();
        var app = m.Aplicacao();
        var outra = m.Cadastrar("capacidade", "Renegociar", ctx, new() { ["resultado_esperado"] = "x" });
        m.Exigir(m.Relacionar(app, cap, "implementa"));
        m.Exigir(m.Relacionar(app, outra, "implementa"));

        var dois = Vizinhanca(m, cap, 2);
        var ids = dois.Nos.Select(n => n.Ativo.Id).ToArray();
        Verificar.Igual(ids.Length, ids.Distinct().Count());
        Verificar.Igual(2, dois.Nos.Single(n => n.Ativo.Id == outra).Salto);
        Verificar.Igual(2, dois.Arestas.Count);
        Verificar.Falso(Vizinhanca(m, cap, 1).Nos.Any(n => n.Ativo.Id == outra), "com um salto, a outra capacidade fica de fora");
    }

    [Teste("o teto marca truncado")]
    public void TetoMarcaTruncado()
    {
        var m = new MundoDaFicha();
        var (_, _, _, cap) = m.Capacidade();
        var sis = m.Cadastrar("sistema", "Plataforma", null, new() { ["plataforma"] = "nuvem" });
        for (var n = 0; n < 6; n++)
            m.Exigir(m.Relacionar(m.Cadastrar("aplicacao", $"App {n}", sis, new() { ["linguagem"] = "Python" }), cap, "implementa"));

        var cortado = Vizinhanca(m, cap, 1, teto: 3);
        Verificar.Verdadeiro(cortado.Truncado);
        Verificar.Verdadeiro(cortado.Nos.Count <= 3, $"{cortado.Nos.Count} nós");
        Verificar.Falso(Vizinhanca(m, cap, 1).Truncado, "com o teto padrão cabe tudo");
    }

    [Teste("o filtro por tipo de relação reduz o grafo")]
    public void FiltroPorTipoDeRelacaoReduzOGrafo()
    {
        var m = new MundoDaFicha();
        var (_, _, ctx, cap) = m.Capacidade();
        var app = m.Aplicacao();
        m.Exigir(m.Relacionar(app, cap, "implementa"));
        m.Exigir(m.Relacionar(app, ctx, "consome"));

        Verificar.Igual(3, Vizinhanca(m, app, 1).Nos.Count);
        Verificar.Igual(2, Vizinhanca(m, app, 1, ["implementa"]).Nos.Count);
    }

    [Teste("a árvore de negócio desce os quatro níveis")]
    public void ArvoreDeNegocioDesceQuatroNiveis()
    {
        var m = new MundoDaFicha();
        m.Capacidade();
        m.Aplicacao();
        var raizes = Arvore(m, "dominio");
        Verificar.Igual("Contrato", string.Join(",", raizes.Select(r => r.Ativo.Nome)), "só os domínios são raiz");
        var sub = raizes[0].Filhos[0];
        var ctx = sub.Filhos[0];
        Verificar.Igual(("Originação", "Simulação"), (sub.Ativo.Nome, ctx.Ativo.Nome));
        Verificar.Igual("Simular Financiamento", ctx.Filhos[0].Ativo.Nome);
    }

    [Teste("a árvore técnica usa a mesma mecânica")]
    public void ArvoreTecnicaUsaAMesmaMecanica()
    {
        var m = new MundoDaFicha();
        var app = m.Aplicacao();
        var api = m.Api(app, "API Simulação");
        foreach (var caminho in new[] { "/simulacoes", "/simulacoes/{id}", "/taxas" })
            m.Cadastrar("endpoint", $"GET {caminho}", api, new() { ["metodo"] = "GET", ["caminho"] = caminho });

        var raizes = Arvore(m, "sistema");
        var aplicacao = raizes[0].Filhos[0];
        var noDaApi = aplicacao.Filhos[0];
        Verificar.Igual("Plataforma de Crédito", raizes[0].Ativo.Nome);
        Verificar.Igual("API Simulação", noDaApi.Ativo.Nome);
        Verificar.Igual(3, noDaApi.Filhos.Count, "os endpoints");
    }

    [Teste("a árvore conta as implementações da capacidade")]
    public void ArvoreContaImplementacoesDaCapacidade()
    {
        var m = new MundoDaFicha();
        var (_, _, _, cap) = m.Capacidade();
        m.Exigir(m.Relacionar(m.Aplicacao(), cap, "implementa"));
        var capacidade = Arvore(m, "dominio")[0].Filhos[0].Filhos[0].Filhos[0];
        Verificar.Igual(1, capacidade.Implementacoes);
    }

    [Teste("a análise agrupa os consumidores por tipo e criticidade")]
    public void AnaliseAgrupaOsConsumidores()
    {
        var m = new MundoDaFicha();
        var (_, _, _, cap) = m.Capacidade();
        var motor = Publicado(m, "aplicacao", "APP-0091", "Motor de Simulação");
        var portal = Publicado(m, "aplicacao", "APP-0092", "Portal");
        m.Repo.RelacionarAsync(motor, cap, "implementa", Criticidade.Critica).Wait();
        m.Repo.RelacionarAsync(portal, cap, "consome", Criticidade.Baixa).Wait();

        var analise = m.Exigir(new AnalisarImpacto(m.Repo, m.Repo).ExecutarAsync(cap).Result);
        Verificar.Igual(2, analise.Total);
        Verificar.Igual(2, analise.PorTipo["aplicacao"]);
        Verificar.Igual((1, 1), (analise.PorCriticidade["critica"], analise.PorCriticidade["baixa"]));
        Verificar.Igual("Motor de Simulação", string.Join(",", analise.Criticos.Select(c => c.Nome)));
    }

    [Teste("o grafo do catálogo, sem recorte, traz todos os ativos, a hierarquia e as relações")]
    public void GrafoDoCatalogoSemRecorte()
    {
        var m = new MundoDaFicha();
        var (dom, sub, _, cap) = m.Capacidade();
        var app = m.Aplicacao();
        m.Exigir(m.Relacionar(app, cap, "implementa"));

        var grafo = Grafo(m, new());
        Verificar.Igual(6, grafo.Nos.Count, "domínio, subdomínio, contexto, capacidade, sistema e aplicação");
        Verificar.Falso(grafo.Filtrado);
        Verificar.Igual(4, grafo.Arestas.Count(a => a.Tipo == MontarGrafoDoCatalogo.Hierarquia));
        Verificar.Verdadeiro(grafo.Arestas.Any(a => a.Tipo == "implementa" && a.Origem == app && a.Destino == cap));
        Verificar.Verdadeiro(grafo.Arestas.Any(a => a.Tipo == MontarGrafoDoCatalogo.Hierarquia && a.Origem == dom && a.Destino == sub));
        Verificar.Igual("Contrato", string.Join(",", grafo.Dominios.Select(d => d.Nome)));
    }

    [Teste("o grafo recortado traz só os descendentes e só a hierarquia")]
    public void GrafoRecortadoSoHierarquia()
    {
        var m = new MundoDaFicha();
        var (dom, sub, ctx, cap) = m.Capacidade();
        var outro = m.Cadastrar("subdominio", "Formalização", dom, new() { ["classificacao"] = "suporte" });
        m.Exigir(m.Relacionar(m.Aplicacao(), cap, "implementa"));

        var porDominio = Grafo(m, new(Dominio: dom));
        Verificar.Igual(Ids(dom, sub, ctx, cap, outro), Ids(porDominio.Nos.Select(n => n.Id).ToArray()));
        Verificar.Verdadeiro(porDominio.Filtrado);
        Verificar.Verdadeiro(porDominio.Arestas.All(a => a.Tipo == MontarGrafoDoCatalogo.Hierarquia), "recortado, só pai → filho");

        var interseccao = Grafo(m, new(Dominio: dom, Subdominio: sub));
        Verificar.Igual(Ids(sub, ctx, cap), Ids(interseccao.Nos.Select(n => n.Id).ToArray()));

        var porItem = Grafo(m, new(Dominio: dom, Item: ctx));
        Verificar.Igual(Ids(ctx, cap), Ids(porItem.Nos.Select(n => n.Id).ToArray()), "o item clicado vence o domínio");

        Verificar.Igual(0, Grafo(m, new(Item: IdDeAtivo.Novo())).Nos.Count, "recorte que não existe é vazio");
        var subdominios = Grafo(m, new()).Subdominios;
        Verificar.Igual("Formalização,Originação", string.Join(",", subdominios.Select(s => s.Nome)));
        Verificar.Verdadeiro(subdominios.All(s => s.IdPai == dom), "cada subdomínio vem com o domínio, para o seletor");
    }

    [Teste("o grafo do catálogo deixa o arquivado de fora e avisa o corte")]
    public void GrafoDoCatalogoSemArquivadoEComTeto()
    {
        var m = new MundoDaFicha();
        m.Capacidade();
        var arquivado = Ativo.Reidratar(IdDeAtivo.Novo(), Cenario.EmpresaA, "sistema", CodigoDeAtivo.De("SIS-0099"), "Legado", "x", null,
            Criticidade.Media, StatusCicloVida.Arquivado, OrigemDoAtivo.Manual, 1, Cenario.Relogio.Agora, Cenario.Relogio.Agora);
        m.Repo.AdicionarAsync(arquivado).Wait();

        Verificar.Falso(Grafo(m, new()).Nos.Any(n => n.Id == arquivado.Id));
        var cortado = Grafo(m, new(), teto: 2);
        Verificar.Verdadeiro(cortado.Truncado);
        Verificar.Igual(2, cortado.Nos.Count);
        Verificar.Falso(Grafo(m, new()).Truncado);
    }

    private static MontarGrafoDoCatalogo.Saida Grafo(MundoDaFicha m, MontarGrafoDoCatalogo.Recorte recorte, int teto = MontarGrafoDoCatalogo.TetoPadrao) =>
        new MontarGrafoDoCatalogo(m.Repo, m.Repo).ExecutarAsync(recorte, teto).Result;

    private static ConsultarVizinhanca.Saida Vizinhanca(MundoDaFicha m, IdDeAtivo id, int saltos, string[]? tipos = null, int teto = ConsultarVizinhanca.TetoPadrao) =>
        m.Exigir(new ConsultarVizinhanca(m.Repo, m.Repo).ExecutarAsync(id, saltos, tipos, teto).Result);

    private static IReadOnlyList<MontarArvore.No> Arvore(MundoDaFicha m, string raiz) =>
        new MontarArvore(m.Repo, m.Repo).ExecutarAsync(raiz).Result;

    private static IdDeAtivo Publicado(MundoDaFicha m, string tipo, string codigo, string nome)
    {
        var ativo = Ativo.Reidratar(IdDeAtivo.Novo(), Cenario.EmpresaA, tipo, CodigoDeAtivo.De(codigo), nome, "x", null,
            Criticidade.Media, StatusCicloVida.Publicado, OrigemDoAtivo.Manual, 1, Cenario.Relogio.Agora, Cenario.Relogio.Agora);
        m.Repo.AdicionarAsync(ativo).Wait();
        return ativo.Id;
    }

    private static string Ids(params IdDeAtivo[] ids) => string.Join(",", ids.Select(i => i.Valor).Order());
}
