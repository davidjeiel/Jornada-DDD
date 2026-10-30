using System.Text.Json;
using Jornada.Aplicacao.Portas;
using Jornada.Dominio.Catalogo;
using Jornada.Dominio.Comum;

namespace Jornada.Persistencia.Cosmos;

/// <summary>
/// Adaptador de saída sobre Cosmos DB (ADR-0016). Implementa as mesmas portas
/// que o adaptador em memória, e a mesma suíte de contrato prova que nenhum caso
/// de uso percebe a diferença.
///
/// Funciona como o change tracker do EF: o que é lido fica RASTREADO nesta
/// sessão (scoped, uma por requisição), com o ETag e um retrato do documento.
/// <see cref="ConfirmarAsync"/> compara cada entidade com o seu retrato, e só
/// grava o que mudou: um <c>TransactionalBatch</c> por ativo, com o ativo, as
/// validações dele e os eventos pendentes (a outbox). Ou tudo entra, ou nada.
///
/// Gravar dois ativos na mesma confirmação vira dois lotes, e o segundo pode
/// falhar depois do primeiro. Hoje nenhum caso de uso faz isso (ADR-0016,
/// consequências negativas).
/// </summary>
public sealed class RepositorioCosmos : IRepositorioDeAtivos, IRepositorioDeRelacoes,
    IRepositorioDeValidacoes, IUnidadeDeTrabalho, IRepositorioDeRevisoes, ITrilhaDeAuditoria
{
    private readonly AcessoDoTenant _acesso;
    private readonly Dictionary<IdDeAtivo, Rastreado<Ativo>> _ativos = [];
    private readonly Dictionary<Guid, Rastreado<ValidacaoEmProcesso>> _validacoes = [];
    private readonly Dictionary<(IdDeAtivo, int), Rastreado<RevisaoDoAtivo>> _revisoes = [];

    /// <summary>Registros de auditoria desta sessão ainda não gravados. Só criação: a trilha não se edita.</summary>
    private readonly List<RegistroDeAuditoria> _auditoriaPendente = [];

    public RepositorioCosmos(BancoDoCatalogo banco, IContextoDeTenantAtual tenant) =>
        _acesso = new AcessoDoTenant(banco, tenant);

    // ─────────────────────────────────────────────────── IRepositorioDeAtivos

    public async Task<Ativo?> ObterAsync(IdDeAtivo id, CancellationToken ct = default)
    {
        if (_ativos.TryGetValue(id, out var rastreado)) return rastreado.Entidade;
        var documento = await _acesso.LerAsync<DocumentoDeAtivo>(id, id.Valor, ct);
        return documento is null ? null : Rastrear(documento);
    }

    public async Task<Ativo?> ObterPorCodigoAsync(CodigoDeAtivo codigo, CancellationToken ct = default)
    {
        var documentos = await _acesso.ConsultarAsync<DocumentoDeAtivo>(TiposDeDocumento.Ativo,
            "c.codigo = @codigo", parametros: new Dictionary<string, object?> { ["@codigo"] = codigo.Valor }, ct: ct);
        return documentos.Count == 0 ? null : Rastrear(documentos[0]);
    }

    public async Task<IReadOnlyList<Ativo>> ListarAsync(
        string? tipoItem = null, StatusCicloVida? status = null,
        int limite = 200, CancellationToken ct = default)
    {
        var filtros = new List<string> { "true" };
        var parametros = new Dictionary<string, object?>();

        if (!string.IsNullOrWhiteSpace(tipoItem))
        {
            filtros.Add("c.tipoItem = @tipoItem");
            parametros["@tipoItem"] = tipoItem.ToLowerInvariant();
        }
        if (status is { } s)
        {
            filtros.Add("c.status = @status");
            parametros["@status"] = MaquinaDeCicloDeVida.Rotulo(s);
        }

        var documentos = await _acesso.ConsultarAsync<DocumentoDeAtivo>(TiposDeDocumento.Ativo,
            string.Join(" AND ", filtros), $"ORDER BY c.codigo OFFSET 0 LIMIT {Math.Max(limite, 0)}", parametros, ct);
        return documentos.Select(Rastrear).ToArray();
    }

    public Task AdicionarAsync(Ativo ativo, CancellationToken ct = default)
    {
        // Só marca: a gravação acontece no ConfirmarAsync.
        if (ativo.Empresa != _acesso.Empresa)
            throw new ErroDeDominio("TENANT_INVALIDO", "Tentativa de gravar ativo de outra empresa.");
        _ativos[ativo.Id] = Rastreado<Ativo>.Novo(ativo);
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<Ativo>> TrilhaAsync(IdDeAtivo id, CancellationToken ct = default)
    {
        var trilha = new List<Ativo>();
        var visitados = new HashSet<IdDeAtivo>();
        var atual = await ObterAsync(id, ct);

        while (atual is not null && visitados.Add(atual.Id))
        {
            trilha.Insert(0, atual);
            atual = atual.IdPai is { } pai ? await ObterAsync(pai, ct) : null;
        }
        return trilha;
    }

    public async Task<bool> ExisteNomeDuplicadoAsync(
        string tipoItem, string nome, IdDeAtivo? exceto = null, CancellationToken ct = default)
    {
        var parametros = new Dictionary<string, object?>
        {
            ["@tipoItem"] = tipoItem.ToLowerInvariant(),
            ["@nome"] = Mapeamento.Normalizar(nome),
            ["@exceto"] = exceto is { } e ? Mapeamento.Texto(e.Valor) : "",
        };
        return await _acesso.ContarAsync(TiposDeDocumento.Ativo,
            "c.tipoItem = @tipoItem AND c.nomeNormalizado = @nome AND c.id != @exceto", parametros, ct) > 0;
    }

    public Task<int> ProximoSequencialAsync(string tipoItem, CancellationToken ct = default) =>
        _acesso.IncrementarAsync(tipoItem.ToLowerInvariant(), ct);

    // ───────────────────────────────────────────────── IRepositorioDeRelacoes

    public Task RelacionarAsync(IdDeAtivo origem, IdDeAtivo destino, string tipo,
        Criticidade criticidade = Criticidade.Media, CancellationToken ct = default) =>
        // Grava na hora: a porta não passa pela unidade de trabalho.
        _acesso.CriarAsync(new DocumentoDeRelacao
        {
            Id = Mapeamento.Texto(Guid.CreateVersion7()),
            Empresa = Mapeamento.Texto(_acesso.Empresa.Valor),
            IdAtivo = Mapeamento.Texto(origem.Valor),
            Destino = Mapeamento.Texto(destino.Valor),
            TipoRelacao = tipo,
            Criticidade = criticidade.Chave(),
            CriadoEm = DateTimeOffset.UtcNow,
        }, ct);

    public async Task<IReadOnlyList<RelacaoResumo>> ListarRelacoesAsync(IdDeAtivo id, CancellationToken ct = default)
    {
        // As de saída moram na partição do ativo; as de entrada, na de quem aponta
        // para ele. A consulta pelo prefixo da empresa pega as duas.
        var documentos = await _acesso.ConsultarAsync<DocumentoDeRelacao>(TiposDeDocumento.Relacao,
            "c.idAtivo = @id OR c.destino = @id",
            parametros: new Dictionary<string, object?> { ["@id"] = Mapeamento.Texto(id.Valor) }, ct: ct);
        return documentos.Select(Resumo).ToArray();
    }

    public async Task<bool> TemDependenciaCircularAsync(IdDeAtivo id, CancellationToken ct = default)
    {
        // Busca em profundidade sobre as arestas `depende_de` da empresa,
        // carregadas numa consulta só (ADR-0016).
        var arestas = (await _acesso.ConsultarAsync<DocumentoDeRelacao>(TiposDeDocumento.Relacao,
                "c.tipoRelacao = 'depende_de'", ct: ct))
            .ToLookup(r => r.IdAtivo, r => r.Destino);

        var alvo = Mapeamento.Texto(id.Valor);
        var visitados = new HashSet<string>();
        var pilha = new Stack<string>([alvo]);

        while (pilha.Count > 0)
            foreach (var destino in arestas[pilha.Pop()])
            {
                if (destino == alvo) return true;
                if (visitados.Add(destino)) pilha.Push(destino);
            }
        return false;
    }

    public async Task<IReadOnlyList<string>> NomesDeDestinosForaDeVigenciaAsync(
        IdDeAtivo id, CancellationToken ct = default)
    {
        var saidas = await _acesso.ConsultarAsync<DocumentoDeRelacao>(TiposDeDocumento.Relacao,
            "c.idAtivo = @id", parametros: new Dictionary<string, object?> { ["@id"] = Mapeamento.Texto(id.Valor) }, ct: ct);

        var nomes = new List<string>();
        foreach (var destino in saidas.Select(r => new IdDeAtivo(Guid.Parse(r.Destino))).Distinct())
            if (await ObterAsync(destino, ct) is { } ativo && MaquinaDeCicloDeVida.ForaDeVigencia(ativo.Status))
                nomes.Add(ativo.Nome);
        return nomes;
    }

    public Task<int> ContarImplementacoesAsync(IdDeAtivo idCapacidade, CancellationToken ct = default) =>
        _acesso.ContarAsync(TiposDeDocumento.Relacao, "c.destino = @id AND c.tipoRelacao = 'implementa'",
            new Dictionary<string, object?> { ["@id"] = Mapeamento.Texto(idCapacidade.Valor) }, ct);

    // ─────────────────────────────────────────────── IRepositorioDeValidacoes

    public Task AdicionarAsync(ValidacaoEmProcesso validacao, CancellationToken ct = default)
    {
        if (validacao.Empresa != _acesso.Empresa)
            throw new ErroDeDominio("TENANT_INVALIDO", "Tentativa de gravar validação de outra empresa.");
        _validacoes[validacao.Id] = Rastreado<ValidacaoEmProcesso>.Novo(validacao);
        return Task.CompletedTask;
    }

    public async Task<ValidacaoEmProcesso?> ObterAsync(Guid id, CancellationToken ct = default)
    {
        if (_validacoes.TryGetValue(id, out var rastreada)) return rastreada.Entidade;

        // A porta só dá o id, não o ativo: consulta pelo prefixo da empresa.
        var documentos = await _acesso.ConsultarAsync<DocumentoDeValidacao>(TiposDeDocumento.Validacao,
            "c.id = @id", parametros: new Dictionary<string, object?> { ["@id"] = Mapeamento.Texto(id) }, ct: ct);
        return documentos.Count == 0 ? null : Rastrear(documentos[0]);
    }

    public async Task<IReadOnlyList<ValidacaoEmProcesso>> ListarAsync(IdDeAtivo? ativo = null, CancellationToken ct = default)
    {
        var documentos = ativo is { } id
            ? await _acesso.ConsultarAsync<DocumentoDeValidacao>(TiposDeDocumento.Validacao, "c.idAtivo = @id",
                parametros: new Dictionary<string, object?> { ["@id"] = Mapeamento.Texto(id.Valor) }, ct: ct)
            : await _acesso.ConsultarAsync<DocumentoDeValidacao>(TiposDeDocumento.Validacao, ct: ct);

        // Mesma ordem do adaptador em memória: abertas primeiro, depois por etapa.
        return documentos.Select(Rastrear).OrderBy(v => v.Status).ThenBy(v => v.Etapa).ToArray();
    }

    // ─────────────────────────────────────────────── IRepositorioDeRevisoes

    public Task AdicionarAsync(RevisaoDoAtivo revisao, CancellationToken ct = default)
    {
        if (revisao.Empresa != _acesso.Empresa)
            throw new ErroDeDominio("TENANT_INVALIDO", "Tentativa de gravar revisão de outra empresa.");
        _revisoes[(revisao.Ativo, revisao.Numero)] = Rastreado<RevisaoDoAtivo>.Novo(revisao);
        return Task.CompletedTask;
    }

    public async Task<RevisaoDoAtivo?> ObterAsync(IdDeAtivo ativo, int numero, CancellationToken ct = default)
    {
        if (_revisoes.TryGetValue((ativo, numero), out var rastreada)) return rastreada.Entidade;
        var documento = await _acesso.LerAsync<DocumentoDeRevisao>(ativo, Mapeamento.IdDaRevisao(numero), ct);
        return documento is null ? null : Rastrear(documento);
    }

    async Task<IReadOnlyList<RevisaoDoAtivo>> IRepositorioDeRevisoes.ListarAsync(IdDeAtivo ativo, CancellationToken ct)
    {
        var documentos = await _acesso.ConsultarAsync<DocumentoDeRevisao>(TiposDeDocumento.Revisao,
            "c.idAtivo = @id", parametros: new Dictionary<string, object?> { ["@id"] = Mapeamento.Texto(ativo.Valor) }, ct: ct);
        return documentos.Select(Rastrear)
            .Concat(_revisoes.Values.Where(r => r.EhNovo && r.Entidade.Ativo == ativo).Select(r => r.Entidade))
            .DistinctBy(r => r.Numero)
            .OrderByDescending(r => r.Numero)
            .ToArray();
    }

    // ──────────────────────────────────────────────────── ITrilhaDeAuditoria

    public Task RegistrarAsync(RegistroDeAuditoria registro, CancellationToken ct = default)
    {
        if (registro.Empresa != _acesso.Empresa)
            throw new ErroDeDominio("TENANT_INVALIDO", "Tentativa de auditar ativo de outra empresa.");
        _auditoriaPendente.Add(registro);
        return Task.CompletedTask;
    }

    async Task<IReadOnlyList<RegistroDeAuditoria>> ITrilhaDeAuditoria.ListarAsync(IdDeAtivo ativo, int limite, CancellationToken ct)
    {
        var documentos = await _acesso.ConsultarAsync<DocumentoDeAuditoria>(TiposDeDocumento.Auditoria,
            "c.idAtivo = @id", $"ORDER BY c.em DESC OFFSET 0 LIMIT {Math.Max(limite, 0)}",
            new Dictionary<string, object?> { ["@id"] = Mapeamento.Texto(ativo.Valor) }, ct);
        return documentos.Select(Mapeamento.ParaDominio).ToArray();
    }

    // ───────────────────────────────────────────────────── IUnidadeDeTrabalho

    public async Task<int> ConfirmarAsync(CancellationToken ct = default)
    {
        var lotes = new Dictionary<IdDeAtivo, AcessoDoTenant.Lote>();
        AcessoDoTenant.Lote LoteDe(IdDeAtivo particao) =>
            lotes.TryGetValue(particao, out var lote) ? lote : lotes[particao] = _acesso.NovoLote(particao);

        var eventos = new List<(Ativo Ativo, int Quantos)>();

        foreach (var rastreado in _ativos.Values)
        {
            var ativo = rastreado.Entidade;
            var documento = Mapeamento.ParaDocumento(ativo);
            Agendar(LoteDe(ativo.Id), rastreado, documento);

            // Peek, não drena: se o lote falhar, os eventos continuam no agregado.
            foreach (var evento in ativo.EventosPendentes)
                LoteDe(ativo.Id).Criar(Mapeamento.ParaDocumento(evento, ativo.Empresa, ativo.Id));
            if (ativo.EventosPendentes.Count > 0)
                eventos.Add((ativo, ativo.EventosPendentes.Count));
        }

        foreach (var rastreada in _validacoes.Values)
            Agendar(LoteDe(rastreada.Entidade.Ativo), rastreada, Mapeamento.ParaDocumento(rastreada.Entidade));

        foreach (var rastreada in _revisoes.Values)
            Agendar(LoteDe(rastreada.Entidade.Ativo), rastreada, Mapeamento.ParaDocumento(rastreada.Entidade));

        foreach (var registro in _auditoriaPendente)
            LoteDe(registro.Ativo).Criar(Mapeamento.ParaDocumento(registro));

        foreach (var lote in lotes.Values)
            await lote.ExecutarAsync(ct);
        _auditoriaPendente.Clear();

        foreach (var (ativo, _) in eventos)
            ativo.DrenarEventos();
        return eventos.Sum(e => e.Quantos);
    }

    /// <summary>Novo vira criação; mudado vira substituição condicionada ao ETag; igual não vai ao banco.</summary>
    private static void Agendar<TEntidade, TDocumento>(
        AcessoDoTenant.Lote lote, Rastreado<TEntidade> rastreado, TDocumento documento)
        where TDocumento : DocumentoDoCatalogo
    {
        var retrato = Retrato(documento);
        if (rastreado.EhNovo)
            lote.Criar(documento, etag => rastreado.Confirmar(etag, retrato));
        else if (retrato != rastreado.Retrato)
            lote.Substituir(documento, rastreado.ETag, etag => rastreado.Confirmar(etag, retrato));
    }

    // ───────────────────────────────────────────────────────────── rastreamento

    /// <summary>
    /// Mapa de identidade: o mesmo id devolve a MESMA instância durante a
    /// sessão, como no EF. Sem isso, duas leituras do mesmo ativo virariam dois
    /// objetos, e a confirmação gravaria só um deles.
    /// </summary>
    private Ativo Rastrear(DocumentoDeAtivo documento)
    {
        var id = new IdDeAtivo(Guid.Parse(documento.Id));
        if (_ativos.TryGetValue(id, out var existente)) return existente.Entidade;

        var ativo = Mapeamento.ParaDominio(documento);
        _ativos[id] = Rastreado<Ativo>.Lido(ativo, documento.ETag, Retrato(Mapeamento.ParaDocumento(ativo)));
        return ativo;
    }

    private ValidacaoEmProcesso Rastrear(DocumentoDeValidacao documento)
    {
        var id = Guid.Parse(documento.Id);
        if (_validacoes.TryGetValue(id, out var existente)) return existente.Entidade;

        var validacao = Mapeamento.ParaDominio(documento);
        _validacoes[id] = Rastreado<ValidacaoEmProcesso>.Lido(
            validacao, documento.ETag, Retrato(Mapeamento.ParaDocumento(validacao)));
        return validacao;
    }

    private RevisaoDoAtivo Rastrear(DocumentoDeRevisao documento)
    {
        var chave = (new IdDeAtivo(Guid.Parse(documento.IdAtivo)), documento.Numero);
        if (_revisoes.TryGetValue(chave, out var existente)) return existente.Entidade;

        var revisao = Mapeamento.ParaDominio(documento);
        _revisoes[chave] = Rastreado<RevisaoDoAtivo>.Lido(
            revisao, documento.ETag, Retrato(Mapeamento.ParaDocumento(revisao)));
        return revisao;
    }

    private static string Retrato<T>(T documento) => JsonSerializer.Serialize(documento, BancoDoCatalogo.Json);

    private static RelacaoResumo Resumo(DocumentoDeRelacao r) => new(
        new IdDeAtivo(Guid.Parse(r.IdAtivo)), new IdDeAtivo(Guid.Parse(r.Destino)), r.TipoRelacao,
        CriticidadeExtensoes.De(r.Criticidade));

    private sealed class Rastreado<T>
    {
        public required T Entidade { get; init; }
        public string? ETag { get; private set; }

        /// <summary>O documento como está no banco. Nulo enquanto a entidade é nova.</summary>
        public string? Retrato { get; private set; }

        public bool EhNovo => Retrato is null;

        public static Rastreado<T> Novo(T entidade) => new() { Entidade = entidade };

        public static Rastreado<T> Lido(T entidade, string? etag, string retrato) =>
            new() { Entidade = entidade, ETag = etag, Retrato = retrato };

        public void Confirmar(string etag, string retrato)
        {
            ETag = etag;
            Retrato = retrato;
        }
    }
}
