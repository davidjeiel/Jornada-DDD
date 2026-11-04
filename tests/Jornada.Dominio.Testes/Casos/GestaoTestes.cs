using Jornada.Adaptadores.Memoria;
using Jornada.Aplicacao;
using Jornada.Aplicacao.Acesso;
using Jornada.Aplicacao.Catalogo.Comandos;
using Jornada.Aplicacao.Portas;
using Jornada.Dominio.Acesso;
using Jornada.Dominio.Catalogo;
using Jornada.Dominio.Governanca;
using Jornada.Dominio.Testes.Harness;

namespace Jornada.Dominio.Testes.Casos;

/// <summary>
/// Portado de <c>tests/test_cartilha.py</c>: o que estes testes protegem é que a
/// cartilha não envelheça. A matriz da página sai da política que a API aplica,
/// e a tabela de ritos, das políticas de governança.
/// </summary>
[Suite("Cartilha: tabelas lidas do código (cartilha.matriz / etapas_por_papel)")]
public sealed class CartilhaTestes
{
    [Teste("cada célula da matriz é a regra que a autorização aplica")]
    public void MatrizRefleteAsPermissoesReais()
    {
        // Sem bloco e na empresa inteira, Pode() responde só pela ação: é a célula da matriz.
        var alvo = AlvoDaAcao.NaEmpresa(Cenario.EmpresaA);
        foreach (var linha in ConsultarCartilha.Matriz())
            foreach (var (papel, pode) in linha.Papeis)
                Verificar.Igual(PoliticaDeAutorizacao.Pode(Cenario.Ator(papel), linha.Acao, alvo, Cenario.Relogio.Hoje), pode,
                    $"{linha.Acao} × {papel}");
        Verificar.Igual(Enum.GetValues<Acao>().Length, ConsultarCartilha.Matriz().Count, "toda ação aparece na matriz");
    }

    [Teste("a segregação de função aparece: curador não decide validação")]
    public void CuradorNaoApareceDecidindoValidacao()
    {
        var decidir = ConsultarCartilha.Matriz().Single(l => l.Acao == Acao.Decidir);
        Verificar.Falso(decidir.Papeis[Papel.Curador]);
        Verificar.Verdadeiro(decidir.Papeis[Papel.Admin]);
    }

    [Teste("consulta não pode nada na matriz")]
    public void ConsultaNaoPodeNadaNaMatriz()
    {
        foreach (var linha in ConsultarCartilha.Matriz())
            Verificar.Falso(linha.Papeis[Papel.Consulta], linha.Rotulo);
    }

    [Teste("as etapas de cada papel saem da tabela de autorização")]
    public void EtapasPorPapelSaiDoAcesso()
    {
        var mapa = ConsultarCartilha.EtapasPorPapel();
        Verificar.Igual("Negocial", string.Join(",", mapa[Papel.Negocio]));
        Verificar.Igual("Tecnica", string.Join(",", mapa[Papel.TechLead]));
        Verificar.Igual("Arquitetural", string.Join(",", mapa[Papel.Arquiteto]));
        Verificar.Igual("Negocial,Tecnica,Arquitetural", string.Join(",", mapa[Papel.Admin]));
        Verificar.Vazio(mapa[Papel.Curador]);
    }

    [Teste("os ritos vêm das políticas de governança, linha por linha")]
    public void RitosVemDaTabelaDePoliticas()
    {
        var saida = new ConsultarCartilha(new PoliticasPadrao(), new MetamodeloPadrao()).ExecutarAsync().Result;
        var politicas = PoliticasDeGovernanca.Padrao().Todas;
        Verificar.Igual(politicas.Count, saida.Ritos.Count);
        foreach (var (rito, politica) in saida.Ritos.Zip(politicas))
        {
            var r = rito.Politica;
            Verificar.Igual($"{politica.TipoItem}/{politica.Criticidade}/{string.Join(">", politica.Etapas)}/{politica.EvidenciaMinima}/{politica.ScoreMinimo}/{politica.SlaHoras}/{politica.PeriodicidadeRevisaoDias}",
                $"{r.TipoItem}/{r.Criticidade}/{string.Join(">", r.Etapas)}/{r.EvidenciaMinima}/{r.ScoreMinimo}/{r.SlaHoras}/{r.PeriodicidadeRevisaoDias}");
        }
        Verificar.Igual("API", saida.Ritos.First(r => r.TipoItem == "api").Rotulo, "o tipo pelo nome do metamodelo");
    }
}

