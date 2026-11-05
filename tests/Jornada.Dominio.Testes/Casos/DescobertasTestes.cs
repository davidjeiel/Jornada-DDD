using Jornada.Adaptadores.Memoria;
using Jornada.Aplicacao.Catalogo.Comandos;
using Jornada.Aplicacao.Catalogo.Descobertas;
using Jornada.Aplicacao.Portas;
using Jornada.Dominio.Acesso;
using Jornada.Dominio.Catalogo;
using Jornada.Dominio.Testes.Harness;

namespace Jornada.Dominio.Testes.Casos;

/// <summary>Portado de <c>tests/test_escala.py</c> na parte das descobertas: prévia, importação, bandeja e triagem.</summary>
[Suite("Descobertas: importação de OpenAPI e inventário Git com prévia, bandeja e triagem")]
public sealed class DescobertasTestes
{
    public const string Contrato = """
        {"info": {"title": "API Simulação", "version": "2.1", "description": "Simulação de financiamento"},
         "paths": {
           "/simulacoes": {"post": {"summary": "Cria uma simulação"}, "get": {"summary": "Lista simulações"}},
           "/simulacoes/{id}": {"get": {"summary": "Consulta uma simulação"}, "options": {"summary": "Ignorado: método fora da lista"}}
         }}
        """;

    public const string Inventario = """
        [{"nome": "motor-simulacao", "url": "https://git.interno/motor", "linguagem": "Python"},
         {"nome": "portal-credito", "url": "https://git.interno/portal", "linguagem": "TypeScript"}]
        """;

    private sealed class Mundo
    {
        private readonly ArmazemEmMemoria _armazem = new();
        private readonly ContextoDeTenantFixo _tenant = ContextoDeTenantFixo.Para(Cenario.EmpresaA);
        private readonly MetamodeloPadrao _meta = new();

        public RepositorioDeAtivosEmMemoria Repo => new(_armazem, _tenant);
        public IdDeAtivo Sistema { get; }
        public IdDeAtivo Aplicacao { get; }

        public Mundo()
        {
            Sistema = Cadastrar("sistema", "Plataforma de Crédito", null, new() { ["plataforma"] = "nuvem" });
            Aplicacao = Cadastrar("aplicacao", "Motor de Simulação", Sistema, new() { ["linguagem"] = "Python" });
        }

        private IdDeAtivo Cadastrar(string tipo, string nome, IdDeAtivo? pai, Dictionary<string, string> atributos) =>
            Exigir(new CadastrarAtivo(Repo, _meta, _tenant, new AtorFixo(MundoDeGovernanca.Curador), Repo, Repo, new RepositorioDeAcessoEmMemoria(_armazem, _tenant), Cenario.Relogio)
                .ExecutarAsync(new ComandoCadastrarAtivo(tipo, nome, "Descrição.", Criticidade.Media, pai, atributos)).Result);

        public ImportarDescobertas Importar(Ator? quem = null) =>
            new(Repo, _meta, _tenant, new AtorFixo(quem ?? MundoDeGovernanca.TechLead), Repo, Repo, Cenario.Relogio);

        public TriarDescobertas Triar(Ator? quem = null) =>
            new(Repo, _meta, _tenant, new AtorFixo(quem ?? MundoDeGovernanca.Curador), Repo, Repo, Cenario.Relogio);

        public int Contar() => ((IRepositorioDeAtivos)Repo).ListarAsync().Result.Count;
        public IReadOnlyList<Ativo> Bandeja() => Triar().BandejaAsync().Result;
        public Ativo Ativo(IdDeAtivo id) => Repo.ObterAsync(id).Result!;

        public static T Exigir<T>(Aplicacao.Resultado<T> r) =>
            r.Sucesso ? r.Valor! : throw new FalhaDeVerificacao($"passo do cenário recusado: {r.Codigo} — {r.Mensagem}");
    }

