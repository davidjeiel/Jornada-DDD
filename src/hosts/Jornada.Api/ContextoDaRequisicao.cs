using Jornada.Api.Contrato;
using Jornada.Aplicacao.Portas;
using Jornada.Dominio.Acesso;
using Jornada.Dominio.Comum;
using Jornada.Dominio.Tenancy;

namespace Jornada.Api;

/// <summary>
/// Contexto de tenant resolvido POR REQUISIÇÃO.
///
/// Em produção a empresa sai do claim <c>tid_empresa</c> do token e é validada
/// contra o control plane; aqui, em desenvolvimento, sai do cabeçalho
/// <c>X-Empresa</c> (ADR-0003, ADR-0007).
///
/// O ponto que não muda entre os dois: NENHUM caso de uso recebe a empresa de
/// quem chama. Ela é resolvida aqui, uma vez, e desce pela porta.
/// </summary>
public sealed class ContextoDaRequisicao : IContextoDeTenantAtual
{
    private ContextoDeTenant? _contexto;

    public ContextoDeTenant Atual => _contexto
        ?? throw new InvalidOperationException(
            "Contexto de tenant não resolvido — o middleware não rodou.");

    /// <summary>Quem a requisição diz ser (<c>X-Pessoa</c>). Até a identidade corporativa (ADR-0007), é o "quem é você" do perfil.</summary>
    public Guid? IdPessoa { get; private set; }

    /// <summary><c>X-Modo: leitura</c>: a pessoa escolheu só consultar.</summary>
    public bool ModoLeitura { get; private set; }

    /// <summary><c>X-Papel</c>. Só vale com <c>Acesso:ModoDemonstracao</c> (ADR-0018).</summary>
    public Papel? PapelDeDemonstracao { get; private set; }

    /// <summary>Ator pronto, para quem age sem requisição (a carga de demonstração).</summary>
    public Ator? AtorDefinido { get; private set; }

    public void Identificar(ContextoDeTenant contexto, Guid? idPessoa, bool modoLeitura, Papel? papelDeDemonstracao)
    {
        _contexto = contexto;
        IdPessoa = idPessoa;
        ModoLeitura = modoLeitura;
        PapelDeDemonstracao = papelDeDemonstracao;
    }

    public void Definir(ContextoDeTenant contexto, Ator ator)
    {
        _contexto = contexto;
        AtorDefinido = ator;
    }
}

/// <summary>O que a plataforma decide sobre identidade. <c>Acesso:*</c> na configuração.</summary>
public sealed class OpcoesDeAcesso
{
    /// <summary>
    /// Liga a escolha livre de papel pelo cabeçalho <c>X-Papel</c>, para testes
    /// e apresentações. Só no ambiente local (local/compose.yaml); fora dele, o
    /// papel é sempre o concedido.
    /// </summary>
    public bool ModoDemonstracao { get; set; }

    /// <summary>Matrículas que viram admin ao pleitear admin numa empresa ainda sem admin (ADR-0018).</summary>
    public string[] AdministradoresIniciais { get; set; } = [];
}

/// <summary>
/// O ator da requisição, resolvido na primeira vez que um caso de uso pergunta:
/// a pessoa do <c>X-Pessoa</c> com os papéis CONCEDIDOS a ela, lidos a cada
/// requisição. Revogação vale na próxima chamada, não no vencimento de um
/// token (ADR-0007 §2). Sem pessoa, o ator é anônimo: consulta e nada mais.
/// </summary>
public sealed class AtorDaRequisicao(
    ContextoDaRequisicao contexto, IRepositorioDePessoas pessoas, OpcoesDeAcesso opcoes, IRelogio relogio) : IAtorAtual
{
    /// <summary>Quem o <c>X-Papel</c> sem <c>X-Pessoa</c> representa no modo demonstração.</summary>
    public static readonly Guid PessoaDeDemonstracao = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private Ator? _ator;

    public async Task<Ator> ObterAsync(CancellationToken ct = default)
    {
        if (contexto.AtorDefinido is { } definido) return definido;
        if (_ator is not null) return _ator;

        var tenant = contexto.Atual;
        if (opcoes.ModoDemonstracao && contexto.PapelDeDemonstracao is { } papel)
        {
            return _ator = new Ator(contexto.IdPessoa ?? PessoaDeDemonstracao, tenant.Empresa,
                [AtribuicaoDePapel.NaEmpresa(papel, relogio.Hoje.AddYears(-1))],
                Unidade: tenant.Unidade, ModoLeitura: contexto.ModoLeitura);
        }

        var pessoa = contexto.IdPessoa is { } id ? await pessoas.ObterAsync(id, ct) : null;
        return _ator = pessoa?.ComoAtor(relogio.Hoje, tenant.Unidade, contexto.ModoLeitura)
                       ?? Ator.Anonimo(tenant.Empresa, contexto.ModoLeitura);
    }
}

public sealed class MiddlewareDeTenant(RequestDelegate proximo)
{
    public async Task InvokeAsync(HttpContext http, ContextoDaRequisicao contexto)
    {
        // Rotas abertas não precisam de contexto. /saude* inclui a probe de
        // prontidão: um orquestrador de containers chama isso sem X-Empresa.
        // /swagger é o contrato: o API Management e o Spectral o leem sem empresa.
        var caminho = http.Request.Path.Value ?? "";
        if (caminho is "/" || caminho.StartsWith("/saude", StringComparison.Ordinal)
                          || caminho.StartsWith("/demo", StringComparison.Ordinal)
                          || caminho.StartsWith("/swagger", StringComparison.Ordinal))
        {
            await proximo(http);
            return;
        }

        if (!http.Request.Headers.TryGetValue("X-Empresa", out var empresaBruta)
            || !Guid.TryParse(empresaBruta.ToString(), out var idEmpresa))
        {
            await RespostaDeErro.EscreverAsync(http, CodigosDeErro.EmpresaNaoIdentificada,
                "Requisição sem empresa identificada. Em produção a empresa vem do claim " +
                "'tid_empresa' do token; neste ambiente de desenvolvimento, informe o cabeçalho X-Empresa.");
            return;
        }

        var empresa = new IdDeEmpresa(idEmpresa);

        // Papéis NÃO vêm no token nem no cabeçalho (ADR-0007 §2, ADR-0018): são
        // os concedidos à pessoa, lidos por AtorDaRequisicao. X-Papel só vale no
        // modo demonstração, e é o AtorDaRequisicao que decide se vale.
        Papel? papel = http.Request.Headers.TryGetValue("X-Papel", out var papelBruto)
                       && PapelExtensoes.TentarDe(papelBruto.ToString(), out var p)
            ? p
            : null;

        Guid? idPessoa = http.Request.Headers.TryGetValue("X-Pessoa", out var pessoaBruta)
                         && Guid.TryParse(pessoaBruta.ToString(), out var gp)
            ? gp
            : null;

        var modoLeitura = string.Equals(http.Request.Headers["X-Modo"].ToString(), "leitura", StringComparison.OrdinalIgnoreCase);

        // Unidade é ESCOPO de autorização, não fronteira de isolamento
        // (ADR-0003 §8) — por isso é opcional e só chega por cabeçalho
        // separado, nunca embutida no id de empresa.
        IdDeUnidade? unidade = http.Request.Headers.TryGetValue("X-Unidade", out var unidadeBruta)
                               && Guid.TryParse(unidadeBruta.ToString(), out var gu)
            ? new IdDeUnidade(gu)
            : null;

        contexto.Identificar(new ContextoDeTenant(empresa, unidade), idPessoa, modoLeitura, papel);
        await proximo(http);
    }
}
