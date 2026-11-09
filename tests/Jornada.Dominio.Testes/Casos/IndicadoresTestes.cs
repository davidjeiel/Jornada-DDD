using Jornada.Adaptadores.Memoria;
using Jornada.Aplicacao.Catalogo.Consultas;
using Jornada.Aplicacao.Portas;
using Jornada.Dominio.Acesso;
using Jornada.Dominio.Catalogo;
using Jornada.Dominio.Governanca;
using Jornada.Dominio.Testes.Harness;

namespace Jornada.Dominio.Testes.Casos;

/// <summary>Portado de <c>tests/test_jornada.py</c> na parte da visão executiva: indicadores e variação no mês.</summary>
[Suite("Visão executiva: indicadores, fotografia mensal e variação")]
public sealed class IndicadoresTestes
{
    [Teste("a variação compara as duas últimas competências")]
    public void VariacaoComparaAsDuasUltimasCompetencias()
    {
        var serie = new[]
        {
            new FotografiaDosIndicadores("2026-07", new Dictionary<string, double> { ["cobertura"] = 60 }, Cenario.Relogio.Agora),
            new FotografiaDosIndicadores("2026-08", new Dictionary<string, double> { ["cobertura"] = 72, ["pendencias"] = 4 }, Cenario.Relogio.Agora),
        };
        var variacao = Indicadores.VariacaoEntre(serie);
        Verificar.Igual(12.0, variacao["cobertura"]);
        Verificar.Falso(variacao.ContainsKey("pendencias"), "uma competência só não vira tendência");
    }

    [Teste("os indicadores contam o catálogo em vigor")]
    public void IndicadoresContamOCatalogoEmVigor()
    {
        var m = new Mundo();
        var dom = m.Ativo("dominio", "DOM-0001", StatusCicloVida.Publicado, comDono: true);
        m.Ativo("dominio", "DOM-0002", StatusCicloVida.Rascunho);
        var cap1 = m.Ativo("capacidade", "CAP-0001", StatusCicloVida.Publicado, comDono: true);
        m.Ativo("capacidade", "CAP-0002", StatusCicloVida.EmRevisao, comDono: true);
        var api1 = m.Ativo("api", "API-0001", StatusCicloVida.Publicado);          // publicada sem responsável
        m.Ativo("api", "API-0002", StatusCicloVida.Rascunho);
        m.Repo.RelacionarAsync(api1, cap1, "implementa").Wait();
        m.Repo.RelacionarAsync(cap1, dom, "depende_de", Criticidade.Critica).Wait();
        m.Repo.RelacionarAsync(api1, dom, "depende_de", Criticidade.Baixa).Wait();
        ((IRepositorioDeValidacoes)m.Repo).AdicionarAsync(new ValidacaoEmProcesso(Guid.NewGuid(), api1, Cenario.EmpresaA, 1,
            EtapaValidacao.Tecnica, Guid.NewGuid(), Cenario.Relogio.Agora.AddDays(-5), Cenario.Relogio.Agora.AddDays(-2))).Wait();

        var atuais = m.Caso(MundoDeAcesso.Admin).ConsultarAsync().Result.Atuais;
        Verificar.Igual(1.0, atuais[Indicadores.Dominios], "rascunho não está em vigor");
        Verificar.Igual(2.0, atuais[Indicadores.Capacidades]);
        Verificar.Igual(50.0, atuais[Indicadores.Cobertura]);
        Verificar.Igual(1.0, atuais[Indicadores.SemOwner]);
        Verificar.Igual(2.0, atuais[Indicadores.Apis]);
        Verificar.Igual(50.0, atuais[Indicadores.ApisGovernadas]);
        Verificar.Igual(1.0, atuais[Indicadores.Pendencias]);
        Verificar.Igual(1.0, atuais[Indicadores.SlaVencido]);
        Verificar.Igual(3.0, atuais[Indicadores.Publicados]);
        Verificar.Igual(1.0, atuais[Indicadores.DependenciasCriticas]);
        Verificar.Verdadeiro(atuais[Indicadores.QualidadeMedia] is > 0 and <= 100, $"qualidade média {atuais[Indicadores.QualidadeMedia]}");
    }

    [Teste("a qualidade média é a média do score de cada ativo, como no pré-check")]
    public void QualidadeMediaEhAMediaDoPreCheck()
    {
        var m = new MundoDaFicha();
        var (dom, sub, ctx, cap) = m.Capacidade();
        m.Completar(cap);
        var ids = new[] { dom, sub, ctx, cap }.Concat(((IRepositorioDeAtivos)m.Repo).ListarAsync().Result
            .Where(a => a.TipoItem is "sistema" or "aplicacao").Select(a => a.Id)).ToArray();
        var esperado = Math.Round(ids.Average(id => (double)m.PreCheck(id).Score.Total));

        var atuais = IndicadoresDe(m).ConsultarAsync().Result.Atuais;
        Verificar.Igual(esperado, atuais[Indicadores.QualidadeMedia]);
    }

    [Teste("a cobertura por domínio conta as capacidades abaixo do domínio e as que têm implementação")]
    public void CoberturaPorDominioContaAsCapacidades()
    {
        var m = new MundoDaFicha();
        var (dom, _, ctx, cap) = m.Capacidade();
        m.Cadastrar("capacidade", "Renegociar", ctx, new() { ["resultado_esperado"] = "x" });
        m.Exigir(m.Relacionar(m.Aplicacao(), cap, "implementa"));
        m.Cadastrar("dominio", "Sem subdomínio", null, new() { ["visao"] = "x" });

        var cobertura = IndicadoresDe(m).ConsultarAsync().Result.CoberturaPorDominio;
        Verificar.Igual(1, cobertura.Count, "domínio sem subdomínio não entra, como no painel");
        Verificar.Igual((dom, 2, 1, 50), (cobertura[0].Id, cobertura[0].Total, cobertura[0].Cobertas, cobertura[0].Percentual));
    }