/// <summary>A gestão de acessos e os squads (rotas <c>/gestao-acessos</c> e <c>/squads</c> do painel).</summary>
[Suite("Gestão de acessos e squads")]
public sealed class GestaoTestes
{
    [Teste("só o administrador entra na gestão de acessos")]
    public void SoAdminGerencia()
    {
        var m = new MundoDeGestao();
        Verificar.Igual("SEM_PERMISSAO", m.Gestao(MundoDeAcesso.Curador).ListarAsync().Result.Codigo);
        Verificar.Verdadeiro(m.Gestao(MundoDeAcesso.Admin).ListarAsync().Result.Sucesso);
    }

    [Teste("a busca acha por nome, matrícula, unidade e squad, e lista inativos depois")]
    public void BuscaPorQualquerDadoVisivel()
    {
        var m = new MundoDeGestao();
        var squad = m.Exigir(m.Squads(MundoDeAcesso.Curador).CriarAsync("cred", "Squad Crédito", "Contratos").Result);
        var davi = m.Pessoa("C123456", "Davi Souza", "0427");
        var eva = m.Pessoa("E654321", "Eva Lima", "0310");
        m.Exigir(m.Gestao(MundoDeAcesso.Admin).CorrigirAsync(eva.Id, "Eva Lima", "eva@empresa.com", "0310", squad.Id, ativa: false).Result);

        string Nomes(string busca) =>
            string.Join(",", m.Exigir(m.Gestao(MundoDeAcesso.Admin).ListarAsync(busca).Result).Select(c => c.Pessoa.Nome));
        Verificar.Igual("Davi Souza", Nomes("c1234"));
        Verificar.Igual("Eva Lima", Nomes("0310"));
        Verificar.Igual("Eva Lima", Nomes("CRED"));
        Verificar.Igual("Davi Souza,Eva Lima", Nomes(""), "ativos primeiro");
        Verificar.Igual(davi.Id, m.Exigir(m.Gestao(MundoDeAcesso.Admin).ListarAsync().Result)[0].Pessoa.Id);
    }

    [Teste("inativar encerra todos os papéis, e reativar não devolve nenhum")]
    public void InativarEncerraOsPapeis()
    {
        var m = new MundoDeGestao();
        var davi = m.Pessoa("C123456", "Davi Souza", "0427", Papel.TechLead);
        var gestao = m.Gestao(MundoDeAcesso.Admin);

        m.Exigir(gestao.CorrigirAsync(davi.Id, "Davi Souza", "davi@empresa.com", "0427", null, ativa: false).Result);
        Verificar.Falso(davi.Ativa);
        Verificar.Vazio(davi.Vigentes(Cenario.Relogio.Hoje), "inativo não tem papel");
        Verificar.Igual(1, davi.Atribuicoes.Count, "a atribuição fica no histórico, encerrada");

        m.Exigir(gestao.CorrigirAsync(davi.Id, "Davi Souza", "davi@empresa.com", "0427", null, ativa: true).Result);
        Verificar.Verdadeiro(davi.Ativa);
        Verificar.Vazio(davi.Vigentes(Cenario.Relogio.Hoje), "o papel volta por pleito ou concessão");
    }

    [Teste("o administrador conectado não se inativa, não se exclui e não revoga os próprios papéis")]
    public void AdminNaoMexeNoProprioCadastro()
    {
        var m = new MundoDeGestao();
        var ana = m.Pessoa("A000001", "Ana Ferreira", "0001", Papel.Admin);
        var gestao = m.Gestao(m.Como(ana));

        Verificar.Igual(GestaoDeAcessos.ProprioCadastro,
            gestao.CorrigirAsync(ana.Id, "Ana Ferreira", "ana@empresa.com", "0001", null, ativa: false).Result.Codigo);
        Verificar.Igual(GestaoDeAcessos.ProprioCadastro, gestao.ExcluirAsync(ana.Id).Result.Codigo);
        Verificar.Igual(GestaoDeAcessos.ProprioCadastro, gestao.RevogarAsync(ana.Id, "admin", "empresa", null).Result.Codigo);
        Verificar.Igual(0, m.Exigir(gestao.InativarEmLoteAsync([ana.Id]).Result), "o lote pula quem está conectado");
        Verificar.Verdadeiro(ana.Ativa);
    }

