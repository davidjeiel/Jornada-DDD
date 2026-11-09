using Jornada.Aplicacao.Acesso;
using Jornada.Aplicacao.Portas;
using Jornada.Dominio.Acesso;
using Jornada.Dominio.Catalogo;
using Jornada.Dominio.Comum;
using Jornada.Dominio.Governanca;
using Jornada.Dominio.Qualidade;

namespace Jornada.Aplicacao.Catalogo.Consultas;

/// <summary>
/// Os indicadores da visão executiva (<c>servicos.indicadores</c>), a
/// fotografia mensal deles (<c>gerar_snapshot</c>), a variação entre as duas
/// últimas competências (<c>variacao_indicadores</c>), a cobertura por domínio
/// (<c>cobertura_por_dominio</c>) e as pendências prioritárias
/// (<c>pendencias_prioritarias</c>).
///
/// Tudo sai de uma leitura só do catálogo e das relações. A qualidade média,
/// que o painel lê do score gravado de cada ativo, aqui é calculada na hora com
/// a mesma calculadora do pré-check, sobre os dados já carregados.
/// </summary>
public sealed class Indicadores(
    IRepositorioDeAtivos ativos,
    IRepositorioDeRelacoes relacoes,
    IRepositorioDeValidacoes validacoes,
    IRepositorioDeIndicadores fotografias,
    IMetamodelo metamodelo,
    IPoliticasDeGovernanca politicas,
    IContextoDeTenantAtual tenant,
    IAtorAtual atorAtual,
    IRelogio relogio)
{
    public const string Dominios = "dominios";
    public const string Capacidades = "capacidades";
    public const string Cobertura = "cobertura";
    public const string SemOwner = "sem_owner";
    public const string Apis = "apis";
    public const string ApisGovernadas = "apis_governadas";
    public const string Pendencias = "pendencias";
    public const string SlaVencido = "sla_vencido";
    public const string Publicados = "publicados";
    public const string QualidadeMedia = "qualidade_media";
    public const string DependenciasCriticas = "dependencias_criticas";

    /// <summary>Indicadores em que cair é bom: a tela pinta a queda como melhora.</summary>
    public static readonly IReadOnlySet<string> QuantoMenosMelhor = new HashSet<string> { SemOwner, Pendencias, SlaVencido, DependenciasCriticas };

    /// <summary>Capacidades de um domínio e quantas têm quem as implemente.</summary>
    public sealed record CoberturaDoDominio(IdDeAtivo Id, string Nome, int Total, int Cobertas)
    {
        public int Percentual => Total == 0 ? 0 : (int)Math.Round(100.0 * Cobertas / Total);
    }

    /// <summary>
    /// Uma pendência e o recorte que a explica: o tipo no catálogo (com ou sem
    /// o filtro de sem responsável), a fila de validação, ou nenhum.
    /// </summary>
    public sealed record PendenciaPrioritaria(string Chave, string Titulo, int Total, string? TipoItem = null, bool SemResponsavel = false, string? Tela = null);

    public sealed record Saida(
        IReadOnlyDictionary<string, double> Atuais,
        IReadOnlyDictionary<string, double> Variacao,
        IReadOnlyList<FotografiaDosIndicadores> Serie,
        IReadOnlyList<CoberturaDoDominio> CoberturaPorDominio,
        IReadOnlyList<PendenciaPrioritaria> Pendencias);

    /// <summary>Os indicadores de agora, a variação entre as duas últimas fotografias, a série inteira e os quadros da visão.</summary>
    public async Task<Saida> ConsultarAsync(CancellationToken ct = default)
    {
        var serie = await fotografias.ListarAsync(ct);
        var b = await CarregarAsync(ct);
        return new Saida(await CalcularAsync(b, ct), VariacaoEntre(serie), serie, CoberturaPorDominioDe(b), PendenciasDe(b));
    }

    /// <summary>
    /// Materializa os indicadores da competência (padrão: o mês corrente).
    /// Gravar de novo a mesma competência substitui a anterior. É tarefa de
    /// quem administra, como o <c>flask snapshot</c> do painel.
    /// </summary>
    public async Task<Resultado<FotografiaDosIndicadores>> FotografarAsync(string? competencia = null, CancellationToken ct = default)
    {
        var ator = await atorAtual.ObterAsync(ct);
        if (!PoliticaDeAutorizacao.Pode(ator, Acao.Administrar, AlvoDaAcao.NaEmpresa(tenant.Atual.Empresa), relogio.Hoje))
            return Recusa.Para<FotografiaDosIndicadores>(ator, "Fotografar os indicadores é atribuição do administrador.");

        var mes = string.IsNullOrWhiteSpace(competencia) ? relogio.Hoje.ToString("yyyy-MM") : competencia.Trim();
        if (mes.Length != 7 || mes[4] != '-' || !DateOnly.TryParseExact($"{mes}-01", "yyyy-MM-dd", out _))
            return Resultado<FotografiaDosIndicadores>.Recusado("COMPETENCIA_INVALIDA", "A competência é o mês, como 2026-08.");

        var fotografia = new FotografiaDosIndicadores(mes, await CalcularAsync(await CarregarAsync(ct), ct), relogio.Agora);
        await fotografias.GravarAsync(fotografia, ct);
        return Resultado<FotografiaDosIndicadores>.Ok(fotografia);
    }

    /// <summary>
    /// Diferença de cada indicador entre as duas últimas competências. Indicador
    /// que só aparece numa delas não vira tendência e fica de fora.
    /// </summary>
    public static IReadOnlyDictionary<string, double> VariacaoEntre(IReadOnlyList<FotografiaDosIndicadores> serie)
    {
        var porIndicador = new Dictionary<string, List<double>>();
        foreach (var fotografia in serie.OrderBy(f => f.Competencia, StringComparer.Ordinal))
            foreach (var (indicador, valor) in fotografia.Valores)
                (porIndicador.TryGetValue(indicador, out var lista) ? lista : porIndicador[indicador] = []).Add(valor);
        return porIndicador.Where(p => p.Value.Count >= 2)
            .ToDictionary(p => p.Key, p => Math.Round(p.Value[^1] - p.Value[^2], 1));
    }

    /// <summary>O que todos os quadros leem, carregado uma vez.</summary>
    private sealed record Base(
        IReadOnlyList<Ativo> Todos,
        IReadOnlyList<RelacaoResumo> Arestas,
        IReadOnlyList<ValidacaoEmProcesso> Abertas)
    {
        public IEnumerable<Ativo> NaoArquivados => Todos.Where(a => a.Status != StatusCicloVida.Arquivado);
        public HashSet<IdDeAtivo> Implementadas { get; } = Arestas.Where(r => r.Tipo == "implementa").Select(r => r.Destino).ToHashSet();
        public HashSet<IdDeAtivo> Implementam { get; } = Arestas.Where(r => r.Tipo == "implementa").Select(r => r.Origem).ToHashSet();
        public IEnumerable<RelacaoResumo> DependenciasCriticas =>
            Arestas.Where(r => r.Tipo == CatalogoDeRelacoes.DependeDe && r.Criticidade is Criticidade.Alta or Criticidade.Critica);
    }

    private async Task<Base> CarregarAsync(CancellationToken ct) => new(
        await ativos.BuscarAsync(new FiltroDoCatalogo(), BuscarNoCatalogo.Teto, ct),
        await relacoes.ListarArestasAsync(ct: ct),
        (await validacoes.ListarAsync(ct: ct)).Where(v => v.Status == StatusDaValidacao.Aberta).ToArray());

    private int Vencidas(Base b) => b.Abertas.Count(v => v.Prazo is { } p && p < relogio.Agora);

    private async Task<IReadOnlyDictionary<string, double>> CalcularAsync(Base b, CancellationToken ct)
    {
        var hoje = relogio.Hoje;
        static bool EmVigor(Ativo a) => a.Status is StatusCicloVida.Publicado or StatusCicloVida.EmRevisao;

        var capacidades = b.Todos.Where(a => a.TipoItem == "capacidade" && EmVigor(a)).ToArray();
        var apis = b.Todos.Where(a => a.TipoItem == "api").ToArray();

        static double Porcentagem(int parte, int total) => total == 0 ? 0 : Math.Round(100.0 * parte / total);

        return new Dictionary<string, double>
        {
            [Dominios] = b.Todos.Count(a => a.TipoItem == "dominio" && EmVigor(a)),
            [Capacidades] = capacidades.Length,
            [Cobertura] = Porcentagem(capacidades.Count(c => b.Implementadas.Contains(c.Id)), capacidades.Length),
            [SemOwner] = b.Todos.Count(a => a.Status == StatusCicloVida.Publicado && !a.Responsaveis.Any(r => r.Vigente(hoje))),
            [Apis] = apis.Length,
            [ApisGovernadas] = Porcentagem(apis.Count(a => a.Status == StatusCicloVida.Publicado && b.Implementam.Contains(a.Id)), apis.Length),
            [Pendencias] = b.Abertas.Count,
            [SlaVencido] = Vencidas(b),
            [Publicados] = b.Todos.Count(a => a.Status == StatusCicloVida.Publicado),
            [QualidadeMedia] = await QualidadeMediaAsync(b, ct),
            [DependenciasCriticas] = b.DependenciasCriticas.Count(),
        };
    }

    /// <summary>
    /// A média do score de qualidade dos ativos não arquivados (<c>qualidade.media_geral</c>).
    /// O contexto que o pré-check busca ativo a ativo sai aqui das listas já carregadas.
    /// </summary>
    private async Task<double> QualidadeMediaAsync(Base b, CancellationToken ct)
    {
        var tipos = await metamodelo.ObterAsync(ct);
        var regras = await politicas.ObterAsync(ct);
        var porId = b.Todos.ToDictionary(a => a.Id);
        var saidas = b.Arestas.ToLookup(r => r.Origem);
        var implementacoes = b.Arestas.Where(r => r.Tipo == "implementa").GroupBy(r => r.Destino).ToDictionary(g => g.Key, g => g.Count());

        var scores = new List<int>();
        foreach (var ativo in b.NaoArquivados.Where(a => tipos.Existe(a.TipoItem)))
        {
            var pai = ativo.IdPai is { } idPai ? porId.GetValueOrDefault(idPai) : null;
            var contexto = new ContextoDeQualidade(
                PaiExiste: pai is not null,
                PaiForaDeVigencia: pai is not null && MaquinaDeCicloDeVida.ForaDeVigencia(pai.Status),
                RelacoesParaAtivosForaDeVigencia: saidas[ativo.Id].Count(r => porId.TryGetValue(r.Destino, out var d) && MaquinaDeCicloDeVida.ForaDeVigencia(d.Status)),
                ImplementacoesDaCapacidade: ativo.TipoItem == "capacidade" ? implementacoes.GetValueOrDefault(ativo.Id) : 0);
            scores.Add(CalculadoraDeQualidade.Avaliar(ativo, tipos.Obter(ativo.TipoItem), regras.Para(ativo.TipoItem, ativo.Criticidade), contexto, relogio).Total);
        }
        return scores.Count == 0 ? 0 : Math.Round(scores.Average());
    }

    /// <summary>
    /// Por domínio, as capacidades abaixo dele, em qualquer profundidade, e
    /// quantas têm implementação. Como no painel, só entra domínio que já tem
    /// subdomínio; arquivados ficam de fora dos dois lados.
    /// </summary>
    private static IReadOnlyList<CoberturaDoDominio> CoberturaPorDominioDe(Base b)
    {
        var vivos = b.NaoArquivados.ToArray();
        var filhos = vivos.Where(a => a.IdPai is not null).ToLookup(a => a.IdPai!.Value);

        IEnumerable<Ativo> Descendentes(IdDeAtivo raiz)
        {
            var vistos = new HashSet<IdDeAtivo>();
            var pilha = new Stack<Ativo>(filhos[raiz]);
            while (pilha.Count > 0)
            {
                var atual = pilha.Pop();
                if (!vistos.Add(atual.Id)) continue;
                yield return atual;
                foreach (var f in filhos[atual.Id]) pilha.Push(f);
            }
        }

        return vivos.Where(a => a.TipoItem == "dominio" && filhos[a.Id].Any(f => f.TipoItem == "subdominio"))
            .OrderBy(a => a.Nome, StringComparer.CurrentCultureIgnoreCase)
            .Select(d =>
            {
                var capacidades = Descendentes(d.Id).Where(a => a.TipoItem == "capacidade").ToArray();
                return new CoberturaDoDominio(d.Id, d.Nome, capacidades.Length, capacidades.Count(c => b.Implementadas.Contains(c.Id)));
            })
            .ToArray();
    }

    /// <summary>As cinco pendências do painel, na mesma ordem, cada uma com o recorte que a explica.</summary>
    private IReadOnlyList<PendenciaPrioritaria> PendenciasDe(Base b)
    {
        var hoje = relogio.Hoje;
        var vivos = b.NaoArquivados.ToArray();
        return
        [
            new("capacidade_sem_owner", "Capacidade sem owner",
                vivos.Count(a => a.TipoItem == "capacidade" && !a.Responsaveis.Any(r => r.Papel == PapelDeOwnership.OwnerNegocial && r.Vigente(hoje))), TipoItem: "capacidade"),
            new("api_sem_capacidade", "API sem capacidade vinculada",
                vivos.Count(a => a.TipoItem == "api" && !b.Implementam.Contains(a.Id)), TipoItem: "api"),
            new("repositorio_sem_mantenedor", "Repositório sem mantenedor",
                vivos.Count(a => a.TipoItem == "repositorio" && !a.Responsaveis.Any(r => r.Vigente(hoje))), TipoItem: "repositorio", SemResponsavel: true),
            new("sla_vencido", "Validação com SLA vencido", Vencidas(b), Tela: "validacoes"),
            new("dependencia_critica_sem_evidencia", "Dependência crítica sem evidência",
                b.DependenciasCriticas.Count(r => r.OrigemDaEvidencia == CatalogoDeRelacoes.OrigemManual)),
        ];
    }
}
