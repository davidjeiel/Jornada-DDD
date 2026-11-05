using Jornada.Adaptadores.Memoria;
using Jornada.Aplicacao.Catalogo.Comandos;
using Jornada.Aplicacao.Notificacoes;
using Jornada.Aplicacao.Portas;
using Jornada.Dominio.Acesso;
using Jornada.Dominio.Catalogo;
using Jornada.Dominio.Comum;
using Jornada.Dominio.Notificacoes;
using Jornada.Dominio.Testes.Harness;

namespace Jornada.Dominio.Testes.Casos;

/// <summary>Portado de <c>tests/test_acesso.py</c> e <c>test_acesso_pleito.py</c> na parte das notificações.</summary>
[Suite("Notificações: avisos na transação do fato, caixa, preferências, despacho e vigia de SLA")]
public sealed class NotificacoesTestes
{
    private static int _matricula = 100000;

    private static Pessoa Registrar(IRepositorioDePessoas pessoas, string nome, Papel papel)
    {
        var pessoa = Pessoa.Cadastrar(Cenario.EmpresaA,
            DadosDeCadastro.Conferir($"C{Interlocked.Increment(ref _matricula)}", nome, $"{nome.Split(' ')[0].ToLowerInvariant()}@empresa.com", "0427"), Cenario.Relogio);
        pessoa.Conceder(AtribuicaoDePapel.NaEmpresa(papel, Cenario.Relogio.Hoje.AddDays(-30)));
        pessoas.AdicionarAsync(pessoa).Wait();
        return pessoa;
    }

    private static IReadOnlyList<Notificacao> Caixa(MundoDeGovernanca m, Guid pessoa) => m.Notificacoes.DaPessoaAsync(pessoa).Result;

    private sealed record Elenco(Pessoa TechLead, Pessoa Negocio, Pessoa Consulta, Pessoa Admin);

    private static Elenco Montar(MundoDeGovernanca m) => new(
        Registrar(m.Squads, "Tiago Lima", Papel.TechLead), Registrar(m.Squads, "Nina Rocha", Papel.Negocio),
        Registrar(m.Squads, "Caio Prado", Papel.Consulta), Registrar(m.Squads, "Ana Ferreira", Papel.Admin));

    [Teste("a submissão avisa quem pode decidir, e só quem pode")]
    public void SubmissaoAvisaQuemPodeDecidir()
    {
        var m = new MundoDeGovernanca();
        var elenco = Montar(m);
        var id = m.SistemaCompleto();
        m.Submeter(id);

        var aviso = Caixa(m, elenco.TechLead.Id).Single();
        Verificar.Igual(TiposDeNotificacao.RevisaoSubmetida, aviso.Tipo);
        Verificar.Igual($"/validacoes?validacao={m.UnicaAberta(id)}", aviso.Url);
        Verificar.Verdadeiro(aviso.Titulo.Contains("aguarda validação tecnica"), "aguarda validação tecnica");
        Verificar.Igual(1, Caixa(m, elenco.Admin.Id).Count, "admin decide toda etapa");
        Verificar.Vazio(Caixa(m, elenco.Consulta.Id), "quem não decide não é incomodado");
        Verificar.Vazio(Caixa(m, elenco.Negocio.Id), "a etapa técnica não é do negócio");
    }

    [Teste("o aviso fica na outbox, e o despacho é idempotente e só do admin")]
    public void NotificacaoEntraNaMesmaTransacaoEFicaNaOutbox()
    {
        var m = new MundoDeGovernanca();
        Montar(m);
        m.Submeter(m.SistemaCompleto());
        var pendentes = m.Notificacoes.PendentesDeEnvioAsync().Result.Count;
        Verificar.Verdadeiro(pendentes > 0);

        DespacharNotificacoes Despacho(Ator quem) => new(m.Notificacoes, m.Tenant, new AtorFixo(quem), m.Repo, Cenario.Relogio);
        Verificar.Igual("SEM_PERMISSAO", Despacho(MundoDeGovernanca.Curador).ExecutarAsync().Result.Codigo);
        var primeiro = Despacho(MundoDeAcesso.Admin).ExecutarAsync().Result.Valor!;
        Verificar.Igual(pendentes, primeiro.Despachadas);
        Verificar.Igual(pendentes, primeiro.PorCanal[CanaisDeNotificacao.App]);
        Verificar.Igual(0, Despacho(MundoDeAcesso.Admin).ExecutarAsync().Result.Valor!.Despachadas, "idempotente");
    }

