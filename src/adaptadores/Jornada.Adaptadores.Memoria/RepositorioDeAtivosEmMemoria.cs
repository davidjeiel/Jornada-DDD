using System.Collections.Concurrent;
using Jornada.Aplicacao.Portas;
using Jornada.Dominio.Catalogo;
using Jornada.Dominio.Comum;
using Jornada.Dominio.Tenancy;

namespace Jornada.Adaptadores.Memoria;

/// <summary>
/// O ARMAZÉM: os dados, compartilhados por todas as requisições — é o análogo
/// do banco. Registre como SINGLETON.
/// </summary>
public sealed class ArmazemEmMemoria
{
    internal ConcurrentDictionary<IdDeAtivo, Ativo> Ativos { get; } = new();
    internal ConcurrentDictionary<string, int> Sequenciais { get; } = new();
    internal List<EventoDeDominio> Eventos { get; } = [];

    /// <summary>Relações: (origem, destino, tipo). Simplificação deliberada.</summary>
    internal List<(IdDeAtivo Origem, IdDeAtivo Destino, string Tipo)> Relacoes { get; } = [];
    internal ConcurrentDictionary<Guid, ValidacaoEmProcesso> Validacoes { get; } = new();

    public void Limpar()
    {
        Ativos.Clear();
        Sequenciais.Clear();
        Eventos.Clear();
        Relacoes.Clear();
        Validacoes.Clear();
    }
}