    [Teste("concessão direta: vale na hora, não se repete e não é para si mesmo")]
    public void ConcessaoDireta()
    {
        var m = new MundoDeGestao();
        var ana = m.Pessoa("A000001", "Ana Ferreira", "0001", Papel.Admin);
        var davi = m.Pessoa("C123456", "Davi Souza", "0427");
        var gestao = m.Gestao(m.Como(ana));

        var cadastro = m.Exigir(gestao.ConcederAsync(davi.Id, "arquiteto", "empresa", null).Result);
        Verificar.Igual("Arquiteto", cadastro.Papeis.Single().Atribuicao.Papel.Rotulo());
        Verificar.Igual<Guid?>(ana.Id, cadastro.Papeis.Single().Atribuicao.ConcedidoPor);
        Verificar.Verdadeiro(PoliticaDeAutorizacao.Pode(m.Como(davi), Acao.Descontinuar, AlvoDaAcao.NaEmpresa(Cenario.EmpresaA), Cenario.Relogio.Hoje));

        Verificar.Igual("PAPEL_JA_CONCEDIDO", gestao.ConcederAsync(davi.Id, "arquiteto", "empresa", null).Result.Codigo);
        Verificar.Igual("SEGREGACAO_DE_FUNCAO", gestao.ConcederAsync(ana.Id, "curador", "empresa", null).Result.Codigo);
        Verificar.Igual("ESCOPO_SEM_ALVO", gestao.ConcederAsync(davi.Id, "negocio", "dominio", null).Result.Codigo);
    }

    [Teste("revogar tira o papel hoje, não amanhã")]
    public void RevogarValeNaHora()
    {
        var m = new MundoDeGestao();
        var davi = m.Pessoa("C123456", "Davi Souza", "0427", Papel.Curador);
        var gestao = m.Gestao(MundoDeAcesso.Admin);

        m.Exigir(gestao.RevogarAsync(davi.Id, "curador", "empresa", null).Result);
        Verificar.Falso(PoliticaDeAutorizacao.PodeConceder(m.Como(davi), null, Cenario.Relogio.Hoje).Permitido,
            "no painel, revogado hoje ainda vale até a meia-noite");
        Verificar.Igual("PAPEL_NAO_CONCEDIDO", gestao.RevogarAsync(davi.Id, "curador", "empresa", null).Result.Codigo);
    }

    [Teste("cadastro com vínculo não se exclui; sem vínculo, sai e libera a matrícula")]
    public void ExclusaoSoSemVinculo()
    {
        var m = new MundoDeGestao();
        var gestao = m.Gestao(MundoDeAcesso.Admin);

        var comPapel = m.Pessoa("C111111", "Caio Papel", "0001", Papel.Negocio);
        var recusa = gestao.ExcluirAsync(comPapel.Id).Result;
        Verificar.Igual("CADASTRO_COM_VINCULOS", recusa.Codigo);
        Verificar.Verdadeiro(recusa.Mensagem!.Contains("papéis atribuídos"), recusa.Mensagem);

        var responsavel = m.Pessoa("C222222", "Rita Responsável", "0001");
        m.Responsavel(responsavel.Id);
        Verificar.Contem(m.Exigir(gestao.VinculosAsync(responsavel.Id).Result), "responsabilidades em ativos");

        var semNada = m.Pessoa("C333333", "Sem Vínculo", "0001");
        Verificar.Vazio(m.Exigir(gestao.VinculosAsync(semNada.Id).Result));
        m.Exigir(gestao.ExcluirAsync(semNada.Id).Result);
        Verificar.Igual(null, m.Acesso.ObterAsync(semNada.Id).Result);
        m.Pessoa("C333333", "Sem Vínculo de Novo", "0001");   // a matrícula ficou livre
    }

