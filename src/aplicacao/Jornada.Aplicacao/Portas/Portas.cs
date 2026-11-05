using Jornada.Dominio.Acesso;
using Jornada.Dominio.Catalogo;
using Jornada.Dominio.Comum;
using Jornada.Dominio.Governanca;
using Jornada.Dominio.Tenancy;

namespace Jornada.Aplicacao.Portas;

/// <summary>
/// De onde vem o contexto de tenant. Repare que NÃO há como DEFINIR a empresa:
/// ela é resolvida no middleware a partir do token e validada contra o control
/// plane. Nenhum caso de uso a recebe de quem chama (ADR-0003).
/// </summary>
public interface IContextoDeTenantAtual
{
    public ContextoDeTenant Atual { get; }
}

/// <summary>Quem está agindo agora, com os papéis já resolvidos.</summary>
public interface IAtorAtual
{
    public Task<Ator> ObterAsync(CancellationToken ct = default);
}

public interface IRepositorioDeAtivos
{
    public Task<Ativo?> ObterAsync(IdDeAtivo id, CancellationToken ct = default);

    public Task<Ativo?> ObterPorCodigoAsync(CodigoDeAtivo codigo, CancellationToken ct = default);

    /// <param name="criadoPor">Só os cadastrados por esta pessoa (a "Minha mesa").</param>
    public Task<IReadOnlyList<Ativo>> ListarAsync(
        string? tipoItem = null, StatusCicloVida? status = null,
        int limite = 200, Guid? criadoPor = null, CancellationToken ct = default);

    public Task AdicionarAsync(Ativo ativo, CancellationToken ct = default);

    /// <summary>A trilha hierárquica até a raiz. Equivale a <c>servicos.trilha</c>.</summary>
    public Task<IReadOnlyList<Ativo>> TrilhaAsync(IdDeAtivo id, CancellationToken ct = default);

    /// <summary>Existe outro ativo do mesmo tipo e nome NESTA empresa?</summary>
    public Task<bool> ExisteNomeDuplicadoAsync(
        string tipoItem, string nome, IdDeAtivo? exceto = null, CancellationToken ct = default);

    /// <summary>Próximo sequencial para o código, por tipo. De <c>servicos.gerar_codigo</c>.</summary>
    public Task<int> ProximoSequencialAsync(string tipoItem, CancellationToken ct = default);
}

/// <summary>
/// Uma aresta do grafo de relações, já resolvida para os dois lados. A
/// criticidade é a da DEPENDÊNCIA, não a dos ativos: uma aplicação de
/// criticidade média pode consumir uma capacidade de forma crítica. É ela que
/// decide se a descontinuação do destino exige confirmação.
/// </summary>
public sealed record RelacaoResumo(
    IdDeAtivo Origem, IdDeAtivo Destino, string Tipo, Criticidade Criticidade = Criticidade.Media, string? Mecanismo = null);

/// <summary>Consultas e escrita sobre o grafo de relações.</summary>
public interface IRepositorioDeRelacoes
{
    public Task<bool> TemDependenciaCircularAsync(IdDeAtivo id, CancellationToken ct = default);

    public Task<IReadOnlyList<string>> NomesDeDestinosForaDeVigenciaAsync(
        IdDeAtivo id, CancellationToken ct = default);

    public Task<int> ContarImplementacoesAsync(IdDeAtivo idCapacidade, CancellationToken ct = default);

    /// <summary>Grava uma aresta (origem → destino). Não valida regra de negócio — isso é do caso de uso.</summary>
    public Task RelacionarAsync(IdDeAtivo origem, IdDeAtivo destino, string tipo,
        Criticidade criticidade = Criticidade.Media, string? mecanismo = null, CancellationToken ct = default);

    /// <summary>
    /// Há caminho de <c>depende_de</c> de <paramref name="de"/> até <paramref name="ate"/>?
    /// É a pergunta antes de gravar <c>ate → de</c>: se já existe o caminho de volta,
    /// a aresta nova fecharia um ciclo (<c>governanca._tem_ciclo</c>).
    /// </summary>
    public Task<bool> TemCaminhoDeDependenciaAsync(IdDeAtivo de, IdDeAtivo ate, CancellationToken ct = default);

    /// <summary>Todas as arestas em que o ativo aparece, como origem OU destino.</summary>
    public Task<IReadOnlyList<RelacaoResumo>> ListarRelacoesAsync(IdDeAtivo id, CancellationToken ct = default);
}

/// <summary>A taxonomia desta empresa, na versão vigente (ADR-0005).</summary>
public interface IMetamodelo
{
    public Task<CatalogoDeTipos> ObterAsync(CancellationToken ct = default);
}

/// <summary>As políticas de governança desta empresa.</summary>
public interface IPoliticasDeGovernanca
{
    public Task<PoliticasDeGovernanca> ObterAsync(CancellationToken ct = default);
}

