using Jornada.Aplicacao;
using Jornada.Aplicacao.Catalogo.Comandos;
using Jornada.Aplicacao.Catalogo.Consultas;
using Jornada.Aplicacao.Governanca;
using Jornada.Adaptadores.Memoria;
using Jornada.Aplicacao.Portas;
using Jornada.Dominio.Acesso;
using Jornada.Dominio.Catalogo;
using Jornada.Dominio.Governanca;
using Jornada.Dominio.Testes.Harness;

namespace Jornada.Dominio.Testes.Casos;

/// <summary>
/// Portado de <c>tests/test_fluxo.py</c> na parte da fila de validação e da
/// "Minha mesa": assumir e liberar, a ordem da fila, o painel do validador e o
/// que é meu.
/// </summary>
[Suite("Validação e mesa: fila, assumir, painel do validador")]
public sealed class MesaTestes
{
    private static readonly Ator Tina = Cenario.Ator(Papel.TechLead);
    private static readonly Ator Tiago = Cenario.Ator(Papel.TechLead);

    [Teste("assumir e liberar uma validação")]
    public void AssumirELiberarUmaValidacao()
    {
        var m = new MundoDeGovernanca();
        var id = m.SistemaCompleto();
        m.Submeter(id);
        var v = m.UnicaAberta(id);

        Verificar.Igual(Tina.IdPessoa, Exigir(Atribuicao(m, Tina).AssumirAsync(v).Result).AtribuidaA);
        Verificar.Igual("VALIDACAO_JA_ASSUMIDA", Atribuicao(m, Tiago).AssumirAsync(v).Result.Codigo, "já está com outra pessoa");
        Verificar.Igual(v, Fila(m, Tina).ExecutarAsync(somenteMinhas: true).Result.Itens.Single().Validacao.Id);
        Verificar.Vazio(Fila(m, Tina).ExecutarAsync(apenasLivres: true).Result.Itens, "assumida sai da fila livre");

        Verificar.Igual("VALIDACAO_JA_ASSUMIDA", Atribuicao(m, Tiago).LiberarAsync(v).Result.Codigo, "só quem assumiu libera");
        Exigir(Atribuicao(m, Tina).LiberarAsync(v).Result);
        Verificar.Igual(v, Fila(m, Tina).ExecutarAsync(apenasLivres: true).Result.Itens.Single().Validacao.Id, "volta para a fila livre");
    }

    [Teste("quem não decide não assume")]
    public void QuemNaoDecideNaoAssume()
    {
        var m = new MundoDeGovernanca();
        var id = m.SistemaCompleto();
        m.Submeter(id);
        Verificar.Igual("SEM_PERMISSAO", Atribuicao(m, MundoDeGovernanca.Curador).AssumirAsync(m.UnicaAberta(id)).Result.Codigo,
            "curador não decide validação, e portanto não a assume");
    }

    [Teste("a fila põe a mais crítica primeiro e conta as abas")]
    public void FilaOrdenaEConta()
    {
        var m = new MundoDeGovernanca();
        var baixa = m.SistemaCompleto("Sistema baixo", Criticidade.Baixa);
        var alta = m.SistemaCompleto("Sistema alto", Criticidade.Alta);
        m.Submeter(baixa);
        m.Submeter(alta);
        Exigir(Atribuicao(m, Tina).AssumirAsync(m.UnicaAberta(baixa)).Result);

        var fila = Fila(m, Tina).ExecutarAsync().Result;
        Verificar.Igual("Sistema alto,Sistema baixo", string.Join(",", fila.Itens.Select(i => i.Ativo.Nome)));
        Verificar.Igual(2, fila.Contagem["tecnica"]);
        Verificar.Igual(1, fila.Contagem["minhas"]);
        Verificar.Vazio(Fila(m, Tina).ExecutarAsync(EtapaValidacao.Negocial).Result.Itens, "a aba da etapa filtra");
    }

    [Teste("o prazo da etapa é o SLA da política, e o restante se lê em horas ou dias")]
    public void PrazoEOSlaDaPolitica()
    {
        var m = new MundoDeGovernanca();
        var id = m.SistemaCompleto();
        m.Submeter(id);
        var v = m.Abertas(id).Single();
        var sla = PoliticasDeGovernanca.Padrao().Para("sistema", Criticidade.Media).SlaHoras;
        Verificar.Igual(Cenario.Relogio.Agora.AddHours(sla), v.Prazo);

        var agora = Cenario.Relogio.Agora;
        Verificar.Igual("vencido", Sla.Restante(agora.AddMinutes(-1), agora));
        Verificar.Igual("47h", Sla.Restante(agora.AddHours(47.5), agora));
        Verificar.Igual("3d", Sla.Restante(agora.AddHours(72), agora));
        Verificar.Igual("—", Sla.Restante(null, agora));
    }