    [Teste("a prévia do OpenAPI não escreve nada")]
    public void PreviaDoOpenApiNaoEscreveNada()
    {
        var m = new Mundo();
        var antes = m.Contar();
        var plano = Mundo.Exigir(m.Importar().PlanejarAsync(FontesDeDescoberta.OpenApi, Contrato, m.Aplicacao).Result);
        Verificar.Igual(antes, m.Contar());
        Verificar.Igual("API Simulação", plano.Titulo);
        Verificar.Igual(4, plano.Criar, "a API e três operações válidas");
        Verificar.Igual(0, plano.Manter);
        Verificar.Verdadeiro(plano.Acoes.Any(a => a.Nome == "GET /simulacoes"));
        Verificar.Falso(plano.Acoes.Any(a => a.Nome.Contains("OPTIONS")), "método fora da lista");
    }

    [Teste("a prévia marca o que já existe")]
    public void PreviaMarcaOQueJaExiste()
    {
        var m = new Mundo();
        Mundo.Exigir(m.Importar().ImportarAsync(FontesDeDescoberta.OpenApi, Contrato, m.Aplicacao).Result);
        var plano = Mundo.Exigir(m.Importar().PlanejarAsync(FontesDeDescoberta.OpenApi, Contrato, m.Aplicacao).Result);
        Verificar.Igual(0, plano.Criar);
        Verificar.Igual(4, plano.Manter);
        var deNovo = Mundo.Exigir(m.Importar().ImportarAsync(FontesDeDescoberta.OpenApi, Contrato, m.Aplicacao).Result);
        Verificar.Igual((0, 4), (deNovo.Criados, deNovo.Mantidos), "importar de novo não duplica");
    }

    [Teste("a importação marca a origem e cai na bandeja, com a hierarquia montada")]
    public void ImportacaoMarcaAOrigemECaiNaBandeja()
    {
        var m = new Mundo();
        var saida = Mundo.Exigir(m.Importar().ImportarAsync(FontesDeDescoberta.OpenApi, Contrato, m.Aplicacao).Result);
        Verificar.Igual(4, saida.Criados);

        var bandeja = m.Bandeja();
        Verificar.Verdadeiro(new[] { "API Simulação", "GET /simulacoes" }.All(n => bandeja.Any(a => a.Nome == n)));
        Verificar.Verdadeiro(bandeja.All(a => a.Origem == OrigemDoAtivo.Automatica && a.Status == StatusCicloVida.Rascunho));
        Verificar.Falso(bandeja.Any(a => a.Nome == "Motor de Simulação"), "cadastro humano intacto, fora da bandeja");

        var api = bandeja.Single(a => a.TipoItem == "api");
        Verificar.Igual<IdDeAtivo?>(m.Aplicacao, api.IdPai);
        Verificar.Igual("2.1", api.Atributos["versao"]);
        Verificar.Igual("openapi", api.Evidencias.Single().Tipo);
        var endpoint = bandeja.Single(a => a.Nome == "POST /simulacoes");
        Verificar.Igual<IdDeAtivo?>(api.Id, endpoint.IdPai);
        Verificar.Igual("POST", endpoint.Atributos["metodo"]);
        Verificar.Igual("/simulacoes", endpoint.Atributos["caminho"]);
    }

    [Teste("o inventário Git também entra na bandeja")]
    public void InventarioGitTambemEntraNaBandeja()
    {
        var m = new Mundo();
        var plano = Mundo.Exigir(m.Importar().PlanejarAsync(FontesDeDescoberta.Git, Inventario, m.Aplicacao).Result);
        Verificar.Igual(2, plano.Criar);
        Mundo.Exigir(m.Importar().ImportarAsync(FontesDeDescoberta.Git, Inventario, m.Aplicacao).Result);
        var nomes = m.Bandeja().Select(a => a.Nome).ToArray();
        Verificar.Verdadeiro(nomes.Contains("motor-simulacao") && nomes.Contains("portal-credito"));

        var repo = m.Bandeja().Single(a => a.Nome == "motor-simulacao");
        Verificar.Igual("https://git.interno/motor", repo.Atributos["url"]);
        Verificar.Igual("https://git.interno/motor", repo.Evidencias.Single().Url);
        Verificar.Igual("atualizar", Mundo.Exigir(m.Importar().PlanejarAsync(FontesDeDescoberta.Git, Inventario, m.Aplicacao).Result).Acoes[0].Acao);
    }

