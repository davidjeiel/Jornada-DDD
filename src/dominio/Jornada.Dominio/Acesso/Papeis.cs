using Jornada.Dominio.Comum;
using Jornada.Dominio.Tenancy;

namespace Jornada.Dominio.Acesso;

/// <summary>Papéis. Portado de <c>acesso.PAPEIS</c>.</summary>
public enum Papel { Admin, Curador, Arquiteto, TechLead, Negocio, Consulta }

public static class PapelExtensoes
{
    /// <summary>A chave na API e no banco: o nome em minúsculas (<c>techlead</c>).</summary>
    public static string Chave(this Papel papel) => papel.ToString().ToLowerInvariant();

    /// <summary>O nome que a tela mostra. De <c>acesso.PAPEIS</c>.</summary>
    public static string Rotulo(this Papel papel) => papel switch
    {
        Papel.Admin => "Administrador do catálogo",
        Papel.Curador => "Curador (cadastra e submete)",
        Papel.Arquiteto => "Arquiteto",
        Papel.TechLead => "Time técnico",
        Papel.Negocio => "Negócio",
        _ => "Somente consulta",
    };

    /// <summary>Aceita a chave (<c>techlead</c>), o nome do enum e o <c>tech_lead</c> do painel.</summary>
    public static bool TentarDe(string? texto, out Papel papel) =>
        Enum.TryParse((texto ?? "").Replace("_", ""), ignoreCase: true, out papel)
        && Enum.IsDefined(papel);
}

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

/// <summary>
/// Uma concessão de papel com escopo. Corresponde a <c>atribuicao_papel</c>.
/// <see cref="ConcedidoPor"/> é quem decidiu o pleito; nulo na carga e nos testes.
/// </summary>
public sealed record AtribuicaoDePapel(
    Papel Papel,
    TipoDeEscopo EscopoTipo,
    Guid? EscopoId,
    DateOnly InicioVigencia,
    DateOnly? FimVigencia = null,
    Guid? ConcedidoPor = null)
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
                "Matrícula inválida: uma letra seguida de seis números, como C123456.");
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
///
/// <see cref="ModoLeitura"/> é o <c>session["modo"]</c> do painel: a pessoa
/// escolheu só consultar, e nenhuma ação de escrita passa, nem para admin.
/// </summary>
public sealed record Ator(
    Guid IdPessoa,
    IdDeEmpresa Empresa,
    IReadOnlyList<AtribuicaoDePapel> Atribuicoes,
    Matricula? Matricula = null,
    IdDeUnidade? Unidade = null,
    bool ModoLeitura = false)
{
    /// <summary>Sem pessoa, tudo é leitura. De <c>pessoa_atual() is None</c>.</summary>
    public bool Identificado => IdPessoa != Guid.Empty;

    /// <summary>Quem ainda não disse quem é: consulta o catálogo e pode se cadastrar.</summary>
    public static Ator Anonimo(IdDeEmpresa empresa, bool modoLeitura = false) =>
        new(Guid.Empty, empresa, [], ModoLeitura: modoLeitura);
}
