using Jornada.Dominio.Comum;
using Jornada.Dominio.Tenancy;

namespace Jornada.Dominio.Acesso;

/// <summary>
/// Os dados do cadastro, já conferidos e normalizados. Portado de
/// <c>acesso.validar_cadastro</c>.
///
/// Erro de digitação em matrícula é caro depois: é ela que amarra a pessoa à
/// trilha de auditoria, e não há SSO para corrigir a grafia. Por isso a regra
/// é conferida aqui, uma vez, e não em cada tela que grava pessoa.
/// </summary>
public sealed record DadosDeCadastro(Matricula Matricula, string Nome, string Email, CodigoDeUnidade Unidade)
{
    public static DadosDeCadastro Conferir(string? matricula, string? nome, string? email, string? unidade)
    {
        var m = Matricula.De(matricula);
        var n = (nome ?? "").Trim();
        var e = (email ?? "").Trim();

        if (n.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length < 2)
            throw new ErroDeDominio("NOME_INCOMPLETO", "Informe o nome completo.");
        var arroba = e.IndexOf('@');
        if (arroba < 1 || !e[(arroba + 1)..].Contains('.'))
            throw new ErroDeDominio("EMAIL_INVALIDO", "Informe um e-mail válido.");
        if (!CodigoDeUnidade.TentarDe(unidade, out var u))
            throw new ErroDeDominio("UNIDADE_INVALIDA", "Unidade inválida: o código tem quatro números, como 0427.");

        return new DadosDeCadastro(m, n, e, u);
    }
}

/// <summary>
/// Uma pessoa cadastrada na empresa, com os papéis que lhe foram concedidos.
/// Corresponde a <c>pessoa</c> mais <c>atribuicao_papel</c> do painel-ddd.
///
/// O cadastro NÃO concede nada: a pessoa nasce sem papel, isto é, com o
/// catálogo inteiro em modo de consulta. Papel só entra por
/// <see cref="Conceder"/>, que só a decisão de um pleito chama.
/// </summary>
public sealed class Pessoa
{
    private readonly List<AtribuicaoDePapel> _atribuicoes;

    public Guid Id { get; }
    public IdDeEmpresa Empresa { get; }
    public Matricula Matricula { get; private set; }
    public string Nome { get; private set; }
    public string Email { get; private set; }
    public CodigoDeUnidade Unidade { get; private set; }
    public bool Ativa { get; private set; }
    public DateTimeOffset CadastradaEm { get; }

    public IReadOnlyList<AtribuicaoDePapel> Atribuicoes => _atribuicoes;

    private Pessoa(Guid id, IdDeEmpresa empresa, DadosDeCadastro dados, bool ativa,
                   DateTimeOffset cadastradaEm, IEnumerable<AtribuicaoDePapel> atribuicoes)
    {
        Id = id;
        Empresa = empresa;
        Matricula = dados.Matricula;
        Nome = dados.Nome;
        Email = dados.Email;
        Unidade = dados.Unidade;
        Ativa = ativa;
        CadastradaEm = cadastradaEm;
        _atribuicoes = [.. atribuicoes];
    }

    public static Pessoa Cadastrar(IdDeEmpresa empresa, DadosDeCadastro dados, IRelogio relogio, Guid? id = null) =>
        new(id ?? Guid.CreateVersion7(), empresa, dados, ativa: true, relogio.Agora, []);

    /// <summary>
    /// Quem se cadastra de novo com a mesma matrícula atualiza o cadastro, sem
    /// duplicar a pessoa, e volta a ficar ativo. A matrícula não muda.
    /// </summary>
    public void Recadastrar(DadosDeCadastro dados)
    {
        if (dados.Matricula != Matricula)
            throw new ErroDeDominio("MATRICULA_DIVERGENTE", "O recadastro é da mesma matrícula.");
        Nome = dados.Nome;
        Email = dados.Email;
        Unidade = dados.Unidade;
        Ativa = true;
    }

    public void Conceder(AtribuicaoDePapel atribuicao) => _atribuicoes.Add(atribuicao);

    public IEnumerable<AtribuicaoDePapel> Vigentes(DateOnly hoje) => _atribuicoes.Where(a => a.Vigente(hoje));

    /// <summary>A pessoa como ator de uma requisição. Inativa age como anônima: não tem papel nenhum.</summary>
    public Ator ComoAtor(DateOnly hoje, IdDeUnidade? unidade = null, bool modoLeitura = false) =>
        new(Id, Empresa, Ativa ? [.. Vigentes(hoje)] : [], Matricula, unidade, modoLeitura);

    /// <summary>Reidratação pela persistência. Não usar em regra de negócio.</summary>
    public static Pessoa Reidratar(Guid id, IdDeEmpresa empresa, DadosDeCadastro dados, bool ativa,
                                   DateTimeOffset cadastradaEm, IEnumerable<AtribuicaoDePapel> atribuicoes) =>
        new(id, empresa, dados, ativa, cadastradaEm, atribuicoes);
}
