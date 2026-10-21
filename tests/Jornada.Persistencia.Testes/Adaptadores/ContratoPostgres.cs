using Jornada.Adaptadores.Memoria;
using Jornada.Dominio.Tenancy;
using Jornada.Dominio.Testes.Harness;
using Jornada.Persistencia.Postgres;
using Jornada.Persistencia.Testes.Contrato;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Jornada.Persistencia.Testes.Adaptadores;

/// <summary>
/// O contrato contra um Postgres de verdade, com RLS.
///
/// Duas variáveis de ambiente, como as duas conexões da API:
///  - <c>JORNADA_TESTES_POSTGRES</c>: conexão de requisição (papel
///    <c>catalogo_app</c>). Sem ela, a suíte é pulada.
///  - <c>JORNADA_TESTES_POSTGRES_MIGRADOR</c>: opcional. Se definida, aplica
///    migrations e <c>Esquema.sql</c> antes (papel <c>catalogo_migrador</c>).
///
/// A conexão de requisição precisa ser de um papel SEM <c>BYPASSRLS</c> e sem
/// superusuário; senão os testes de isolamento passariam pelo filtro do EF e o
/// RLS ficaria sem cobertura. A suíte confere isso e recusa rodar.
/// </summary>
[Suite("Contrato de persistência — Postgres")]
public sealed class ContratoPostgres : ContratoDePersistencia
{
    private const string VariavelDaAplicacao = "JORNADA_TESTES_POSTGRES";
    private const string VariavelDoMigrador = "JORNADA_TESTES_POSTGRES_MIGRADOR";

    private static readonly Lazy<DbContextOptions<ContextoDeDados>?> Opcoes = new(Preparar);

    protected override Sessao Abrir(IdDeEmpresa empresa)
    {
        var opcoes = Opcoes.Value
            ?? throw new TestePulado($"defina {VariavelDaAplicacao} para rodar contra o Postgres");

        var tenant = ContextoDeTenantFixo.Para(empresa);
        var db = new ContextoDeDados(opcoes, tenant);
        var repositorio = new RepositorioDeAtivosPostgres(db, tenant);
        return new Sessao(repositorio, repositorio, repositorio, new TransacaoComTenant(db, tenant), db);
    }

    private static DbContextOptions<ContextoDeDados>? Preparar()
    {
        var conexao = Environment.GetEnvironmentVariable(VariavelDaAplicacao);
        if (string.IsNullOrWhiteSpace(conexao)) return null;

        if (Environment.GetEnvironmentVariable(VariavelDoMigrador) is { Length: > 0 } migrador)
            AplicarEsquema(migrador).GetAwaiter().GetResult();

        // Igual ao Program.cs: sem EnableDynamicJson o Npgsql recusa os atributos (jsonb).
        var fonte = new NpgsqlDataSourceBuilder(conexao).EnableDynamicJson().Build();
        ExigirPapelSemBypass(fonte).GetAwaiter().GetResult();

        return new DbContextOptionsBuilder<ContextoDeDados>().UseNpgsql(fonte).Options;
    }

    /// <summary>A mesma sequência do host: migrations, depois RLS e outbox (Esquema.sql).</summary>
    private static async Task AplicarEsquema(string conexaoDoMigrador)
    {
        var opcoes = new DbContextOptionsBuilder<ContextoDeDados>()
            .UseNpgsql(conexaoDoMigrador, o => o.MigrationsHistoryTable("__ef_migrations_historico", "catalogo"))
            .Options;

        await using var db = new ContextoDeDados(opcoes, ContextoDeTenantFixo.Para(IdDeEmpresa.Novo()));
        await db.Database.MigrateAsync();

        var esquema = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Esquema.sql"));
        await db.Database.ExecuteSqlRawAsync(esquema);
        await db.Database.ExecuteSqlRawAsync("SELECT plataforma.exigir_rls()");
    }

    private static async Task ExigirPapelSemBypass(NpgsqlDataSource fonte)
    {
        await using var comando = fonte.CreateCommand(
            "SELECT current_user, rolsuper OR rolbypassrls FROM pg_roles WHERE rolname = current_user");
        await using var leitor = await comando.ExecuteReaderAsync();
        await leitor.ReadAsync();

        if (leitor.GetBoolean(1))
            throw new InvalidOperationException(
                $"O papel '{leitor.GetString(0)}' contorna RLS (superusuário ou BYPASSRLS). " +
                $"Use o papel da aplicação (catalogo_app) em {VariavelDaAplicacao}.");
    }
}
