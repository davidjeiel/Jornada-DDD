using System.Text.Json.Serialization;

namespace Jornada.Persistencia.Cosmos;

/// <summary>
/// O formato gravado no container <c>catalogo</c> (ADR-0016 §2). Todo documento
/// carrega <c>empresa</c> e <c>idAtivo</c>, que formam a chave de partição
/// hierárquica, e um <c>tipo</c> que diz o que ele é.
///
/// Documento não é agregado: é o formato no banco, mapeado de e para o domínio
/// em <see cref="Mapeamento"/>. Os Guids são texto porque a chave de partição
/// compara texto, e um formato só evita a partição "quase igual".
/// </summary>
internal abstract class DocumentoDoCatalogo
{
    public required string Id { get; init; }
    public required string Empresa { get; init; }
    public required string IdAtivo { get; init; }
    public abstract string Tipo { get; }

    /// <summary>Controle de concorrência otimista. Preenchido pelo Cosmos; nunca gravado por nós.</summary>
    [JsonPropertyName("_etag")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ETag { get; init; }
}

internal static class TiposDeDocumento
{
    public const string Ativo = "ativo";
    public const string Validacao = "validacao";
    public const string Relacao = "relacao";
    public const string Evento = "evento";
}

internal sealed class DocumentoDeAtivo : DocumentoDoCatalogo
{
    public override string Tipo => TiposDeDocumento.Ativo;

    public string? Unidade { get; init; }
    public required string TipoItem { get; init; }
    public required string Codigo { get; init; }
    public required string Nome { get; init; }

    /// <summary>Nome em minúsculas, para a checagem de duplicidade sem depender de função do banco.</summary>
    public required string NomeNormalizado { get; init; }
    public required string Descricao { get; init; }
    public string? IdPai { get; init; }
    public required string Criticidade { get; init; }
    public required string Status { get; init; }
    public required string Origem { get; init; }
    public int RevisaoAtual { get; init; }
    public int? UltimaRevisaoPublicada { get; init; }
    public DateOnly? FimVigencia { get; init; }
    public DateTimeOffset CriadoEm { get; init; }
    public DateTimeOffset AtualizadoEm { get; init; }
    public Dictionary<string, string> Atributos { get; init; } = [];
    public List<ResponsavelEmbutido> Responsaveis { get; init; } = [];
    public List<EvidenciaEmbutida> Evidencias { get; init; } = [];
}

internal sealed record ResponsavelEmbutido(Guid IdPessoa, string Papel, DateOnly InicioVigencia, DateOnly? FimVigencia);

internal sealed record EvidenciaEmbutida(Guid Id, string Tipo, string Titulo, string? Url);

internal sealed class DocumentoDeValidacao : DocumentoDoCatalogo
{
    public override string Tipo => TiposDeDocumento.Validacao;

    public int Revisao { get; init; }
    public required string Etapa { get; init; }
    public Guid IdSubmissor { get; init; }
    public required string Status { get; init; }
    public Guid? IdDecisor { get; init; }
    public string? Motivo { get; init; }
}

/// <summary>
/// Uma aresta que SAI do ativo da partição. Documento próprio, e não array no
/// ativo: o agregado não é dono das relações, e regravá-lo apagaria arestas
/// gravadas por outro caso de uso (ADR-0016 §2).
/// </summary>
internal sealed class DocumentoDeRelacao : DocumentoDoCatalogo
{
    public override string Tipo => TiposDeDocumento.Relacao;

    public required string Destino { get; init; }
    public required string TipoRelacao { get; init; }
    public DateTimeOffset CriadoEm { get; init; }
}

/// <summary>
/// Evento de domínio esperando despacho: a outbox do ADR-0008, na mesma
/// partição do ativo, gravada no mesmo lote. O despachante lê pelo change feed.
/// </summary>
internal sealed class DocumentoDeEvento : DocumentoDoCatalogo
{
    public override string Tipo => TiposDeDocumento.Evento;

    public required string TipoEvento { get; init; }
    public required string Payload { get; init; }
    public required string ChaveDeIdempotencia { get; init; }
    public DateTimeOffset OcorridoEm { get; init; }
    public DateTimeOffset? DespachadoEm { get; init; }
}

/// <summary>Sequencial do código por (empresa, tipo). Container <c>contadores</c>, partição <c>/empresa</c>.</summary>
internal sealed class DocumentoDeContador
{
    public required string Id { get; init; }
    public required string Empresa { get; init; }
    public int Valor { get; init; }
}
