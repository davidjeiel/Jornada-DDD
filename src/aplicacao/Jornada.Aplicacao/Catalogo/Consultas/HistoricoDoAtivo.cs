using Jornada.Aplicacao.Portas;
using Jornada.Dominio.Catalogo;

namespace Jornada.Aplicacao.Catalogo.Consultas;

/// <summary>
/// A aba Histórico da visão 360° do painel-ddd: as revisões submetidas (com o
/// hash e se ele ainda confere) e a trilha de auditoria. Só leitura.
/// </summary>
public sealed class ConsultarHistorico(
    IRepositorioDeAtivos ativos, IRepositorioDeRevisoes revisoes, ITrilhaDeAuditoria trilha)
{
    public sealed record Revisao(
        int Numero, string Motivo, Guid IdAutor, DateTimeOffset CriadaEm, DateTimeOffset? PublicadaEm,
        string Hash, bool Integra);

    public sealed record Saida(IReadOnlyList<Revisao> Revisoes, IReadOnlyList<RegistroDeAuditoria> Auditoria);

    public async Task<Resultado<Saida>> ExecutarAsync(IdDeAtivo id, int limiteDeAuditoria = 30, CancellationToken ct = default)
    {
        if (await ativos.ObterAsync(id, ct) is null)
            return Resultado<Saida>.Recusado("ATIVO_NAO_ENCONTRADO", "Ativo não encontrado.");

        var lista = (await revisoes.ListarAsync(id, ct))
            .Select(r => new Revisao(r.Numero, r.Motivo, r.IdAutor, r.CriadaEm, r.PublicadaEm, r.Hash, r.Integra))
            .ToArray();
        return Resultado<Saida>.Ok(new Saida(lista, await trilha.ListarAsync(id, limiteDeAuditoria, ct)));
    }
}

/// <summary>
/// Revisão A contra revisão B. Equivale a <c>servicos.comparar_revisoes</c> e à
/// rota <c>/ativo/&lt;id&gt;/comparar</c>. Sem números, compara a última com a
/// anterior — o atalho "Comparar as duas últimas revisões" do painel.
/// </summary>
public sealed class CompararRevisoes(IRepositorioDeAtivos ativos, IRepositorioDeRevisoes revisoes)
{
    public sealed record Saida(int A, int B, IReadOnlyList<DiferencaEntreRevisoes> Diferencas);

    public async Task<Resultado<Saida>> ExecutarAsync(IdDeAtivo id, int? a = null, int? b = null, CancellationToken ct = default)
    {
        if (await ativos.ObterAsync(id, ct) is null)
            return Resultado<Saida>.Recusado("ATIVO_NAO_ENCONTRADO", "Ativo não encontrado.");

        var depois = b ?? (await revisoes.ListarAsync(id, ct)).FirstOrDefault()?.Numero ?? 0;
        var antes = a ?? depois - 1;

        var ra = await revisoes.ObterAsync(id, antes, ct);
        var rb = await revisoes.ObterAsync(id, depois, ct);
        if (ra is null || rb is null)
        {
            return Resultado<Saida>.Recusado("REVISAO_NAO_ENCONTRADA",
                $"Para comparar, as revisões {antes} e {depois} precisam existir. Revisão anterior à comparação não tem retrato.");
        }

        // Pai e destinos de relação aparecem pelo código, não pelo id.
        var nomes = new Dictionary<Guid, string>();
        foreach (var citado in ComparadorDeRevisoes.IdsCitados(ra, rb))
            if (await ativos.ObterAsync(new IdDeAtivo(citado), ct) is { } ativo)
                nomes[citado] = $"{ativo.Codigo.Valor} {ativo.Nome}";

        var diferencas = ComparadorDeRevisoes.Comparar(ra, rb,
            id => nomes.GetValueOrDefault(id) ?? id.ToString()[..8]);
        return Resultado<Saida>.Ok(new Saida(antes, depois, diferencas));
    }
}
