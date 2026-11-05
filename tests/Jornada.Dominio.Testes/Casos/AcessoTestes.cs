using Jornada.Adaptadores.Memoria;
using Jornada.Aplicacao;
using Jornada.Aplicacao.Acesso;
using Jornada.Aplicacao.Catalogo.Comandos;
using Jornada.Aplicacao.Catalogo.Consultas;
using Jornada.Aplicacao.Portas;
using Jornada.Dominio.Acesso;
using Jornada.Dominio.Catalogo;
using Jornada.Dominio.Comum;
using Jornada.Dominio.Testes.Harness;

namespace Jornada.Dominio.Testes.Casos;

/// <summary>
/// Portado de <c>tests/test_acesso_pleito.py</c> e dos testes de modo leitura e
/// escopo de <c>tests/test_acesso.py</c>. O que eles protegem: ninguém ganha
/// papel por se cadastrar; o papel nasce de uma decisão registrada de quem tem
/// alçada, e depois de concedido vale para o que ele responde, não para o
/// catálogo inteiro.
/// </summary>
[Suite("Acesso: cadastro, pleito e concessão (acesso.solicitar_acesso / decidir_solicitacao)")]
public sealed class AcessoTestes
{
    // ─────────────────────────────────────────────── validação do cadastro

    [Teste("matrícula fora do formato é recusada")]
    public void MatriculaForaDoFormatoERecusada()
    {
        foreach (var ruim in new[] { "123456", "CC12345", "C12345", "C1234567", "", "C12345a", " C123456 x" })
            Verificar.Igual("MATRICULA_INVALIDA",
                Verificar.Lanca<ErroDeDominio>(() => DadosDeCadastro.Conferir(ruim, "Davi Souza", "d@e.com", "0427")).Codigo, ruim);
    }

    [Teste("matrícula vale com qualquer letra inicial")]
    public void MatriculaValidaEmQualquerLetraInicial()
    {
        foreach (var letra in new[] { "C", "E", "F", "P", "X" })
            Verificar.Igual($"{letra}123456", DadosDeCadastro.Conferir($"{letra}123456", "Davi Souza", "d@e.com", "0427").Matricula.Valor);
    }

    [Teste("matrícula é normalizada para maiúscula")]
    public void MatriculaENormalizadaParaMaiuscula() =>
        Verificar.Igual("C123456", DadosDeCadastro.Conferir("c123456", "Davi Souza", "d@e.com", "0427").Matricula.Valor);

    [Teste("unidade exige quatro números")]
    public void UnidadeExigeQuatroNumeros()
    {
        foreach (var ruim in new[] { "427", "04277", "abcd", "", "04 7" })
            Verificar.Igual("UNIDADE_INVALIDA",
                Verificar.Lanca<ErroDeDominio>(() => DadosDeCadastro.Conferir("C123456", "Davi Souza", "d@e.com", ruim)).Codigo, ruim);
        Verificar.Igual("0427", DadosDeCadastro.Conferir("C123456", "Davi Souza", "d@e.com", "0427").Unidade.Valor);
    }

    [Teste("nome completo e e-mail são conferidos")]
    public void NomeEEmailSaoConferidos()
    {
        Verificar.Igual("NOME_INCOMPLETO",
            Verificar.Lanca<ErroDeDominio>(() => DadosDeCadastro.Conferir("C123456", "Davi", "d@e.com", "0427")).Codigo);
        Verificar.Igual("EMAIL_INVALIDO",
            Verificar.Lanca<ErroDeDominio>(() => DadosDeCadastro.Conferir("C123456", "Davi Souza", "davi", "0427")).Codigo);
    }

    // ────────────────────────────────────────────────────────────── o pleito

