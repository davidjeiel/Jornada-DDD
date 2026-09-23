using Dapper;
using Jornada.Aplicacao.Portas;
using Jornada.Dominio.Catalogo;
using Jornada.Dominio.Comum;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Jornada.Persistencia.Postgres;

/// <summary>
/// Adaptador de saída sobre Postgres. Substitui
/// <c>RepositorioDeAtivosEmMemoria</c> sem que nenhum caso de uso mude uma
/// linha (ADR-0001) — implementa as mesmas três portas de leitura/escrita.
///
/// Toda operação — leitura OU escrita — passa por
/// <see cref="ContextoDeDados.ComContextoDeTenantAsync{T}"/>. Isso custa uma
/// transação por chamada mesmo em consultas simples; o preço é aceito aqui
/// porque a alternativa (uma transação por requisição HTTP, aberta no
/// middleware) exigiria linkar o pipeline da API ao ciclo de vida da conexão
/// Postgres, e essa segunda decisão fica para quando o custo da primeira
/// aparecer num profiling real, não antes.
/// </summary>
public sealed class RepositorioDeAtivosPostgres(ContextoDeDados db, IContextoDeTenantAtual tenant)
    : IRepositorioDeAtivos, IRepositorioDeRelacoes, IRepositorioDeValidacoes
{
    // ─────────────────────────────────────────────────── IRepositorioDeAtivos

    public Task<Ativo?> ObterAsync(IdDeAtivo id, CancellationToken ct = default) =>
        db.ComContextoDeTenantAsync(tenant,
            () => db.Ativos.FirstOrDefaultAsync(a => a.Id == id, ct), ct);

    public Task<Ativo?> ObterPorCodigoAsync(CodigoDeAtivo codigo, CancellationToken ct = default) =>
        db.ComContextoDeTenantAsync(tenant,
            () => db.Ativos.FirstOrDefaultAsync(a => a.Codigo == codigo, ct), ct);

    public Task<IReadOnlyList<Ativo>> ListarAsync(
        string? tipoItem = null, StatusCicloVida? status = null,
        int limite = 200, CancellationToken ct = default) =>
        db.ComContextoDeTenantAsync(tenant, async () =>
        {
            IQueryable<Ativo> consulta = db.Ativos;

            if (!string.IsNullOrWhiteSpace(tipoItem))
                consulta = consulta.Where(a => a.TipoItem == tipoItem.ToLower());
            if (status is { } s)
                consulta = consulta.Where(a => a.Status == s);

            var lista = await consulta
                .OrderBy(a => a.Codigo)
                .Take(limite)
                .ToListAsync(ct);

            return (IReadOnlyList<Ativo>)lista;
        }, ct);

    public Task AdicionarAsync(Ativo ativo, CancellationToken ct = default)
    {
        // Sem ida ao banco aqui de propósito: só marca "Added" no change
        // tracker. A escrita de verdade — dentro da transação com o contexto
        // de tenant já definido — acontece em IUnidadeDeTrabalho.ConfirmarAsync
        // (TransacaoComTenant), do jeito que CadastrarAtivo já espera.
        db.Ativos.Add(ativo);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Ativo>> TrilhaAsync(IdDeAtivo id, CancellationToken ct = default) =>
        db.ComContextoDeTenantAsync(tenant, async () =>
        {
            var trilha = new List<Ativo>();
            var visitados = new HashSet<IdDeAtivo>();
            IdDeAtivo? atualId = id;

            while (atualId is { } idAtual && visitados.Add(idAtual))
            {
                var atual = await db.Ativos.FirstOrDefaultAsync(a => a.Id == idAtual, ct);
                if (atual is null) break;
                trilha.Insert(0, atual);
                atualId = atual.IdPai;
            }
            return (IReadOnlyList<Ativo>)trilha;
        }, ct);

    public Task<bool> ExisteNomeDuplicadoAsync(
        string tipoItem, string nome, IdDeAtivo? exceto = null, CancellationToken ct = default) =>
        db.ComContextoDeTenantAsync(tenant, () =>
        {
            var nomeNormalizado = nome.ToLower();
            return db.Ativos.AnyAsync(a =>
                a.TipoItem == tipoItem.ToLower() &&
                a.Nome.ToLower() == nomeNormalizado &&
                (exceto == null || a.Id != exceto), ct);
        }, ct);

    /// <summary>
    /// Equivalente Postgres do <c>ConcurrentDictionary.AddOrUpdate</c> em
    /// memória: um upsert atômico que serializa concorrência por linha
    /// (tenant_id, tipo_item) sem precisar de lock explícito nem de sequência
    /// global do banco.
    /// </summary>
    public Task<int> ProximoSequencialAsync(string tipoItem, CancellationToken ct = default) =>
        db.ComContextoDeTenantAsync(tenant, async () =>
        {
            const string sql = """
                INSERT INTO catalogo.sequencial_por_tipo (tenant_id, tipo_item, valor)
                VALUES (@Tenant, @Tipo, 1)
                ON CONFLICT (tenant_id, tipo_item)
                DO UPDATE SET valor = catalogo.sequencial_por_tipo.valor + 1
                RETURNING valor;
                """;

            var conexao = db.Database.GetDbConnection();
            var transacao = db.Database.CurrentTransaction!.GetDbTransaction();

            return await conexao.ExecuteScalarAsync<int>(
                new CommandDefinition(sql,
                    new { Tenant = tenant.Atual.Empresa.Valor, Tipo = tipoItem.ToLowerInvariant() },
                    transacao, cancellationToken: ct));
        }, ct);

    // ────────────────────────────────────────────────── IRepositorioDeRelacoes
    //
    // Consultas sobre o grafo de relações (ADR-0004: CTE recursiva no Postgres).
    // Não existe, ainda, caso de uso nem rota para GRAVAR uma relação — por
    // isso estas consultas sempre respondem "nenhuma", honestamente, até que
    // a funcionalidade de relacionar ativos exista. A tabela e o índice já
    // estão prontos para quando ela chegar.

    public Task<bool> TemDependenciaCircularAsync(IdDeAtivo id, CancellationToken ct = default) =>
        db.ComContextoDeTenantAsync(tenant, async () =>
        {
            const string sql = """
                WITH RECURSIVE alcance AS (
                    SELECT destino_id FROM catalogo.relacao_ativo
                     WHERE tenant_id = @Tenant AND origem_id = @Id AND tipo = 'depende_de'
                  UNION
                    SELECT r.destino_id FROM catalogo.relacao_ativo r
                    JOIN alcance a ON r.origem_id = a.destino_id
                    WHERE r.tenant_id = @Tenant AND r.tipo = 'depende_de'
                )
                SELECT EXISTS (SELECT 1 FROM alcance WHERE destino_id = @Id);
                """;

            var conexao = db.Database.GetDbConnection();
            var transacao = db.Database.CurrentTransaction!.GetDbTransaction();

            return await conexao.ExecuteScalarAsync<bool>(
                new CommandDefinition(sql,
                    new { Tenant = tenant.Atual.Empresa.Valor, Id = id.Valor },
                    transacao, cancellationToken: ct));
        }, ct);

    public Task<IReadOnlyList<string>> NomesDeDestinosForaDeVigenciaAsync(
        IdDeAtivo id, CancellationToken ct = default) =>
        db.ComContextoDeTenantAsync(tenant, async () =>
        {
            const string sql = """
                SELECT d.nome
                  FROM catalogo.relacao_ativo r
                  JOIN catalogo.item_catalogo d ON d.codigo_publico = r.destino_id
                                                AND d.tenant_id = r.tenant_id
                 WHERE r.tenant_id = @Tenant AND r.origem_id = @Id
                   AND d.status_ciclo_vida IN ('descontinuado', 'arquivado');
                """;

            var conexao = db.Database.GetDbConnection();
            var transacao = db.Database.CurrentTransaction!.GetDbTransaction();

            var nomes = await conexao.QueryAsync<string>(
                new CommandDefinition(sql,
                    new { Tenant = tenant.Atual.Empresa.Valor, Id = id.Valor },
                    transacao, cancellationToken: ct));

            return (IReadOnlyList<string>)nomes.ToArray();
        }, ct);

    public Task<int> ContarImplementacoesAsync(IdDeAtivo idCapacidade, CancellationToken ct = default) =>
        db.ComContextoDeTenantAsync(tenant, async () =>
        {
            const string sql = """
                SELECT count(*) FROM catalogo.relacao_ativo
                 WHERE tenant_id = @Tenant AND destino_id = @Id AND tipo = 'implementa';
                """;

            var conexao = db.Database.GetDbConnection();
            var transacao = db.Database.CurrentTransaction!.GetDbTransaction();

            return await conexao.ExecuteScalarAsync<int>(
                new CommandDefinition(sql,
                    new { Tenant = tenant.Atual.Empresa.Valor, Id = idCapacidade.Valor },
                    transacao, cancellationToken: ct));
        }, ct);

    // ─────────────────────────────────────────────── IRepositorioDeValidacoes

    public Task AdicionarAsync(ValidacaoEmProcesso validacao, CancellationToken ct = default)
    {
        // Mesmo raciocínio de AdicionarAsync(Ativo): a gravação de verdade
        // acontece no ConfirmarAsync que SubmeterAtivo já chama depois.
        db.Validacoes.Add(validacao);
        return Task.CompletedTask;
    }

    public Task<ValidacaoEmProcesso?> ObterAsync(Guid id, CancellationToken ct = default) =>
        db.ComContextoDeTenantAsync(tenant,
            () => db.Validacoes.FirstOrDefaultAsync(v => v.Id == id, ct), ct);

    public Task<IReadOnlyList<ValidacaoEmProcesso>> ListarAsync(
        IdDeAtivo? ativo = null, CancellationToken ct = default) =>
        db.ComContextoDeTenantAsync(tenant, async () =>
        {
            IQueryable<ValidacaoEmProcesso> consulta = db.Validacoes;
            if (ativo is { } id) consulta = consulta.Where(v => v.Ativo == id);

            var lista = await consulta
                .OrderBy(v => v.Status).ThenBy(v => v.Etapa)
                .ToListAsync(ct);

            return (IReadOnlyList<ValidacaoEmProcesso>)lista;
        }, ct);
}
