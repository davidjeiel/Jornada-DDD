using Jornada.Dominio.Comum;
using Jornada.Dominio.Tenancy;

namespace Jornada.Dominio.Catalogo;

/// <summary>Papel de responsabilidade sobre um ativo. De <c>responsabilidade.papel</c>.</summary>
public enum PapelDeOwnership { OwnerNegocial, OwnerTecnico, Arquiteto, Mantenedor }

/// <summary>Responsável vigente por um ativo.</summary>
public sealed record Responsabilidade(
    Guid IdPessoa,
    PapelDeOwnership Papel,
    DateOnly InicioVigencia,
    DateOnly? FimVigencia = null)
{
    public bool Vigente(DateOnly hoje) =>
        InicioVigencia <= hoje && (FimVigencia is null || FimVigencia >= hoje);
}

/// <summary>Evidência anexada (ADR, OpenAPI, repositório, documento, link).</summary>
public sealed record Evidencia(Guid Id, string Tipo, string Titulo, string? Url);

/// <summary>
/// Raiz de agregado do catálogo. Corresponde a <c>item_catalogo</c>.
///
/// Note que <see cref="Empresa"/> é atribuída na criação e é <c>init</c>: não há
/// como mover um ativo entre empresas, nem por acidente (ADR-0003).
/// </summary>
public sealed class Ativo : RaizDeAgregado
{
    private readonly Dictionary<string, string> _atributos;
    private readonly List<Responsabilidade> _responsaveis;
    private readonly List<Evidencia> _evidencias;

    public IdDeAtivo Id { get; }
    public IdDeEmpresa Empresa { get; }
    public IdDeUnidade? Unidade { get; private set; }
    public string TipoItem { get; }
    public CodigoDeAtivo Codigo { get; }
    public string Nome { get; private set; }
    public string Descricao { get; private set; }
    public IdDeAtivo? IdPai { get; private set; }
    public Guid? IdSquad { get; private set; }
    public Criticidade Criticidade { get; private set; }
    public StatusCicloVida Status { get; private set; }
    public OrigemDoAtivo Origem { get; }
    public int RevisaoAtual { get; private set; }
    public DateOnly? FimVigencia { get; private set; }
    public DateTimeOffset CriadoEm { get; }
    public DateTimeOffset AtualizadoEm { get; private set; }

    public IReadOnlyDictionary<string, string> Atributos => _atributos;
    public IReadOnlyList<Responsabilidade> Responsaveis => _responsaveis;
    public IReadOnlyList<Evidencia> Evidencias => _evidencias;

    private Ativo(IdDeAtivo id, IdDeEmpresa empresa, string tipoItem, CodigoDeAtivo codigo,
                  string nome, string descricao, Criticidade criticidade,
                  OrigemDoAtivo origem, DateTimeOffset agora)
    {
        Id = id;
        Empresa = empresa;
        TipoItem = tipoItem;
        Codigo = codigo;
        Nome = nome;
        Descricao = descricao;
        Criticidade = criticidade;
        Origem = origem;
        Status = StatusCicloVida.Rascunho;
        CriadoEm = agora;
        AtualizadoEm = agora;
        _atributos = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        _responsaveis = [];
        _evidencias = [];
    }

    /// <summary>
    /// Único caminho para criar um ativo. Construtor privado + fábrica nomeada:
    /// não existe instância inválida (ADR-0001).
    /// </summary>
    public static Ativo Rascunhar(
        IdDeEmpresa empresa, TipoDeAtivo tipo, CodigoDeAtivo codigo, string nome,
        IRelogio relogio, string descricao = "",
        Criticidade criticidade = Criticidade.Media,
        IdDeAtivo? idPai = null, IdDeUnidade? unidade = null,
        IReadOnlyDictionary<string, string>? atributos = null,
        OrigemDoAtivo origem = OrigemDoAtivo.Manual)
    {
        if (string.IsNullOrWhiteSpace(nome))
            throw new ErroDeDominio("NOME_OBRIGATORIO", "Nome do ativo é obrigatório.");

        // A hierarquia é regra de APLICAÇÃO, não constraint de banco — igual ao
        // painel-ddd (ver docs/MODELO.md). Mas "declarou pai que o tipo não
        // admite" é erro estrutural e morre aqui, na entrada.
        if (idPai is not null && tipo.Pai is null)
            throw new ErroDeDominio("HIERARQUIA_INVALIDA",
                $"{tipo.Rotulo} não admite item pai.");

        var ativo = new Ativo(IdDeAtivo.Novo(), empresa, tipo.Chave, codigo,
                              nome.Trim(), (descricao ?? "").Trim(), criticidade,
                              origem, relogio.Agora)
        {
            IdPai = idPai,
            Unidade = unidade,
        };

        if (atributos is not null)
            foreach (var (k, v) in atributos) ativo._atributos[k] = v;

        ativo.Registrar(new AtivoCadastrado(ativo.Id, empresa, tipo.Chave, codigo.Valor));
        return ativo;
    }

    public void Renomear(string nome, IRelogio relogio)
    {
        if (string.IsNullOrWhiteSpace(nome))
            throw new ErroDeDominio("NOME_OBRIGATORIO", "Nome do ativo é obrigatório.");
        Nome = nome.Trim();
        Tocar(relogio);
    }

    public void Descrever(string descricao, IRelogio relogio)
    {
        Descricao = (descricao ?? "").Trim();
        Tocar(relogio);
    }

    public void DefinirAtributo(string nome, string valor, IRelogio relogio)
    {
        _atributos[nome] = valor;
        Tocar(relogio);
    }