    [Teste("cadastrar-se não concede papel nenhum")]
    public void CadastroNaoConcedePapelNenhum()
    {
        var m = new MundoDeAcesso();
        var pleito = m.Pedir();
        var pessoa = m.Pessoa(pleito.Pessoa.Id);
        Verificar.Vazio(pessoa.Atribuicoes, "o cadastro cria a pessoa e um pedido, não um papel");
        Verificar.Falso(PoliticaDeAutorizacao.Pode(m.Como(pessoa), Acao.Cadastrar, Cenario.Alvo("dominio"), Cenario.Relogio.Hoje));
    }

    [Teste("quem se cadastra consulta o catálogo, e só consulta")]
    public void QuemSeCadastraConsultaOCatalogo()
    {
        var m = new MundoDeAcesso();
        m.Dominio("Crédito");
        var pessoa = m.Pessoa(m.Pedir().Pessoa.Id);

        var perfil = m.Perfil(m.Como(pessoa));
        Verificar.Verdadeiro(perfil.Ator.Identificado, "já é quem disse que é");
        Verificar.Vazio(perfil.BlocosQueEscreve, "sem papel, nenhum bloco de escrita");
        Verificar.Igual(1, ((IRepositorioDeAtivos)m.Repo).ListarAsync().Result.Count, "a leitura do catálogo não pede papel");
    }

    [Teste("o pleito nasce pendente, com os dados informados")]
    public void PleitoNascePendenteComOsDadosInformados()
    {
        var m = new MundoDeAcesso();
        var saida = m.Pedir(papel: "negocio");
        Verificar.Igual(StatusDoPleito.Pendente, saida.Pleito.Status);
        Verificar.Igual(Papel.Negocio, saida.Pleito.PapelPleiteado);
        Verificar.Igual("C123456", saida.Pessoa.Matricula.Valor);
        Verificar.Igual("0427", saida.Pessoa.Unidade.Valor);
        Verificar.Igual<Guid?>(null, saida.Pleito.DecididoPor);
    }

    [Teste("segundo pleito pendente da mesma pessoa é recusado")]
    public void SegundoPleitoPendenteERecusado()
    {
        var m = new MundoDeAcesso();
        m.Pedir();
        Verificar.Igual("PLEITO_PENDENTE", m.Solicitar(papel: "negocio").Codigo);
    }

    [Teste("recadastro atualiza a pessoa, sem duplicar")]
    public void RecadastroAtualizaAPessoaSemDuplicar()
    {
        var m = new MundoDeAcesso();
        var primeiro = m.Pedir();
        m.Exigir(m.Decidir(primeiro.Pleito.Id, MundoDeAcesso.Admin, aprovar: true));

        var de_novo = m.Exigir(m.Solicitar("c123456", "Davi Souza Lima", "davi.lima@empresa.com", "0500", "arquiteto"));
        Verificar.Igual(primeiro.Pessoa.Id, de_novo.Pessoa.Id);
        var pessoa = m.Pessoa(primeiro.Pessoa.Id);
        Verificar.Igual("Davi Souza Lima", pessoa.Nome);
        Verificar.Igual("0500", pessoa.Unidade.Valor);
        Verificar.Igual(1, m.Pessoas.ListarAsync().Result.Count(p => p.Matricula.Valor == "C123456"));
    }

    // ─────────────────────────────────────────────────────────── a concessão

    [Teste("aprovar concede o papel e registra a resposta")]
    public void AprovacaoConcedeOPapelEResponde()
    {
        var m = new MundoDeAcesso();
        var pedido = m.Pedir();
        var decidido = m.Exigir(m.Decidir(pedido.Pleito.Id, MundoDeAcesso.Curador, aprovar: true, resposta: "Bem-vindo."));

        Verificar.Igual(StatusDoPleito.Aprovado, decidido.Pleito.Status);
        Verificar.Igual<Papel?>(Papel.TechLead, decidido.Pleito.PapelConcedido);
        Verificar.Igual("Bem-vindo.", decidido.Pleito.Resposta);
        Verificar.Igual<Guid?>(MundoDeAcesso.Curador.IdPessoa, decidido.Pleito.DecididoPor);
        Verificar.Verdadeiro(PoliticaDeAutorizacao.Pode(m.Como(m.Pessoa(pedido.Pessoa.Id)), Acao.Importar,
            Cenario.Alvo("api"), Cenario.Relogio.Hoje), "tech lead importa");
    }

