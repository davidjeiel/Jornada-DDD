using Jornada.Adaptadores.Memoria;
using Jornada.Aplicacao.Catalogo.Comandos;
using Jornada.Aplicacao.Catalogo.Consultas;
using Jornada.Aplicacao.Portas;
using Jornada.Dominio.Acesso;
using Jornada.Dominio.Catalogo;
using Jornada.Dominio.Testes.Harness;

namespace Jornada.Dominio.Testes.Casos;

/// <summary>Um catálogo em memória e os casos de uso, montados como no composition root.</summary>
internal sealed class MundoDeGovernanca
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

    public RepositorioDeAtivosEmMemoria Repo => new(_armazem, _tenant);

    public Ativo Ativo(IdDeAtivo id) => Repo.ObterAsync(id).Result!;

    public IReadOnlyList<ValidacaoEmProcesso> Validacoes(IdDeAtivo id) =>
        ((IRepositorioDeValidacoes)Repo).ListarAsync(id).Result;

    public IReadOnlyList<ValidacaoEmProcesso> Abertas(IdDeAtivo id) =>
        Validacoes(id).Where(v => v.Status == StatusDaValidacao.Aberta).ToArray();

    public Guid UnicaAberta(IdDeAtivo id) => Abertas(id).Single().Id;

    public IdDeAtivo SistemaCompleto(string nome = "Core Bancário") => Cadastrar("sistema", nome,
        new() { ["plataforma"] = "mainframe" }, PapelDeOwnership.OwnerTecnico);

    public IdDeAtivo DominioCompleto()
    {
        var id = Cadastrar("dominio", "Crédito",
            new() { ["visao"] = "Conceder crédito com risco controlado" }, PapelDeOwnership.OwnerNegocial);
        Exigir(AnexarEvidencia(id));                 // a política de domínio exige 1
        return id;
    }

    public IdDeAtivo SistemaPublicado(string nome = "Core Bancário")
    {
        var id = SistemaCompleto(nome);
        Submeter(id);
        Decidir(UnicaAberta(id), aprovada: true, TechLead);
        Verificar.Igual(StatusCicloVida.Publicado, Ativo(id).Status, "pré-condição: publicado");
        return id;
    }

    public void Submeter(IdDeAtivo id) =>
        Exigir(new SubmeterAtivo(Repo, PreCheck(), _metamodelo, _tenant, new AtorFixo(Curador),
            Repo, Repo, Repo, Repo, Repo, Cenario.Relogio).ExecutarAsync(new ComandoSubmeterAtivo(id, "teste")).Result);

    public void Decidir(Guid validacao, bool aprovada, Ator quem) =>
        Exigir(new DecidirValidacao(Repo, Repo, _metamodelo, _tenant, new AtorFixo(quem), Repo,
            Repo, Repo, Cenario.Relogio).ExecutarAsync(new ComandoDecidirValidacao(validacao, aprovada, "teste")).Result);

    public Aplicacao.Resultado<bool> Editar(IdDeAtivo id, string nome) =>
        new EditarAtivo(Repo, _metamodelo, _tenant, new AtorFixo(Curador), Repo, Repo, Cenario.Relogio)
            .ExecutarAsync(new ComandoEditarAtivo(id, nome, "Sistema central de contas e lançamentos.",
                Criticidade.Media, null)).Result;

    public Aplicacao.Resultado<bool> AnexarEvidencia(IdDeAtivo id) =>
        new AnexarEvidencia(Repo, _metamodelo, _tenant, new AtorFixo(Curador), Repo, Repo, Cenario.Relogio)
            .ExecutarAsync(new ComandoAnexarEvidencia(id, "adr", "ADR de criação", null)).Result;

    public Aplicacao.Resultado<bool> AtribuirOwner(IdDeAtivo id) =>
        new AtribuirResponsavel(Repo, _metamodelo, _tenant, new AtorFixo(Curador), Repo, Repo, Cenario.Relogio)
            .ExecutarAsync(new ComandoAtribuirResponsavel(id, Guid.NewGuid(), PapelDeOwnership.Mantenedor)).Result;

    public Aplicacao.Resultado<bool> AbrirRevisao(IdDeAtivo id, Ator? quem = null) =>
        new AbrirRevisao(Repo, _metamodelo, _tenant, new AtorFixo(quem ?? Curador), Repo, Repo, Cenario.Relogio)
            .ExecutarAsync(new ComandoAbrirRevisao(id)).Result;

    public Aplicacao.Resultado<bool> Relacionar(IdDeAtivo origem, IdDeAtivo destino, Criticidade criticidade = Criticidade.Alta) =>
        new RelacionarAtivos(Repo, Repo, _metamodelo, _tenant, new AtorFixo(Curador), Repo, Repo, Cenario.Relogio)
            .ExecutarAsync(new ComandoRelacionarAtivos(origem, destino, "depende_de", criticidade)).Result;

    public ConsultarHistorico.Saida Historico(IdDeAtivo id) =>
        new ConsultarHistorico(Repo, Repo, Repo).ExecutarAsync(id).Result.Valor!;

    public Aplicacao.Resultado<CompararRevisoes.Saida> Comparar(IdDeAtivo id, int? a = null, int? b = null) =>
        new CompararRevisoes(Repo, Repo).ExecutarAsync(id, a, b).Result;

    private IdDeAtivo Cadastrar(string tipo, string nome, Dictionary<string, string> atributos,
                                PapelDeOwnership owner)
    {
        var r = new CadastrarAtivo(Repo, _metamodelo, _tenant, new AtorFixo(Curador), Repo, Repo, Cenario.Relogio)
            .ExecutarAsync(new ComandoCadastrarAtivo(tipo, nome, "Descrição completa do ativo.",
                Criticidade.Media, null, atributos)).Result;
        Exigir(r);
        Exigir(new AtribuirResponsavel(Repo, _metamodelo, _tenant, new AtorFixo(Curador), Repo, Repo, Cenario.Relogio)
            .ExecutarAsync(new ComandoAtribuirResponsavel(r.Valor, Guid.NewGuid(), owner)).Result);
        return r.Valor;
    }

    private AvaliarPreCheck PreCheck() => new(Repo, Repo, _metamodelo, _politicas, Cenario.Relogio);

    private static void Exigir<T>(Aplicacao.Resultado<T> r)
    {
        if (!r.Sucesso) throw new FalhaDeVerificacao($"passo do cenário recusado: {r.Codigo} — {r.Mensagem}");
    }
}
