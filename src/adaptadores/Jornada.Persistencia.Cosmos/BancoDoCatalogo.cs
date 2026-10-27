using System.Text.Json;
using Microsoft.Azure.Cosmos;

namespace Jornada.Persistencia.Cosmos;

/// <summary>
/// A conexão com a conta e os containers. É o análogo do banco: um por
/// processo (registre como SINGLETON). Não sabe nada de empresa. Quem sabe é
/// <see cref="AcessoDoTenant"/>, o único que recebe os containers.
/// </summary>
public sealed class BancoDoCatalogo : IDisposable
{
    public const string ContainerDoCatalogo = "catalogo";
    public const string ContainerDeContadores = "contadores";

    /// <summary>Camel case na gravação: as propriedades da chave de partição são <c>/empresa</c> e <c>/idAtivo</c>.</summary>
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly CosmosClient _cliente;
    private readonly string _nomeDoBanco;

    internal Container Catalogo { get; }
    internal Container Contadores { get; }

    private BancoDoCatalogo(CosmosClient cliente, string nomeDoBanco)
    {
        _cliente = cliente;
        _nomeDoBanco = nomeDoBanco;
        Catalogo = cliente.GetContainer(nomeDoBanco, ContainerDoCatalogo);
        Contadores = cliente.GetContainer(nomeDoBanco, ContainerDeContadores);
    }

    /// <param name="stringDeConexao">AccountEndpoint=...;AccountKey=...</param>
    /// <param name="nomeDoBanco">Nome do database na conta.</param>
    /// <param name="emulador">
    /// Liga o modo do emulador local: modo gateway, só o endpoint informado e
    /// certificado autoassinado aceito. NUNCA em produção.
    /// </param>
    public static BancoDoCatalogo Conectar(string stringDeConexao, string nomeDoBanco, bool emulador = false)
    {
        var opcoes = new CosmosClientOptions
        {
            UseSystemTextJsonSerializerWithOptions = Json,
            ApplicationName = "jornada-ddd",
        };

        if (emulador)
        {
            opcoes.ConnectionMode = ConnectionMode.Gateway;
            opcoes.LimitToEndpoint = true;
            opcoes.HttpClientFactory = () => new HttpClient(new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
            });
        }

        return new BancoDoCatalogo(new CosmosClient(stringDeConexao, opcoes), nomeDoBanco);
    }

    /// <summary>
    /// Cria database e containers se não existirem. Idempotente; roda na
    /// inicialização, antes de a API aceitar tráfego.
    /// </summary>
    public async Task ProvisionarAsync(CancellationToken ct = default)
    {
        var banco = (await _cliente.CreateDatabaseIfNotExistsAsync(_nomeDoBanco, cancellationToken: ct)).Database;

        // Chave hierárquica: empresa primeiro, sempre (ADR-0016 §1).
        await banco.CreateContainerIfNotExistsAsync(
            new ContainerProperties(ContainerDoCatalogo, new List<string> { "/empresa", "/idAtivo" }),
            cancellationToken: ct);

        await banco.CreateContainerIfNotExistsAsync(
            new ContainerProperties(ContainerDeContadores, "/empresa"), cancellationToken: ct);
    }

    /// <summary>Prontidão: o container do catálogo responde?</summary>
    public async Task<bool> VerificarAsync(CancellationToken ct = default)
    {
        try
        {
            await Catalogo.ReadContainerAsync(cancellationToken: ct);
            return true;
        }
        catch (CosmosException)
        {
            return false;
        }
    }

    public void Dispose() => _cliente.Dispose();
}
