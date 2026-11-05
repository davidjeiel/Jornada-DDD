using Jornada.Aplicacao.Portas;

namespace Jornada.Persistencia.Cosmos;

/// <summary>
/// As fotografias mensais dos indicadores. Uma por competência, gravada direto
/// (fora da unidade de trabalho): não há agregado a manter coerente com ela.
/// </summary>
public sealed partial class RepositorioCosmos
{
    async Task IRepositorioDeIndicadores.GravarAsync(FotografiaDosIndicadores fotografia, CancellationToken ct) =>
        await _acesso.GravarAsync(new DocumentoDeFotografia
        {
            Id = $"fotografia-{fotografia.Competencia}",
            Empresa = Mapeamento.Texto(_acesso.Empresa.Valor),
            IdAtivo = AcessoDoTenant.ParticaoDeIndicadores,
            Competencia = fotografia.Competencia,
            Valores = new Dictionary<string, double>(fotografia.Valores),
            GeradaEm = fotografia.GeradaEm,
        }, ct);

    async Task<IReadOnlyList<FotografiaDosIndicadores>> IRepositorioDeIndicadores.ListarAsync(CancellationToken ct) =>
        (await _acesso.ConsultarAsync<DocumentoDeFotografia>(TiposDeDocumento.Fotografia, ct: ct))
            .Select(d => new FotografiaDosIndicadores(d.Competencia, d.Valores, d.GeradaEm))
            .OrderBy(f => f.Competencia, StringComparer.Ordinal)
            .ToArray();
}
