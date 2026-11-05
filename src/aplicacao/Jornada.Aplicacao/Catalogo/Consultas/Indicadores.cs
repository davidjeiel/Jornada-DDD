using Jornada.Aplicacao.Acesso;
using Jornada.Aplicacao.Portas;
using Jornada.Dominio.Acesso;
using Jornada.Dominio.Catalogo;
using Jornada.Dominio.Comum;

namespace Jornada.Aplicacao.Catalogo.Consultas;

/// <summary>
/// Os indicadores da visão executiva (<c>servicos.indicadores</c>), a
/// fotografia mensal deles (<c>gerar_snapshot</c>) e a variação entre as duas
/// últimas competências (<c>variacao_indicadores</c>).
///
/// Fora, de propósito: a qualidade média. O painel guarda o score de cada
/// ativo; o Jornada o calcula no pré-check, e recalcular o catálogo inteiro a
/// cada visita à visão executiva não compensa.
/// </summary>
public sealed class Indicadores(
    IRepositorioDeAtivos ativos,
    IRepositorioDeRelacoes relacoes,
    IRepositorioDeValidacoes validacoes,
    IRepositorioDeIndicadores fotografias,
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
    public const string DependenciasCriticas = "dependencias_criticas";

    /// <summary>Indicadores em que cair é bom: a tela pinta a queda como melhora.</summary>
    public static readonly IReadOnlySet<string> QuantoMenosMelhor = new HashSet<string> { SemOwner, Pendencias, SlaVencido, DependenciasCriticas };

    public sealed record Saida(
        IReadOnlyDictionary<string, double> Atuais,
        IReadOnlyDictionary<string, double> Variacao,
        IReadOnlyList<FotografiaDosIndicadores> Serie);

    /// <summary>Os indicadores de agora, a variação entre as duas últimas fotografias e a série inteira.</summary>
    public async Task<Saida> ConsultarAsync(CancellationToken ct = default)
    {
        var serie = await fotografias.ListarAsync(ct);
        return new Saida(await CalcularAsync(ct), VariacaoEntre(serie), serie);
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

        var fotografia = new FotografiaDosIndicadores(mes, await CalcularAsync(ct), relogio.Agora);
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

    private async Task<IReadOnlyDictionary<string, double>> CalcularAsync(CancellationToken ct)
    {
        var hoje = relogio.Hoje;
        var todos = await ativos.BuscarAsync(new FiltroDoCatalogo(), BuscarNoCatalogo.Teto, ct);
        static bool EmVigor(Ativo a) => a.Status is StatusCicloVida.Publicado or StatusCicloVida.EmRevisao;

        var implementa = await relacoes.ListarArestasAsync("implementa", ct);
        var implementadas = implementa.Select(r => r.Destino).ToHashSet();
        var implementam = implementa.Select(r => r.Origem).ToHashSet();
        var dependencias = await relacoes.ListarArestasAsync(CatalogoDeRelacoes.DependeDe, ct);

        var capacidades = todos.Where(a => a.TipoItem == "capacidade" && EmVigor(a)).ToArray();
        var apis = todos.Where(a => a.TipoItem == "api").ToArray();
        var abertas = (await validacoes.ListarAsync(ct: ct)).Where(v => v.Status == StatusDaValidacao.Aberta).ToArray();

        static double Porcentagem(int parte, int total) => total == 0 ? 0 : Math.Round(100.0 * parte / total);

        return new Dictionary<string, double>
        {
            [Dominios] = todos.Count(a => a.TipoItem == "dominio" && EmVigor(a)),
            [Capacidades] = capacidades.Length,
            [Cobertura] = Porcentagem(capacidades.Count(c => implementadas.Contains(c.Id)), capacidades.Length),
            [SemOwner] = todos.Count(a => a.Status == StatusCicloVida.Publicado && !a.Responsaveis.Any(r => r.Vigente(hoje))),
            [Apis] = apis.Length,
            [ApisGovernadas] = Porcentagem(apis.Count(a => a.Status == StatusCicloVida.Publicado && implementam.Contains(a.Id)), apis.Length),
            [Pendencias] = abertas.Length,
            [SlaVencido] = abertas.Count(v => v.Prazo is { } p && p < relogio.Agora),
            [Publicados] = todos.Count(a => a.Status == StatusCicloVida.Publicado),
            [DependenciasCriticas] = dependencias.Count(r => r.Criticidade is Criticidade.Alta or Criticidade.Critica),
        };
    }
}
