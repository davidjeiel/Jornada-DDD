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
        var m = new Mundo();
        var id = m.SistemaCompleto();

        m.Submeter(id);
        m.Decidir(m.UnicaAberta(id), aprovada: false, Mundo.TechLead);
        Verificar.Igual(StatusCicloVida.Rascunho, m.Ativo(id).Status, "rejeição devolve ao rascunho");

        m.Submeter(id);
        m.Decidir(m.UnicaAberta(id), aprovada: true, Mundo.TechLead);

        Verificar.Igual(StatusCicloVida.Publicado, m.Ativo(id).Status,
            "a reprovação da revisão 1 não pode impedir a publicação da revisão 2");
    }

    [Teste("rejeitar uma etapa cancela as outras etapas abertas da mesma revisão")]
    public void RejeicaoCancelaEtapasIrmas()
    {
        var m = new Mundo();
        var id = m.DominioCompleto();          // domínio: etapas negocial e arquitetural

        m.Submeter(id);
        var negocial = m.Abertas(id).Single(v => v.Etapa == Governanca.EtapaValidacao.Negocial);
        m.Decidir(negocial.Id, aprovada: false, Mundo.Negocio);

        Verificar.Vazio(m.Abertas(id), "nenhuma etapa da revisão rejeitada pode ficar esperando decisão");
        var arquitetural = m.Validacoes(id).Single(v => v.Etapa == Governanca.EtapaValidacao.Arquitetural);
        Verificar.Igual(StatusDaValidacao.Cancelada, arquitetural.Status);
    }

    [Teste("com duas etapas, só publica quando as duas da revisão atual forem aprovadas")]
    public void DuasEtapasSoPublicaComAsDuas()
    {
        var m = new Mundo();
        var id = m.DominioCompleto();

        m.Submeter(id);
        var abertas = m.Abertas(id);
        m.Decidir(abertas.Single(v => v.Etapa == Governanca.EtapaValidacao.Negocial).Id, true, Mundo.Negocio);
        Verificar.Igual(StatusCicloVida.EmValidacao, m.Ativo(id).Status, "falta a arquitetural");

        m.Decidir(abertas.Single(v => v.Etapa == Governanca.EtapaValidacao.Arquitetural).Id, true, Mundo.Arquiteto);
        Verificar.Igual(StatusCicloVida.Publicado, m.Ativo(id).Status);
    }

    // ── publicado é imutável; mudar exige revisão ─────────────────────────────

    [Teste("ativo publicado não pode ser editado sem abrir revisão")]
    public void PublicadoNaoEditaSemRevisao()
    {
        var m = new Mundo();
        var id = m.SistemaPublicado();

        var r = m.Editar(id, "Nome alterado sem revisão");

        Verificar.Falso(r.Sucesso, "edição de publicado deve ser recusada");
        Verificar.Igual("ATIVO_NAO_EDITAVEL", r.Codigo);
        Verificar.Igual("Core Bancário", m.Ativo(id).Nome, "a versão publicada não muda");
    }

    [Teste("publicado também não recebe evidência nem responsável sem revisão")]
    public void PublicadoNaoRecebeEvidenciaNemResponsavel()
    {
        var m = new Mundo();
        var id = m.SistemaPublicado();

        Verificar.Igual("ATIVO_NAO_EDITAVEL", m.AnexarEvidencia(id).Codigo);
        Verificar.Igual("ATIVO_NAO_EDITAVEL", m.AtribuirOwner(id).Codigo);
    }

    [Teste("depois de abrir revisão, o ativo pode ser editado e submetido de novo")]
    public void AbrirRevisaoPermiteEditarESubmeter()
    {
        var m = new Mundo();
        var id = m.SistemaPublicado();

        Verificar.Verdadeiro(m.AbrirRevisao(id).Sucesso, "abrir revisão de publicado");
        Verificar.Igual(StatusCicloVida.EmRevisao, m.Ativo(id).Status);

        Verificar.Verdadeiro(m.Editar(id, "Core Bancário v2").Sucesso, "editar em revisão");
        m.Submeter(id);
        m.Decidir(m.UnicaAberta(id), aprovada: true, Mundo.TechLead);

        Verificar.Igual(StatusCicloVida.Publicado, m.Ativo(id).Status);
        Verificar.Igual("Core Bancário v2", m.Ativo(id).Nome);
        Verificar.Igual(2, m.Ativo(id).RevisaoAtual);
    }

    [Teste("revisão rejeitada volta para em_revisao, não para rascunho")]
    public void RevisaoRejeitadaVoltaParaEmRevisao()
    {
        var m = new Mundo();
        var id = m.SistemaPublicado();

        m.AbrirRevisao(id);
        m.Submeter(id);
        m.Decidir(m.UnicaAberta(id), aprovada: false, Mundo.TechLead);

        Verificar.Igual(StatusCicloVida.EmRevisao, m.Ativo(id).Status,
            "um ativo que já foi publicado não volta a ser rascunho");
    }

    [Teste("não se abre revisão de ativo que nunca foi publicado")]
    public void NaoAbreRevisaoDeRascunho()
    {
        var m = new Mundo();
        var id = m.SistemaCompleto();

        var r = m.AbrirRevisao(id);
        Verificar.Falso(r.Sucesso);
        Verificar.Igual("TRANSICAO_INVALIDA", r.Codigo);
    }

    [Teste("consulta não abre revisão")]
    public void ConsultaNaoAbreRevisao()
    {
        var m = new Mundo();
        var id = m.SistemaPublicado();

        var r = m.AbrirRevisao(id, Mundo.Consulta);
        Verificar.Igual("SEM_PERMISSAO", r.Codigo);
    }

    // ── cenário ───────────────────────────────────────────────────────────────

    /// <summary>Um catálogo em memória e os casos de uso, montados como no composition root.</summary>
    private sealed class Mundo
    {
        public static readonly Ator Curador = Cenario.Ator(Papel.Curador);
        public static readonly Ator TechLead = Cenario.Ator(Papel.TechLead);
        public static readonly Ator Negocio = Cenario.Ator(Papel.Negocio);
        public static readonly Ator Arquiteto = Cenario.Ator(Papel.Arquiteto);
        public static readonly Ator Consulta = Cenario.Ator(Papel.Consulta);

        private readonly ArmazemEmMemoria _armazem = new();
        private readonly ContextoDeTenantFixo _tenant = ContextoDeTenantFixo.Para(Cenario.EmpresaA);
        private readonly MetamodeloPadrao _metamodelo = new();
        private readonly PoliticasPadrao _politicas = new();

        private RepositorioDeAtivosEmMemoria Repo => new(_armazem, _tenant);

        public Ativo Ativo(IdDeAtivo id) => Repo.ObterAsync(id).Result!;

        public IReadOnlyList<ValidacaoEmProcesso> Validacoes(IdDeAtivo id) =>
            ((IRepositorioDeValidacoes)Repo).ListarAsync(id).Result;

        public IReadOnlyList<ValidacaoEmProcesso> Abertas(IdDeAtivo id) =>
            Validacoes(id).Where(v => v.Status == StatusDaValidacao.Aberta).ToArray();

        public Guid UnicaAberta(IdDeAtivo id) => Abertas(id).Single().Id;

        public IdDeAtivo SistemaCompleto() => Cadastrar("sistema", "Core Bancário",
            new() { ["plataforma"] = "mainframe" }, PapelDeOwnership.OwnerTecnico);

        public IdDeAtivo DominioCompleto()
        {
            var id = Cadastrar("dominio", "Crédito",
                new() { ["visao"] = "Conceder crédito com risco controlado" }, PapelDeOwnership.OwnerNegocial);
            Exigir(AnexarEvidencia(id));                 // a política de domínio exige 1
            return id;
        }

        public IdDeAtivo SistemaPublicado()
        {
            var id = SistemaCompleto();
            Submeter(id);
            Decidir(UnicaAberta(id), aprovada: true, TechLead);
            Verificar.Igual(StatusCicloVida.Publicado, Ativo(id).Status, "pré-condição: publicado");
            return id;
        }

        public void Submeter(IdDeAtivo id) =>
            Exigir(new SubmeterAtivo(Repo, PreCheck(), _metamodelo, _tenant, new AtorFixo(Curador),
                Repo, Repo, Cenario.Relogio).ExecutarAsync(new ComandoSubmeterAtivo(id, "teste")).Result);

        public void Decidir(Guid validacao, bool aprovada, Ator quem) =>
            Exigir(new DecidirValidacao(Repo, Repo, _metamodelo, _tenant, new AtorFixo(quem), Repo,
                Cenario.Relogio).ExecutarAsync(new ComandoDecidirValidacao(validacao, aprovada, "teste")).Result);

        public Aplicacao.Resultado<bool> Editar(IdDeAtivo id, string nome) =>
            new EditarAtivo(Repo, _metamodelo, _tenant, new AtorFixo(Curador), Repo, Cenario.Relogio)
                .ExecutarAsync(new ComandoEditarAtivo(id, nome, "Sistema central de contas e lançamentos.",
                    Criticidade.Media, null)).Result;

        public Aplicacao.Resultado<bool> AnexarEvidencia(IdDeAtivo id) =>
            new AnexarEvidencia(Repo, _metamodelo, _tenant, new AtorFixo(Curador), Repo, Cenario.Relogio)
                .ExecutarAsync(new ComandoAnexarEvidencia(id, "adr", "ADR de criação", null)).Result;

        public Aplicacao.Resultado<bool> AtribuirOwner(IdDeAtivo id) =>
            new AtribuirResponsavel(Repo, _metamodelo, _tenant, new AtorFixo(Curador), Repo, Cenario.Relogio)
                .ExecutarAsync(new ComandoAtribuirResponsavel(id, Guid.NewGuid(), PapelDeOwnership.Mantenedor)).Result;

        public Aplicacao.Resultado<bool> AbrirRevisao(IdDeAtivo id, Ator? quem = null) =>
            new AbrirRevisao(Repo, _metamodelo, _tenant, new AtorFixo(quem ?? Curador), Repo, Cenario.Relogio)
                .ExecutarAsync(new ComandoAbrirRevisao(id)).Result;

        private IdDeAtivo Cadastrar(string tipo, string nome, Dictionary<string, string> atributos,
                                    PapelDeOwnership owner)
        {
            var r = new CadastrarAtivo(Repo, _metamodelo, _tenant, new AtorFixo(Curador), Repo, Cenario.Relogio)
                .ExecutarAsync(new ComandoCadastrarAtivo(tipo, nome, "Descrição completa do ativo.",
                    Criticidade.Media, null, atributos)).Result;
            Exigir(r);
            Exigir(new AtribuirResponsavel(Repo, _metamodelo, _tenant, new AtorFixo(Curador), Repo, Cenario.Relogio)
                .ExecutarAsync(new ComandoAtribuirResponsavel(r.Valor, Guid.NewGuid(), owner)).Result);
            return r.Valor;
        }

        private AvaliarPreCheck PreCheck() => new(Repo, Repo, _metamodelo, _politicas, Cenario.Relogio);

        private static void Exigir<T>(Aplicacao.Resultado<T> r)
        {
            if (!r.Sucesso) throw new FalhaDeVerificacao($"passo do cenário recusado: {r.Codigo} — {r.Mensagem}");
        }
    }
}
