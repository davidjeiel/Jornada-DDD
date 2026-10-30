using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Jornada.Dominio.Comum;
using Jornada.Dominio.Tenancy;

namespace Jornada.Dominio.Catalogo;

/// <summary>Uma relação de saída, como entra no retrato da revisão.</summary>
public sealed record RelacaoRetratada(IdDeAtivo Destino, string Tipo, Criticidade Criticidade);

/// <summary>
/// O retrato de uma revisão submetida: o cadastro inteiro como estava no
/// momento da submissão, com hash SHA-256. Equivale a <c>revisao_catalogo</c>
/// (<c>payload</c> + <c>payload_hash</c>) e ao <c>servicos._snapshot</c> do
/// painel-ddd. É o que permite dizer, depois, o que foi aprovado (ADR-0017).
///
/// O conteúdo nunca muda. A única coisa que se acrescenta é o momento da
/// publicação, uma vez só. <see cref="Integra"/> recalcula o hash: um
/// conteúdo alterado no banco aparece como revisão adulterada.
/// </summary>
public sealed class RevisaoDoAtivo
{
    public IdDeAtivo Ativo { get; }
    public IdDeEmpresa Empresa { get; }
    public int Numero { get; }

    /// <summary>JSON canônico (chaves em ordem fixa, listas ordenadas): o mesmo cadastro gera sempre o mesmo texto.</summary>
    public string Conteudo { get; }

    /// <summary>SHA-256 do <see cref="Conteudo"/> em UTF-8, em hexadecimal minúsculo.</summary>
    public string Hash { get; }

    public string Motivo { get; }
    public Guid IdAutor { get; }
    public DateTimeOffset CriadaEm { get; }
    public DateTimeOffset? PublicadaEm { get; private set; }

    private RevisaoDoAtivo(IdDeAtivo ativo, IdDeEmpresa empresa, int numero, string conteudo, string hash,
                           string motivo, Guid idAutor, DateTimeOffset criadaEm, DateTimeOffset? publicadaEm)
    {
        Ativo = ativo;
        Empresa = empresa;
        Numero = numero;
        Conteudo = conteudo;
        Hash = hash;
        Motivo = motivo;
        IdAutor = idAutor;
        CriadaEm = criadaEm;
        PublicadaEm = publicadaEm;
    }

    public bool Integra => Hash == CalcularHash(Conteudo);

    /// <summary>
    /// Retrata o ativo na revisão que ele acabou de abrir com
    /// <see cref="Catalogo.Ativo.Submeter"/>. As relações vêm de fora porque o
    /// agregado não é dono delas (ADR-0016 §2).
    /// </summary>
    public static RevisaoDoAtivo Retratar(
        Ativo ativo, IEnumerable<RelacaoRetratada> relacoesDeSaida, string motivo, Guid idAutor, IRelogio relogio)
    {
        if (ativo.Status != StatusCicloVida.EmValidacao)
            throw new ErroDeDominio("RETRATO_FORA_DA_SUBMISSAO",
                "O retrato da revisão é tirado na submissão, com o ativo em validação.");

        var conteudo = Serializar(ativo, relacoesDeSaida, relogio.Hoje);
        return new RevisaoDoAtivo(ativo.Id, ativo.Empresa, ativo.RevisaoAtual, conteudo, CalcularHash(conteudo),
            (motivo ?? "").Trim(), idAutor, relogio.Agora, null);
    }

    public void MarcarPublicada(IRelogio relogio)
    {
        if (PublicadaEm is not null)
            throw new ErroDeDominio("REVISAO_JA_PUBLICADA", $"A revisão {Numero} já foi publicada.");
        PublicadaEm = relogio.Agora;
    }

    /// <summary>Reidratação pela persistência. Não recalcula o hash: é assim que se descobre adulteração.</summary>
    public static RevisaoDoAtivo Reidratar(
        IdDeAtivo ativo, IdDeEmpresa empresa, int numero, string conteudo, string hash,
        string motivo, Guid idAutor, DateTimeOffset criadaEm, DateTimeOffset? publicadaEm) =>
        new(ativo, empresa, numero, conteudo, hash, motivo, idAutor, criadaEm, publicadaEm);

