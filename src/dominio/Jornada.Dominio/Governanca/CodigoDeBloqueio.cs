namespace Jornada.Dominio.Governanca;

/// <summary>
/// Motivos que impedem a publicação.
///
/// No painel-ddd isto já existe como lista de strings (<c>codigos</c>), e é o
/// que permite ao checklist de <c>caminho_publicacao</c> agrupar impedimentos
/// por etapa sem reler frases. Aqui vira enum — e passa a fazer parte do
/// CONTRATO PÚBLICO da API (ADR-0009), para que o cliente automatize a correção
/// em vez de casar mensagens de erro.
/// </summary>
public enum CodigoDeBloqueio
{
    Campo,
    Descricao,
    Hierarquia,
    Owner,
    Duplicidade,
    Ciclo,
    Evidencia,
    Score,
}

public static class CodigoDeBloqueioExtensoes
{
    /// <summary>Chave estável exposta na API. Não mude sem nova versão maior.</summary>
    public static string Chave(this CodigoDeBloqueio c) => c switch
    {
        CodigoDeBloqueio.Campo => "CAMPO_OBRIGATORIO_VAZIO",
        CodigoDeBloqueio.Descricao => "DESCRICAO_OBRIGATORIA",
        CodigoDeBloqueio.Hierarquia => "HIERARQUIA_INCOMPLETA",
        CodigoDeBloqueio.Owner => "OWNER_NAO_DEFINIDO",
        CodigoDeBloqueio.Duplicidade => "NOME_DUPLICADO",
        CodigoDeBloqueio.Ciclo => "DEPENDENCIA_CIRCULAR",
        CodigoDeBloqueio.Evidencia => "EVIDENCIA_INSUFICIENTE",
        CodigoDeBloqueio.Score => "SCORE_ABAIXO_DO_MINIMO",
        _ => c.ToString().ToUpperInvariant(),
    };
}

/// <summary>Um impedimento: o código (para máquina) e o texto (para gente).</summary>
public sealed record Bloqueio(CodigoDeBloqueio Codigo, string Mensagem)
{
    public override string ToString() => $"{Codigo.Chave()}: {Mensagem}";
}
