using System.Text.Json;
using Jornada.Aplicacao.Portas;
using Jornada.Dominio.Catalogo;
using Jornada.Dominio.Comum;
using Jornada.Dominio.Governanca;
using Jornada.Dominio.Tenancy;
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
    public DbSet<ValidacaoEmProcesso> Validacoes => Set<ValidacaoEmProcesso>();
    public DbSet<RelacaoAtivo> Relacoes => Set<RelacaoAtivo>();
    public DbSet<SequencialPorTipo> Sequenciais => Set<SequencialPorTipo>();

    protected override void OnModelCreating(ModelBuilder modelo)
    {
        modelo.HasDefaultSchema("catalogo");
        modelo.ApplyConfigurationsFromAssembly(typeof(ContextoDeDados).Assembly);

        // A outbox é criada e mantida pelo Esquema.sql (ADR-0008), não pelas
        // migrations do EF — ela é infraestrutura pura, sem regra de negócio.
        // ExcludeFromMigrations evita que `dotnet ef migrations add` tente
        // gerar um CREATE TABLE concorrente com o que o Esquema.sql já faz.
        modelo.Entity<MensagemDeSaida>(e =>
        {
            e.ToTable("mensagem_de_saida", "catalogo", t => t.ExcludeFromMigrations());
            e.HasKey(m => m.Id);
            e.Property(m => m.Id).HasColumnName("id");
            e.Property(m => m.TenantId).HasColumnName("tenant_id");
            e.Property(m => m.Tipo).HasColumnName("tipo");
            e.Property(m => m.Payload).HasColumnName("payload").HasColumnType("jsonb");
            e.Property(m => m.ChaveDeIdempotencia).HasColumnName("chave_idempotencia");
            e.Property(m => m.Correlacao).HasColumnName("correlacao");
            e.Property(m => m.OcorridoEm).HasColumnName("ocorrido_em");
            e.Property(m => m.DespachadoEm).HasColumnName("despachado_em");
            e.Property(m => m.Tentativas).HasColumnName("tentativas");
            e.Property(m => m.ProximaTentativa).HasColumnName("proxima_tentativa");
            e.Property(m => m.UltimoErro).HasColumnName("ultimo_erro");
        });

        modelo.Entity<RelacaoAtivo>(e =>
        {
            e.ToTable("relacao_ativo", "catalogo");
            e.HasKey(r => r.Id);
            e.Property(r => r.TenantId).HasColumnName("tenant_id").IsRequired();
            e.Property(r => r.OrigemId).HasColumnName("origem_id").IsRequired();
            e.Property(r => r.DestinoId).HasColumnName("destino_id").IsRequired();
            e.Property(r => r.Tipo).HasColumnName("tipo").HasMaxLength(30).IsRequired();
            e.Property(r => r.CriadoEm).HasColumnName("criado_em");
            e.HasIndex(r => new { r.TenantId, r.OrigemId }).HasDatabaseName("ix_relacao_tenant_origem");
            e.HasIndex(r => new { r.TenantId, r.DestinoId }).HasDatabaseName("ix_relacao_tenant_destino");
        });

        modelo.Entity<SequencialPorTipo>(e =>
        {
            e.ToTable("sequencial_por_tipo", "catalogo");
            e.HasKey(s => new { s.TenantId, s.TipoItem });
            e.Property(s => s.TenantId).HasColumnName("tenant_id");
            e.Property(s => s.TipoItem).HasColumnName("tipo_item").HasMaxLength(60);
            e.Property(s => s.Valor).HasColumnName("valor");
        });

        // Filtro de consulta por empresa: rede de segurança da APLICAÇÃO.
        // NÃO substitui o RLS — é a segunda das três camadas (ADR-0007 §4).
        // Se este filtro falhar, o banco ainda barra; se o RLS falhar, este
        // ainda barra. As duas juntas exigem duas falhas simultâneas.
        modelo.Entity<Ativo>().HasQueryFilter(a => a.Empresa == tenant.Atual.Empresa);
        modelo.Entity<MensagemDeSaida>().HasQueryFilter(m => m.TenantId == tenant.Atual.Empresa.Valor);
        modelo.Entity<ValidacaoEmProcesso>().HasQueryFilter(v => v.Empresa == tenant.Atual.Empresa);
        modelo.Entity<RelacaoAtivo>().HasQueryFilter(r => r.TenantId == tenant.Atual.Empresa.Valor);
        modelo.Entity<SequencialPorTipo>().HasQueryFilter(s => s.TenantId == tenant.Atual.Empresa.Valor);
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

    /// <summary>
    /// Abre uma transação com <c>app.tenant_id</c> já definido e devolve o
    /// resultado de <paramref name="corpo"/>. Usado tanto para leitura quanto
    /// para escrita (ver <see cref="TransacaoComTenant"/>): com o PgBouncer em
    /// transaction pooling (ADR-0003), QUALQUER ida ao banco — não só escrita —
    /// precisa desse contexto, porque o RLS do Postgres é avaliado em toda
    /// consulta, e a conexão física pode ter sido usada por outra empresa na
    /// transação anterior.
    ///
    /// O terceiro argumento de <c>set_config</c> é <c>true</c> — LOCAL. É o que
    /// faz o contexto morrer junto com a transação em vez de vazar para a
    /// próxima requisição que reusa a mesma conexão física.
    /// </summary>
    internal async Task<T> ComContextoDeTenantAsync<T>(
        IContextoDeTenantAtual tenantAtual, Func<Task<T>> corpo, CancellationToken ct)
    {
        await using var tx = await Database.BeginTransactionAsync(ct);

        await Database.ExecuteSqlRawAsync(
            "SELECT set_config('app.tenant_id', {0}, true)",
            [tenantAtual.Atual.Empresa.Valor.ToString()], ct);

        var resultado = await corpo();
        await tx.CommitAsync(ct);
        return resultado;
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
    public Task<int> ConfirmarAsync(CancellationToken ct = default) =>
        contexto.ComContextoDeTenantAsync(tenant, () => contexto.SaveChangesAsync(ct), ct);
}

/// <summary>
/// Uma aresta do grafo de relações entre ativos (<c>implementa</c>,
/// <c>expõe</c>, <c>consome</c>, <c>depende_de</c>...). Simplificação
/// deliberada: hoje só existe leitura (<see cref="IRepositorioDeRelacoes"/>);
/// a escrita chega junto da funcionalidade de relacionar ativos (ver
/// docs/ARQUITETURA.md — ainda não tem caso de uso nem rota).
/// </summary>
public sealed class RelacaoAtivo
{
    public long Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid OrigemId { get; private set; }
    public Guid DestinoId { get; private set; }
    public string Tipo { get; private set; } = "depende_de";
    public DateTimeOffset CriadoEm { get; private set; }

    private RelacaoAtivo() { }

    public static RelacaoAtivo Nova(IdDeEmpresa tenant, IdDeAtivo origem, IdDeAtivo destino,
        string tipo, DateTimeOffset agora) => new()
    {
        TenantId = tenant.Valor,
        OrigemId = origem.Valor,
        DestinoId = destino.Valor,
        Tipo = tipo,
        CriadoEm = agora,
    };
}

/// <summary>
/// Contador atômico de código por (empresa, tipo). Substitui o
/// <c>ConcurrentDictionary</c> do adaptador em memória: no Postgres, o mesmo
/// resultado vem de um <c>INSERT ... ON CONFLICT DO UPDATE ... RETURNING</c>
/// dentro da transação do tenant, que serializa concorrência por linha sem
/// precisar de lock explícito nem de sequência global do banco.
/// </summary>
public sealed class SequencialPorTipo
{
    public Guid TenantId { get; private set; }
    public string TipoItem { get; private set; } = "";
    public int Valor { get; private set; }

    private SequencialPorTipo() { }
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
