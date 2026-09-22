using Jornada.Dominio.Comum;

namespace Jornada.Dominio.Catalogo;

public readonly record struct IdDeAtivo(Guid Valor)
{
    public static IdDeAtivo Novo() => new(Guid.CreateVersion7());
    public override string ToString() => Valor.ToString();
}

/// <summary>
/// Código legível do ativo (DOM-0001, API-0042). Único POR EMPRESA — no schema
/// SQLite atual é <c>UNIQUE</c> global, o que quebraria assim que duas empresas
/// usassem o mesmo código (ADR-0003).
/// </summary>
public readonly record struct CodigoDeAtivo
{
    public string Valor { get; }
    private CodigoDeAtivo(string valor) => Valor = valor;

    public static CodigoDeAtivo De(string? entrada)
    {
        var limpo = (entrada ?? string.Empty).Trim().ToUpperInvariant();
        if (limpo.Length is < 3 or > 24)
            throw new ErroDeDominio("CODIGO_INVALIDO",
                "Código do ativo deve ter entre 3 e 24 caracteres.");
        return new CodigoDeAtivo(limpo);
    }

    public static CodigoDeAtivo Gerar(string prefixo, int sequencial) =>
        De($"{prefixo}-{sequencial:D4}");

    public override string ToString() => Valor;
}

/// <summary>Criticidade do ativo. Seleciona a política de governança aplicável.</summary>
public enum Criticidade { Baixa, Media, Alta, Critica }

/// <summary>Como o ativo entrou no catálogo — cadastro manual ou descoberta automática.</summary>
public enum OrigemDoAtivo { Manual, Automatica }

public static class CriticidadeExtensoes
{
    /// <summary>Chave usada na política e na persistência, igual ao painel-ddd.</summary>
    public static string Chave(this Criticidade c) => c switch
    {
        Criticidade.Baixa => "baixa",
        Criticidade.Media => "media",
        Criticidade.Alta => "alta",
        Criticidade.Critica => "critica",
        _ => "media",
    };

    public static Criticidade De(string? chave) => (chave ?? "").Trim().ToLowerInvariant() switch
    {
        "baixa" => Criticidade.Baixa,
        "alta" => Criticidade.Alta,
        "critica" => Criticidade.Critica,
        _ => Criticidade.Media,
    };
}