/// <summary>
/// Repositório em memória que FILTRA POR EMPRESA em toda consulta.
///
/// Isso não é detalhe de brinquedo: é o que faz o comportamento aqui espelhar
/// o do Postgres com RLS (ADR-0003). Se este adaptador devolvesse ativos de
/// outra empresa, os testes de caso de uso passariam e a proteção real ficaria
/// sem cobertura até a integração.
///
/// ┌─────────────────────────────────────────────────────────────────────────┐
/// │ POR QUE ARMAZÉM E REPOSITÓRIO SÃO CLASSES SEPARADAS                     │
/// │                                                                         │
/// │ Os dados vivem no processo inteiro (singleton); o CONTEXTO DE TENANT    │
/// │ vive numa requisição (scoped). Na primeira versão isto era uma classe   │
/// │ só, registrada como singleton — e o container entregou a ela o contexto │
/// │ da PRIMEIRA requisição, para sempre. É a "dependência cativa", e num    │
/// │ produto multiempresa ela é exatamente o caminho pelo qual um cliente    │
/// │ passa a enxergar o dado de outro.                                       │
/// │                                                                         │
/// │ A separação torna o erro difícil de repetir; `ValidateScopes` no        │
/// │ composition root faz o container RECUSAR a montagem se ele voltar.      │
/// └─────────────────────────────────────────────────────────────────────────┘
/// </summary>
public sealed class RepositorioDeAtivosEmMemoria(
    ArmazemEmMemoria armazem, IContextoDeTenantAtual tenant)
    : IRepositorioDeAtivos, IRepositorioDeRelacoes, IUnidadeDeTrabalho, IRepositorioDeValidacoes
{
    private ConcurrentDictionary<IdDeAtivo, Ativo> _ativos => armazem.Ativos;
    private ConcurrentDictionary<string, int> _sequenciais => armazem.Sequenciais;
    private List<EventoDeDominio> _eventosPublicados => armazem.Eventos;
    private List<(IdDeAtivo Origem, IdDeAtivo Destino, string Tipo)> _relacoes => armazem.Relacoes;
    private ConcurrentDictionary<Guid, ValidacaoEmProcesso> _validacoes => armazem.Validacoes;

    public IReadOnlyList<EventoDeDominio> EventosPublicados => _eventosPublicados;

    /// <summary>Atalho para teste: armazém novo com um contexto fixo.</summary>
    public static RepositorioDeAtivosEmMemoria Novo(IContextoDeTenantAtual tenant) =>
        new(new ArmazemEmMemoria(), tenant);

    private IdDeEmpresa Empresa => tenant.Atual.Empresa;

    private bool MinhaEmpresa(Ativo a) => a.Empresa == Empresa;

    public Task<Ativo?> ObterAsync(IdDeAtivo id, CancellationToken ct = default) =>
        Task.FromResult(_ativos.TryGetValue(id, out var a) && MinhaEmpresa(a) ? a : null);

    public Task<Ativo?> ObterPorCodigoAsync(CodigoDeAtivo codigo, CancellationToken ct = default) =>
        Task.FromResult(_ativos.Values.FirstOrDefault(a => MinhaEmpresa(a) && a.Codigo == codigo));

    public Task<IReadOnlyList<Ativo>> ListarAsync(
        string? tipoItem = null, StatusCicloVida? status = null,
        int limite = 200, CancellationToken ct = default)
    {
        IEnumerable<Ativo> q = _ativos.Values.Where(MinhaEmpresa);

        if (!string.IsNullOrWhiteSpace(tipoItem))
            q = q.Where(a => a.TipoItem.Equals(tipoItem, StringComparison.OrdinalIgnoreCase));
        if (status is { } s)
            q = q.Where(a => a.Status == s);

        return Task.FromResult<IReadOnlyList<Ativo>>(
            q.OrderBy(a => a.Codigo.Valor, StringComparer.Ordinal).Take(limite).ToArray());
    }

    public Task AdicionarAsync(Ativo ativo, CancellationToken ct = default)
    {
        if (ativo.Empresa != Empresa)
            throw new ErroDeDominio("TENANT_INVALIDO",
                "Tentativa de gravar ativo de outra empresa.");
        _ativos[ativo.Id] = ativo;
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<Ativo>> TrilhaAsync(IdDeAtivo id, CancellationToken ct = default)
    {
        var trilha = new List<Ativo>();
        var atual = await ObterAsync(id, ct);
        var visitados = new HashSet<IdDeAtivo>();

        while (atual is not null && visitados.Add(atual.Id))
        {
            trilha.Insert(0, atual);
            atual = atual.IdPai is { } pai ? await ObterAsync(pai, ct) : null;
        }
        return trilha;
    }

    public Task<bool> ExisteNomeDuplicadoAsync(
        string tipoItem, string nome, IdDeAtivo? exceto = null, CancellationToken ct = default) =>
        Task.FromResult(_ativos.Values.Any(a =>
            MinhaEmpresa(a) &&
            a.TipoItem.Equals(tipoItem, StringComparison.OrdinalIgnoreCase) &&
            a.Nome.Equals(nome, StringComparison.OrdinalIgnoreCase) &&
            (exceto is null || a.Id != exceto)));

    public Task<int> ProximoSequencialAsync(string tipoItem, CancellationToken ct = default) =>
        Task.FromResult(_sequenciais.AddOrUpdate(
            $"{Empresa}:{tipoItem.ToLowerInvariant()}", 1, (_, atual) => atual + 1));

    // ───────────────────────────────────────────────── IRepositorioDeRelacoes

    public Task RelacionarAsync(IdDeAtivo origem, IdDeAtivo destino, string tipo, CancellationToken ct = default)
    {
        _relacoes.Add((origem, destino, tipo));
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<RelacaoResumo>> ListarRelacoesAsync(IdDeAtivo id, CancellationToken ct = default)
    {
        var lista = _relacoes
            .Where(r => r.Origem == id || r.Destino == id)
            .Select(r => new RelacaoResumo(r.Origem, r.Destino, r.Tipo))
            .ToArray();
        return Task.FromResult<IReadOnlyList<RelacaoResumo>>(lista);
    }

    public Task<bool> TemDependenciaCircularAsync(IdDeAtivo id, CancellationToken ct = default)
    {
        // Busca em profundidade sobre `depende_de`, igual ao `_tem_ciclo`.
        var visitados = new HashSet<IdDeAtivo>();
        var pilha = new Stack<IdDeAtivo>();
        pilha.Push(id);

        while (pilha.Count > 0)
        {
            var atual = pilha.Pop();
            foreach (var (_, destino, _) in _relacoes.Where(
                         r => r.Origem == atual && r.Tipo == "depende_de"))
            {
                if (destino == id) return Task.FromResult(true);
                if (visitados.Add(destino)) pilha.Push(destino);
            }
        }
        return Task.FromResult(false);
    }

    public Task<IReadOnlyList<string>> NomesDeDestinosForaDeVigenciaAsync(
        IdDeAtivo id, CancellationToken ct = default)
    {
        var nomes = _relacoes
            .Where(r => r.Origem == id)
            .Select(r => _ativos.TryGetValue(r.Destino, out var d) ? d : null)
            .Where(d => d is not null && MinhaEmpresa(d) && MaquinaDeCicloDeVida.ForaDeVigencia(d.Status))
            .Select(d => d!.Nome)
            .ToArray();

        return Task.FromResult<IReadOnlyList<string>>(nomes);
    }

    public Task<int> ContarImplementacoesAsync(IdDeAtivo idCapacidade, CancellationToken ct = default) =>
        Task.FromResult(_relacoes.Count(r => r.Destino == idCapacidade && r.Tipo == "implementa"));

    // ───────────────────────────────────────────────────── IUnidadeDeTrabalho

    /// <summary>
    /// Drena os eventos dos agregados — o mesmo ponto único em que o adaptador
    /// Postgres grava na outbox dentro da transação (ADR-0008).
    /// </summary>
    public Task<int> ConfirmarAsync(CancellationToken ct = default)
    {
        var drenados = 0;
        foreach (var ativo in _ativos.Values)
        {
            var eventos = ativo.DrenarEventos();
            _eventosPublicados.AddRange(eventos);
            drenados += eventos.Count;
        }
        return Task.FromResult(drenados);
    }

    public Task AdicionarAsync(ValidacaoEmProcesso validacao, CancellationToken ct = default)
    {
        if (validacao.Empresa != Empresa)
            throw new ErroDeDominio("TENANT_INVALIDO", "Tentativa de gravar validação de outra empresa.");
        _validacoes[validacao.Id] = validacao;
        return Task.CompletedTask;
    }

    public Task<ValidacaoEmProcesso?> ObterAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(_validacoes.TryGetValue(id, out var validacao)
            && validacao.Empresa == Empresa ? validacao : null);

    public Task<IReadOnlyList<ValidacaoEmProcesso>> ListarAsync(IdDeAtivo? ativo = null, CancellationToken ct = default)
    {
        IEnumerable<ValidacaoEmProcesso> validacoes = _validacoes.Values.Where(v => v.Empresa == Empresa);
        if (ativo is { } id) validacoes = validacoes.Where(v => v.Ativo == id);
        return Task.FromResult<IReadOnlyList<ValidacaoEmProcesso>>(
            validacoes.OrderBy(v => v.Status).ThenBy(v => v.Etapa).ToArray());
    }
}
