using Jornada.Aplicacao.Portas;
using Jornada.Dominio.Catalogo;

namespace Jornada.Aplicacao.Catalogo.Consultas;

/// <summary>
/// Visão consolidada de um ativo. Versão enxuta de <c>servicos.visao_360</c>.
///
/// Consulta de leitura pesada não precisa passar por agregado: o ADR-0001
/// prevê CQRS parcial justamente para casos assim. Aqui ainda usamos o
/// repositório porque o volume não justifica SQL dedicado — quando justificar,
/// é um adaptador de consulta com Dapper, sem tocar no domínio.
/// </summary>
public sealed class ObterVisaoDoAtivo(IRepositorioDeAtivos ativos)
{
    public sealed record Saida(
        IdDeAtivo Id,
        string Codigo,
        string Nome,
        string TipoItem,
        string Status,
        string Criticidade,
        string Descricao,
        int RevisaoAtual,
        IReadOnlyList<string> Trilha,
        IReadOnlyDictionary<string, string> Atributos,
        int Evidencias,
        int Responsaveis);

    public async Task<Resultado<Saida>> ExecutarAsync(IdDeAtivo id, CancellationToken ct = default)
    {
        var ativo = await ativos.ObterAsync(id, ct);
        if (ativo is null)
            return Resultado<Saida>.Recusado("ATIVO_NAO_ENCONTRADO", "Ativo não encontrado.");

        var trilha = await ativos.TrilhaAsync(id, ct);

        return Resultado<Saida>.Ok(new Saida(
            ativo.Id,
            ativo.Codigo.Valor,
            ativo.Nome,
            ativo.TipoItem,
            MaquinaDeCicloDeVida.Rotulo(ativo.Status),
            ativo.Criticidade.Chave(),
            ativo.Descricao,
            ativo.RevisaoAtual,
            trilha.Select(a => a.Nome).ToArray(),
            ativo.Atributos,
            ativo.Evidencias.Count,
            ativo.Responsaveis.Count));
    }
}