    [Teste("preferência desligada não gera notificação")]
    public void PreferenciaDesligadaNaoGeraNotificacao()
    {
        var m = new MundoDeGovernanca();
        var elenco = Montar(m);
        Verificar.Verdadeiro(new PreferenciasDoNotificado(m.Preferencias, new AtorFixo(elenco.TechLead.ComoAtor(Cenario.Relogio.Hoje)))
            .SalvarAsync([]).Result.Sucesso);
        m.Submeter(m.SistemaCompleto());
        Verificar.Vazio(Caixa(m, elenco.TechLead.Id));
        Verificar.Igual(1, Caixa(m, elenco.Admin.Id).Count, "a preferência é de cada um");
    }

    [Teste("preferências: o padrão é tudo no app, e e-mail ainda não se liga")]
    public void PreferenciasValidamTipoECanal()
    {
        var m = new MundoDeGovernanca();
        var caso = new PreferenciasDoNotificado(m.Preferencias, new AtorFixo(MundoDeGovernanca.Curador));
        var padrao = caso.ObterAsync().Result.Valor!;
        Verificar.Verdadeiro(TiposDeNotificacao.Todos.All(t => padrao.QuerReceber(t.Chave, CanaisDeNotificacao.App)));
        Verificar.Falso(padrao.QuerReceber(TiposDeNotificacao.SlaVencido, CanaisDeNotificacao.Email));
        Verificar.Igual("CANAL_INDISPONIVEL", caso.SalvarAsync([(TiposDeNotificacao.SlaVencido, CanaisDeNotificacao.Email)]).Result.Codigo);
        Verificar.Igual("TIPO_DE_NOTIFICACAO_INVALIDO", caso.SalvarAsync([("inventado", CanaisDeNotificacao.App)]).Result.Codigo);
        Verificar.Igual("PESSOA_NAO_IDENTIFICADA", new PreferenciasDoNotificado(m.Preferencias, new AtorFixo(Ator.Anonimo(Cenario.EmpresaA)))
            .ObterAsync().Result.Codigo);
    }

    [Teste("a rejeição avisa quem submeteu, com o parecer")]
    public void RejeicaoAvisaOAutor()
    {
        var m = new MundoDeGovernanca();
        var id = m.SistemaCompleto();
        m.Submeter(id);
        var r = new DecidirValidacao(m.Repo, m.Repo, m.Metamodelo, m.Tenant, new AtorFixo(MundoDeGovernanca.TechLead), m.Repo,
            m.Repo, m.Repo, m.Repo, m.Notificador, Cenario.Relogio)
            .ExecutarAsync(new ComandoDecidirValidacao(m.UnicaAberta(id), false, "faltou contexto")).Result;
        Verificar.Verdadeiro(r.Sucesso);

        var aviso = Caixa(m, MundoDeGovernanca.Curador.IdPessoa).Single(n => n.Tipo == TiposDeNotificacao.RevisaoRejeitada);
        Verificar.Verdadeiro(aviso.Corpo.Contains("faltou contexto"), "faltou contexto");
        Verificar.Igual($"/ativos/{id}", aviso.Url);
    }

