namespace Jornada.Dominio.Governanca;

/// <summary>
/// A matriz mínima de aprovação por tipo de mudança. Portada de
/// <c>governanca.MATRIZ_MUDANCA</c>: para cada tipo de mudança, se cada etapa
/// é obrigatória, condicional, opcional ou não se aplica.
///
/// No painel, como aqui, a matriz é de referência: a página de políticas a
/// mostra, e o rito aplicado continua sendo o da política (tipo × criticidade).
/// </summary>
public static class MatrizDeMudanca
{
    public const string Obrigatoria = "obrigatoria";
    public const string Condicional = "condicional";
    public const string Opcional = "opcional";
    public const string NaoSeAplica = "nao";

    public sealed record Linha(string Mudanca, string Rotulo, string Negocial, string Tecnica, string Arquitetural);

    public static readonly IReadOnlyList<Linha> Linhas =
    [
        new("texto", "Texto (nome, descrição)", Opcional, Opcional, NaoSeAplica),
        new("escopo", "Escopo", Obrigatoria, Obrigatoria, Condicional),
        new("contrato", "Contrato", Condicional, Obrigatoria, Obrigatoria),
        new("dependencia", "Dependência", Condicional, Obrigatoria, Obrigatoria),
        new("descontinuacao", "Descontinuação", Obrigatoria, Obrigatoria, Condicional),
    ];
}
