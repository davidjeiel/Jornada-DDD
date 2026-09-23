using Jornada.Aplicacao.Portas;
using Jornada.Dominio.Catalogo;

namespace Jornada.Aplicacao.Catalogo.Consultas;

/// <summary>
/// Visão consolidada de um ativo — a "ficha completa" (versão enxuta de
/// <c>servicos.visao_360</c>): atributos, evidências, responsáveis e relações,
/// não só as contagens. Consulta de leitura pesada não precisa passar por
/// agregado: o ADR-0001 prevê CQRS parcial justamente para casos assim.
/// </summary>
public sealed class ObterVisaoDoAtivo(IRepositorioDeAtivos ativos, IRepositorioDeRelacoes relacoes)
{
    public sealed record EvidenciaResumo(Guid Id, string Tipo, string Titulo, string? Url);

    public sealed record ResponsavelResumo(
        Guid IdPessoa, string Papel, DateOnly InicioVigencia, DateOnly? FimVigencia, bool Vigente);

    public sealed record RelacaoResumo(
        string Direcao, string AtivoId, string Codigo, string Nome, string Tipo);

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
        IReadOnlyList<EvidenciaResumo> Evidencias,
        IReadOnlyList<ResponsavelResumo> Responsaveis,
        IReadOnlyList<RelacaoResumo> Relacoes);

    public async Task<Resultado<Saida>> ExecutarAsync(IdDeAtivo id, CancellationToken ct = default)
    {
        var ativo = await ativos.ObterAsync(id, ct);
        if (ativo is null)
            return Resultado<Saida>.Recusado("ATIVO_NAO_ENCONTRADO", "Ativo não encontrado.");

        var trilha = await ativos.TrilhaAsync(id, ct);
        var hoje = DateOnly.FromDateTime(DateTime.UtcNow);

        var arestas = await relacoes.ListarRelacoesAsync(id, ct);
        var relacoesResolvidas = new List<RelacaoResumo>();
        foreach (var aresta in arestas)
        {
            var outroLado = aresta.Origem == id ? aresta.Destino : aresta.Origem;
            var direcao = aresta.Origem == id ? "saida" : "entrada";
            var outroAtivo = await ativos.ObterAsync(outroLado, ct);
            if (outroAtivo is null) continue; // relação órfã: já vira alerta no pré-check, não quebra a ficha

            relacoesResolvidas.Add(new RelacaoResumo(
                direcao, outroAtivo.Id.Valor.ToString(), outroAtivo.Codigo.Valor, outroAtivo.Nome, aresta.Tipo));
        }

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
            ativo.Evidencias.Select(e => new EvidenciaResumo(e.Id, e.Tipo, e.Titulo, e.Url)).ToArray(),
            ativo.Responsaveis.Select(r => new ResponsavelResumo(
                r.IdPessoa, r.Papel.ToString(), r.InicioVigencia, r.FimVigencia, r.Vigente(hoje))).ToArray(),
            relacoesResolvidas));
    }
}