/// <summary>
/// Transação. O <c>ConfirmarAsync</c> é o ponto — e o único ponto — em que os
/// eventos dos agregados vão para a outbox, na MESMA transação (ADR-0008).
/// </summary>
public interface IUnidadeDeTrabalho
{
    public Task<int> ConfirmarAsync(CancellationToken ct = default);
}

/// <summary>
/// Cancelada: a etapa não chegou a ser decidida porque outra etapa da MESMA
/// revisão foi reprovada — a revisão inteira volta para ajuste, e não faz
/// sentido deixar alguém decidindo sobre ela.
/// </summary>
public enum StatusDaValidacao { Aberta, Aprovada, Reprovada, Cancelada }

public sealed class ValidacaoEmProcesso(
    Guid id, IdDeAtivo ativo, IdDeEmpresa empresa, int revisao,
    EtapaValidacao etapa, Guid idSubmissor, DateTimeOffset? abertaEm = null, DateTimeOffset? prazo = null)
{
    public Guid Id { get; } = id;
    public IdDeAtivo Ativo { get; } = ativo;
    public IdDeEmpresa Empresa { get; } = empresa;
    public int Revisao { get; } = revisao;
    public EtapaValidacao Etapa { get; } = etapa;
    public Guid IdSubmissor { get; } = idSubmissor;

    /// <summary>Quando a etapa abriu. Nulo em etapa anterior a este campo.</summary>
    public DateTimeOffset? AbertaEm { get; } = abertaEm;

    /// <summary>Até quando a etapa deve ser decidida: abertura + SLA da política (<c>validacao.prazo</c>).</summary>
    public DateTimeOffset? Prazo { get; } = prazo;

    public StatusDaValidacao Status { get; private set; } = StatusDaValidacao.Aberta;
    public Guid? IdDecisor { get; private set; }
    public string? Motivo { get; private set; }

    /// <summary>Quem está analisando (<c>validacao.atribuido_a</c>). Nulo: na fila livre.</summary>
    public Guid? AtribuidaA { get; private set; }

    public void Decidir(bool aprovada, Guid idDecisor, string? motivo)
    {
        Status = aprovada ? StatusDaValidacao.Aprovada : StatusDaValidacao.Reprovada;
        IdDecisor = idDecisor;
        Motivo = motivo;
        AtribuidaA = null;
    }

    /// <summary>Encerra a etapa sem decisão, porque a revisão já foi reprovada em outra etapa.</summary>
    public void Cancelar(string motivo)
    {
        Status = StatusDaValidacao.Cancelada;
        Motivo = motivo;
        AtribuidaA = null;
    }

    /// <summary>
    /// Marca quem está analisando, para dois validadores não duplicarem
    /// trabalho (<c>governanca.assumir_validacao</c>). Assumir de novo o que já é
    /// seu não muda nada; o que está com outra pessoa é recusado.
    /// </summary>
    public void Assumir(Guid idPessoa)
    {
        if (Status != StatusDaValidacao.Aberta)
            throw new ErroDeDominio("VALIDACAO_JA_DECIDIDA", "Validação já concluída.");
        if (AtribuidaA is { } outra && outra != idPessoa)
            throw new ErroDeDominio("VALIDACAO_JA_ASSUMIDA", "Esta validação já está com outra pessoa.");
        AtribuidaA = idPessoa;
    }

    /// <summary>Devolve à fila livre. Só quem assumiu libera (<c>liberar_validacao</c>).</summary>
    public void Liberar(Guid idPessoa)
    {
        if (AtribuidaA is { } dono && dono != idPessoa)
            throw new ErroDeDominio("VALIDACAO_JA_ASSUMIDA", "Só quem assumiu a validação pode liberá-la.");
        AtribuidaA = null;
    }

    /// <summary>
    /// Reidratação pela persistência, para adaptador sem mapeamento por reflexão
    /// (o EF preenche os setters privados sozinho; o Cosmos não). Não usar em regra de negócio.
    /// </summary>
    public static ValidacaoEmProcesso Reidratar(
        Guid id, IdDeAtivo ativo, IdDeEmpresa empresa, int revisao, EtapaValidacao etapa,
        Guid idSubmissor, StatusDaValidacao status, Guid? idDecisor, string? motivo,
        DateTimeOffset? abertaEm = null, DateTimeOffset? prazo = null, Guid? atribuidaA = null) =>
        new(id, ativo, empresa, revisao, etapa, idSubmissor, abertaEm, prazo)
        {
            Status = status,
            IdDecisor = idDecisor,
            Motivo = motivo,
            AtribuidaA = atribuidaA,
        };
}