    [Teste("negar exige motivo e não concede nada")]
    public void NegativaExigeMotivoENaoConcedeNada()
    {
        var m = new MundoDeAcesso();
        var pedido = m.Pedir();
        Verificar.Igual("RESPOSTA_OBRIGATORIA", m.Decidir(pedido.Pleito.Id, MundoDeAcesso.Curador, aprovar: false).Codigo);

        var negado = m.Exigir(m.Decidir(pedido.Pleito.Id, MundoDeAcesso.Curador, aprovar: false, resposta: "Peça ao seu gestor primeiro."));
        Verificar.Igual(StatusDoPleito.Negado, negado.Pleito.Status);
        Verificar.Vazio(m.Pessoa(pedido.Pessoa.Id).Atribuicoes);
        Verificar.Verdadeiro(negado.Pleito.Resposta.Contains("gestor"));
    }

    [Teste("conceder papel diferente do pleiteado exige explicar a troca")]
    public void PapelDiferenteDoPleiteadoExigeExplicacao()
    {
        var m = new MundoDeAcesso();
        var pedido = m.Pedir(papel: "admin");
        Verificar.Igual("RESPOSTA_OBRIGATORIA",
            m.Decidir(pedido.Pleito.Id, MundoDeAcesso.Admin, aprovar: true, papel: "negocio").Codigo);

        var decidido = m.Exigir(m.Decidir(pedido.Pleito.Id, MundoDeAcesso.Admin, aprovar: true, papel: "negocio",
            resposta: "Admin não cabe aqui; negócio resolve."));
        Verificar.Igual<Papel?>(Papel.Negocio, decidido.Pleito.PapelConcedido);
    }

    [Teste("concessão com escopo de domínio não vale fora dele")]
    public void ConcessaoComEscopoDeDominioNaoValeForaDele()
    {
        var m = new MundoDeAcesso();
        var dentro = m.Dominio("Crédito");
        var fora = m.Dominio("Logística");
        var pedido = m.Pedir(papel: "negocio");
        m.Exigir(m.Decidir(pedido.Pleito.Id, MundoDeAcesso.Admin, aprovar: true,
            escopo: "dominio", escopoId: dentro.Valor, resposta: "Só no seu domínio."));

        var quem = m.Como(m.Pessoa(pedido.Pessoa.Id));
        Verificar.Verdadeiro(m.Editar(dentro, quem).Sucesso, "dentro do domínio");
        Verificar.Igual("SEM_PERMISSAO", m.Editar(fora, quem).Codigo, "fora dele");
    }

    [Teste("papel de domínio vale nos filhos do domínio, e não em outro domínio")]
    public void PapelDeDominioNaoValeEmOutroDominio()
    {
        // test_acesso.py: o escopo sobe a hierarquia, como o breadcrumb.
        var m = new MundoDeAcesso();
        var dominio = m.Dominio("Crédito");
        var sub = m.Subdominio("Originação", dominio);
        var outro = m.Dominio("Garantias");
        var local = new Ator(Guid.NewGuid(), Cenario.EmpresaA,
            [AtribuicaoDePapel.NoDominio(Papel.Curador, dominio.Valor, Cenario.Relogio.Hoje.AddDays(-1))]);

        Verificar.Verdadeiro(m.Editar(sub, local).Sucesso, "o subdomínio está dentro do domínio");
        Verificar.Igual("SEM_PERMISSAO", m.Editar(outro, local).Codigo);
    }

