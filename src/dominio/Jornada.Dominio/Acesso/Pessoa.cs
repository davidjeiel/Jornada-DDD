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

    /// <summary>O time da pessoa (<c>pessoa.id_squad</c>). Nulo: sem squad.</summary>
    public Guid? IdSquad { get; private set; }

    public IReadOnlyList<AtribuicaoDePapel> Atribuicoes => _atribuicoes;

    private Pessoa(Guid id, IdDeEmpresa empresa, DadosDeCadastro dados, bool ativa,
                   DateTimeOffset cadastradaEm, IEnumerable<AtribuicaoDePapel> atribuicoes, Guid? idSquad = null)
    {
        IdSquad = idSquad;
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

    /// <summary>
    /// Correção do cadastro pela gestão de acessos (<c>atualizar_pessoa</c>). A
    /// matrícula não muda: é ela que amarra a pessoa à trilha.
    /// </summary>
    public void Corrigir(string? nome, string? email, string? unidade, Guid? idSquad)
    {
        var dados = DadosDeCadastro.Conferir(Matricula.Valor, nome, email, unidade);
        Nome = dados.Nome;
        Email = dados.Email;
        Unidade = dados.Unidade;
        IdSquad = idSquad;
    }

    /// <summary>
    /// Encerra a atribuição. A vigência termina ONTEM: revogado hoje, não vale
    /// mais hoje. (No painel, <c>fim_vigencia</c> = hoje e o papel ainda vale até
    /// a meia-noite.) A atribuição fica no histórico, encerrada.
    /// </summary>
    public bool Revogar(Papel papel, TipoDeEscopo escopoTipo, Guid? escopoId, DateOnly hoje)
    {
        var revogou = false;
        for (var i = 0; i < _atribuicoes.Count; i++)
        {
            var a = _atribuicoes[i];
            if (a.Vigente(hoje) && a.Papel == papel && a.EscopoTipo == escopoTipo && a.EscopoId == escopoId)
            {
                _atribuicoes[i] = a with { FimVigencia = hoje.AddDays(-1) };
                revogou = true;
            }
        }
        return revogou;
    }

    /// <summary>Inativa o cadastro e encerra todos os papéis (<c>inativar_pessoa</c>).</summary>
    public void Inativar(DateOnly hoje)
    {
        foreach (var a in Vigentes(hoje).ToArray())
            Revogar(a.Papel, a.EscopoTipo, a.EscopoId, hoje);
        Ativa = false;
    }

    /// <summary>Reativa o cadastro, sem devolver papel nenhum: o papel volta por pleito ou concessão.</summary>
    public void Reativar() => Ativa = true;

    public IEnumerable<AtribuicaoDePapel> Vigentes(DateOnly hoje) => _atribuicoes.Where(a => a.Vigente(hoje));

    /// <summary>A pessoa como ator de uma requisição. Inativa age como anônima: não tem papel nenhum.</summary>
    public Ator ComoAtor(DateOnly hoje, IdDeUnidade? unidade = null, bool modoLeitura = false) =>
        new(Id, Empresa, Ativa ? [.. Vigentes(hoje)] : [], Matricula, unidade, modoLeitura);

    /// <summary>Reidratação pela persistência. Não usar em regra de negócio.</summary>
    public static Pessoa Reidratar(Guid id, IdDeEmpresa empresa, DadosDeCadastro dados, bool ativa,
                                   DateTimeOffset cadastradaEm, IEnumerable<AtribuicaoDePapel> atribuicoes,
                                   Guid? idSquad = null) =>
        new(id, empresa, dados, ativa, cadastradaEm, atribuicoes, idSquad);
}
