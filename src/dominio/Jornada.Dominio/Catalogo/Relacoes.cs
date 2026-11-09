using Jornada.Dominio.Comum;

namespace Jornada.Dominio.Catalogo;

/// <summary>
/// Os tipos de relação entre ativos, os destinos que fazem sentido para cada
/// um e o mecanismo da relação. Portado de <c>tipos.RELACOES</c>,
/// <c>tipos.DESTINOS_SUGERIDOS</c> e <c>tipos.MECANISMOS</c>.
///
/// Os destinos sugeridos orientam o formulário ("implementa" propõe
/// capacidades, não endpoints) sem virar proibição: o catálogo real tem
/// exceções, e quem cadastra pode escolher qualquer ativo.
/// </summary>
public static class CatalogoDeRelacoes
{
    public const string DependeDe = "depende_de";

    public sealed record TipoDeRelacao(string Chave, string Rotulo, IReadOnlyList<string> DestinosSugeridos);

    public static readonly IReadOnlyList<TipoDeRelacao> Tipos =
    [
        new("implementa", "implementa a capacidade", ["capacidade"]),
        new("expoe", "expõe", ["api", "endpoint", "evento"]),
        new("consome", "consome", ["api", "endpoint", "evento", "objeto_dado"]),
        new("produz", "produz", ["evento", "objeto_dado"]),
        new(DependeDe, "depende de", ["dominio", "subdominio", "contexto", "capacidade", "sistema", "aplicacao", "api", "base_dados"]),
        new("persiste_em", "persiste em", ["base_dados", "objeto_dado"]),
    ];

    /// <summary>Como a relação acontece na prática: a pergunta que o time técnico responde.</summary>
    public static readonly IReadOnlyList<(string Chave, string Rotulo)> Mecanismos =
    [
        ("sincrono", "Síncrono (HTTP/RPC)"),
        ("assincrono", "Assíncrono (evento/fila)"),
        ("batch", "Batch (job agendado)"),
        ("arquivo", "Arquivo (troca de arquivos)"),
        ("biblioteca", "Biblioteca compartilhada"),
    ];

    /// <summary>
    /// De onde veio a evidência da relação (<c>relacionamento_ativo.origem_evidencia</c>
    /// do painel). Manual é a declarada por uma pessoa; as outras, lidas de uma fonte.
    /// A pendência "dependência crítica sem evidência" conta só as manuais.
    /// </summary>
    public const string OrigemManual = "manual";

    public static readonly IReadOnlyList<(string Chave, string Rotulo)> OrigensDeEvidencia =
    [
        (OrigemManual, "Declarada (manual)"),
        ("git", "Repositório Git"),
        ("openapi", "Contrato OpenAPI"),
        ("cmdb", "CMDB"),
        ("cicd", "Esteira de CI/CD"),
    ];

    /// <summary>A origem é opcional e vale manual; quando vem, tem de ser uma das conhecidas.</summary>
    public static string ConferirOrigem(string? origem)
    {
        var chave = (origem ?? "").Trim().ToLowerInvariant();
        if (chave.Length == 0) return OrigemManual;
        if (OrigensDeEvidencia.All(o => o.Chave != chave))
            throw new ErroDeDominio("ORIGEM_DE_EVIDENCIA_INVALIDA",
                $"Origem da evidência inválida: {origem}. Use {string.Join(", ", OrigensDeEvidencia.Select(o => o.Chave))}.");
        return chave;
    }

    /// <summary>A chave do tipo, normalizada; tipo desconhecido é recusado.</summary>
    public static string ConferirTipo(string? tipo)
    {
        var chave = (tipo ?? "").Trim().ToLowerInvariant();
        if (chave.Length == 0)
            throw new ErroDeDominio("TIPO_OBRIGATORIO", "Tipo de relação é obrigatório.");
        if (Tipos.All(t => t.Chave != chave))
            throw new ErroDeDominio("TIPO_DE_RELACAO_INVALIDO",
                $"Tipo de relação inválido: {tipo}. Use {string.Join(", ", Tipos.Select(t => t.Chave))}.");
        return chave;
    }

    /// <summary>O mecanismo é opcional; quando vem, tem de ser um dos conhecidos.</summary>
    public static string? ConferirMecanismo(string? mecanismo)
    {
        var chave = (mecanismo ?? "").Trim().ToLowerInvariant();
        if (chave.Length == 0) return null;
        if (Mecanismos.All(m => m.Chave != chave))
            throw new ErroDeDominio("MECANISMO_INVALIDO",
                $"Mecanismo inválido: {mecanismo}. Use {string.Join(", ", Mecanismos.Select(m => m.Chave))}.");
        return chave;
    }
}
