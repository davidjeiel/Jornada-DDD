namespace Jornada.Dominio.Comum;

/// <summary>
/// Regra de negócio violada. Substitui a <c>RegraDeNegocio</c> do
/// <c>servicos.py</c>, que hoje vira um 422 genérico com <c>{"erro": "..."}</c>.
///
/// Aqui a exceção carrega um <see cref="Codigo"/> estável, que o adaptador HTTP
/// traduz em Problem Details (RFC 9457) — e que o cliente da API pode tratar
/// programaticamente em vez de casar strings (ADR-0009).
/// </summary>
public class ErroDeDominio(string codigo, string mensagem) : Exception(mensagem)
{
    public string Codigo { get; } = codigo;
}

/// <summary>Ação recusada por falta de papel: 403, não 422.</summary>
public sealed class SemPermissao(string mensagem)
    : ErroDeDominio("SEM_PERMISSAO", mensagem);

/// <summary>Transição de ciclo de vida inválida.</summary>
public sealed class TransicaoInvalida(string mensagem)
    : ErroDeDominio("TRANSICAO_INVALIDA", mensagem);
