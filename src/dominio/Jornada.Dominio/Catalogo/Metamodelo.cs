namespace Jornada.Dominio.Catalogo;

/// <summary>Campo específico de um tipo de ativo. Portado de <c>tipos.Campo</c>.</summary>
public sealed record CampoDoTipo(
    string Nome,
    string Rotulo,
    string Tipo = "texto",           // texto | area | selecao | url
    bool Obrigatorio = false,
    IReadOnlyList<string>? Opcoes = null);

/// <summary>
/// Um tipo de ativo governado. Portado de <c>tipos.TipoAtivo</c>.
///
/// No painel-ddd isto é um <c>dict</c> em código. Aqui continua sendo um objeto
/// de domínio, MAS o <see cref="CatalogoDeTipos"/> que o contém passa a ser
/// carregado por uma porta — porque no produto multiempresa a taxonomia é
/// configurável por empresa (ADR-0005). Uma financeira fala "jornada" e
/// "produto"; uma indústria fala "planta" e "linha". Nenhuma adota
/// domínio → subdomínio → contexto sem adaptar.
/// </summary>
public sealed record TipoDeAtivo(
    string Chave,
    string Rotulo,
    string Bloco,
    string? Pai = null,
    string Prefixo = "ATV",
    IReadOnlyList<CampoDoTipo>? Campos = null,
    bool ExigeOwnerNegocial = false,
    bool ExigeOwnerTecnico = false)
{
    public IReadOnlyList<CampoDoTipo> CamposDoTipo => Campos ?? [];

    public IEnumerable<CampoDoTipo> CamposObrigatorios =>
        CamposDoTipo.Where(c => c.Obrigatorio);
}

/// <summary>
/// A taxonomia de UMA empresa, numa versão. Imutável por versão: editar publica
/// uma nova (ADR-0005) — mesmo princípio de <c>revisao_catalogo</c>, aplicado à
/// própria definição dos ativos.
/// </summary>
public sealed class CatalogoDeTipos
{
    private readonly Dictionary<string, TipoDeAtivo> _tipos;

    public int Versao { get; }

    public CatalogoDeTipos(int versao, IEnumerable<TipoDeAtivo> tipos)
    {
        Versao = versao;
        _tipos = tipos.ToDictionary(t => t.Chave, StringComparer.OrdinalIgnoreCase);
    }

    public TipoDeAtivo Obter(string chave) =>
        _tipos.TryGetValue(chave, out var t)
            ? t
            : throw new Comum.ErroDeDominio("TIPO_DESCONHECIDO", $"Tipo de ativo desconhecido: {chave}");

    public bool Existe(string chave) => _tipos.ContainsKey(chave);

    public IReadOnlyCollection<TipoDeAtivo> Todos => _tipos.Values;

    public IReadOnlyCollection<string> Blocos =>
        _tipos.Values.Select(t => t.Bloco).Distinct().ToArray();

    /// <summary>
    /// O catálogo PADRÃO: a taxonomia DDD do painel-ddd, exatamente como está em
    /// <c>catalogo/tipos.py</c>. Toda empresa nova nasce com ele e o ESTENDE —
    /// ninguém parte do zero (ADR-0005). É também o que <c>docs/MODELO.md</c>
    /// especifica, e por isso aquele documento continua valendo.
    /// </summary>
    public static CatalogoDeTipos Padrao() => new(1,
    [
        // ───────────────────────────────────────────── bloco "Estrutura DDD"
        new TipoDeAtivo("dominio", "Domínio", BlocoEstruturaDdd, null, "DOM",
            [new CampoDoTipo("visao", "Visão de negócio", "area", Obrigatorio: true),
             new CampoDoTipo("forum", "Fórum de governança")],
            ExigeOwnerNegocial: true),

        new TipoDeAtivo("subdominio", "Subdomínio", BlocoEstruturaDdd, "dominio", "SUB",
            [new CampoDoTipo("classificacao", "Classificação", "selecao", true,
                ["core", "suporte", "genérico"])],
            ExigeOwnerNegocial: true),

        new TipoDeAtivo("contexto", "Contextos Delimitados", BlocoEstruturaDdd, "subdominio", "CTX",
            [new CampoDoTipo("linguagem_ubiqua", "Termos da linguagem ubíqua", "area"),
             new CampoDoTipo("estrategia_integracao", "Estratégia de integração", "selecao", false,
                ["parceria", "cliente-fornecedor", "conformista",
                 "camada anticorrupção", "serviço aberto"])],
            ExigeOwnerNegocial: true, ExigeOwnerTecnico: true),

        new TipoDeAtivo("capacidade", "Capacidade de negócio", BlocoEstruturaDdd, "contexto", "CAP",
            [new CampoDoTipo("resultado_esperado", "Resultado esperado", "area", true),
             new CampoDoTipo("processo_negocio", "Processo de negócio")],
            ExigeOwnerNegocial: true, ExigeOwnerTecnico: true),

        // ──────────────────────────────────────────── bloco "Ativos técnicos"
        new TipoDeAtivo("sistema", "Sistema", BlocoAtivosTecnicos, null, "SIS",
            [new CampoDoTipo("plataforma", "Plataforma", "selecao", true,
                ["mainframe", "distribuído", "nuvem", "híbrido"]),
             new CampoDoTipo("situacao", "Situação", "selecao", false,
                ["legado", "em modernização", "estratégico"])],
            ExigeOwnerTecnico: true),

        new TipoDeAtivo("aplicacao", "Aplicação", BlocoAtivosTecnicos, "sistema", "APP",
            [new CampoDoTipo("linguagem", "Linguagem principal"),
             new CampoDoTipo("repositorio_url", "Repositório", "url")],
            ExigeOwnerTecnico: true),

        new TipoDeAtivo("repositorio", "Repositório", BlocoAtivosTecnicos, "aplicacao", "REP",
            [new CampoDoTipo("url", "URL", "url", true)],
            ExigeOwnerTecnico: true),

        new TipoDeAtivo("api", "API", BlocoAtivosTecnicos, "aplicacao", "API",
            [new CampoDoTipo("contrato_url", "Contrato OpenAPI", "url", true),
             new CampoDoTipo("versao", "Versão")],
            ExigeOwnerTecnico: true),

        new TipoDeAtivo("endpoint", "Endpoint", BlocoAtivosTecnicos, "api", "END",
            [new CampoDoTipo("metodo", "Método", "selecao", false,
                ["GET", "POST", "PUT", "PATCH", "DELETE"]),
             new CampoDoTipo("caminho", "Caminho")]),

        new TipoDeAtivo("base_dados", "Base de dados", BlocoAtivosTecnicos, "sistema", "BDD",
            [new CampoDoTipo("tecnologia", "Tecnologia")],
            ExigeOwnerTecnico: true),

        new TipoDeAtivo("objeto_dado", "Objeto de dado", BlocoAtivosTecnicos, "base_dados", "OBJ",
            [new CampoDoTipo("classificacao_dado", "Classificação do dado", "selecao", false,
                ["público", "interno", "confidencial", "restrito"])]),

        new TipoDeAtivo("evento", "Evento de negócio", BlocoAtivosTecnicos, "contexto", "EVT",
            [new CampoDoTipo("schema_url", "Schema", "url"),
             new CampoDoTipo("mecanismo", "Mecanismo", "selecao", false,
                ["sincrono", "assincrono", "batch"])],
            ExigeOwnerTecnico: true),
    ]);

    public const string BlocoEstruturaDdd = "Estrutura DDD";
    public const string BlocoAtivosTecnicos = "Ativos técnicos";
}
