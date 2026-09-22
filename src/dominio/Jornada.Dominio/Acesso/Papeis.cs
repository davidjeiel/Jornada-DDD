using Jornada.Dominio.Comum;
using Jornada.Dominio.Tenancy;

namespace Jornada.Dominio.Acesso;

/// <summary>Papéis. Portado de <c>acesso.PAPEIS</c>.</summary>
public enum Papel { Admin, Curador, Arquiteto, TechLead, Negocio, Consulta }

/// <summary>
/// Escopo do papel. Portado de <c>acesso.ESCOPOS</c> com UMA mudança do
/// ADR-0003: o antigo <c>global</c> vira <see cref="Empresa"/> (e "global"
/// passa a significar operador da plataforma, no control plane), e entra
/// <see cref="Unidade"/>.
/// </summary>
public enum TipoDeEscopo { Empresa, Unidade, Dominio, Squad }

/// <summary>Ações autorizáveis. Portado de <c>acesso.PERMISSOES</c> (11 ações).</summary>
public enum Acao
{
    Cadastrar, Editar, Relacionar, Submeter, Revisar,
    Descontinuar, Importar, Triar, Decidir, Conceder, Administrar,
}

/// <summary>Uma concessão de papel com escopo. Corresponde a <c>atribuicao_papel</c>.</summary>
public sealed record AtribuicaoDePapel(
    Papel Papel,
    TipoDeEscopo EscopoTipo,
    Guid? EscopoId,
    DateOnly InicioVigencia,
    DateOnly? FimVigencia = null)
{
    public bool Vigente(DateOnly hoje) =>
        InicioVigencia <= hoje && (FimVigencia is null || FimVigencia >= hoje);

    /// <summary>Papel que vale em toda a empresa.</summary>
    public static AtribuicaoDePapel NaEmpresa(Papel papel, DateOnly desde) =>
        new(papel, TipoDeEscopo.Empresa, null, desde);

    public static AtribuicaoDePapel NoDominio(Papel papel, Guid idDominio, DateOnly desde) =>
        new(papel, TipoDeEscopo.Dominio, idDominio, desde);

    public static AtribuicaoDePapel NaUnidade(Papel papel, Guid idUnidade, DateOnly desde) =>
        new(papel, TipoDeEscopo.Unidade, idUnidade, desde);

    public static AtribuicaoDePapel NaSquad(Papel papel, Guid idSquad, DateOnly desde) =>
        new(papel, TipoDeEscopo.Squad, idSquad, desde);
}

/// <summary>
/// Matrícula funcional. Portado da regex <c>^[A-Za-z][0-9]{6}$</c> que hoje vive
/// solta em <c>acesso.MATRICULA</c>. No tipo, não existe matrícula inválida.
/// </summary>
public readonly record struct Matricula
{
    public string Valor { get; }
    private Matricula(string valor) => Valor = valor;

    public static Matricula De(string? entrada)
    {
        var limpo = (entrada ?? string.Empty).Trim().ToUpperInvariant();
        if (limpo.Length != 7
            || !char.IsAsciiLetter(limpo[0])
            || !limpo[1..].All(char.IsAsciiDigit))
        {
            throw new ErroDeDominio("MATRICULA_INVALIDA",
                "Matrícula deve ser uma letra seguida de 6 dígitos (ex.: C123456).");
        }
        return new Matricula(limpo);
    }

    public static bool TentarDe(string? entrada, out Matricula matricula)
    {
        try { matricula = De(entrada); return true; }
        catch (ErroDeDominio) { matricula = default; return false; }
    }

    public override string ToString() => Valor;
}

/// <summary>
/// Quem está agindo. Carrega a empresa (do token, validada contra o control
/// plane) e as atribuições de papel — que NÃO vêm no token, porque revogação
/// tem de valer em segundos, não no vencimento do JWT (ADR-0007 §2).
/// </summary>
public sealed record Ator(
    Guid IdPessoa,
    IdDeEmpresa Empresa,
    IReadOnlyList<AtribuicaoDePapel> Atribuicoes,
    Matricula? Matricula = null,
    IdDeUnidade? Unidade = null);