    public static string CalcularHash(string conteudo) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(conteudo)));

    // Ordem fixa das chaves e das listas: o hash só serve se o mesmo cadastro
    // gerar sempre o mesmo texto. O que o próprio ciclo de vida mexe (status,
    // datas, número da revisão) fica de fora, como os CAMPOS_MECANICOS do painel.
    private static string Serializar(Ativo ativo, IEnumerable<RelacaoRetratada> relacoes, DateOnly hoje)
    {
        using var buffer = new MemoryStream();
        // Acentos sem escape: o retrato fica legível no banco, e continua determinístico.
        using (var json = new Utf8JsonWriter(buffer, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            json.WriteStartObject();

            json.WriteStartObject(CamposDoRetrato.Atributos);
            foreach (var (chave, valor) in ativo.Atributos.OrderBy(a => a.Key, StringComparer.Ordinal))
                json.WriteString(chave, valor);
            json.WriteEndObject();

            json.WriteString(CamposDoRetrato.Criticidade, ativo.Criticidade.Chave());
            json.WriteString(CamposDoRetrato.Descricao, ativo.Descricao);

            json.WriteStartArray(CamposDoRetrato.Evidencias);
            foreach (var e in ativo.Evidencias.OrderBy(e => e.Id))
            {
                json.WriteStartObject();
                json.WriteString("tipo", e.Tipo);
                json.WriteString("titulo", e.Titulo);
                json.WriteString("url", e.Url);
                json.WriteEndObject();
            }
            json.WriteEndArray();

            if (ativo.IdPai is { } pai) json.WriteString(CamposDoRetrato.IdPai, pai.Valor);
            else json.WriteNull(CamposDoRetrato.IdPai);

            json.WriteString(CamposDoRetrato.Nome, ativo.Nome);

            json.WriteStartArray(CamposDoRetrato.Relacoes);
            foreach (var r in relacoes.OrderBy(r => r.Tipo, StringComparer.Ordinal).ThenBy(r => r.Destino.Valor))
            {
                json.WriteStartObject();
                json.WriteString("criticidade", r.Criticidade.Chave());
                json.WriteString("destino", r.Destino.Valor);
                json.WriteString("tipo", r.Tipo);
                json.WriteEndObject();
            }
            json.WriteEndArray();

            json.WriteStartArray(CamposDoRetrato.Responsaveis);
            foreach (var r in ativo.Responsaveis.Where(r => r.Vigente(hoje))
                         .OrderBy(r => r.Papel).ThenBy(r => r.IdPessoa))
            {
                json.WriteStartObject();
                json.WriteString("idPessoa", r.IdPessoa);
                json.WriteString("papel", r.Papel.ToString());
                json.WriteEndObject();
            }
            json.WriteEndArray();

            json.WriteString(CamposDoRetrato.TipoItem, ativo.TipoItem);
            json.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }
}

/// <summary>As chaves do retrato, com o rótulo que a comparação mostra (de <c>ROTULOS_DIFF</c>).</summary>
public static class CamposDoRetrato
{
    public const string Nome = "nome";
    public const string Descricao = "descricao";
    public const string Criticidade = "criticidade";
    public const string TipoItem = "tipoItem";
    public const string IdPai = "idPai";
    public const string Atributos = "atributos";
    public const string Responsaveis = "responsaveis";
    public const string Relacoes = "relacoes";
    public const string Evidencias = "evidencias";

    public static string Rotulo(string campo) => campo switch
    {
        Nome => "Nome",
        Descricao => "Descrição",
        Criticidade => "Criticidade",
        TipoItem => "Tipo",
        IdPai => "Item pai",
        Atributos => "Campos do tipo",
        Responsaveis => "Responsáveis",
        Relacoes => "Relações",
        Evidencias => "Evidências",
        _ => campo,
    };
}
