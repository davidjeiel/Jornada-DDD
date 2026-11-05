using Jornada.Aplicacao.Acesso;
using Jornada.Aplicacao.Portas;
using Jornada.Dominio.Acesso;
using Jornada.Dominio.Catalogo;
using Jornada.Dominio.Comum;
using Jornada.Dominio.Governanca;
using Jornada.Dominio.Notificacoes;

namespace Jornada.Aplicacao.Notificacoes;

/// <summary>
/// A caixa de quem está identificado (<c>notificacoes.caixa</c>, <c>nao_lidas</c>,
/// <c>marcar_lida</c>, <c>marcar_todas_lidas</c>). Ler e marcar como lida mexem
/// só na caixa da própria pessoa, e por isso valem também no modo leitura.
/// </summary>
public sealed class CaixaDeNotificacoes(IRepositorioDeNotificacoes notificacoes, IAtorAtual atorAtual, IUnidadeDeTrabalho uow, IRelogio relogio)
{
    public const int LimitePadrao = 50;

    public sealed record Saida(IReadOnlyList<Notificacao> Itens, int NaoLidas);

    public async Task<Resultado<Saida>> ListarAsync(bool apenasNaoLidas = false, int limite = LimitePadrao, CancellationToken ct = default)
    {
        var ator = await atorAtual.ObterAsync(ct);
        if (!ator.Identificado) return Recusa.Para<Saida>(ator, "");
        var itens = await notificacoes.DaPessoaAsync(ator.IdPessoa, apenasNaoLidas, Math.Clamp(limite, 1, 200), ct);
        return Resultado<Saida>.Ok(new Saida(itens, await notificacoes.ContarNaoLidasAsync(ator.IdPessoa, ct)));
    }

    /// <summary>A bolha do sino. Sem pessoa, zero: o sino não é motivo para recusar a página.</summary>
    public async Task<int> ContarNaoLidasAsync(CancellationToken ct = default)
    {
        var ator = await atorAtual.ObterAsync(ct);
        return ator.Identificado ? await notificacoes.ContarNaoLidasAsync(ator.IdPessoa, ct) : 0;
    }

    public async Task<Resultado<bool>> MarcarLidaAsync(Guid id, CancellationToken ct = default)
    {
        var ator = await atorAtual.ObterAsync(ct);
        if (!ator.Identificado) return Recusa.Para<bool>(ator, "");
        // A notificação de outra pessoa é tratada como inexistente: não se confirma que ela existe.
        if (await notificacoes.ObterAsync(id, ct) is not { } notificacao || notificacao.IdPessoa != ator.IdPessoa)
            return Resultado<bool>.Recusado("NOTIFICACAO_NAO_ENCONTRADA", "Notificação não encontrada.");
        if (notificacao.MarcarLida(relogio.Agora)) await uow.ConfirmarAsync(ct);
        return Resultado<bool>.Ok(true);
    }

    public async Task<Resultado<int>> MarcarTodasLidasAsync(CancellationToken ct = default)
    {
        var ator = await atorAtual.ObterAsync(ct);
        if (!ator.Identificado) return Recusa.Para<int>(ator, "");
        var marcadas = (await notificacoes.DaPessoaAsync(ator.IdPessoa, apenasNaoLidas: true, limite: 1000, ct: ct))
            .Count(n => n.MarcarLida(relogio.Agora));
        if (marcadas > 0) await uow.ConfirmarAsync(ct);
        return Resultado<int>.Ok(marcadas);
    }
}

/// <summary>As preferências de quem está identificado (<c>notificacoes.preferencias</c> e <c>salvar_preferencias</c>).</summary>
public sealed class PreferenciasDoNotificado(IRepositorioDePreferencias preferencias, IAtorAtual atorAtual)
{
    public async Task<Resultado<PreferenciasDeNotificacao>> ObterAsync(CancellationToken ct = default)
    {
        var ator = await atorAtual.ObterAsync(ct);
        if (!ator.Identificado) return Recusa.Para<PreferenciasDeNotificacao>(ator, "");
        return Resultado<PreferenciasDeNotificacao>.Ok(await preferencias.ObterAsync(ator.IdPessoa, ct));
    }

    /// <summary>Guarda exatamente o que veio ligado: o que não veio fica desligado.</summary>
    public async Task<Resultado<PreferenciasDeNotificacao>> SalvarAsync(
        IEnumerable<(string Tipo, string Canal)> ativas, CancellationToken ct = default)
    {
        var ator = await atorAtual.ObterAsync(ct);
        if (!ator.Identificado) return Recusa.Para<PreferenciasDeNotificacao>(ator, "");

        var chaves = new HashSet<string>();
        foreach (var (tipo, canal) in ativas)
        {
            if (!TiposDeNotificacao.Existe(tipo))
                return Resultado<PreferenciasDeNotificacao>.Recusado("TIPO_DE_NOTIFICACAO_INVALIDO", $"Tipo de notificação desconhecido: {tipo}.");
            if (CanaisDeNotificacao.Todos.All(c => c.Chave != canal))
                return Resultado<PreferenciasDeNotificacao>.Recusado("CANAL_INVALIDO", $"Canal desconhecido: {canal}.");
            if (!CanaisDeNotificacao.Disponiveis.Contains(canal))
                return Resultado<PreferenciasDeNotificacao>.Recusado("CANAL_INDISPONIVEL",
                    $"O canal {canal} ainda não está disponível: depende da fonte de identidade corporativa.");
            chaves.Add(PreferenciasDeNotificacao.Chave(tipo, canal));
        }

        var novas = new PreferenciasDeNotificacao(ator.IdPessoa, chaves);
        await preferencias.SalvarAsync(novas, ct);
        return Resultado<PreferenciasDeNotificacao>.Ok(novas);
    }
}

