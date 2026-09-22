using Jornada.Dominio.Catalogo;
using Jornada.Dominio.Governanca;
using Jornada.Dominio.Tenancy;

namespace Jornada.Dominio.Acesso;

/// <summary>
/// O alvo da ação, já resolvido. O <c>_dominio_do_item</c> do painel-ddd sobe a
/// hierarquia até achar o domínio; aqui esse trabalho é do caso de uso (que tem
/// o repositório) e chega pronto — a política continua pura.
/// </summary>
public sealed record AlvoDaAcao(
    IdDeEmpresa Empresa,
    string? TipoItem = null,
    string? Bloco = null,
    Guid? IdDominio = null,
    Guid? IdSquad = null,
    IdDeUnidade? Unidade = null)
{
    public static AlvoDaAcao NaEmpresa(IdDeEmpresa empresa) => new(empresa);
}

/// <summary>
/// ═══════════════════════════════════════════════════════════════════════════
///  PONTO ÚNICO DE AUTORIZAÇÃO.
///
///  Portado de <c>acesso.pode()</c> e <c>acesso.pode_decidir()</c>, preservando
///  a arquitetura que o painel-ddd já tinha certa — e removendo o `con`.
///
///  Hoje, testar autorização exige banco. Aqui a matriz real (6 papéis ×
///  4 escopos × 11 ações, cruzada com os blocos de tipo) vira tabela de casos
///  rodando em milissegundos. É a diferença entre cobertura real e cobertura
///  de fachada (ADR-0007 §3).
/// ═══════════════════════════════════════════════════════════════════════════
/// </summary>
public static class PoliticaDeAutorizacao
{
    /// <summary>Ação → papéis que a permitem. Portado de <c>acesso.PERMISSOES</c>.</summary>
    private static readonly Dictionary<Acao, Papel[]> Permissoes = new()
    {
        [Acao.Cadastrar]     = [Papel.Admin, Papel.Curador, Papel.Arquiteto, Papel.TechLead, Papel.Negocio],
        [Acao.Editar]        = [Papel.Admin, Papel.Curador, Papel.Arquiteto, Papel.TechLead, Papel.Negocio],
        [Acao.Relacionar]    = [Papel.Admin, Papel.Curador, Papel.Arquiteto, Papel.TechLead],
        [Acao.Submeter]      = [Papel.Admin, Papel.Curador, Papel.Arquiteto, Papel.TechLead, Papel.Negocio],
        [Acao.Revisar]       = [Papel.Admin, Papel.Curador, Papel.Arquiteto, Papel.TechLead, Papel.Negocio],
        [Acao.Descontinuar]  = [Papel.Admin, Papel.Curador, Papel.Arquiteto],
        [Acao.Importar]      = [Papel.Admin, Papel.Curador, Papel.TechLead],
        [Acao.Triar]         = [Papel.Admin, Papel.Curador, Papel.TechLead],
        [Acao.Decidir]       = [Papel.Admin, Papel.Arquiteto, Papel.TechLead, Papel.Negocio],
        [Acao.Conceder]      = [Papel.Admin, Papel.Curador],
        [Acao.Administrar]   = [Papel.Admin],
    };

    /// <summary>
    /// Bloco de tipos em que cada papel pode ESCREVER. Portado de
    /// <c>acesso.BLOCOS_POR_PAPEL</c>.
    ///
    /// Sem isto, o papel autoriza a AÇÃO mas não o OBJETO: quem responde pelo
    /// negócio conseguiria cadastrar uma API, e quem responde pela técnica
    /// conseguiria redesenhar a hierarquia de domínios. Arquiteto, curador e
    /// admin atravessam os dois blocos por ofício.
    /// </summary>
    private static readonly Dictionary<Papel, string[]?> BlocosPorPapel = new()
    {
        [Papel.Negocio]   = [CatalogoDeTipos.BlocoEstruturaDdd],
        [Papel.TechLead]  = [CatalogoDeTipos.BlocoAtivosTecnicos],
        [Papel.Arquiteto] = null,   // null = todos os blocos
        [Papel.Curador]   = null,
        [Papel.Admin]     = null,
        [Papel.Consulta]  = [],     // vazio = nenhum bloco de escrita
    };

    /// <summary>Ações que recaem sobre um ativo — só estas olham o bloco do tipo.</summary>
    private static readonly Acao[] AcoesSobreAtivo =
    [
        Acao.Cadastrar, Acao.Editar, Acao.Relacionar,
        Acao.Submeter, Acao.Revisar, Acao.Descontinuar,
    ];

    /// <summary>Etapa → papéis que a decidem. Portado de <c>acesso.PAPEL_POR_ETAPA</c>.</summary>
    private static readonly Dictionary<EtapaValidacao, Papel[]> PapelPorEtapa = new()
    {
        [EtapaValidacao.Negocial]     = [Papel.Admin, Papel.Negocio],
        [EtapaValidacao.Tecnica]      = [Papel.Admin, Papel.TechLead],
        [EtapaValidacao.Arquitetural] = [Papel.Admin, Papel.Arquiteto],
    };

    /// <summary>
    /// Papel de ownership que TAMBÉM autoriza a etapa, quando a pessoa responde
    /// pelo próprio ativo. Portado de <c>acesso.OWNER_POR_ETAPA</c>.
    /// </summary>
    private static readonly Dictionary<EtapaValidacao, PapelDeOwnership> OwnerPorEtapa = new()
    {
        [EtapaValidacao.Negocial]     = PapelDeOwnership.OwnerNegocial,
        [EtapaValidacao.Tecnica]      = PapelDeOwnership.OwnerTecnico,
        [EtapaValidacao.Arquitetural] = PapelDeOwnership.Arquiteto,
    };