    [Teste("concessão com escopo de squad vale nos ativos do squad, e mostra a sigla")]
    public void ConcessaoComEscopoDeSquad()
    {
        var m = new MundoDeAcesso();
        var cred = m.Squad("CRED");
        var pedido = m.Pedir(papel: "techlead");
        Verificar.Igual("ESCOPO_INVALIDO",
            m.Decidir(pedido.Pleito.Id, MundoDeAcesso.Admin, aprovar: true, escopo: "squad", escopoId: Guid.NewGuid(), resposta: "x").Codigo);

        var decidido = m.Exigir(m.Decidir(pedido.Pleito.Id, MundoDeAcesso.Admin, aprovar: true,
            escopo: "squad", escopoId: cred.Id, resposta: "Só nos ativos do seu squad."));
        Verificar.Igual("squad CRED", decidido.EscopoRotulo);

        var quem = m.Como(m.Pessoa(pedido.Pessoa.Id));
        var doSquad = m.Cadastrar("sistema", "Core de Crédito", squad: cred.Id);
        var deFora = m.Cadastrar("sistema", "Core de Garantias");
        Verificar.Verdadeiro(m.Editar(doSquad, quem, squad: cred.Id).Sucesso, "o ativo é do squad");
        Verificar.Igual("SEM_PERMISSAO", m.Editar(deFora, quem).Codigo, "fora do squad o papel não vale");
    }

    [Teste("pleito já decidido não se decide de novo")]
    public void PleitoJaDecididoNaoSeDecideDeNovo()
    {
        var m = new MundoDeAcesso();
        var pedido = m.Pedir();
        m.Exigir(m.Decidir(pedido.Pleito.Id, MundoDeAcesso.Admin, aprovar: true));
        Verificar.Igual("PLEITO_JA_DECIDIDO",
            m.Decidir(pedido.Pleito.Id, MundoDeAcesso.Admin, aprovar: false, resposta: "mudei de ideia").Codigo);
    }

    [Teste("ninguém concede acesso a si mesmo")]
    public void NinguemConcedeAcessoASiMesmo()
    {
        // Segregação de função também no acesso, não só na validação.
        var m = new MundoDeAcesso();
        var meu = m.Pedir(papel: "arquiteto");
        var euAdmin = new Ator(meu.Pessoa.Id, Cenario.EmpresaA,
            [AtribuicaoDePapel.NaEmpresa(Papel.Admin, Cenario.Relogio.Hoje.AddDays(-1))]);
        Verificar.Igual("SEGREGACAO_DE_FUNCAO", m.Decidir(meu.Pleito.Id, euAdmin, aprovar: true).Codigo);
    }

    // ──────────────────────────────────────────────────── quem pode conceder

    [Teste("curador concede tudo, menos admin")]
    public void CuradorConcedeTudoMenosAdmin()
    {
        var hoje = Cenario.Relogio.Hoje;
        Verificar.Verdadeiro(PoliticaDeAutorizacao.PodeConceder(MundoDeAcesso.Curador, Papel.TechLead, hoje).Permitido);
        var recusa = PoliticaDeAutorizacao.PodeConceder(MundoDeAcesso.Curador, Papel.Admin, hoje);
        Verificar.Falso(recusa.Permitido);
        Verificar.Verdadeiro(recusa.Motivo.Contains("administrador"), recusa.Motivo);
        Verificar.Verdadeiro(PoliticaDeAutorizacao.PodeConceder(MundoDeAcesso.Admin, Papel.Admin, hoje).Permitido);
    }

    [Teste("quem não concede não entra na fila de pleitos")]
    public void QuemNaoConcedeNaoEntraNaFila()
    {
        var m = new MundoDeAcesso();
        m.Pedir();
        Verificar.Falso(PoliticaDeAutorizacao.PodeConceder(MundoDeGovernanca.Negocio, null, Cenario.Relogio.Hoje).Permitido);
        Verificar.Igual("SEM_PERMISSAO", m.Fila(MundoDeGovernanca.Negocio).Codigo);

        var fila = m.Exigir(m.Fila(MundoDeAcesso.Curador));
        Verificar.Igual(1, fila.Pendentes);
        Verificar.Igual("Davi Souza", fila.Pleitos[0].Pessoa!.Nome, "a fila mostra quem pediu");
        Verificar.Falso(fila.PodeConcederAdmin, "curador não vê admin entre os papéis a conceder");
        Verificar.Verdadeiro(m.Exigir(m.Fila(MundoDeAcesso.Admin)).PodeConcederAdmin);
    }

