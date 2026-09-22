using Jornada.Aplicacao.Portas;
using Jornada.Dominio.Catalogo;
using Jornada.Dominio.Comum;
using Jornada.Dominio.Governanca;
using Jornada.Dominio.Qualidade;

namespace Jornada.Aplicacao.Catalogo.Consultas;

/// <summary>
/// Monta o contexto que as políticas puras precisam e as executa.
///
/// É aqui que fica TODO o I/O que hoje está dentro de
/// <c>governanca.pre_check</c> e <c>qualidade.avaliar</c>. As regras em si não
/// tocam em nada — é essa separação que o ADR-0001 compra.
/// </summary>
public sealed class AvaliarPreCheck(
    IRepositorioDeAtivos ativos,
    IRepositorioDeRelacoes relacoes,
    IMetamodelo metamodelo,
    IPoliticasDeGovernanca politicas,
    IRelogio relogio)
{
    public sealed record Saida(
        ResultadoPreCheck Checagem,
        ScoreDeQualidade Score,
        IReadOnlyList<PassoDePublicacao> Caminho);

    public async Task<Resultado<Saida>> ExecutarAsync(IdDeAtivo id, CancellationToken ct = default)
    {
        var ativo = await ativos.ObterAsync(id, ct);
        if (ativo is null)
            return Resultado<Saida>.Recusado("ATIVO_NAO_ENCONTRADO", "Ativo não encontrado.");

        var (checagem, score) = await AvaliarAsync(ativo, ct);
        return Resultado<Saida>.Ok(new Saida(checagem, score, CaminhoDePublicacao.De(checagem)));
    }

    /// <summary>Reutilizado por <c>SubmeterAtivo</c> — uma regra, dois chamadores.</summary>
    public async Task<(ResultadoPreCheck Checagem, ScoreDeQualidade Score)> AvaliarAsync(
        Ativo ativo, CancellationToken ct = default)
    {
        var tipos = await metamodelo.ObterAsync(ct);
        var tipo = tipos.Obter(ativo.TipoItem);
        var politica = (await politicas.ObterAsync(ct)).Para(ativo.TipoItem, ativo.Criticidade);

        // ── fatos que as políticas puras não conseguem descobrir sozinhas ──
        var pai = ativo.IdPai is { } idPai ? await ativos.ObterAsync(idPai, ct) : null;

        var foraDeVigencia = await relacoes.NomesDeDestinosForaDeVigenciaAsync(ativo.Id, ct);

        var implementacoes = ativo.TipoItem.Equals("capacidade", StringComparison.OrdinalIgnoreCase)
            ? await relacoes.ContarImplementacoesAsync(ativo.Id, ct)
            : 0;

        var contextoQualidade = new ContextoDeQualidade(
            PaiExiste: pai is not null,
            PaiForaDeVigencia: pai is not null && MaquinaDeCicloDeVida.ForaDeVigencia(pai.Status),
            RelacoesParaAtivosForaDeVigencia: foraDeVigencia.Count,
            ImplementacoesDaCapacidade: implementacoes);

        var score = CalculadoraDeQualidade.Avaliar(ativo, tipo, politica, contextoQualidade, relogio);

        var contextoPreCheck = new ContextoDePreCheck(
            PaiInformado: ativo.IdPai is not null,
            NomeDuplicadoNaEmpresa: await ativos.ExisteNomeDuplicadoAsync(
                ativo.TipoItem, ativo.Nome, ativo.Id, ct),
            TemDependenciaCircular: await relacoes.TemDependenciaCircularAsync(ativo.Id, ct),
            RelacoesForaDeVigencia: foraDeVigencia);

        var checagem = PoliticaDePublicacao.Avaliar(
            ativo, tipo, politica, score, contextoPreCheck, relogio);

        return (checagem, score);
    }
}