public interface IRepositorioDeValidacoes
{
    public Task AdicionarAsync(ValidacaoEmProcesso validacao, CancellationToken ct = default);
    public Task<ValidacaoEmProcesso?> ObterAsync(Guid id, CancellationToken ct = default);
    public Task<IReadOnlyList<ValidacaoEmProcesso>> ListarAsync(IdDeAtivo? ativo = null, CancellationToken ct = default);
}

/// <summary>Saída de eventos. Em dev é barramento em memória; em produção, Service Bus.</summary>
public interface IPublicadorDeEventos
{
    public Task PublicarAsync(IEnumerable<EventoDeDominio> eventos, CancellationToken ct = default);
}

/// <summary>
/// Os retratos das revisões submetidas (ADR-0017). Gravados na mesma unidade de
/// trabalho do ativo, na partição dele: a submissão e o retrato entram juntos.
/// </summary>
public interface IRepositorioDeRevisoes
{
    public Task AdicionarAsync(RevisaoDoAtivo revisao, CancellationToken ct = default);
    public Task<RevisaoDoAtivo?> ObterAsync(IdDeAtivo ativo, int numero, CancellationToken ct = default);

    /// <summary>Da mais recente para a mais antiga.</summary>
    public Task<IReadOnlyList<RevisaoDoAtivo>> ListarAsync(IdDeAtivo ativo, CancellationToken ct = default);
}

/// <summary>
/// A trilha de auditoria por ativo (<c>auditoria_evento</c>). Só acrescenta.
/// O registro vai na mesma unidade de trabalho do fato que ele descreve.
/// </summary>
public interface ITrilhaDeAuditoria
{
    public Task RegistrarAsync(RegistroDeAuditoria registro, CancellationToken ct = default);

    /// <summary>Do mais recente para o mais antigo, até <paramref name="limite"/> registros.</summary>
    public Task<IReadOnlyList<RegistroDeAuditoria>> ListarAsync(IdDeAtivo ativo, int limite = 30, CancellationToken ct = default);
}

/// <summary>
/// As pessoas cadastradas na empresa, com os papéis concedidos. Gravadas pela
/// unidade de trabalho, como o resto. A matrícula é única por empresa.
/// </summary>
public interface IRepositorioDePessoas
{
    public Task<Pessoa?> ObterAsync(Guid id, CancellationToken ct = default);
    public Task<Pessoa?> ObterPorMatriculaAsync(Matricula matricula, CancellationToken ct = default);

    /// <summary>Por nome. Sem <paramref name="incluirInativas"/>, só as ativas (<c>pessoas_ativas</c>).</summary>
    public Task<IReadOnlyList<Pessoa>> ListarAsync(bool incluirInativas = false, CancellationToken ct = default);

    public Task AdicionarAsync(Pessoa pessoa, CancellationToken ct = default);

    /// <summary>Exclui o cadastro na confirmação, liberando a matrícula. Só para cadastro sem vínculo nenhum.</summary>
    public Task RemoverAsync(Pessoa pessoa, CancellationToken ct = default);
}

/// <summary>
/// O que amarra uma pessoa ao histórico do catálogo, fora do cadastro dela.
/// Equivale à parte de <c>acesso.interacoes_pessoa</c> que olha ativos,
/// revisões, validações e auditoria. Cadastro com vínculo não se exclui: inativa-se.
/// </summary>
public interface IConsultaDeVinculos
{
    /// <summary>Os nomes dos vínculos encontrados ("responsabilidades em ativos", ...). Vazio: nenhum.</summary>
    public Task<IReadOnlyList<string>> DaPessoaAsync(Guid idPessoa, CancellationToken ct = default);
}

/// <summary>Os squads da empresa. A sigla é única na empresa.</summary>
public interface IRepositorioDeSquads
{
    public Task<Squad?> ObterAsync(Guid id, CancellationToken ct = default);
    public Task<Squad?> ObterPorSiglaAsync(string sigla, CancellationToken ct = default);

    /// <summary>Ativas primeiro, depois por título (<c>ORDER BY ativo DESC, nome</c>).</summary>
    public Task<IReadOnlyList<Squad>> ListarAsync(CancellationToken ct = default);

    public Task AdicionarAsync(Squad squad, CancellationToken ct = default);
}

/// <summary>Os pleitos de acesso da empresa (<c>solicitacao_acesso</c>).</summary>
public interface IRepositorioDePleitos
{
    public Task AdicionarAsync(PleitoDeAcesso pleito, CancellationToken ct = default);
    public Task<PleitoDeAcesso?> ObterAsync(Guid id, CancellationToken ct = default);

    /// <summary>Pendentes primeiro e, dentro de cada grupo, quem espera há mais tempo primeiro.</summary>
    public Task<IReadOnlyList<PleitoDeAcesso>> ListarAsync(
        StatusDoPleito? status = null, Guid? idPessoa = null, CancellationToken ct = default);
}