    /// <summary>
    /// Papéis que valem para ESTE alvo: os da empresa, mais os cujo escopo
    /// alcança o alvo. Portado de <c>acesso.papeis_efetivos</c>.
    /// </summary>
    public static IReadOnlySet<Papel> PapeisEfetivos(Ator ator, AlvoDaAcao alvo, DateOnly hoje)
    {
        // Primeira barreira: papel de uma empresa não alcança ativo de outra.
        // Na prática o RLS já teria escondido o ativo (ADR-0003), mas as três
        // camadas de defesa são propositalmente redundantes (ADR-0007 §4).
        if (ator.Empresa != alvo.Empresa) return new HashSet<Papel>();

        var efetivos = new HashSet<Papel>();
        foreach (var a in ator.Atribuicoes.Where(a => a.Vigente(hoje)))
        {
            var alcanca = a.EscopoTipo switch
            {
                TipoDeEscopo.Empresa => true,
                TipoDeEscopo.Unidade => alvo.Unidade is { } u && a.EscopoId == u.Valor,
                TipoDeEscopo.Dominio => alvo.IdDominio is { } d && a.EscopoId == d,
                TipoDeEscopo.Squad   => alvo.IdSquad is { } s && a.EscopoId == s,
                _ => false,
            };
            if (alcanca) efetivos.Add(a.Papel);
        }
        return efetivos;
    }

    /// <summary>
    /// Ponto único de decisão. Três filtros, nesta ordem — igual ao painel-ddd:
    /// o papel permite a ação, o escopo do papel alcança o ativo, e o papel
    /// escreve no bloco de tipos daquele ativo.
    /// </summary>
    public static bool Pode(Ator ator, Acao acao, AlvoDaAcao alvo, DateOnly hoje)
    {
        var permitidos = Permissoes[acao];
        var efetivos = PapeisEfetivos(ator, alvo, hoje);

        if (AcoesSobreAtivo.Contains(acao) && alvo.Bloco is { } bloco)
        {
            efetivos = efetivos
                .Where(p => !BlocosPorPapel.TryGetValue(p, out var blocos)
                            || blocos is null
                            || blocos.Contains(bloco))
                .ToHashSet();
        }

        return efetivos.Overlaps(permitidos);
    }

    /// <summary>Mesma decisão, mas levanta <see cref="Comum.SemPermissao"/> (403).</summary>
    public static void Exigir(Ator ator, Acao acao, AlvoDaAcao alvo, DateOnly hoje)
    {
        if (Pode(ator, acao, alvo, hoje)) return;
        throw new Comum.SemPermissao(
            $"Sem permissão para {acao.ToString().ToLowerInvariant()} neste ativo.");
    }

    /// <summary>Blocos em que a pessoa pode cadastrar. De <c>acesso.blocos_que_escreve</c>.</summary>
    public static IReadOnlySet<string> BlocosQueEscreve(Ator ator, DateOnly hoje)
    {
        var todos = new[] { CatalogoDeTipos.BlocoEstruturaDdd, CatalogoDeTipos.BlocoAtivosTecnicos };
        var alcance = new HashSet<string>();

        foreach (var papel in ator.Atribuicoes.Where(a => a.Vigente(hoje)).Select(a => a.Papel).Distinct())
        {
            if (!Permissoes[Acao.Cadastrar].Contains(papel)) continue;
            var blocos = BlocosPorPapel.TryGetValue(papel, out var b) ? b : null;
            foreach (var bloco in blocos ?? todos) alcance.Add(bloco);
        }
        return alcance;
    }

    /// <summary>Decisão de validação e o motivo da recusa. De <c>acesso.pode_decidir</c>.</summary>
    public sealed record DecisaoDeAutorizacao(bool Permitido, string Motivo)
    {
        public static readonly DecisaoDeAutorizacao Sim = new(true, "");
        public static DecisaoDeAutorizacao Nao(string motivo) => new(false, motivo);
    }

    /// <summary>
    /// Autorização para DECIDIR uma etapa de validação. Duas regras, e a segunda
    /// é a que mais importa:
    ///
    /// 1. o papel tem de bater com a etapa (ou a pessoa é owner do ativo para
    ///    aquela etapa);
    /// 2. <b>quem submeteu a revisão não decide sobre ela</b> — segregação de
    ///    função, verificada ANTES do papel, porque nem admin a contorna.
    /// </summary>
    public static DecisaoDeAutorizacao PodeDecidir(
        Ator ator,
        EtapaValidacao etapa,
        Guid quemSubmeteu,
        Ativo ativo,
        AlvoDaAcao alvo,
        DateOnly hoje)
    {
        if (ator.IdPessoa == quemSubmeteu)
            return DecisaoDeAutorizacao.Nao(
                "Segregação de função: quem submeteu a revisão não decide sobre ela.");

        var efetivos = PapeisEfetivos(ator, alvo, hoje);
        var exigidos = PapelPorEtapa[etapa];

        if (efetivos.Overlaps(exigidos)) return DecisaoDeAutorizacao.Sim;

        // O responsável formal pelo ativo também responde pela etapa dele.
        if (OwnerPorEtapa.TryGetValue(etapa, out var papelOwner)
            && ativo.EhResponsavel(ator.IdPessoa, papelOwner, hoje))
        {
            return DecisaoDeAutorizacao.Sim;
        }

        var nomes = exigidos.Where(p => p != Papel.Admin)
                            .Select(p => p.ToString().ToLowerInvariant())
                            .Order()
                            .ToArray();
        var texto = nomes.Length > 0 ? string.Join(", ", nomes) : "administrador";
        return DecisaoDeAutorizacao.Nao(
            $"A etapa {etapa.ToString().ToLowerInvariant()} exige papel de {texto}.");
    }
}