    [Teste("curador que tenta conceder admin é recusado com 403")]
    public void CuradorQueTentaConcederAdminERecusado()
    {
        var m = new MundoDeAcesso();
        var pedido = m.Pedir(papel: "admin");
        Verificar.Igual("SEM_PERMISSAO", m.Decidir(pedido.Pleito.Id, MundoDeAcesso.Curador, aprovar: true).Codigo);
        Verificar.Igual(StatusDoPleito.Pendente, m.Pleitos.ObterAsync(pedido.Pleito.Id).Result!.Status, "nada mudou");
    }

    [Teste("a decisão aparece no perfil de quem pediu")]
    public void DecisaoApareceNoPerfil()
    {
        var m = new MundoDeAcesso();
        var pedido = m.Pedir();
        m.Exigir(m.Decidir(pedido.Pleito.Id, MundoDeAcesso.Admin, aprovar: true, resposta: "Ok."));

        var perfil = m.Perfil(m.Como(m.Pessoa(pedido.Pessoa.Id)));
        Verificar.Igual(StatusDoPleito.Aprovado, perfil.Pleitos.Single().Pleito.Status);
        Verificar.Igual("Time técnico", perfil.Papeis.Single().Atribuicao.Papel.Rotulo());
        Verificar.Igual("todo o catálogo", perfil.Papeis.Single().EscopoRotulo);
        Verificar.Contem(perfil.BlocosQueEscreve, CatalogoDeTipos.BlocoAtivosTecnicos);
    }

    [Teste("o primeiro admin vem da configuração, e só enquanto a empresa não tem admin")]
    public void PrimeiroAdministradorPelaConfiguracao()
    {
        var m = new MundoDeAcesso(new RegrasDeAcesso(new HashSet<string> { "A000001", "A000002" }));
        var primeiro = m.Exigir(m.Solicitar("a000001", "Ana Admin", "ana@empresa.com", "0001", "admin"));
        Verificar.Igual(StatusDoPleito.Aprovado, primeiro.Pleito.Status);
        Verificar.Igual<Guid?>(null, primeiro.Pleito.DecididoPor, "decidido pela configuração, não por uma pessoa");
        Verificar.Contem(m.Pessoa(primeiro.Pessoa.Id).Atribuicoes.Select(a => a.Papel), Papel.Admin);

        var segundo = m.Exigir(m.Solicitar("A000002", "Beto Admin", "beto@empresa.com", "0001", "admin"));
        Verificar.Igual(StatusDoPleito.Pendente, segundo.Pleito.Status, "já há admin: o segundo passa pelo rito");
    }

    // ───────────────────────────────────────── sem pessoa e modo de leitura

    [Teste("sem pessoa identificada, a escrita manda ao perfil")]
    public void SessaoSemPessoaEMandadaAoPerfil()
    {
        var m = new MundoDeAcesso();
        var dominio = m.Dominio("Crédito");
        Verificar.Igual(Recusa.PessoaNaoIdentificada, m.Editar(dominio, Ator.Anonimo(Cenario.EmpresaA)).Codigo);
    }

    [Teste("modo de leitura recusa na rota, até para admin")]
    public void ModoLeituraTambemRecusaNaRota()
    {
        var m = new MundoDeAcesso();
        var dominio = m.Dominio("Crédito");
        var leitor = MundoDeAcesso.Admin with { ModoLeitura = true };

        Verificar.Igual(Recusa.ModoLeitura, m.AnexarEvidencia(dominio, leitor).Codigo);
        Verificar.Vazio(m.Repo.ObterAsync(dominio).Result!.Evidencias, "nada gravado");
        Verificar.Vazio(PoliticaDeAutorizacao.BlocosQueEscreve(leitor, Cenario.Relogio.Hoje), "o assistente não oferece bloco");
        Verificar.Falso(PoliticaDeAutorizacao.PodeConceder(leitor, null, Cenario.Relogio.Hoje).Permitido, "nem concede");
    }

