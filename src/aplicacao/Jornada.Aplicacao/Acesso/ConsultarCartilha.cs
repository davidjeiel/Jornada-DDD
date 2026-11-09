using Jornada.Aplicacao.Portas;
using Jornada.Dominio.Acesso;
using Jornada.Dominio.Governanca;

namespace Jornada.Aplicacao.Acesso;

/// <summary>
/// As tabelas de referência da cartilha, lidas do código. Portado de
/// <c>cartilha.matriz</c>, <c>cartilha.etapas_por_papel</c> e da tabela de
/// ritos que o painel lê de <c>politica_governanca</c>.
///
/// O risco de uma cartilha não é quebrar, é envelhecer: uma página que descreve
/// uma regra que o sistema já não aplica é pior do que nenhuma. Por isso nada
/// aqui é escrito à mão.
/// </summary>
public sealed class ConsultarCartilha(IPoliticasDeGovernanca politicas, IMetamodelo metamodelo)
{
    /// <summary>A ordem das colunas na tela, a do painel (<c>PAPEIS_ORDEM</c>).</summary>
    public static readonly Papel[] OrdemDosPapeis =
        [Papel.Negocio, Papel.TechLead, Papel.Arquiteto, Papel.Curador, Papel.Admin, Papel.Consulta];

    /// <summary>A ordem do dia de trabalho, com o rótulo de <c>ACOES_ROTULO</c>.</summary>
    public static readonly (Acao Acao, string Rotulo)[] Acoes =
    [
        (Acao.Cadastrar, "Cadastrar ativos"),
        (Acao.Editar, "Editar rascunho"),
        (Acao.Relacionar, "Registrar relação"),
        (Acao.Submeter, "Enviar para validação"),
        (Acao.Revisar, "Abrir revisão"),
        (Acao.Decidir, "Decidir validação"),
        (Acao.Importar, "Importar descobertas"),
        (Acao.Triar, "Triar a bandeja"),
        (Acao.Descontinuar, "Descontinuar ativo"),
        (Acao.Conceder, "Conceder papéis"),
        (Acao.Administrar, "Administrar a ferramenta"),
    ];

    public sealed record LinhaDaMatriz(Acao Acao, string Rotulo, IReadOnlyDictionary<Papel, bool> Papeis);

    public sealed record Rito(string TipoItem, string Rotulo, PoliticaDeGovernanca Politica);

    public sealed record Saida(
        IReadOnlyList<LinhaDaMatriz> Matriz,
        IReadOnlyDictionary<Papel, IReadOnlyList<EtapaValidacao>> EtapasPorPapel,
        IReadOnlyDictionary<Papel, IReadOnlyList<string>> BlocosPorPapel,
        IReadOnlyList<Rito> Ritos,
        IReadOnlyList<MatrizDeMudanca.Linha> MatrizDeMudanca);

    /// <summary>Cada célula é <see cref="PoliticaDeAutorizacao.PapeisQuePodem"/>: mudou a regra, muda a página.</summary>
    public static IReadOnlyList<LinhaDaMatriz> Matriz() =>
        Acoes.Select(a =>
        {
            var papeis = PoliticaDeAutorizacao.PapeisQuePodem(a.Acao);
            return new LinhaDaMatriz(a.Acao, a.Rotulo, OrdemDosPapeis.ToDictionary(p => p, papeis.Contains));
        }).ToArray();

    public static IReadOnlyDictionary<Papel, IReadOnlyList<EtapaValidacao>> EtapasPorPapel() =>
        OrdemDosPapeis.ToDictionary(p => p, PoliticaDeAutorizacao.EtapasDoPapel);

    public async Task<Saida> ExecutarAsync(CancellationToken ct = default)
    {
        var tipos = await metamodelo.ObterAsync(ct);
        var ritos = (await politicas.ObterAsync(ct)).Todas
            .Select(p => new Rito(p.TipoItem, tipos.Existe(p.TipoItem) ? tipos.Obter(p.TipoItem).Rotulo : p.TipoItem, p))
            .ToArray();

        return new Saida(Matriz(), EtapasPorPapel(),
            OrdemDosPapeis.ToDictionary(p => p, PoliticaDeAutorizacao.BlocosDoPapel),
            ritos,
            Dominio.Governanca.MatrizDeMudanca.Linhas);
    }
}
