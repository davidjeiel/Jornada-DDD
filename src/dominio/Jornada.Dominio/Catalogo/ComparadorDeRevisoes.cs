using System.Text.Json;

namespace Jornada.Dominio.Catalogo;

/// <summary>Um campo que mudou entre duas revisões, já em texto para pessoas.</summary>
public sealed record DiferencaEntreRevisoes(string Campo, string Rotulo, string Antes, string Depois);

/// <summary>
/// O que mudou de uma revisão para outra. Equivale a
/// <c>servicos.comparar_revisoes</c> + <c>_resumir_valor</c>: compara campo a
/// campo do retrato e resume cada valor numa linha.
///
/// Ids (pai, destino de relação) viram texto pelo <c>nomeDe</c> que quem chama
/// fornece — o domínio não consulta o catálogo.
/// </summary>
public static class ComparadorDeRevisoes
{
    private const string Vazio = "—";

    public static IReadOnlyList<DiferencaEntreRevisoes> Comparar(
        RevisaoDoAtivo antes, RevisaoDoAtivo depois, Func<Guid, string> nomeDe)
    {
        using var a = JsonDocument.Parse(antes.Conteudo);
        using var b = JsonDocument.Parse(depois.Conteudo);

        var campos = Campos(a.RootElement).Union(Campos(b.RootElement)).Order(StringComparer.Ordinal);
        var diferencas = new List<DiferencaEntreRevisoes>();
        foreach (var campo in campos)
        {
            var va = Valor(a.RootElement, campo);
            var vb = Valor(b.RootElement, campo);
            if (va?.GetRawText() == vb?.GetRawText()) continue;

            diferencas.Add(new DiferencaEntreRevisoes(campo, CamposDoRetrato.Rotulo(campo),
                Resumir(campo, va, nomeDe), Resumir(campo, vb, nomeDe)));
        }
        return diferencas;
    }

    /// <summary>Os ids que aparecem no retrato e precisam de nome: o pai e os destinos das relações.</summary>
    public static IReadOnlySet<Guid> IdsCitados(params RevisaoDoAtivo[] revisoes)
    {
        var ids = new HashSet<Guid>();
        foreach (var revisao in revisoes)
        {
            using var doc = JsonDocument.Parse(revisao.Conteudo);
            if (Valor(doc.RootElement, CamposDoRetrato.IdPai) is { ValueKind: JsonValueKind.String } pai)
                ids.Add(pai.GetGuid());
            if (Valor(doc.RootElement, CamposDoRetrato.Relacoes) is { ValueKind: JsonValueKind.Array } relacoes)
                foreach (var r in relacoes.EnumerateArray())
                    ids.Add(r.GetProperty("destino").GetGuid());
        }
        return ids;
    }

    private static IEnumerable<string> Campos(JsonElement raiz) => raiz.EnumerateObject().Select(p => p.Name);

    private static JsonElement? Valor(JsonElement raiz, string campo) =>
        raiz.TryGetProperty(campo, out var v) ? v.Clone() : null;

    private static string Resumir(string campo, JsonElement? valor, Func<Guid, string> nomeDe)
    {
        if (valor is not { } v || v.ValueKind == JsonValueKind.Null) return Vazio;

        var texto = campo switch
        {
            CamposDoRetrato.Atributos => string.Join("; ",
                v.EnumerateObject().Where(p => p.Value.GetString() is { Length: > 0 })
                    .Select(p => $"{p.Name}: {p.Value.GetString()}")),
            CamposDoRetrato.Responsaveis => string.Join("; ",
                v.EnumerateArray().Select(r =>
                    $"{r.GetProperty("papel").GetString()}: {nomeDe(r.GetProperty("idPessoa").GetGuid())}")),
            CamposDoRetrato.Relacoes => string.Join("; ",
                v.EnumerateArray().Select(r =>
                    $"{r.GetProperty("tipo").GetString()} → {nomeDe(r.GetProperty("destino").GetGuid())}" +
                    $" ({r.GetProperty("criticidade").GetString()})")),
            CamposDoRetrato.Evidencias => string.Join("; ",
                v.EnumerateArray().Select(e => $"{e.GetProperty("tipo").GetString()}: {e.GetProperty("titulo").GetString()}")),
            CamposDoRetrato.IdPai => nomeDe(v.GetGuid()),
            _ => v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : v.GetRawText(),
        };
        return string.IsNullOrWhiteSpace(texto) ? Vazio : texto;
    }
}
