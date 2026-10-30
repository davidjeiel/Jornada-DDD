using System.Net;
using Jornada.Aplicacao.Portas;
using Jornada.Dominio.Catalogo;
using Jornada.Dominio.Comum;
using Jornada.Dominio.Tenancy;
using Microsoft.Azure.Cosmos;

namespace Jornada.Persistencia.Cosmos;

/// <summary>
/// O PONTO ÚNICO de acesso ao Cosmos (ADR-0016 §4). Faz, no código, o papel
/// que o RLS fazia no Postgres:
///
///  - toda leitura pontual usa a chave <c>[empresa, idAtivo]</c> com a empresa
///    da requisição;
///  - toda consulta vai com o PREFIXO <c>[empresa]</c> como chave de partição E
///    com <c>c.empresa = @empresa</c> no filtro: quem chama escreve só o resto do
///    filtro, e não tem como esquecer a empresa;
///  - todo documento gravado é conferido antes de sair (o <c>WITH CHECK</c>);
///  - todo documento lido é conferido antes de entrar.
///
/// Scoped, como o contexto de tenant que ele carrega. Uma guarda de
/// arquitetura garante que nenhum outro tipo deste adaptador guarde container
/// ou cliente do Cosmos.
/// </summary>
internal sealed class AcessoDoTenant(BancoDoCatalogo banco, IContextoDeTenantAtual tenant)
{
    private readonly Container _catalogo = banco.Catalogo;
    private readonly Container _contadores = banco.Contadores;

    public IdDeEmpresa Empresa => tenant.Atual.Empresa;

    private string EmpresaTexto => Mapeamento.Texto(Empresa.Valor);

    private PartitionKey Chave(IdDeAtivo particao) =>
        new PartitionKeyBuilder().Add(EmpresaTexto).Add(Mapeamento.Texto(particao.Valor)).Build();

    private PartitionKey PrefixoDaEmpresa => new PartitionKeyBuilder().Add(EmpresaTexto).Build();

    // ───────────────────────────────────────────────────────────── leitura

    public Task<T?> LerAsync<T>(IdDeAtivo particao, Guid id, CancellationToken ct)
        where T : DocumentoDoCatalogo =>
        LerAsync<T>(particao, Mapeamento.Texto(id), ct);