    [Teste("a publicação avisa quem responde pelos consumidores")]
    public void PublicacaoAvisaOsResponsaveisDosConsumidores()
    {
        var m = new MundoDeGovernanca();
        var consumidor = m.SistemaCompleto("Portal");
        var dono = m.Ativo(consumidor).Responsaveis.Single().IdPessoa;
        var publicado = m.SistemaCompleto("Core");
        m.Repo.RelacionarAsync(consumidor, publicado, "consome").Wait();
        m.Submeter(publicado);
        m.Decidir(m.UnicaAberta(publicado), aprovada: true, MundoDeGovernanca.TechLead);

        var aviso = Caixa(m, dono).Single(n => n.Tipo == TiposDeNotificacao.AtivoPublicado);
        Verificar.Verdadeiro(aviso.Titulo.Contains("Core publicou uma nova revisão"), "Core publicou uma nova revisão");
    }

    [Teste("o vigia de SLA avisa uma vez por dia, a quem assumiu ou a quem pode decidir")]
    public void VigiaDeSlaNaoRepeteOAlertaNoMesmoDia()
    {
        var m = new MundoDeGovernanca();
        var elenco = Montar(m);
        var id = m.SistemaCompleto();
        m.Submeter(id);
        var prazo = m.Abertas(id).Single().Prazo!.Value;

        VigiarSla Vigia(DateTimeOffset agora, Ator? quem = null) => new(m.Repo, m.Repo, m.Metamodelo, m.Notificador, m.Tenant,
            new AtorFixo(quem ?? MundoDeAcesso.Admin), m.Repo, new RelogioFixo(agora));

        Verificar.Igual("SEM_PERMISSAO", Vigia(prazo.AddHours(2), MundoDeGovernanca.Curador).ExecutarAsync().Result.Codigo);
        Verificar.Igual(0, Vigia(prazo.AddDays(-3)).ExecutarAsync().Result.Valor!.SlaVencendo, "longe do prazo, nada");

        var vencendo = Vigia(prazo.AddHours(-2)).ExecutarAsync().Result.Valor!;
        Verificar.Igual(2, vencendo.SlaVencendo, "techlead e admin");

        var primeiro = Vigia(prazo.AddHours(2)).ExecutarAsync().Result.Valor!;
        var segundo = Vigia(prazo.AddHours(3)).ExecutarAsync().Result.Valor!;
        Verificar.Igual(2, primeiro.SlaVencido);
        Verificar.Igual(0, segundo.SlaVencido, "mesmo dia, não repete");
        Verificar.Igual(2, Vigia(prazo.AddDays(1).AddHours(2)).ExecutarAsync().Result.Valor!.SlaVencido, "no dia seguinte, avisa de novo");
        Verificar.Vazio(Caixa(m, elenco.Consulta.Id));
    }

    [Teste("etapa assumida: o vigia avisa só quem assumiu")]
    public void VigiaAvisaQuemAssumiu()
    {
        var m = new MundoDeGovernanca();
        var elenco = Montar(m);
        var id = m.SistemaCompleto();
        m.Submeter(id);
        var etapa = m.Abertas(id).Single();
        etapa.Assumir(elenco.TechLead.Id);
        var r = new VigiarSla(m.Repo, m.Repo, m.Metamodelo, m.Notificador, m.Tenant, new AtorFixo(MundoDeAcesso.Admin), m.Repo,
            new RelogioFixo(etapa.Prazo!.Value.AddHours(1))).ExecutarAsync().Result.Valor!;
        Verificar.Igual(1, r.SlaVencido);
        Verificar.Verdadeiro(Caixa(m, elenco.TechLead.Id).Any(n => n.Tipo == TiposDeNotificacao.SlaVencido));
        Verificar.Falso(Caixa(m, elenco.Admin.Id).Any(n => n.Tipo == TiposDeNotificacao.SlaVencido));
    }

