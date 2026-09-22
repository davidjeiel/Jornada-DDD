using System.Text.Json;
using Jornada.Aplicacao.Portas;
using Jornada.Dominio.Catalogo;
using Jornada.Dominio.Comum;
using Microsoft.EntityFrameworkCore;

namespace Jornada.Persistencia.Postgres;

/// <summary>
/// DbContext do data plane.
///
/// Duas responsabilidades que NÃO podem ser esquecidas em lugar nenhum:
///  1. abrir toda transação com o contexto de tenant (ADR-0003);
///  2. drenar os eventos dos agregados para a outbox NA MESMA transação (ADR-0008).
///
/// Ambas ficam aqui, num ponto só, justamente para que ninguém precise lembrar.
/// </summary>
public sealed class ContextoDeDados(
    DbContextOptions<ContextoDeDados> opcoes,
    IContextoDeTenantAtual tenant)
    : DbContext(opcoes)
{
    public DbSet<Ativo> Ativos => Set<Ativo>();
    public DbSet<MensagemDeSaida> Outbox => Set<MensagemDeSaida>();

    protected override void OnModelCreating(ModelBuilder modelo)
    {
        modelo.HasDefaultSchema("catalogo");
        modelo.ApplyConfigurationsFromAssembly(typeof(ContextoDeDados).Assembly);

        // Filtro de consulta por empresa: rede de segurança da APLICAÇÃO.
        // NÃO substitui o RLS — é a segunda das três camadas (ADR-0007 §4).
        // Se este filtro falhar, o banco ainda barra; se o RLS falhar, este
        // ainda barra. As duas juntas exigem duas falhas simultâneas.
        modelo.Entity<Ativo>().HasQueryFilter(a => a.Empresa == tenant.Atual.Empresa);
        modelo.Entity<MensagemDeSaida>().HasQueryFilter(m => m.TenantId == tenant.Atual.Empresa.Valor);
    }

    /// <summary>
    /// Ponto ÚNICO em que evento de domínio vira linha de outbox. Como roda
    /// dentro do mesmo <c>SaveChanges</c>, ou as duas coisas acontecem ou
    /// nenhuma acontece — que é a razão inteira do padrão outbox existir.
    /// </summary>
    public override async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        var agregados = ChangeTracker.Entries<RaizDeAgregado>()
            .Select(e => e.Entity)
            .Where(a => a.EventosPendentes.Count > 0)
            .ToArray();

        foreach (var agregado in agregados)
            foreach (var evento in agregado.DrenarEventos())
                Outbox.Add(MensagemDeSaida.De(evento, tenant.Atual.Empresa.Valor));

        return await base.SaveChangesAsync(ct);
    }
}

/// <summary>
/// Abre toda transação já com <c>app.tenant_id</c> definido.
///
/// O terceiro argumento de <c>set_config</c> é <c>true</c> — LOCAL. Isso é o
/// que faz o contexto morrer junto com a transação, e é indispensável porque o
/// PgBouncer opera em transaction pooling: a conexão volta ao pool entre
/// transações e pode ser entregue à requisição de OUTRA empresa. Um `SET` de
/// sessão aqui seria um vazamento entre clientes esperando acontecer.
/// </summary>
public sealed class TransacaoComTenant(ContextoDeDados contexto, IContextoDeTenantAtual tenant)
    : IUnidadeDeTrabalho
{
    public async Task<int> ConfirmarAsync(CancellationToken ct = default)
    {
        await using var tx = await contexto.Database.BeginTransactionAsync(ct);

        await contexto.Database.ExecuteSqlRawAsync(
            "SELECT set_config('app.tenant_id', {0}, true)",
            [tenant.Atual.Empresa.Valor.ToString()], ct);

        var afetadas = await contexto.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return afetadas;
    }
}

/// <summary>Linha da outbox. Espelha <c>mensagem_de_saida</c> do ADR-0008 §2.</summary>
public sealed class MensagemDeSaida
{
    public long Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string Tipo { get; private set; } = "";
    public string Payload { get; private set; } = "{}";
    public string ChaveDeIdempotencia { get; private set; } = "";
    public Guid Correlacao { get; private set; }
    public DateTimeOffset OcorridoEm { get; private set; }
    public DateTimeOffset? DespachadoEm { get; private set; }
    public int Tentativas { get; private set; }
    public DateTimeOffset? ProximaTentativa { get; private set; }
    public string? UltimoErro { get; private set; }

    private MensagemDeSaida() { }

    public static MensagemDeSaida De(EventoDeDominio evento, Guid tenantId) => new()
    {
        TenantId = tenantId,
        Tipo = evento.GetType().Name,
        Payload = JsonSerializer.Serialize(evento, evento.GetType()),
        ChaveDeIdempotencia = evento.ChaveDeIdempotencia,
        // Reconcilia a trilha de auditoria de negócio com o rastro distribuído:
        // dá para pular do evento auditado para o trace da requisição (ADR-0011).
        Correlacao = System.Diagnostics.Activity.Current?.RootId is { } raiz
                     && Guid.TryParse(raiz, out var g) ? g : Guid.CreateVersion7(),
        OcorridoEm = evento.OcorridoEm,
        ProximaTentativa = evento.OcorridoEm,
    };

    public void MarcarDespachada(DateTimeOffset quando) => DespachadoEm = quando;

    public void RegistrarFalha(string erro, DateTimeOffset proxima)
    {
        Tentativas++;
        UltimoErro = erro;
        ProximaTentativa = proxima;
    }
}