    [Teste("o ativo guarda o squad; squad inexistente ou inativo não recebe ativo novo")]
    public void AtivoGuardaOSquad()
    {
        var m = new MundoDeAcesso();
        var cred = m.Squad("CRED");
        var gar = m.Squad("GAR");
        var id = m.Cadastrar("sistema", "Core de Crédito", squad: cred.Id);
        Verificar.Igual<Guid?>(cred.Id, m.Ativo(id).IdSquad);

        Verificar.Igual("SQUAD_NAO_ENCONTRADA", m.CadastrarComo(MundoDeAcesso.Curador, "sistema", "Fantasma", squad: Guid.NewGuid()).Codigo);
        m.Exigir(new CadastroDeSquads(m.Acesso, ContextoDeTenantFixo.Para(Cenario.EmpresaA), new AtorFixo(MundoDeAcesso.Curador), m.Repo, Cenario.Relogio)
            .InativarAsync(gar.Id).Result);
        Verificar.Igual("SQUAD_INATIVA", m.CadastrarComo(MundoDeAcesso.Curador, "sistema", "Novo", squad: gar.Id).Codigo);

        // Inativar o squad não desfaz o vínculo: editar o ativo com o mesmo squad continua valendo.
        m.Exigir(new CadastroDeSquads(m.Acesso, ContextoDeTenantFixo.Para(Cenario.EmpresaA), new AtorFixo(MundoDeAcesso.Curador), m.Repo, Cenario.Relogio)
            .InativarAsync(cred.Id).Result);
        Verificar.Verdadeiro(m.Editar(id, MundoDeAcesso.Curador, squad: cred.Id).Sucesso);
        Verificar.Verdadeiro(m.Editar(id, MundoDeAcesso.Curador).Sucesso, "tirar do squad");
        Verificar.Igual<Guid?>(null, m.Ativo(id).IdSquad);
    }

    [Teste("edição de ativo existente respeita o bloco do tipo")]
    public void EdicaoDeAtivoExistenteRespeitaOBloco()
    {
        var m = new MundoDeAcesso();
        var sistema = m.Cadastrar("sistema", "Core de Crédito");
        Verificar.Igual("SEM_PERMISSAO", m.Editar(sistema, MundoDeGovernanca.Negocio).Codigo, "negócio não edita ativo técnico");
        Verificar.Verdadeiro(m.Editar(sistema, MundoDeGovernanca.TechLead).Sucesso);
    }
}

/// <summary>Pessoas, pleitos e um catálogo em memória, montados como no composition root.</summary>
internal sealed class MundoDeAcesso(RegrasDeAcesso? regras = null)
{
    public static readonly Ator Admin = Cenario.Ator(Papel.Admin);
    public static readonly Ator Curador = MundoDeGovernanca.Curador;

    private readonly ArmazemEmMemoria _armazem = new();
    private readonly ContextoDeTenantFixo _tenant = ContextoDeTenantFixo.Para(Cenario.EmpresaA);
    private readonly MetamodeloPadrao _metamodelo = new();
    private readonly RegrasDeAcesso _regras = regras ?? RegrasDeAcesso.Nenhuma;

    public RepositorioDeAtivosEmMemoria Repo => new(_armazem, _tenant);
    public RepositorioDeAcessoEmMemoria Acesso => new(_armazem, _tenant);
    public IRepositorioDePessoas Pessoas => Acesso;
    public IRepositorioDePleitos Pleitos => Acesso;

    public Pessoa Pessoa(Guid id) => Pessoas.ObterAsync(id).Result!;

    public Ator Como(Pessoa pessoa) => pessoa.ComoAtor(Cenario.Relogio.Hoje);

