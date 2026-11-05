using Jornada.Aplicacao.Acesso;
using Jornada.Aplicacao.Catalogo.Consultas;
using Jornada.Aplicacao.Portas;
using Jornada.Dominio.Acesso;
using Jornada.Dominio.Catalogo;
using Jornada.Dominio.Comum;
using Jornada.Dominio.Governanca;

namespace Jornada.Aplicacao.Governanca;

/// <summary>
/// O tempo que falta até o prazo, como a fila mostra (<c>governanca.sla_restante</c>):
/// "vencido", horas abaixo de dois dias, dias a partir daí.
/// </summary>
public static class Sla
{
    public static string Restante(DateTimeOffset? prazo, DateTimeOffset agora)
    {
        if (prazo is not { } p) return "—";
        var falta = p - agora;
        if (falta < TimeSpan.Zero) return "vencido";
        var horas = (int)falta.TotalHours;
        return horas < 48 ? $"{horas}h" : $"{horas / 24}d";
    }
}

/// <summary>Uma etapa aberta na fila, com o ativo e o tempo que falta.</summary>
public sealed record ItemDaFila(ValidacaoEmProcesso Validacao, Ativo Ativo, string SlaRestante);

/// <summary>
/// A fila de validação (<c>governanca.fila_validacao</c>): só etapas abertas, a
/// mais crítica primeiro e, na mesma criticidade, o prazo mais próximo. Com a
/// contagem por etapa das abas, e a das "minhas".
/// </summary>
public sealed class ConsultarFila(IRepositorioDeValidacoes validacoes, IRepositorioDeAtivos ativos, IAtorAtual atorAtual, IRelogio relogio)
{
    public sealed record Saida(IReadOnlyList<ItemDaFila> Itens, IReadOnlyDictionary<string, int> Contagem);

    public async Task<Saida> ExecutarAsync(
        EtapaValidacao? etapa = null, bool somenteMinhas = false, bool apenasLivres = false, CancellationToken ct = default)
    {
        var ator = await atorAtual.ObterAsync(ct);
        var abertas = await AbertasAsync(ct);

        var contagem = abertas.GroupBy(i => i.Validacao.Etapa.ToString().ToLowerInvariant()).ToDictionary(g => g.Key, g => g.Count());
        contagem["minhas"] = abertas.Count(i => ator.Identificado && i.Validacao.AtribuidaA == ator.IdPessoa);

        var itens = abertas
            .Where(i => etapa is null || i.Validacao.Etapa == etapa)
            .Where(i => !somenteMinhas || (ator.Identificado && i.Validacao.AtribuidaA == ator.IdPessoa))
            .Where(i => !apenasLivres || i.Validacao.AtribuidaA is null)
            .ToArray();
        return new Saida(itens, contagem);
    }

    internal async Task<IReadOnlyList<ItemDaFila>> AbertasAsync(CancellationToken ct)
    {
        var itens = new List<ItemDaFila>();
        foreach (var v in (await validacoes.ListarAsync(ct: ct)).Where(v => v.Status == StatusDaValidacao.Aberta))
            if (await ativos.ObterAsync(v.Ativo, ct) is { } ativo)
                itens.Add(new ItemDaFila(v, ativo, Sla.Restante(v.Prazo, relogio.Agora)));

        return itens
            .OrderByDescending(i => i.Ativo.Criticidade)
            .ThenBy(i => i.Validacao.Prazo ?? DateTimeOffset.MaxValue)
            .ThenBy(i => i.Validacao.Id)
            .ToArray();
    }
}

/// <summary>Assumir e liberar uma validação (<c>assumir_validacao</c> / <c>liberar_validacao</c>), para quem decide.</summary>
public sealed class AtribuirValidacao(
    IRepositorioDeValidacoes validacoes,
    IRepositorioDeAtivos ativos,
    IMetamodelo metamodelo,
    IContextoDeTenantAtual tenant,
    IAtorAtual atorAtual,
    IUnidadeDeTrabalho uow,
    IRelogio relogio)
{
    public Task<Resultado<ValidacaoEmProcesso>> AssumirAsync(Guid id, CancellationToken ct = default) =>
        AlterarAsync(id, (v, ator) => v.Assumir(ator.IdPessoa), ct);

    public Task<Resultado<ValidacaoEmProcesso>> LiberarAsync(Guid id, CancellationToken ct = default) =>
        AlterarAsync(id, (v, ator) => v.Liberar(ator.IdPessoa), ct);

    private async Task<Resultado<ValidacaoEmProcesso>> AlterarAsync(
        Guid id, Action<ValidacaoEmProcesso, Ator> alteracao, CancellationToken ct)
    {
        var validacao = await validacoes.ObterAsync(id, ct);
        if (validacao is null)
            return Resultado<ValidacaoEmProcesso>.Recusado("VALIDACAO_NAO_ENCONTRADA", "Validação não encontrada.");
        var ativo = await ativos.ObterAsync(validacao.Ativo, ct);
        if (ativo is null)
            return Resultado<ValidacaoEmProcesso>.Recusado("ATIVO_NAO_ENCONTRADO", "Ativo não encontrado.");

        var ator = await atorAtual.ObterAsync(ct);
        var tipo = (await metamodelo.ObterAsync(ct)).Obter(ativo.TipoItem);
        var alvo = await AlvoDoAtivo.ResolverAsync(ativos, tenant.Atual, ativo, tipo, ct);
        if (!PoliticaDeAutorizacao.Pode(ator, Acao.Decidir, alvo, relogio.Hoje))
            return Recusa.Para<ValidacaoEmProcesso>(ator, "Assumir validação é de quem decide.");

        try { alteracao(validacao, ator); }
        catch (ErroDeDominio e) { return Resultado<ValidacaoEmProcesso>.Recusado(e.Codigo, e.Message); }

        await uow.ConfirmarAsync(ct);
        return Resultado<ValidacaoEmProcesso>.Ok(validacao);
    }
}