    [Teste("o painel do validador mostra evidências e responsáveis, e avisa a primeira revisão")]
    public void PainelMostraEvidenciasEResponsaveis()
    {
        var m = new MundoDeGovernanca();
        var id = m.SistemaCompleto();
        m.AnexarEvidencia(id);
        m.Submeter(id);
        var painel = Exigir(Painel(m).ExecutarAsync(m.UnicaAberta(id)).Result);

        Verificar.Igual("ADR de criação", painel.Ficha.Evidencias.Single().Titulo);
        Verificar.Igual("OwnerTecnico", painel.Ficha.Responsaveis.Single().Papel);
        Verificar.Verdadeiro(painel.PrimeiraRevisao, "primeira revisão deste ativo");
        Verificar.Igual(null, painel.Mudancas);
    }

    [Teste("o painel mostra o que mudou na segunda revisão, com o rótulo do campo")]
    public void PainelMostraOQueMudouNaSegundaRevisao()
    {
        var m = new MundoDeGovernanca();
        var id = m.SistemaPublicado();
        Exigir(m.AbrirRevisao(id));
        Exigir(new EditarAtivo(m.Repo, m.Metamodelo, m.Tenant, new AtorFixo(MundoDeGovernanca.Curador), m.Repo, m.Repo, m.Squads, Cenario.Relogio)
            .ExecutarAsync(new ComandoEditarAtivo(id, "Core Bancário", "Calcula condições e taxas", Criticidade.Media, null)).Result);
        m.Submeter(id);

        var painel = Exigir(Painel(m).ExecutarAsync(m.UnicaAberta(id)).Result);
        Verificar.Falso(painel.PrimeiraRevisao);
        Verificar.Igual(2, painel.Mudancas!.B, "revisão #2");
        var descricao = painel.Mudancas.Diferencas.Single(d => d.Campo == CamposDoRetrato.Descricao);
        Verificar.Igual("Descrição", descricao.Rotulo, "rótulo legível, não o nome do campo");
        Verificar.Igual("Calcula condições e taxas", descricao.Depois);
        Verificar.Falso(painel.Mudancas.Diferencas.Any(d => d.Campo.Contains("revisao", StringComparison.OrdinalIgnoreCase)),
            "campo mecânico fica fora do resumo");
    }

    [Teste("minha mesa separa o que é meu")]
    public void MinhaMesaSeparaOQueEMeu()
    {
        var m = new MundoDeGovernanca();
        var arquiteta = Cenario.Ator(Papel.Arquiteto);
        var submetido = m.SistemaCompleto("Simular Financiamento", quem: arquiteta);
        m.Submeter(submetido);
        var rascunho = m.SistemaCompleto("Rascunho da arquiteta", quem: arquiteta);
        m.SistemaCompleto("Garantias");                    // de outra pessoa
        var alheio = m.SistemaCompleto("Alheio");
        m.Submeter(alheio);
        Exigir(Atribuicao(m, arquiteta).AssumirAsync(m.UnicaAberta(alheio)).Result);

        var mesa = new MinhaMesa(Fila(m, arquiteta), m.Repo, new AtorFixo(arquiteta)).ExecutarAsync().Result;
        Verificar.Igual(m.UnicaAberta(alheio), mesa.MinhasValidacoes.Single().Validacao.Id);
        Verificar.Igual("Simular Financiamento", mesa.FilaLivre.Single().Ativo.Nome, "a que eu assumi não está na fila livre");
        Verificar.Igual(rascunho, mesa.MeusRascunhos.Single().Id, "Garantias é de outra pessoa");
        Verificar.Igual("Simular Financiamento", mesa.AguardandoDecisao.Single().Nome);
    }

    private static ConsultarFila Fila(MundoDeGovernanca m, Ator quem) =>
        new(m.Repo, m.Repo, new AtorFixo(quem), Cenario.Relogio);

    private static AtribuirValidacao Atribuicao(MundoDeGovernanca m, Ator quem) =>
        new(m.Repo, m.Repo, m.Metamodelo, m.Tenant, new AtorFixo(quem), m.Repo, Cenario.Relogio);

    private static PainelDoValidador Painel(MundoDeGovernanca m) =>
        new(m.Repo, new ObterVisaoDoAtivo(m.Repo, m.Repo), m.PreCheck(), new CompararRevisoes(m.Repo, m.Repo));

    private static T Exigir<T>(Resultado<T> r) =>
        r.Sucesso ? r.Valor! : throw new FalhaDeVerificacao($"passo do cenário recusado: {r.Codigo} — {r.Mensagem}");
}
