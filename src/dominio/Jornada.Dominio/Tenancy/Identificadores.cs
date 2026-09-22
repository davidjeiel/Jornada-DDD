namespace Jornada.Dominio.Tenancy;

/// <summary>
/// Empresa cliente. É a FRONTEIRA DE ISOLAMENTO (ADR-0003): define RLS, backup,
/// criptografia, residência de dados, plano, fatura e SLA. Nunca é atravessada.
///
/// É um tipo nominal de propósito. Com <c>Guid</c> cru, passar um id de ativo
/// onde se espera um id de empresa compila; aqui, o compilador recusa. Num
/// produto cujo principal risco é confundir identificadores entre clientes,
/// isso não é preciosismo — é a defesa mais barata que existe (ADR-0002).
/// </summary>
public readonly record struct IdDeEmpresa(Guid Valor)
{
    public static IdDeEmpresa Novo() => new(Guid.CreateVersion7());
    public static IdDeEmpresa De(string texto) => new(Guid.Parse(texto));
    public override string ToString() => Valor.ToString();
}

/// <summary>
/// Divisão interna de uma empresa. É ESCOPO DE AUTORIZAÇÃO, não fronteira de
/// isolamento (ADR-0003): as unidades de uma mesma empresa querem, e devem,
/// enxergar o catálogo umas das outras.
///
/// Hierárquica (holding → diretoria → filial), pelo mesmo mecanismo de
/// <c>id_pai</c> que o catálogo já usa em <c>item_catalogo</c>.
/// </summary>
public readonly record struct IdDeUnidade(Guid Valor)
{
    public static IdDeUnidade Novo() => new(Guid.CreateVersion7());
    public override string ToString() => Valor.ToString();
}

/// <summary>
/// Código de quatro dígitos da unidade organizacional. Vem do
/// <c>pessoa.unidade</c> do painel-ddd, onde a regra <c>^[0-9]{4}$</c> estava
/// solta em <c>acesso.validar_cadastro</c>. Aqui ela mora no tipo: não existe
/// instância inválida.
/// </summary>
public readonly record struct CodigoDeUnidade
{
    public string Valor { get; }

    private CodigoDeUnidade(string valor) => Valor = valor;

    public static CodigoDeUnidade De(string? entrada)
    {
        var limpo = (entrada ?? string.Empty).Trim();
        if (limpo.Length != 4 || !limpo.All(char.IsAsciiDigit))
            throw new Comum.ErroDeDominio(
                "UNIDADE_INVALIDA", "Unidade deve ter exatamente 4 dígitos.");
        return new CodigoDeUnidade(limpo);
    }

    public static bool TentarDe(string? entrada, out CodigoDeUnidade unidade)
    {
        try { unidade = De(entrada); return true; }
        catch (Comum.ErroDeDominio) { unidade = default; return false; }
    }

    public override string ToString() => Valor;
}
