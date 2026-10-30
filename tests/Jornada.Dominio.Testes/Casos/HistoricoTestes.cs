using Jornada.Dominio.Catalogo;
using Jornada.Dominio.Comum;
using Jornada.Dominio.Testes.Harness;

namespace Jornada.Dominio.Testes.Casos;

/// <summary>
/// O histórico do ativo (ADR-0017): o retrato de cada revisão submetida, com
/// hash; a comparação entre revisões; e a trilha de auditoria. Equivalem a
/// <c>servicos._snapshot</c>, <c>comparar_revisoes</c> e <c>auditar</c> do
/// painel-ddd, que não tem teste próprio para eles.
/// </summary>
[Suite("Histórico: retrato da revisão, comparação e auditoria")]
public sealed class HistoricoTestes
{
    // ── retrato da revisão ────────────────────────────────────────────────────

    [Teste("a submissão grava o retrato da revisão, com SHA-256 que confere")]
    public void SubmissaoGravaRetrato()
    {
        var m = new MundoDeGovernanca();
        var id = m.SistemaCompleto();

        m.Submeter(id);

        var revisao = m.Historico(id).Revisoes.Single();
        Verificar.Igual(1, revisao.Numero);
        Verificar.Igual(64, revisao.Hash.Length, "SHA-256 em hexadecimal");
        Verificar.Verdadeiro(revisao.Integra, "o hash confere com o conteúdo");
        Verificar.Igual(null, revisao.PublicadaEm, "em validação ainda não foi publicada");
    }

    [Teste("aprovada, a revisão ganha a data de publicação; reprovada, não")]
    public void PublicacaoMarcaORetrato()
    {
        var m = new MundoDeGovernanca();
        var id = m.SistemaCompleto();

        m.Submeter(id);
        m.Decidir(m.UnicaAberta(id), aprovada: false, MundoDeGovernanca.TechLead);
        m.Submeter(id);
        m.Decidir(m.UnicaAberta(id), aprovada: true, MundoDeGovernanca.TechLead);

        var revisoes = m.Historico(id).Revisoes;
        Verificar.Igual(2, revisoes[0].Numero, "a mais recente primeiro");
        Verificar.Verdadeiro(revisoes[0].PublicadaEm is not null, "a revisão 2 foi publicada");
        Verificar.Igual(null, revisoes[1].PublicadaEm, "a revisão 1 foi reprovada");
    }

    [Teste("o mesmo cadastro gera o mesmo hash; outro cadastro, outro hash")]
    public void HashDeterministico()
    {
        var ativo = Cenario.AtivoCompleto();
        ativo.Submeter("v1", Guid.NewGuid(), Cenario.Relogio);

        var a = RevisaoDoAtivo.Retratar(ativo, [], "v1", Guid.NewGuid(), Cenario.Relogio);
        var b = RevisaoDoAtivo.Retratar(ativo, [], "outro motivo", Guid.NewGuid(), Cenario.Relogio);
        var comRelacao = RevisaoDoAtivo.Retratar(ativo,
            [new RelacaoRetratada(IdDeAtivo.Novo(), "depende_de", Criticidade.Alta)], "v1", Guid.NewGuid(), Cenario.Relogio);

        Verificar.Igual(a.Hash, b.Hash, "o hash é do cadastro, não do motivo nem de quem submeteu");
        Verificar.Verdadeiro(a.Hash != comRelacao.Hash, "uma relação a mais muda o retrato");
    }

    [Teste("conteúdo alterado depois de gravado não confere com o hash")]
    public void AdulteracaoAparece()
    {
        var ativo = Cenario.AtivoCompleto();
        ativo.Submeter("v1", Guid.NewGuid(), Cenario.Relogio);
        var original = RevisaoDoAtivo.Retratar(ativo, [], "v1", Guid.NewGuid(), Cenario.Relogio);

        var adulterada = RevisaoDoAtivo.Reidratar(original.Ativo, original.Empresa, original.Numero,
            original.Conteudo.Replace("Core Bancário", "Core Adulterado"), original.Hash,
            original.Motivo, original.IdAutor, original.CriadaEm, original.PublicadaEm);

        Verificar.Verdadeiro(original.Integra);
        Verificar.Falso(adulterada.Integra, "o conteúdo não bate mais com o hash gravado");
    }