    public Resultado<SolicitarAcesso.Saida> Solicitar(
        string matricula = "C123456", string nome = "Davi Souza", string email = "davi@empresa.com",
        string unidade = "0427", string papel = "techlead", string justificativa = "Vou cadastrar APIs.") =>
        new SolicitarAcesso(Pessoas, Pleitos, _tenant, Repo, _regras, Cenario.Relogio)
            .ExecutarAsync(new ComandoSolicitarAcesso(matricula, nome, email, unidade, papel, justificativa)).Result;

    public SolicitarAcesso.Saida Pedir(string papel = "techlead") => Exigir(Solicitar(papel: papel));

    public Resultado<PleitoLegivel> Decidir(Guid pleito, Ator quem, bool aprovar, string? resposta = null,
                                            string? papel = null, string? escopo = null, Guid? escopoId = null) =>
        new DecidirPleito(Pleitos, Pessoas, Repo, Acesso, new AtorFixo(quem), Repo, Cenario.Relogio)
            .ExecutarAsync(new ComandoDecidirPleito(pleito, aprovar, resposta, papel, escopo, escopoId)).Result;

    public Resultado<ListarPleitos.Saida> Fila(Ator quem) =>
        new ListarPleitos(Pleitos, Pessoas, Repo, Acesso, new AtorFixo(quem), Cenario.Relogio).ExecutarAsync().Result;

    public ConsultarPerfil.Saida Perfil(Ator quem) =>
        new ConsultarPerfil(Pessoas, Pleitos, Repo, Acesso, new AtorFixo(quem), Cenario.Relogio).ExecutarAsync().Result;

    public Squad Squad(string sigla) =>
        Exigir(new CadastroDeSquads(Acesso, _tenant, new AtorFixo(Curador), Repo, Cenario.Relogio)
            .CriarAsync(sigla, $"Squad {sigla}", "Time de teste.").Result);

    public IdDeAtivo Dominio(string nome) => Cadastrar("dominio", nome);

    public IdDeAtivo Subdominio(string nome, IdDeAtivo pai) => Cadastrar("subdominio", nome, pai);

    public IdDeAtivo Cadastrar(string tipo, string nome, IdDeAtivo? pai = null, Guid? squad = null) =>
        Exigir(CadastrarComo(Curador, tipo, nome, pai, squad));

    public Resultado<IdDeAtivo> CadastrarComo(Ator quem, string tipo, string nome, IdDeAtivo? pai = null, Guid? squad = null) =>
        new CadastrarAtivo(Repo, _metamodelo, _tenant, new AtorFixo(quem), Repo, Repo, Acesso, Cenario.Relogio)
            .ExecutarAsync(new ComandoCadastrarAtivo(tipo, nome, "Descrição do ativo.", Criticidade.Media, pai, IdSquad: squad)).Result;

    public Resultado<bool> Editar(IdDeAtivo id, Ator quem, Guid? squad = null) =>
        new EditarAtivo(Repo, _metamodelo, _tenant, new AtorFixo(quem), Repo, Repo, Acesso, Cenario.Relogio)
            .ExecutarAsync(new ComandoEditarAtivo(id, "Nome revisto", "Descrição revista.", Criticidade.Media, null, squad)).Result;

    public Ativo Ativo(IdDeAtivo id) => Repo.ObterAsync(id).Result!;

    public Resultado<bool> AnexarEvidencia(IdDeAtivo id, Ator quem) =>
        new AnexarEvidencia(Repo, _metamodelo, _tenant, new AtorFixo(quem), Repo, Repo, Cenario.Relogio)
            .ExecutarAsync(new ComandoAnexarEvidencia(id, "link", "X", null)).Result;

    public T Exigir<T>(Resultado<T> r) =>
        r.Sucesso ? r.Valor! : throw new FalhaDeVerificacao($"passo do cenário recusado: {r.Codigo} — {r.Mensagem}");
}
