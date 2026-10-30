using Jornada.Adaptadores.Memoria;
using Jornada.Aplicacao.Catalogo.Comandos;
using Jornada.Aplicacao.Catalogo.Consultas;
using Jornada.Aplicacao.Portas;
using Jornada.Dominio.Acesso;
using Jornada.Dominio.Catalogo;
using Jornada.Dominio.Testes.Harness;

namespace Jornada.Dominio.Testes.Casos;

/// <summary>
/// O fluxo de governança de ponta a ponta, pelos CASOS DE USO (submeter,
/// decidir, editar, abrir revisão), com o repositório em memória.
///
/// Os testes de domínio cobrem as regras isoladas; estes cobrem o que só
/// aparece quando os casos de uso se encadeiam — que é onde estavam os dois
/// bugs desta suíte: o ativo rejeitado que nunca mais publicava, e o publicado
/// que podia ser alterado sem revisão.
/// </summary>
[Suite("Fluxo de governança (casos de uso)")]
public sealed class FluxoDeGovernancaTestes
{
    // ── rejeição e reenvio ────────────────────────────────────────────────────

    [Teste("ativo rejeitado e reenviado é publicado quando a nova revisão é aprovada")]
    public void RejeitadoEReenviadoPublica()
    {
        var m = new MundoDeGovernanca();
        var id = m.SistemaCompleto();

        m.Submeter(id);
        m.Decidir(m.UnicaAberta(id), aprovada: false, MundoDeGovernanca.TechLead);
        Verificar.Igual(StatusCicloVida.Rascunho, m.Ativo(id).Status, "rejeição devolve ao rascunho");

        m.Submeter(id);
        m.Decidir(m.UnicaAberta(id), aprovada: true, MundoDeGovernanca.TechLead);

        Verificar.Igual(StatusCicloVida.Publicado, m.Ativo(id).Status,
            "a reprovação da revisão 1 não pode impedir a publicação da revisão 2");
    }

    [Teste("rejeitar uma etapa cancela as outras etapas abertas da mesma revisão")]
    public void RejeicaoCancelaEtapasIrmas()
    {
        var m = new MundoDeGovernanca();
        var id = m.DominioCompleto();          // domínio: etapas negocial e arquitetural

        m.Submeter(id);
        var negocial = m.Abertas(id).Single(v => v.Etapa == Governanca.EtapaValidacao.Negocial);
        m.Decidir(negocial.Id, aprovada: false, MundoDeGovernanca.Negocio);

        Verificar.Vazio(m.Abertas(id), "nenhuma etapa da revisão rejeitada pode ficar esperando decisão");
        var arquitetural = m.Validacoes(id).Single(v => v.Etapa == Governanca.EtapaValidacao.Arquitetural);
        Verificar.Igual(StatusDaValidacao.Cancelada, arquitetural.Status);
    }

    [Teste("com duas etapas, só publica quando as duas da revisão atual forem aprovadas")]
    public void DuasEtapasSoPublicaComAsDuas()
    {
        var m = new MundoDeGovernanca();
        var id = m.DominioCompleto();

        m.Submeter(id);
        var abertas = m.Abertas(id);
        m.Decidir(abertas.Single(v => v.Etapa == Governanca.EtapaValidacao.Negocial).Id, true, MundoDeGovernanca.Negocio);
        Verificar.Igual(StatusCicloVida.EmValidacao, m.Ativo(id).Status, "falta a arquitetural");

        m.Decidir(abertas.Single(v => v.Etapa == Governanca.EtapaValidacao.Arquitetural).Id, true, MundoDeGovernanca.Arquiteto);
        Verificar.Igual(StatusCicloVida.Publicado, m.Ativo(id).Status);
    }

    // ── publicado é imutável; mudar exige revisão ─────────────────────────────

    [Teste("ativo publicado não pode ser editado sem abrir revisão")]
    public void PublicadoNaoEditaSemRevisao()
    {
        var m = new MundoDeGovernanca();
        var id = m.SistemaPublicado();

        var r = m.Editar(id, "Nome alterado sem revisão");

        Verificar.Falso(r.Sucesso, "edição de publicado deve ser recusada");
        Verificar.Igual("ATIVO_NAO_EDITAVEL", r.Codigo);
        Verificar.Igual("Core Bancário", m.Ativo(id).Nome, "a versão publicada não muda");
    }

    [Teste("publicado também não recebe evidência nem responsável sem revisão")]
    public void PublicadoNaoRecebeEvidenciaNemResponsavel()
    {
        var m = new MundoDeGovernanca();
        var id = m.SistemaPublicado();

        Verificar.Igual("ATIVO_NAO_EDITAVEL", m.AnexarEvidencia(id).Codigo);
        Verificar.Igual("ATIVO_NAO_EDITAVEL", m.AtribuirOwner(id).Codigo);
    }

    [Teste("depois de abrir revisão, o ativo pode ser editado e submetido de novo")]
    public void AbrirRevisaoPermiteEditarESubmeter()
    {
        var m = new MundoDeGovernanca();
        var id = m.SistemaPublicado();

        Verificar.Verdadeiro(m.AbrirRevisao(id).Sucesso, "abrir revisão de publicado");
        Verificar.Igual(StatusCicloVida.EmRevisao, m.Ativo(id).Status);

        Verificar.Verdadeiro(m.Editar(id, "Core Bancário v2").Sucesso, "editar em revisão");
        m.Submeter(id);
        m.Decidir(m.UnicaAberta(id), aprovada: true, MundoDeGovernanca.TechLead);

        Verificar.Igual(StatusCicloVida.Publicado, m.Ativo(id).Status);
        Verificar.Igual("Core Bancário v2", m.Ativo(id).Nome);
        Verificar.Igual(2, m.Ativo(id).RevisaoAtual);
    }

    [Teste("revisão rejeitada volta para em_revisao, não para rascunho")]
    public void RevisaoRejeitadaVoltaParaEmRevisao()
    {
        var m = new MundoDeGovernanca();
        var id = m.SistemaPublicado();

        m.AbrirRevisao(id);
        m.Submeter(id);
        m.Decidir(m.UnicaAberta(id), aprovada: false, MundoDeGovernanca.TechLead);

        Verificar.Igual(StatusCicloVida.EmRevisao, m.Ativo(id).Status,
            "um ativo que já foi publicado não volta a ser rascunho");
    }

    [Teste("não se abre revisão de ativo que nunca foi publicado")]
    public void NaoAbreRevisaoDeRascunho()
    {
        var m = new MundoDeGovernanca();
        var id = m.SistemaCompleto();

        var r = m.AbrirRevisao(id);
        Verificar.Falso(r.Sucesso);
        Verificar.Igual("TRANSICAO_INVALIDA", r.Codigo);
    }

    [Teste("consulta não abre revisão")]
    public void ConsultaNaoAbreRevisao()
    {
        var m = new MundoDeGovernanca();
        var id = m.SistemaPublicado();

        var r = m.AbrirRevisao(id, MundoDeGovernanca.Consulta);
        Verificar.Igual("SEM_PERMISSAO", r.Codigo);
    }
}