    [Teste("squad: só quem concede cadastra; sigla única, em maiúsculas, até 20")]
    public void SquadsComSiglaUnica()
    {
        var m = new MundoDeGestao();
        Verificar.Igual("SEM_PERMISSAO", m.Squads(MundoDeGovernanca.Negocio).CriarAsync("CRED", "Crédito", "x").Result.Codigo);

        var curador = m.Squads(MundoDeAcesso.Curador);
        var cred = m.Exigir(curador.CriarAsync(" cred ", "Squad Crédito", "Contratos de crédito").Result);
        Verificar.Igual("CRED", cred.Sigla);
        Verificar.Igual("SIGLA_DUPLICADA", curador.CriarAsync("Cred", "Outro", "x").Result.Codigo);
        Verificar.Igual("SQUAD_INCOMPLETA", curador.CriarAsync("GAR", "Garantias", "").Result.Codigo);
        Verificar.Igual("SIGLA_INVALIDA", curador.CriarAsync(new string('X', 21), "Longa", "x").Result.Codigo);

        var gar = m.Exigir(curador.CriarAsync("GAR", "Squad Garantias", "Garantias").Result);
        Verificar.Igual("SIGLA_DUPLICADA", curador.AlterarAsync(gar.Id, "cred", "Squad Garantias", "Garantias").Result.Codigo);
        m.Exigir(curador.AlterarAsync(gar.Id, "GARA", "Squad de Garantias", "Garantias").Result);

        m.Exigir(curador.InativarAsync(cred.Id).Result);
        Verificar.Igual("Squad de Garantias,Squad Crédito",
            string.Join(",", curador.ListarAsync().Result.Select(s => s.Titulo)), "ativas primeiro");
        Verificar.Igual("Squad Crédito", curador.ListarAsync("contratos").Result.Single().Titulo);
    }
}

/// <summary>Pessoas, squads e um catálogo em memória para a gestão de acessos.</summary>
internal sealed class MundoDeGestao
{
    private readonly ArmazemEmMemoria _armazem = new();
    private readonly ContextoDeTenantFixo _tenant = ContextoDeTenantFixo.Para(Cenario.EmpresaA);

    private RepositorioDeAtivosEmMemoria Repo => new(_armazem, _tenant);
    public RepositorioDeAcessoEmMemoria Acesso => new(_armazem, _tenant);

    public GestaoDeAcessos Gestao(Ator quem) =>
        new(Acesso, Acesso, Acesso, Repo, Repo, _tenant, new AtorFixo(quem), Repo, Cenario.Relogio);

    public CadastroDeSquads Squads(Ator quem) => new(Acesso, _tenant, new AtorFixo(quem), Repo, Cenario.Relogio);

    public Ator Como(Pessoa pessoa) => pessoa.ComoAtor(Cenario.Relogio.Hoje);

    public Pessoa Pessoa(string matricula, string nome, string unidade, Papel? papel = null)
    {
        var pessoa = Dominio.Acesso.Pessoa.Cadastrar(Cenario.EmpresaA,
            DadosDeCadastro.Conferir(matricula, nome, $"{matricula.ToLowerInvariant()}@empresa.com", unidade), Cenario.Relogio);
        if (papel is { } p) pessoa.Conceder(AtribuicaoDePapel.NaEmpresa(p, Cenario.Relogio.Hoje.AddDays(-1)));
        ((IRepositorioDePessoas)Acesso).AdicionarAsync(pessoa).Wait();
        return pessoa;
    }

    /// <summary>Um ativo com a pessoa como owner técnica: é vínculo com o histórico.</summary>
    public void Responsavel(Guid idPessoa)
    {
        var meta = new MetamodeloPadrao();
        var id = Exigir(new CadastrarAtivo(Repo, meta, _tenant, new AtorFixo(MundoDeAcesso.Curador), Repo, Repo, Cenario.Relogio)
            .ExecutarAsync(new ComandoCadastrarAtivo("sistema", $"Sistema {idPessoa:N}", "x")).Result);
        Exigir(new AtribuirResponsavel(Repo, meta, _tenant, new AtorFixo(MundoDeAcesso.Curador), Repo, Repo, Cenario.Relogio)
            .ExecutarAsync(new ComandoAtribuirResponsavel(id, idPessoa, PapelDeOwnership.OwnerTecnico)).Result);
    }

    public T Exigir<T>(Resultado<T> r) =>
        r.Sucesso ? r.Valor! : throw new FalhaDeVerificacao($"passo do cenário recusado: {r.Codigo} — {r.Mensagem}");
}
