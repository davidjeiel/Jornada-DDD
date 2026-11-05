using Jornada.Aplicacao.Notificacoes;
using Jornada.Aplicacao.Portas;
using Jornada.Dominio.Acesso;
using Jornada.Dominio.Comum;
using Jornada.Dominio.Tenancy;

namespace Jornada.Api;

/// <summary><c>Notificacoes:*</c> na configuração.</summary>
public sealed class OpcoesDeNotificacao
{
    /// <summary>De quanto em quanto tempo despachar a outbox e rodar o vigia de SLA. Zero desliga.</summary>
    public int IntervaloEmMinutos { get; set; } = 15;
}

/// <summary>
/// O <c>flask notificar</c> agendado do painel: de tempos em tempos, em cada
/// empresa, despacha a outbox de avisos e roda o vigia de SLA. Cada empresa
/// num escopo próprio, com o contexto dela, como uma requisição; o ator é o
/// sistema, com papel de admin só naquela empresa. Falha numa empresa não
/// impede as outras.
/// </summary>
public sealed class TarefasDeNotificacao(
    IServiceScopeFactory escopos, IEmpresasDaPlataforma empresas, OpcoesDeNotificacao opcoes,
    IRelogio relogio, ILogger<TarefasDeNotificacao> log) : BackgroundService
{
    /// <summary>Quem age nas tarefas de fundo. Não é pessoa cadastrada: não recebe aviso nem aparece em lista.</summary>
    public static readonly Guid Sistema = Guid.Parse("00000000-0000-0000-0000-00000000f00d");

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var relogioDoLaco = new PeriodicTimer(TimeSpan.FromMinutes(opcoes.IntervaloEmMinutos));
        do
        {
            await RodarAsync(ct);
        }
        while (await relogioDoLaco.WaitForNextTickAsync(ct));
    }

    public async Task RodarAsync(CancellationToken ct)
    {
        IReadOnlyList<IdDeEmpresa> lista;
        try { lista = await empresas.ListarAsync(ct); }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            log.LogWarning(e, "Tarefas de notificação: não foi possível listar as empresas.");
            return;
        }

        foreach (var empresa in lista)
        {
            try
            {
                await using var escopo = escopos.CreateAsyncScope();
                escopo.ServiceProvider.GetRequiredService<ContextoDaRequisicao>().Definir(new ContextoDeTenant(empresa),
                    new Ator(Sistema, empresa, [AtribuicaoDePapel.NaEmpresa(Papel.Admin, relogio.Hoje)]));

                var vigia = await escopo.ServiceProvider.GetRequiredService<VigiarSla>().ExecutarAsync(ct);
                var despacho = await escopo.ServiceProvider.GetRequiredService<DespacharNotificacoes>().ExecutarAsync(ct: ct);
                if (vigia.Valor is { } v && despacho.Valor is { } d && v.SlaVencendo + v.SlaVencido + d.Despachadas > 0)
                    log.LogInformation("Notificações da {Empresa}: {Vencendo} SLA vencendo, {Vencido} vencido, {Despachadas} despachadas.",
                        empresa, v.SlaVencendo, v.SlaVencido, d.Despachadas);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                log.LogWarning(e, "Tarefas de notificação falharam na {Empresa}.", empresa);
            }
        }
    }
}