    [Teste("a mesma revisão não é publicada duas vezes")]
    public void PublicaUmaVezSo()
    {
        var ativo = Cenario.AtivoCompleto();
        ativo.Submeter("v1", Guid.NewGuid(), Cenario.Relogio);
        var revisao = RevisaoDoAtivo.Retratar(ativo, [], "v1", Guid.NewGuid(), Cenario.Relogio);

        revisao.MarcarPublicada(Cenario.Relogio);
        var erro = Verificar.Lanca<ErroDeDominio>(() => revisao.MarcarPublicada(Cenario.Relogio));
        Verificar.Igual("REVISAO_JA_PUBLICADA", erro.Codigo);
    }

    // ── comparação ────────────────────────────────────────────────────────────

    [Teste("comparar a última revisão com a anterior mostra o que mudou")]
    public void ComparaAsDuasUltimas()
    {
        var m = new MundoDeGovernanca();
        var id = m.SistemaPublicado();
        var outro = m.SistemaPublicado("Pagamentos");

        m.AbrirRevisao(id);
        m.Editar(id, "Core Bancário v2");
        m.Relacionar(id, outro, Criticidade.Alta);
        m.Submeter(id);

        var r = m.Comparar(id);
        Verificar.Verdadeiro(r.Sucesso, r.Mensagem ?? "");
        Verificar.Igual(1, r.Valor!.A);
        Verificar.Igual(2, r.Valor.B);

        var nome = r.Valor.Diferencas.Single(d => d.Campo == CamposDoRetrato.Nome);
        Verificar.Igual("Nome", nome.Rotulo);
        Verificar.Igual("Core Bancário", nome.Antes);
        Verificar.Igual("Core Bancário v2", nome.Depois);

        var relacoes = r.Valor.Diferencas.Single(d => d.Campo == CamposDoRetrato.Relacoes);
        Verificar.Igual("—", relacoes.Antes);
        Verificar.Igual("depende_de → SIS-0002 Pagamentos (alta)", relacoes.Depois, "o destino aparece pelo código e nome");

        Verificar.Falso(r.Valor.Diferencas.Any(d => d.Campo == CamposDoRetrato.Criticidade), "o que não mudou não aparece");
    }

    [Teste("sem revisão anterior, não há o que comparar")]
    public void PrimeiraRevisaoNaoCompara()
    {
        var m = new MundoDeGovernanca();
        var id = m.SistemaCompleto();
        m.Submeter(id);

        Verificar.Igual("REVISAO_NAO_ENCONTRADA", m.Comparar(id).Codigo);
    }

    // ── auditoria ─────────────────────────────────────────────────────────────

    [Teste("a trilha registra o ciclo inteiro, do mais recente para o mais antigo, com quem agiu")]
    public void TrilhaDoCiclo()
    {
        var m = new MundoDeGovernanca();
        var id = m.SistemaPublicado();

        var trilha = m.Historico(id).Auditoria;
        Verificar.Igual(
            string.Join(",", [AcoesAuditadas.Publicar, AcoesAuditadas.Validar, AcoesAuditadas.Submeter,
                AcoesAuditadas.DefinirResponsavel, AcoesAuditadas.Criar]),
            string.Join(",", trilha.Select(a => a.Acao)));

        Verificar.Igual(MundoDeGovernanca.TechLead.IdPessoa, trilha[0].IdPessoa, "quem decidiu publicou");
        Verificar.Igual(MundoDeGovernanca.Curador.IdPessoa, trilha[^1].IdPessoa, "quem cadastrou");
        Verificar.Igual("SIS-0001", trilha[^1].Depois!["codigo"]);
        Verificar.Igual("aprovada", trilha[1].Depois!["decisao"]);
    }

    [Teste("a edição registra só o que mudou, com antes e depois")]
    public void EdicaoRegistraSoOQueMudou()
    {
        var m = new MundoDeGovernanca();
        var id = m.SistemaCompleto();

        m.Editar(id, "Core Bancário 2");

        var alteracao = m.Historico(id).Auditoria[0];
        Verificar.Igual(AcoesAuditadas.Alterar, alteracao.Acao);
        Verificar.Igual("Core Bancário", alteracao.Antes!["nome"]);
        Verificar.Igual("Core Bancário 2", alteracao.Depois!["nome"]);
        Verificar.Falso(alteracao.Depois.ContainsKey("criticidade"), "a criticidade não mudou");
    }

    [Teste("a trilha é por ativo")]
    public void TrilhaPorAtivo()
    {
        var m = new MundoDeGovernanca();
        var a = m.SistemaCompleto();
        var b = m.SistemaCompleto("Pagamentos");
        m.Editar(a, "Core Bancário 2");

        Verificar.Falso(m.Historico(b).Auditoria.Any(r => r.Acao == AcoesAuditadas.Alterar),
            "a edição de A não aparece em B");
    }
}