    [Teste("JSON inválido avisa em vez de quebrar")]
    public void JsonInvalidoAvisaEmVezDeQuebrar()
    {
        var m = new Mundo();
        var r = m.Importar().PlanejarAsync(FontesDeDescoberta.OpenApi, "{isto não é json", null).Result;
        Verificar.Igual("CONTEUDO_INVALIDO", r.Codigo);
        Verificar.Verdadeiro(r.Mensagem!.StartsWith("Não consegui ler esse conteúdo"));
        Verificar.Igual("CONTEUDO_INVALIDO", m.Importar().PlanejarAsync(FontesDeDescoberta.Git, Contrato, null).Result.Codigo, "inventário é lista");
        Verificar.Igual("FONTE_INVALIDA", m.Importar().PlanejarAsync("svn", "[]", null).Result.Codigo);
    }

    [Teste("só quem importa vê a prévia e importa; o pai tem de ser aplicação")]
    public void ImportacaoExigePapelEPaiCerto()
    {
        var m = new Mundo();
        Verificar.Igual("SEM_PERMISSAO", m.Importar(MundoDeGovernanca.Negocio).PlanejarAsync(FontesDeDescoberta.OpenApi, Contrato, m.Aplicacao).Result.Codigo);
        Verificar.Igual("SEM_PERMISSAO", m.Importar(MundoDeGovernanca.Arquiteto).ImportarAsync(FontesDeDescoberta.Git, Inventario, m.Aplicacao).Result.Codigo);
        Verificar.Igual("HIERARQUIA_INVALIDA", m.Importar().PlanejarAsync(FontesDeDescoberta.OpenApi, Contrato, m.Sistema).Result.Codigo);
    }

    [Teste("a triagem aceita e descarta")]
    public void TriagemAceitaEDescarta()
    {
        var m = new Mundo();
        Mundo.Exigir(m.Importar().ImportarAsync(FontesDeDescoberta.OpenApi, Contrato, m.Aplicacao).Result);
        var bandeja = m.Bandeja();
        var (aceitar, descartar) = (bandeja[0].Id, bandeja[1].Id);

        Verificar.Igual(1, Mundo.Exigir(m.Triar().TriarAsync([aceitar], "aceitar").Result).Tratados);
        Verificar.Igual(1, Mundo.Exigir(m.Triar().TriarAsync([descartar], "descartar").Result).Tratados);
        var restantes = m.Bandeja().Select(a => a.Id).ToArray();
        Verificar.Falso(restantes.Contains(aceitar) || restantes.Contains(descartar));
        Verificar.Igual(OrigemDoAtivo.Manual, m.Ativo(aceitar).Origem, "segue no catálogo");
        Verificar.Igual(StatusCicloVida.Rascunho, m.Ativo(aceitar).Status, "sem mexer no ciclo de vida");
        Verificar.Igual(StatusCicloVida.Arquivado, m.Ativo(descartar).Status);
        Verificar.Igual(0, Mundo.Exigir(m.Triar().TriarAsync([aceitar], "aceitar").Result).Tratados, "já triado sai da conta");
    }

    [Teste("a triagem não toca em cadastro humano")]
    public void TriagemNaoTocaEmCadastroHumano()
    {
        var m = new Mundo();
        var r = Mundo.Exigir(m.Triar().TriarAsync([m.Sistema], "descartar").Result);
        Verificar.Igual((0, 1), (r.Tratados, r.Ignorados));
        Verificar.Igual(StatusCicloVida.Rascunho, m.Ativo(m.Sistema).Status);
    }

    [Teste("a triagem recusa ação desconhecida, e quem não importa não tria")]
    public void TriagemRecusaAcaoDesconhecida()
    {
        var m = new Mundo();
        Verificar.Igual("ACAO_DE_TRIAGEM_INVALIDA", m.Triar().TriarAsync([m.Sistema], "apagar").Result.Codigo);
        Mundo.Exigir(m.Importar().ImportarAsync(FontesDeDescoberta.Git, Inventario, m.Aplicacao).Result);
        Verificar.Igual("SEM_PERMISSAO", m.Triar(MundoDeGovernanca.Negocio).TriarAsync(m.Bandeja().Select(a => a.Id).ToArray(), "descartar").Result.Codigo);
        Verificar.Igual(2, m.Bandeja().Count, "nada saiu da bandeja");
    }
}