    [Teste("o sino conta as não lidas; ler uma ou todas zera, e a caixa é só da pessoa")]
    public void SinoMostraAContagemDeNaoLidas()
    {
        var m = new MundoDeGovernanca();
        var elenco = Montar(m);
        m.Submeter(m.SistemaCompleto("Core"));
        m.Submeter(m.SistemaCompleto("Portal"));

        CaixaDeNotificacoes Caso(Pessoa p) => new(m.Notificacoes, new AtorFixo(p.ComoAtor(Cenario.Relogio.Hoje)), m.Repo, Cenario.Relogio);
        var caixa = Caso(elenco.TechLead).ListarAsync().Result.Valor!;
        Verificar.Igual(2, caixa.NaoLidas);
        Verificar.Verdadeiro(caixa.Itens[0].Titulo.Contains("aguarda validação"), "aguarda validação");

        Verificar.Igual("NOTIFICACAO_NAO_ENCONTRADA", Caso(elenco.Admin).MarcarLidaAsync(caixa.Itens[0].Id).Result.Codigo, "não é dela");
        Verificar.Verdadeiro(Caso(elenco.TechLead).MarcarLidaAsync(caixa.Itens[0].Id).Result.Sucesso);
        Verificar.Igual(1, Caso(elenco.TechLead).ContarNaoLidasAsync().Result);
        Verificar.Igual(1, Caso(elenco.TechLead).ListarAsync(apenasNaoLidas: true).Result.Valor!.Itens.Count);
        Verificar.Igual(1, Caso(elenco.TechLead).MarcarTodasLidasAsync().Result.Valor);
        Verificar.Igual(0, Caso(elenco.TechLead).ContarNaoLidasAsync().Result);
        Verificar.Igual(2, Caso(elenco.Admin).ContarNaoLidasAsync().Result, "ler a própria caixa não mexe na dos outros");
        Verificar.Igual(0, new CaixaDeNotificacoes(m.Notificacoes, new AtorFixo(Ator.Anonimo(Cenario.EmpresaA)), m.Repo, Cenario.Relogio)
            .ContarNaoLidasAsync().Result, "sem pessoa, zero");
    }

    [Teste("quem concede é avisado do pleito; admin só por admin")]
    public void QuemConcedeEAvisadoDoPleito()
    {
        var m = new MundoDeAcesso();
        var admin = Registrar(m.Pessoas, "Ana Ferreira", Papel.Admin);
        var curador = Registrar(m.Pessoas, "Ciro Alves", Papel.Curador);
        var negocio = Registrar(m.Pessoas, "Nina Rocha", Papel.Negocio);
        var pedido = m.Pedir("techlead");

        bool Avisado(Pessoa p) => m.Notificacoes.DaPessoaAsync(p.Id).Result.Any(n => n.Tipo == TiposDeNotificacao.AcessoSolicitado);
        Verificar.Verdadeiro(Avisado(admin));
        Verificar.Verdadeiro(Avisado(curador));
        Verificar.Falso(Avisado(negocio));
        Verificar.Verdadeiro(m.Notificacoes.DaPessoaAsync(curador.Id).Result.Single().Titulo.Contains("pleiteia o papel de"), "pleiteia o papel de");

        m.Decidir(pedido.Pleito.Id, m.Como(curador), aprovar: true);
        var resposta = m.Notificacoes.DaPessoaAsync(pedido.Pessoa.Id).Result.Single();
        Verificar.Igual(TiposDeNotificacao.AcessoDecidido, resposta.Tipo);
        Verificar.Verdadeiro(resposta.Titulo.Contains("Acesso concedido"), "Acesso concedido");

        var outro = m.Exigir(m.Solicitar(matricula: "C654321", nome: "Bia Souza", email: "bia@empresa.com", papel: "admin"));
        Verificar.Falso(m.Notificacoes.DaPessoaAsync(curador.Id).Result.Any(n => n.Titulo.Contains("Bia")), "admin, só admin concede");
        Verificar.Verdadeiro(m.Notificacoes.DaPessoaAsync(admin.Id).Result.Any(n => n.Titulo.Contains("Bia")));
        Verificar.Verdadeiro(outro.Pleito.Status == StatusDoPleito.Pendente);
    }
}
