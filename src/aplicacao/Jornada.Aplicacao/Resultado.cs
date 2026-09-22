namespace Jornada.Aplicacao;

/// <summary>
/// Resultado de caso de uso. Recusa esperada (limite de plano atingido,
/// pré-check reprovado) é VALOR, não exceção — exceção fica para o inesperado.
/// </summary>
public readonly record struct Resultado<T>
{
    public bool Sucesso { get; }
    public T? Valor { get; }
    public string? Codigo { get; }
    public string? Mensagem { get; }

    private Resultado(bool sucesso, T? valor, string? codigo, string? mensagem)
    {
        Sucesso = sucesso; Valor = valor; Codigo = codigo; Mensagem = mensagem;
    }

    public static Resultado<T> Ok(T valor) => new(true, valor, null, null);

    public static Resultado<T> Recusado(string codigo, string mensagem) =>
        new(false, default, codigo, mensagem);
}