    [Teste("as pendências prioritárias contam o que falta e dizem onde olhar")]
    public void PendenciasPrioritariasContamEApontamORecorte()
    {
        var m = new MundoDaFicha();
        var (_, _, _, cap) = m.Capacidade();
        var app = m.Aplicacao();
        var api = m.Api(app, "API Simulação");
        m.Api(app, "API Taxas");
        m.Exigir(m.Relacionar(api, cap, "implementa"));
        m.Exigir(m.Relacionar(app, api, "depende_de", Criticidade.Alta));
        m.Cadastrar("repositorio", "motor-simulacao", app, new() { ["url"] = "https://git.exemplo/motor" });

        var pendencias = IndicadoresDe(m).ConsultarAsync().Result.Pendencias.ToDictionary(p => p.Chave);
        Verificar.Igual("capacidade_sem_owner,api_sem_capacidade,repositorio_sem_mantenedor,sla_vencido,dependencia_critica_sem_evidencia",
            string.Join(",", pendencias.Keys));
        Verificar.Igual(1, pendencias["capacidade_sem_owner"].Total);
        Verificar.Igual(1, pendencias["api_sem_capacidade"].Total, "a API Taxas não implementa nada");
        Verificar.Igual(("api", false), (pendencias["api_sem_capacidade"].TipoItem, pendencias["api_sem_capacidade"].SemResponsavel));
        Verificar.Igual((1, true), (pendencias["repositorio_sem_mantenedor"].Total, pendencias["repositorio_sem_mantenedor"].SemResponsavel));
        Verificar.Igual("validacoes", pendencias["sla_vencido"].Tela);
        Verificar.Igual(1, pendencias["dependencia_critica_sem_evidencia"].Total);

        m.Completar(cap);
        Verificar.Igual(0, IndicadoresDe(m).ConsultarAsync().Result.Pendencias.Single(p => p.Chave == "capacidade_sem_owner").Total,
            "com owner negocial, a capacidade sai da pendência");
    }

    private static Indicadores IndicadoresDe(MundoDaFicha m) =>
        new(m.Repo, m.Repo, m.Repo, new RepositorioDeIndicadoresEmMemoria(new ArmazemEmMemoria(), ContextoDeTenantFixo.Para(Cenario.EmpresaA)),
            new MetamodeloPadrao(), new PoliticasPadrao(), ContextoDeTenantFixo.Para(Cenario.EmpresaA), new AtorFixo(MundoDeAcesso.Admin), Cenario.Relogio);

    [Teste("só admin fotografa; a mesma competência é substituída, e a variação sai das fotografias")]
    public void FotografiaMensal()
    {
        var m = new Mundo();
        m.Ativo("dominio", "DOM-0001", StatusCicloVida.Publicado, comDono: true);
        Verificar.Igual("SEM_PERMISSAO", m.Caso(MundoDeAcesso.Curador).FotografarAsync().Result.Codigo);
        Verificar.Igual("COMPETENCIA_INVALIDA", m.Caso(MundoDeAcesso.Admin).FotografarAsync("2026-13").Result.Codigo);

        Verificar.Verdadeiro(m.Caso(MundoDeAcesso.Admin).FotografarAsync("2026-07").Result.Sucesso);
        m.Ativo("dominio", "DOM-0002", StatusCicloVida.Publicado, comDono: true);
        Verificar.Verdadeiro(m.Caso(MundoDeAcesso.Admin).FotografarAsync("2026-08").Result.Sucesso);
        Verificar.Verdadeiro(m.Caso(MundoDeAcesso.Admin).FotografarAsync("2026-08").Result.Sucesso, "de novo, substitui");

        var saida = m.Caso(MundoDeAcesso.Admin).ConsultarAsync().Result;
        Verificar.Igual("2026-07,2026-08", string.Join(",", saida.Serie.Select(f => f.Competencia)));
        Verificar.Igual(1.0, saida.Variacao[Indicadores.Dominios]);
        Verificar.Verdadeiro(Indicadores.QuantoMenosMelhor.Contains(Indicadores.SemOwner));
    }

    private sealed class Mundo
    {
        private readonly ArmazemEmMemoria _armazem = new();
        private readonly ContextoDeTenantFixo _tenant = ContextoDeTenantFixo.Para(Cenario.EmpresaA);
        public RepositorioDeAtivosEmMemoria Repo => new(_armazem, _tenant);

        public IdDeAtivo Ativo(string tipo, string codigo, StatusCicloVida status, bool comDono = false)
        {
            var hoje = Cenario.Relogio.Hoje;
            var ativo = Dominio.Catalogo.Ativo.Reidratar(IdDeAtivo.Novo(), Cenario.EmpresaA, tipo, CodigoDeAtivo.De(codigo), codigo, "x", null,
                Criticidade.Media, status, OrigemDoAtivo.Manual, 1, Cenario.Relogio.Agora, Cenario.Relogio.Agora,
                responsaveis: comDono ? [new Responsabilidade(Guid.NewGuid(), PapelDeOwnership.OwnerTecnico, hoje.AddDays(-10), null)] : null);
            Repo.AdicionarAsync(ativo).Wait();
            return ativo.Id;
        }

        public Indicadores Caso(Ator quem) =>
            new(Repo, Repo, Repo, new RepositorioDeIndicadoresEmMemoria(_armazem, _tenant), new MetamodeloPadrao(), new PoliticasPadrao(),
                _tenant, new AtorFixo(quem), Cenario.Relogio);
    }
}
