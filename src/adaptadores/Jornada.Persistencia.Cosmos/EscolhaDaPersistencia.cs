namespace Jornada.Persistencia.Cosmos;

/// <summary>
/// Qual adaptador a API liga e com que conexão. O ambiente corporativo entrega
/// a conexão do Cosmos em <see cref="VariavelDoAmbiente"/>; o compose local e
/// quem já configurava assim seguem com <c>ConnectionStrings:Cosmos</c>.
///
/// Com a variável do ambiente presente e nenhum provedor declarado, o Cosmos é
/// ligado sozinho: cair na memória ali perderia os dados a cada reinício.
/// Um provedor declarado sempre vence.
/// </summary>
public sealed record EscolhaDaPersistencia(bool UsaCosmos, string? StringDeConexao)
{
    public const string VariavelDoAmbiente = "COSMOS_CONNECTION_STRING";

    /// <param name="provedor"><c>Persistencia:Provedor</c>, se declarado.</param>
    /// <param name="conexaoDoAmbiente">O valor de <see cref="VariavelDoAmbiente"/>.</param>
    /// <param name="conexaoDaConfiguracao"><c>ConnectionStrings:Cosmos</c>.</param>
    public static EscolhaDaPersistencia De(string? provedor, string? conexaoDoAmbiente, string? conexaoDaConfiguracao)
    {
        var doAmbiente = Preenchido(conexaoDoAmbiente);
        var conexao = doAmbiente ?? Preenchido(conexaoDaConfiguracao);
        var declarado = Preenchido(provedor) ?? (doAmbiente is null ? "memoria" : "cosmos");

        if (string.Equals(declarado, "memoria", StringComparison.OrdinalIgnoreCase))
            return new EscolhaDaPersistencia(false, null);

        // Um valor desconhecido derruba a partida, em vez de cair em silêncio
        // na memória (o Postgres saiu do produto, ADR-0016).
        if (!string.Equals(declarado, "cosmos", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"Persistencia:Provedor='{declarado}' não existe. Use 'cosmos' ou 'memoria'.");

        return conexao is null
            ? throw new InvalidOperationException(
                $"Com Persistencia:Provedor=cosmos, informe a conexão em {VariavelDoAmbiente} ou em ConnectionStrings:Cosmos.")
            : new EscolhaDaPersistencia(true, conexao);
    }

    private static string? Preenchido(string? valor) => string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();
}