    public void ReclassificarCriticidade(Criticidade nova, IRelogio relogio)
    {
        Criticidade = nova;
        Tocar(relogio);
    }

    public void VincularPai(IdDeAtivo? pai, IRelogio relogio)
    {
        if (pai == Id) throw new ErroDeDominio("CICLO", "Um ativo não pode ser pai de si mesmo.");
        IdPai = pai;
        Tocar(relogio);
    }

    public void AtribuirResponsavel(Guid idPessoa, PapelDeOwnership papel, IRelogio relogio)
    {
        _responsaveis.RemoveAll(r => r.IdPessoa == idPessoa && r.Papel == papel);
        _responsaveis.Add(new Responsabilidade(idPessoa, papel, relogio.Hoje));
        Tocar(relogio);
    }

    public void AnexarEvidencia(string tipo, string titulo, string? url, IRelogio relogio)
    {
        _evidencias.Add(new Evidencia(Guid.CreateVersion7(), tipo, titulo, url));
        Tocar(relogio);
    }

    /// <summary>Papéis de ownership vigentes hoje. Equivale a <c>qualidade._papeis</c>.</summary>
    public IReadOnlySet<PapelDeOwnership> PapeisVigentes(DateOnly hoje) =>
        _responsaveis.Where(r => r.Vigente(hoje)).Select(r => r.Papel).ToHashSet();

    public bool TemOwner(PapelDeOwnership papel, DateOnly hoje) =>
        _responsaveis.Any(r => r.Papel == papel && r.Vigente(hoje));

    public bool EhResponsavel(Guid idPessoa, PapelDeOwnership papel, DateOnly hoje) =>
        _responsaveis.Any(r => r.IdPessoa == idPessoa && r.Papel == papel && r.Vigente(hoje));

    /// <summary>
    /// Submete para validação. A decisão de PODER submeter (o pré-check) é da
    /// <see cref="Governanca.PoliticaDePublicacao"/> e é avaliada pelo caso de
    /// uso: o agregado não sabe consultar duplicidade nem detectar ciclo, porque
    /// isso exige conhecer o resto do catálogo.
    /// </summary>
    public void Submeter(string motivo, Guid idAutor, IRelogio relogio)
    {
        MaquinaDeCicloDeVida.Exigir(Status, StatusCicloVida.EmValidacao);
        Status = StatusCicloVida.EmValidacao;
        RevisaoAtual += 1;
        Tocar(relogio);
        Registrar(new AtivoSubmetido(Id, Empresa, RevisaoAtual, idAutor, motivo));
    }

    public void Publicar(IRelogio relogio)
    {
        MaquinaDeCicloDeVida.Exigir(Status, StatusCicloVida.Publicado);
        Status = StatusCicloVida.Publicado;
        Tocar(relogio);
        Registrar(new RevisaoPublicada(Id, Empresa, RevisaoAtual));
    }

    /// <summary>Rejeição na validação: volta para rascunho.</summary>
    public void DevolverParaRascunho(IRelogio relogio)
    {
        MaquinaDeCicloDeVida.Exigir(Status, StatusCicloVida.Rascunho);
        Status = StatusCicloVida.Rascunho;
        Tocar(relogio);
    }

    public void AbrirRevisao(IRelogio relogio)
    {
        MaquinaDeCicloDeVida.Exigir(Status, StatusCicloVida.EmRevisao);
        Status = StatusCicloVida.EmRevisao;
        Tocar(relogio);
    }

    public void Descontinuar(string motivo, IRelogio relogio)
    {
        MaquinaDeCicloDeVida.Exigir(Status, StatusCicloVida.Descontinuado);
        Status = StatusCicloVida.Descontinuado;
        FimVigencia = relogio.Hoje;
        Tocar(relogio);
        Registrar(new AtivoDescontinuado(Id, Empresa, motivo));
    }

    public void Arquivar(IRelogio relogio)
    {
        MaquinaDeCicloDeVida.Exigir(Status, StatusCicloVida.Arquivado);
        Status = StatusCicloVida.Arquivado;
        Tocar(relogio);
    }

    private void Tocar(IRelogio relogio) => AtualizadoEm = relogio.Agora;

    /// <summary>Reidratação pela persistência. Não usar em regra de negócio.</summary>
    public static Ativo Reidratar(
        IdDeAtivo id, IdDeEmpresa empresa, string tipoItem, CodigoDeAtivo codigo,
        string nome, string descricao, IdDeAtivo? idPai, Criticidade criticidade,
        StatusCicloVida status, OrigemDoAtivo origem, int revisaoAtual,
        DateTimeOffset criadoEm, DateTimeOffset atualizadoEm,
        IdDeUnidade? unidade = null, DateOnly? fimVigencia = null,
        IReadOnlyDictionary<string, string>? atributos = null,
        IEnumerable<Responsabilidade>? responsaveis = null,
        IEnumerable<Evidencia>? evidencias = null)
    {
        var a = new Ativo(id, empresa, tipoItem, codigo, nome, descricao,
                          criticidade, origem, criadoEm)
        {
            IdPai = idPai,
            Unidade = unidade,
            Status = status,
            RevisaoAtual = revisaoAtual,
            FimVigencia = fimVigencia,
            AtualizadoEm = atualizadoEm,
        };
        if (atributos is not null) foreach (var (k, v) in atributos) a._atributos[k] = v;
        if (responsaveis is not null) a._responsaveis.AddRange(responsaveis);
        if (evidencias is not null) a._evidencias.AddRange(evidencias);
        a.DrenarEventos();   // reidratar não é um fato novo
        return a;
    }
}
