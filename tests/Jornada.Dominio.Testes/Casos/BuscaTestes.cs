using Jornada.Adaptadores.Memoria;
using Jornada.Aplicacao.Catalogo.Consultas;
using Jornada.Aplicacao.Portas;
using Jornada.Dominio.Catalogo;
using Jornada.Dominio.Testes.Harness;

namespace Jornada.Dominio.Testes.Casos;

/// <summary>
/// Portado de <c>tests/test_jornada.py</c> e <c>tests/test_escala.py</c> na parte
/// da busca: sugestões do topo, recorte, ordem e página com o total real.
/// </summary>
[Suite("Catálogo: busca, filtros, ordem e página (servicos.buscar_pagina)")]
public sealed class BuscaTestes
{
    private static readonly DateOnly Hoje = Cenario.Relogio.Hoje;

    [Teste("a busca do topo sugere ativos pelo termo")]
    public void BuscaGlobalSugereAtivosPorTermo()
    {
        var m = new MundoDaFicha();
        m.Capacidade();
        var nomes = Busca(m).SugerirAsync("simul").Result.Select(a => a.Nome).ToArray();
        Verificar.Contem(nomes, "Simulação");
        Verificar.Contem(nomes, "Simular Financiamento");
    }

    [Teste("a busca do topo ignora termo curto")]
    public void BuscaGlobalIgnoraTermoCurto()
    {
        var m = new MundoDaFicha();
        m.Capacidade();
        Verificar.Vazio(Busca(m).SugerirAsync("s").Result);
        Verificar.Vazio(Busca(m).SugerirAsync("  ").Result);
    }

    [Teste("o filtro sem owner isola os ativos sem responsável")]
    public void FiltroSemOwnerIsolaOsAtivosSemResponsavel()
    {
        var m = new MundoDaFicha();
        var (_, _, _, cap) = m.Capacidade();
        m.Completar(cap);
        var orfaos = Nomes(m, new FiltroDoCatalogo(SemOwner: true, Hoje: Hoje));
        Verificar.NaoContem(orfaos, "Simular Financiamento");
        Verificar.Contem(orfaos, "Contrato");
    }

    [Teste("a página traz o total real, não o tamanho da página")]
    public void PaginaTrazOTotalReal()
    {
        var m = new MundoDaFicha();
        Muitos(m, 57);
        var filtro = new FiltroDoCatalogo(TipoItem: "aplicacao");
        var primeira = Busca(m).ExecutarAsync(filtro, pagina: 1, porPagina: 50).Result;
        var segunda = Busca(m).ExecutarAsync(filtro, pagina: 2, porPagina: 50).Result;
        Verificar.Igual(57, primeira.Total);
        Verificar.Igual(2, primeira.Paginas);
        Verificar.Igual(50, primeira.Itens.Count);
        Verificar.Igual((1, 50), (primeira.Primeiro, primeira.Ultimo));
        Verificar.Igual(7, segunda.Itens.Count);
        Verificar.Igual(57, segunda.Ultimo);
    }

    [Teste("página fora do intervalo volta para a última")]
    public void PaginaForaDoIntervaloVoltaParaAUltima()
    {
        var m = new MundoDaFicha();
        Muitos(m, 10);
        var r = Busca(m).ExecutarAsync(new FiltroDoCatalogo(), pagina: 99, porPagina: 50).Result;
        Verificar.Igual(r.Paginas, r.Pagina);
        Verificar.Igual(1, Busca(m).ExecutarAsync(new FiltroDoCatalogo(), pagina: -3).Result.Pagina);
    }

    [Teste("ordena por nome, ao contrário e por criticidade")]
    public void OrdenacaoPorNomeEPorCriticidade()
    {
        var m = new MundoDaFicha();
        m.Capacidade();
        var nomes = Busca(m).ExecutarAsync(new FiltroDoCatalogo(), "nome").Result.Itens.Select(a => a.Nome).ToArray();
        var invertido = Busca(m).ExecutarAsync(new FiltroDoCatalogo(), "nome", descendente: true).Result.Itens.Select(a => a.Nome).ToArray();
        Verificar.Igual(string.Join("|", nomes.Order(StringComparer.CurrentCultureIgnoreCase)), string.Join("|", nomes));
        Verificar.Igual(string.Join("|", nomes.Reverse()), string.Join("|", invertido));

        var critica = Ativo.Rascunhar(Cenario.EmpresaA, Cenario.Tipo("sistema"), CodigoDeAtivo.De("SIS-0099"), "Zeta crítico",
            Cenario.Relogio, criticidade: Criticidade.Critica);
        m.Repo.AdicionarAsync(critica).Wait();
        Verificar.Igual("Zeta crítico", Busca(m).ExecutarAsync(new FiltroDoCatalogo(), "criticidade").Result.Itens[0].Nome, "crítica primeiro");
    }

    [Teste("ordenação desconhecida cai no padrão")]
    public void OrdenacaoDesconhecidaCaiNoPadrao()
    {
        var m = new MundoDaFicha();
        m.Capacidade();
        var r = Busca(m).ExecutarAsync(new FiltroDoCatalogo(), "DROP TABLE").Result;
        Verificar.Igual(BuscarNoCatalogo.OrdemPadrao, r.Ordem);
        Verificar.Igual(4, r.Total);
    }

    [Teste("o filtro de origem separa o que a máquina trouxe")]
    public void FiltroDeOrigemSeparaOQueAMaquinaTrouxe()
    {
        var m = new MundoDaFicha();
        m.Aplicacao();
        var descoberta = Ativo.Rascunhar(Cenario.EmpresaA, Cenario.Tipo("api"), CodigoDeAtivo.De("API-0099"), "API Simulação",
            Cenario.Relogio, origem: OrigemDoAtivo.Automatica);
        m.Repo.AdicionarAsync(descoberta).Wait();
        Verificar.Igual("API Simulação", string.Join(",", Nomes(m, new FiltroDoCatalogo(Origem: OrigemDoAtivo.Automatica))));
        Verificar.NaoContem(Nomes(m, new FiltroDoCatalogo(Origem: OrigemDoAtivo.Automatica)), "Motor de Simulação");
    }

    [Teste("os filtros se somam: tipo, criticidade e termo")]
    public void FiltrosSeSomam()
    {
        var m = new MundoDaFicha();
        m.Capacidade();
        Verificar.Igual("Contrato", string.Join(",", Nomes(m, new FiltroDoCatalogo(TipoItem: "dominio", Criticidade: Criticidade.Media))));
        Verificar.Vazio(Nomes(m, new FiltroDoCatalogo(TipoItem: "dominio", Criticidade: Criticidade.Alta)));
        Verificar.Igual("Simular Financiamento", string.Join(",", Nomes(m, new FiltroDoCatalogo(Termo: "FINANC"))), "sem diferenciar maiúsculas");
    }

    private static BuscarNoCatalogo Busca(MundoDaFicha m) => new(m.Repo);

    private static string[] Nomes(MundoDaFicha m, FiltroDoCatalogo filtro) =>
        Busca(m).ExecutarAsync(filtro, porPagina: 200).Result.Itens.Select(a => a.Nome).ToArray();

    private static void Muitos(MundoDaFicha m, int quantos)
    {
        for (var i = 1; i <= quantos; i++)
            m.Repo.AdicionarAsync(Ativo.Rascunhar(Cenario.EmpresaA, Cenario.Tipo("aplicacao"), CodigoDeAtivo.De($"APP-{i:0000}"),
                $"Aplicação {i:00}", Cenario.Relogio)).Wait();
    }
}
