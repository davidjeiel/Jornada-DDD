using System.Collections.Concurrent;
using Jornada.Aplicacao.Portas;
using Jornada.Dominio.Acesso;
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

    /// <summary>
    /// Relações: (empresa, origem, destino, tipo, criticidade, mecanismo). A empresa vai junto pelo
    /// mesmo motivo do <c>tenant_id</c> em <c>relacao_ativo</c>: sem ela, uma
    /// empresa enxergaria as arestas de outra.
    /// </summary>
    internal List<(IdDeEmpresa Empresa, IdDeAtivo Origem, IdDeAtivo Destino, string Tipo, Criticidade Criticidade, string? Mecanismo)> Relacoes { get; } = [];
    internal ConcurrentDictionary<Guid, ValidacaoEmProcesso> Validacoes { get; } = new();
    internal ConcurrentDictionary<(IdDeAtivo Ativo, int Numero), RevisaoDoAtivo> Revisoes { get; } = new();
    internal ConcurrentQueue<RegistroDeAuditoria> Auditoria { get; } = new();
    internal ConcurrentDictionary<Guid, Pessoa> Pessoas { get; } = new();
    internal ConcurrentDictionary<Guid, PleitoDeAcesso> Pleitos { get; } = new();
    internal ConcurrentDictionary<Guid, Squad> Squads { get; } = new();

    public void Limpar()
    {
        Squads.Clear();
        Pessoas.Clear();
        Pleitos.Clear();
        Revisoes.Clear();
        Auditoria.Clear();
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
/// o do Cosmos, onde toda consulta leva a empresa na chave (ADR-0016). Se este adaptador devolvesse ativos de
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
    : IRepositorioDeAtivos, IRepositorioDeRelacoes, IUnidadeDeTrabalho, IRepositorioDeValidacoes,
      IRepositorioDeRevisoes, ITrilhaDeAuditoria, IConsultaDeVinculos
{
    private ConcurrentDictionary<IdDeAtivo, Ativo> _ativos => armazem.Ativos;
    private ConcurrentDictionary<string, int> _sequenciais => armazem.Sequenciais;
    private List<EventoDeDominio> _eventosPublicados => armazem.Eventos;
    private List<(IdDeEmpresa Empresa, IdDeAtivo Origem, IdDeAtivo Destino, string Tipo, Criticidade Criticidade, string? Mecanismo)> _relacoes => armazem.Relacoes;

    /// <summary>As relações desta empresa. Toda consulta ao grafo parte daqui.</summary>
    private IEnumerable<(IdDeEmpresa Empresa, IdDeAtivo Origem, IdDeAtivo Destino, string Tipo, Criticidade Criticidade, string? Mecanismo)> MinhasRelacoes =>
        _relacoes.Where(r => r.Empresa == Empresa);
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
        int limite = 200, Guid? criadoPor = null, CancellationToken ct = default)
    {
        IEnumerable<Ativo> q = _ativos.Values.Where(MinhaEmpresa);
        if (criadoPor is { } autor) q = q.Where(a => a.CriadoPor == autor);

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

    public Task RelacionarAsync(IdDeAtivo origem, IdDeAtivo destino, string tipo,
        Criticidade criticidade = Criticidade.Media, string? mecanismo = null, CancellationToken ct = default)
    {
        _relacoes.Add((Empresa, origem, destino, tipo, criticidade, mecanismo));
        return Task.CompletedTask;
    }

    public Task<bool> TemCaminhoDeDependenciaAsync(IdDeAtivo de, IdDeAtivo ate, CancellationToken ct = default)
    {
        var visitados = new HashSet<IdDeAtivo> { de };
        var pilha = new Stack<IdDeAtivo>([de]);
        while (pilha.Count > 0)
        {
            var atual = pilha.Pop();
            if (atual == ate) return Task.FromResult(true);
            foreach (var r in MinhasRelacoes.Where(r => r.Origem == atual && r.Tipo == CatalogoDeRelacoes.DependeDe))
                if (visitados.Add(r.Destino)) pilha.Push(r.Destino);
        }
        return Task.FromResult(false);
    }

    public Task<IReadOnlyList<RelacaoResumo>> ListarRelacoesAsync(IdDeAtivo id, CancellationToken ct = default)
    {
        var lista = MinhasRelacoes
            .Where(r => r.Origem == id || r.Destino == id)
            .Select(r => new RelacaoResumo(r.Origem, r.Destino, r.Tipo, r.Criticidade, r.Mecanismo))
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
            foreach (var (_, _, destino, _, _, _) in MinhasRelacoes.Where(
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
        var nomes = MinhasRelacoes
            .Where(r => r.Origem == id)
            .Select(r => _ativos.TryGetValue(r.Destino, out var d) ? d : null)
            .Where(d => d is not null && MinhaEmpresa(d) && MaquinaDeCicloDeVida.ForaDeVigencia(d.Status))
            .Select(d => d!.Nome)
            .ToArray();

        return Task.FromResult<IReadOnlyList<string>>(nomes);
    }

    public Task<int> ContarImplementacoesAsync(IdDeAtivo idCapacidade, CancellationToken ct = default) =>
        Task.FromResult(MinhasRelacoes.Count(r => r.Destino == idCapacidade && r.Tipo == "implementa"));

    // ───────────────────────────────────────────────────── IUnidadeDeTrabalho

    /// <summary>
    /// Drena os eventos dos agregados — o mesmo ponto único em que o adaptador
    /// Cosmos grava os eventos no lote do ativo (ADR-0008, ADR-0016).
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

    // ───────────────────────────────────────────────── IRepositorioDeRevisoes

    public Task AdicionarAsync(RevisaoDoAtivo revisao, CancellationToken ct = default)
    {
        if (revisao.Empresa != Empresa)
            throw new ErroDeDominio("TENANT_INVALIDO", "Tentativa de gravar revisão de outra empresa.");
        if (!armazem.Revisoes.TryAdd((revisao.Ativo, revisao.Numero), revisao))
            throw new ErroDeDominio("CONFLITO_DE_EDICAO", $"A revisão {revisao.Numero} deste ativo já existe.");
        return Task.CompletedTask;
    }

    public Task<RevisaoDoAtivo?> ObterAsync(IdDeAtivo ativo, int numero, CancellationToken ct = default) =>
        Task.FromResult(armazem.Revisoes.TryGetValue((ativo, numero), out var r) && r.Empresa == Empresa ? r : null);

    Task<IReadOnlyList<RevisaoDoAtivo>> IRepositorioDeRevisoes.ListarAsync(IdDeAtivo ativo, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<RevisaoDoAtivo>>(armazem.Revisoes.Values
            .Where(r => r.Empresa == Empresa && r.Ativo == ativo)
            .OrderByDescending(r => r.Numero)
            .ToArray());

    // ─────────────────────────────────────────────────── IConsultaDeVinculos

    public Task<IReadOnlyList<string>> DaPessoaAsync(Guid idPessoa, CancellationToken ct = default)
    {
        var vinculos = new List<string>();
        if (_ativos.Values.Any(a => MinhaEmpresa(a) && a.Responsaveis.Any(r => r.IdPessoa == idPessoa)))
            vinculos.Add("responsabilidades em ativos");
        if (armazem.Revisoes.Values.Any(r => r.Empresa == Empresa && r.IdAutor == idPessoa))
            vinculos.Add("revisões criadas");
        if (_validacoes.Values.Any(v => v.Empresa == Empresa && (v.IdSubmissor == idPessoa || v.IdDecisor == idPessoa)))
            vinculos.Add("validações submetidas ou decididas");
        if (armazem.Auditoria.Any(r => r.Empresa == Empresa && r.IdPessoa == idPessoa))
            vinculos.Add("eventos de auditoria");
        return Task.FromResult<IReadOnlyList<string>>(vinculos);
    }

    // ──────────────────────────────────────────────────── ITrilhaDeAuditoria

    public Task RegistrarAsync(RegistroDeAuditoria registro, CancellationToken ct = default)
    {
        if (registro.Empresa != Empresa)
            throw new ErroDeDominio("TENANT_INVALIDO", "Tentativa de auditar ativo de outra empresa.");
        armazem.Auditoria.Enqueue(registro);
        return Task.CompletedTask;
    }

    Task<IReadOnlyList<RegistroDeAuditoria>> ITrilhaDeAuditoria.ListarAsync(IdDeAtivo ativo, int limite, CancellationToken ct) =>
        // Mesmo instante (relógio fixo nos testes): vale a ordem de gravação.
        Task.FromResult<IReadOnlyList<RegistroDeAuditoria>>(armazem.Auditoria
            .Select((registro, ordem) => (registro, ordem))
            .Where(x => x.registro.Empresa == Empresa && x.registro.Ativo == ativo)
            .OrderByDescending(x => x.registro.Em).ThenByDescending(x => x.ordem)
            .Take(Math.Max(limite, 0))
            .Select(x => x.registro)
            .ToArray());
}

/// <summary>
/// Pessoas e pleitos em memória, filtrados por empresa como o resto. Classe à
/// parte porque as portas repetem assinaturas das de validação
/// (<c>ObterAsync(Guid)</c>); a unidade de trabalho em memória não tem nada a
/// fazer com eles, que já são gravados ao adicionar.
/// </summary>
public sealed class RepositorioDeAcessoEmMemoria(ArmazemEmMemoria armazem, IContextoDeTenantAtual tenant)
    : IRepositorioDePessoas, IRepositorioDePleitos, IRepositorioDeSquads
{
    private IdDeEmpresa Empresa => tenant.Atual.Empresa;

    public Task<Pessoa?> ObterAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(armazem.Pessoas.TryGetValue(id, out var p) && p.Empresa == Empresa ? p : null);

    public Task<Pessoa?> ObterPorMatriculaAsync(Matricula matricula, CancellationToken ct = default) =>
        Task.FromResult(armazem.Pessoas.Values.FirstOrDefault(p => p.Empresa == Empresa && p.Matricula == matricula));

    public Task<IReadOnlyList<Pessoa>> ListarAsync(bool incluirInativas = false, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Pessoa>>(armazem.Pessoas.Values
            .Where(p => p.Empresa == Empresa && (incluirInativas || p.Ativa))
            .OrderBy(p => p.Nome, StringComparer.CurrentCulture)
            .ToArray());

    public Task AdicionarAsync(Pessoa pessoa, CancellationToken ct = default)
    {
        if (pessoa.Empresa != Empresa)
            throw new ErroDeDominio("TENANT_INVALIDO", "Tentativa de gravar pessoa de outra empresa.");
        lock (armazem.Pessoas)
        {
            if (armazem.Pessoas.Values.Any(p => p.Empresa == Empresa && p.Matricula == pessoa.Matricula && p.Id != pessoa.Id))
                throw new ErroDeDominio("CONFLITO_DE_EDICAO", $"Já existe uma pessoa com a matrícula {pessoa.Matricula}.");
            armazem.Pessoas[pessoa.Id] = pessoa;
        }
        return Task.CompletedTask;
    }

    public Task RemoverAsync(Pessoa pessoa, CancellationToken ct = default)
    {
        if (pessoa.Empresa == Empresa) armazem.Pessoas.TryRemove(pessoa.Id, out _);
        return Task.CompletedTask;
    }

    // ───────────────────────────────────────────────── IRepositorioDeSquads

    Task<Squad?> IRepositorioDeSquads.ObterAsync(Guid id, CancellationToken ct) =>
        Task.FromResult(armazem.Squads.TryGetValue(id, out var s) && s.Empresa == Empresa ? s : null);

    public Task<Squad?> ObterPorSiglaAsync(string sigla, CancellationToken ct = default) =>
        Task.FromResult(armazem.Squads.Values.FirstOrDefault(s => s.Empresa == Empresa && s.Sigla == Squad.NormalizarSigla(sigla)));

    Task<IReadOnlyList<Squad>> IRepositorioDeSquads.ListarAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Squad>>(armazem.Squads.Values
            .Where(s => s.Empresa == Empresa)
            .OrderBy(s => s.Ativa ? 0 : 1).ThenBy(s => s.Titulo, StringComparer.CurrentCulture)
            .ToArray());

    public Task AdicionarAsync(Squad squad, CancellationToken ct = default)
    {
        if (squad.Empresa != Empresa)
            throw new ErroDeDominio("TENANT_INVALIDO", "Tentativa de gravar squad de outra empresa.");
        lock (armazem.Squads)
        {
            if (armazem.Squads.Values.Any(s => s.Empresa == Empresa && s.Sigla == squad.Sigla && s.Id != squad.Id))
                throw new ErroDeDominio("CONFLITO_DE_EDICAO", $"Já existe uma squad com a sigla {squad.Sigla}.");
            armazem.Squads[squad.Id] = squad;
        }
        return Task.CompletedTask;
    }

    // ───────────────────────────────────────────────── IRepositorioDePleitos

    public Task AdicionarAsync(PleitoDeAcesso pleito, CancellationToken ct = default)
    {
        if (pleito.Empresa != Empresa)
            throw new ErroDeDominio("TENANT_INVALIDO", "Tentativa de gravar pleito de outra empresa.");
        armazem.Pleitos[pleito.Id] = pleito;
        return Task.CompletedTask;
    }

    Task<PleitoDeAcesso?> IRepositorioDePleitos.ObterAsync(Guid id, CancellationToken ct) =>
        Task.FromResult(armazem.Pleitos.TryGetValue(id, out var p) && p.Empresa == Empresa ? p : null);

    public Task<IReadOnlyList<PleitoDeAcesso>> ListarAsync(
        StatusDoPleito? status = null, Guid? idPessoa = null, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<PleitoDeAcesso>>(armazem.Pleitos.Values
            .Where(p => p.Empresa == Empresa && (status is null || p.Status == status) && (idPessoa is null || p.IdPessoa == idPessoa))
            .OrderBy(p => p.Status == StatusDoPleito.Pendente ? 0 : 1).ThenBy(p => p.CriadoEm).ThenBy(p => p.Id)
            .ToArray());
}