    public async Task<T?> LerAsync<T>(IdDeAtivo particao, string id, CancellationToken ct)
        where T : DocumentoDoCatalogo
    {
        try
        {
            var resposta = await _catalogo.ReadItemAsync<T>(id, Chave(particao), cancellationToken: ct);
            return Conferido(resposta.Resource);
        }
        catch (CosmosException e) when (e.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    /// <summary>
    /// Documentos de um tipo desta empresa. <paramref name="filtro"/> e
    /// <paramref name="sufixo"/> são SQL do Cosmos sobre o alias <c>c</c>; a
    /// restrição de empresa e de tipo é acrescentada aqui, nunca por quem chama.
    /// </summary>
    public async Task<List<T>> ConsultarAsync<T>(
        string tipo, string filtro = "true", string sufixo = "",
        IReadOnlyDictionary<string, object?>? parametros = null, CancellationToken ct = default)
        where T : DocumentoDoCatalogo
    {
        var consulta = Montar($"SELECT * FROM c WHERE c.empresa = @empresa AND c.tipo = @tipo AND ({filtro}) {sufixo}",
            tipo, parametros);

        var lista = new List<T>();
        using var iterador = _catalogo.GetItemQueryIterator<T>(consulta,
            requestOptions: new QueryRequestOptions { PartitionKey = PrefixoDaEmpresa });
        while (iterador.HasMoreResults)
            foreach (var documento in await iterador.ReadNextAsync(ct))
                lista.Add(Conferido(documento));
        return lista;
    }

    public async Task<int> ContarAsync(
        string tipo, string filtro, IReadOnlyDictionary<string, object?>? parametros = null, CancellationToken ct = default)
    {
        var consulta = Montar("SELECT VALUE COUNT(1) FROM c WHERE c.empresa = @empresa AND c.tipo = @tipo AND (" + filtro + ")",
            tipo, parametros);

        var total = 0;
        using var iterador = _catalogo.GetItemQueryIterator<int>(consulta,
            requestOptions: new QueryRequestOptions { PartitionKey = PrefixoDaEmpresa });
        while (iterador.HasMoreResults)
            total += (await iterador.ReadNextAsync(ct)).Sum();
        return total;
    }

    private QueryDefinition Montar(string sql, string tipo, IReadOnlyDictionary<string, object?>? parametros)
    {
        var consulta = new QueryDefinition(sql)
            .WithParameter("@empresa", EmpresaTexto)
            .WithParameter("@tipo", tipo);
        foreach (var (nome, valor) in parametros ?? new Dictionary<string, object?>())
            consulta = consulta.WithParameter(nome, valor);
        return consulta;
    }

    // ───────────────────────────────────────────────────────────── escrita

    /// <summary>Gravação avulsa, fora de lote. Hoje só a relação (a porta grava na hora).</summary>
    public async Task CriarAsync<T>(T documento, CancellationToken ct) where T : DocumentoDoCatalogo
    {
        Conferir(documento, null);
        await _catalogo.CreateItemAsync(documento, Chave(new IdDeAtivo(Guid.Parse(documento.IdAtivo))), cancellationToken: ct);
    }

    public Lote NovoLote(IdDeAtivo particao) => new(this, particao);

    /// <summary>
    /// Sequencial por (empresa, nome) com incremento atômico no servidor. O
    /// primeiro uso cria o contador; se duas requisições criarem ao mesmo tempo,
    /// a que perder volta ao incremento.
    /// </summary>
    public async Task<int> IncrementarAsync(string nome, CancellationToken ct)
    {
        var chave = new PartitionKey(EmpresaTexto);
        for (var tentativa = 0; ; tentativa++)
        {
            try
            {
                var r = await _contadores.PatchItemAsync<DocumentoDeContador>(
                    nome, chave, [PatchOperation.Increment("/valor", 1)], cancellationToken: ct);
                return r.Resource.Valor;
            }
            catch (CosmosException e) when (e.StatusCode == HttpStatusCode.NotFound && tentativa < 3)
            {
                try
                {
                    await _contadores.CreateItemAsync(
                        new DocumentoDeContador { Id = nome, Empresa = EmpresaTexto, Valor = 1 }, chave, cancellationToken: ct);
                    return 1;
                }
                catch (CosmosException c) when (c.StatusCode == HttpStatusCode.Conflict)
                {
                    // outra requisição criou primeiro: volta a incrementar
                }
            }
        }
    }

    // ──────────────────────────────────────────────────────────── conferência

    private T Conferido<T>(T documento) where T : DocumentoDoCatalogo
    {
        if (documento.Empresa != EmpresaTexto)
            throw new InvalidOperationException(
                "O Cosmos devolveu documento de outra empresa. A consulta furou o isolamento (ADR-0016 §4).");
        return documento;
    }

    private void Conferir(DocumentoDoCatalogo documento, IdDeAtivo? particao)
    {
        if (documento.Empresa != EmpresaTexto)
            throw new ErroDeDominio("TENANT_INVALIDO", "Tentativa de gravar documento de outra empresa.");
        if (particao is { } p && documento.IdAtivo != Mapeamento.Texto(p.Valor))
            throw new InvalidOperationException(
                $"Documento {documento.Tipo} {documento.Id} não pertence à partição do ativo {p}.");
    }

    /// <summary>
    /// Um <c>TransactionalBatch</c> sobre a partição de UM ativo: tudo entra ou
    /// nada entra (ADR-0016 §2). Cada operação leva um retorno que recebe o
    /// ETag novo, para o rastreamento da sessão continuar valendo depois.
    /// </summary>
    public sealed class Lote
    {
        // O Cosmos recusa lote com mais de 100 operações.
        private const int LimiteDeOperacoes = 100;

        private readonly AcessoDoTenant _acesso;
        private readonly IdDeAtivo _particao;
        private readonly TransactionalBatch _lote;
        private readonly List<Action<string>> _aoConfirmar = [];

        internal Lote(AcessoDoTenant acesso, IdDeAtivo particao)
        {
            _acesso = acesso;
            _particao = particao;
            _lote = acesso._catalogo.CreateTransactionalBatch(acesso.Chave(particao));
        }

        public int Operacoes => _aoConfirmar.Count;

        public void Criar<T>(T documento, Action<string>? aoConfirmar = null) where T : DocumentoDoCatalogo
        {
            _acesso.Conferir(documento, _particao);
            _lote.CreateItem(documento);
            _aoConfirmar.Add(aoConfirmar ?? (_ => { }));
        }

        /// <summary>Substitui só se ninguém gravou desde a leitura (concorrência otimista).</summary>
        public void Substituir<T>(T documento, string? etag, Action<string>? aoConfirmar = null) where T : DocumentoDoCatalogo
        {
            _acesso.Conferir(documento, _particao);
            _lote.ReplaceItem(documento.Id, documento, new TransactionalBatchItemRequestOptions { IfMatchEtag = etag });
            _aoConfirmar.Add(aoConfirmar ?? (_ => { }));
        }

        public async Task ExecutarAsync(CancellationToken ct)
        {
            if (Operacoes == 0) return;
            if (Operacoes > LimiteDeOperacoes)
                throw new InvalidOperationException(
                    $"{Operacoes} operações num lote; o Cosmos aceita até {LimiteDeOperacoes}.");

            using var resposta = await _lote.ExecuteAsync(ct);
            if (!resposta.IsSuccessStatusCode)
            {
                // As outras operações vêm como 424 (dependência falhou); a causa é a que não for 424.
                var causa = resposta.FirstOrDefault(r => r.StatusCode != HttpStatusCode.FailedDependency)?.StatusCode
                            ?? resposta.StatusCode;
                throw causa switch
                {
                    HttpStatusCode.PreconditionFailed => new ErroDeDominio("CONFLITO_DE_EDICAO",
                        "O ativo foi alterado por outra requisição. Recarregue e tente de novo."),
                    HttpStatusCode.Conflict => new ErroDeDominio("CONFLITO_DE_EDICAO",
                        "Já existe um registro com este identificador."),
                    _ => new InvalidOperationException($"O Cosmos recusou o lote: {causa} ({resposta.ErrorMessage})."),
                };
            }

            for (var i = 0; i < _aoConfirmar.Count; i++)
                _aoConfirmar[i](resposta[i].ETag);
        }
    }
}