/// <summary>
/// O despacho da outbox (<c>notificacoes.despachar</c>). O canal app já está
/// entregue quando a notificação existe: marcar o envio só fecha o ciclo.
/// Canal sem entrega disponível fica esperando. Idempotente.
/// </summary>
public sealed class DespacharNotificacoes(
    IRepositorioDeNotificacoes notificacoes, IContextoDeTenantAtual tenant, IAtorAtual atorAtual, IUnidadeDeTrabalho uow, IRelogio relogio)
{
    public sealed record Saida(int Despachadas, IReadOnlyDictionary<string, int> PorCanal, int SemCanalDisponivel);

    public async Task<Resultado<Saida>> ExecutarAsync(int limite = 200, CancellationToken ct = default)
    {
        var ator = await atorAtual.ObterAsync(ct);
        if (!PoliticaDeAutorizacao.Pode(ator, Acao.Administrar, AlvoDaAcao.NaEmpresa(tenant.Atual.Empresa), relogio.Hoje))
            return Recusa.Para<Saida>(ator, "Despachar notificações é tarefa do administrador.");

        var porCanal = new Dictionary<string, int>();
        var semCanal = 0;
        foreach (var n in await notificacoes.PendentesDeEnvioAsync(Math.Clamp(limite, 1, 1000), ct))
        {
            if (!CanaisDeNotificacao.Disponiveis.Contains(n.Canal)) { semCanal++; continue; }
            n.MarcarEnviada(relogio.Agora);
            porCanal[n.Canal] = porCanal.GetValueOrDefault(n.Canal) + 1;
        }
        if (porCanal.Count > 0) await uow.ConfirmarAsync(ct);
        return Resultado<Saida>.Ok(new Saida(porCanal.Values.Sum(), porCanal, semCanal));
    }
}

/// <summary>
/// O vigia de SLA (<c>notificacoes.vigiar_sla</c>): avisa das etapas abertas
/// vencidas ou que vencem em menos de 24 horas. Avisa quem assumiu a etapa ou,
/// na fila livre, quem pode decidi-la. A chave carrega o dia: rodar de hora em
/// hora não repete o aviso.
/// </summary>
public sealed class VigiarSla(
    IRepositorioDeValidacoes validacoes,
    IRepositorioDeAtivos ativos,
    IMetamodelo metamodelo,
    Notificador notificador,
    IContextoDeTenantAtual tenant,
    IAtorAtual atorAtual,
    IUnidadeDeTrabalho uow,
    IRelogio relogio)
{
    public const int HorasDeAviso = 24;

    public sealed record Saida(int SlaVencendo, int SlaVencido);

    public async Task<Resultado<Saida>> ExecutarAsync(CancellationToken ct = default)
    {
        var ator = await atorAtual.ObterAsync(ct);
        if (!PoliticaDeAutorizacao.Pode(ator, Acao.Administrar, AlvoDaAcao.NaEmpresa(tenant.Atual.Empresa), relogio.Hoje))
            return Recusa.Para<Saida>(ator, "Rodar o vigia de SLA é tarefa do administrador.");

        var agora = relogio.Agora;
        var hoje = relogio.Hoje.ToString("yyyy-MM-dd");
        var tipos = await metamodelo.ObterAsync(ct);
        int vencendo = 0, vencido = 0;

        foreach (var v in (await validacoes.ListarAsync(ct: ct)).Where(v => v.Status == StatusDaValidacao.Aberta && v.Prazo is not null))
        {
            var prazo = v.Prazo!.Value;
            var tipo = prazo < agora ? TiposDeNotificacao.SlaVencido
                : prazo <= agora.AddHours(HorasDeAviso) ? TiposDeNotificacao.SlaVencendo
                : null;
            if (tipo is null || await ativos.ObterAsync(v.Ativo, ct) is not { } ativo) continue;

            var destinatarios = v.AtribuidaA is { } dono
                ? [dono]
                : await notificador.QuemPodeDecidirAsync(v, ativo,
                    await AlvoDoAtivo.ResolverAsync(ativos, tenant.Atual, ativo, tipos.Obter(ativo.TipoItem), ct), ct);
            var etapa = v.Etapa.ToString().ToLowerInvariant();
            var enviadas = await notificador.ParaMuitosAsync(destinatarios, tipo,
                $"{TiposDeNotificacao.Obter(tipo).Rotulo}: {ativo.Nome}",
                $"Etapa {etapa} · prazo {prazo:dd/MM/yyyy HH:mm}. {(tipo == TiposDeNotificacao.SlaVencido ? "Já venceu." : "Vence em menos de 24h.")}",
                $"/validacoes?validacao={v.Id}", ativo.Id, v.Id, $"{tipo}:{v.Id}:{hoje}", ct: ct);
            if (tipo == TiposDeNotificacao.SlaVencido) vencido += enviadas; else vencendo += enviadas;
        }
        if (vencendo + vencido > 0) await uow.ConfirmarAsync(ct);
        return Resultado<Saida>.Ok(new Saida(vencendo, vencido));
    }
}