/// <summary>
/// O painel do validador (a seleção em <c>/validacoes?id_validacao=</c>): a
/// etapa, a ficha do ativo, o pré-check e o que mudou desde a revisão anterior.
/// </summary>
public sealed class PainelDoValidador(
    IRepositorioDeValidacoes validacoes,
    ObterVisaoDoAtivo visao,
    AvaliarPreCheck preCheck,
    CompararRevisoes comparar)
{
    /// <param name="Mudancas">Nulo na primeira revisão, ou quando a anterior não tem retrato.</param>
    public sealed record Saida(
        ValidacaoEmProcesso Validacao, ObterVisaoDoAtivo.Saida Ficha, AvaliarPreCheck.Saida PreCheck,
        CompararRevisoes.Saida? Mudancas, bool PrimeiraRevisao);

    public async Task<Resultado<Saida>> ExecutarAsync(Guid id, CancellationToken ct = default)
    {
        var validacao = await validacoes.ObterAsync(id, ct);
        if (validacao is null)
            return Resultado<Saida>.Recusado("VALIDACAO_NAO_ENCONTRADA", "Validação não encontrada.");

        var ficha = await visao.ExecutarAsync(validacao.Ativo, ct);
        if (!ficha.Sucesso) return Resultado<Saida>.Recusado(ficha.Codigo!, ficha.Mensagem!);
        var checagem = await preCheck.ExecutarAsync(validacao.Ativo, ct);
        if (!checagem.Sucesso) return Resultado<Saida>.Recusado(checagem.Codigo!, checagem.Mensagem!);

        var primeira = validacao.Revisao <= 1;
        CompararRevisoes.Saida? mudancas = null;
        if (!primeira)
        {
            var r = await comparar.ExecutarAsync(validacao.Ativo, validacao.Revisao - 1, validacao.Revisao, ct);
            if (r.Sucesso) mudancas = r.Valor;
        }
        return Resultado<Saida>.Ok(new Saida(validacao, ficha.Valor!, checagem.Valor!, mudancas, primeira));
    }
}

/// <summary>
/// "Minha mesa" (<c>servicos.minha_mesa</c>): o que assumi, o que posso
/// assumir, o que cadastrei e ainda está comigo, e o que cadastrei e espera decisão.
/// </summary>
public sealed class MinhaMesa(ConsultarFila fila, IRepositorioDeAtivos ativos, IAtorAtual atorAtual)
{
    public sealed record Saida(
        IReadOnlyList<ItemDaFila> MinhasValidacoes, IReadOnlyList<ItemDaFila> FilaLivre,
        IReadOnlyList<Ativo> MeusRascunhos, IReadOnlyList<Ativo> AguardandoDecisao);

    public async Task<Saida> ExecutarAsync(CancellationToken ct = default)
    {
        var ator = await atorAtual.ObterAsync(ct);
        var abertas = await fila.AbertasAsync(ct);
        if (!ator.Identificado)
            return new Saida([], abertas.Where(i => i.Validacao.AtribuidaA is null).ToArray(), [], []);

        var meus = new List<Ativo>();
        foreach (var status in new[] { StatusCicloVida.Rascunho, StatusCicloVida.EmRevisao, StatusCicloVida.EmValidacao })
            meus.AddRange(await ativos.ListarAsync(status: status, criadoPor: ator.IdPessoa, ct: ct));

        return new Saida(
            abertas.Where(i => i.Validacao.AtribuidaA == ator.IdPessoa).ToArray(),
            abertas.Where(i => i.Validacao.AtribuidaA is null).ToArray(),
            meus.Where(a => a.Status != StatusCicloVida.EmValidacao).OrderByDescending(a => a.AtualizadoEm).ToArray(),
            meus.Where(a => a.Status == StatusCicloVida.EmValidacao).OrderByDescending(a => a.AtualizadoEm).ToArray());
    }
}
