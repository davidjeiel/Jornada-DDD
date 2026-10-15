using Jornada.Api.Contrato;
using Jornada.Aplicacao.Portas;
using Jornada.Dominio.Acesso;
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
public sealed class ContextoDaRequisicao : IContextoDeTenantAtual, IAtorAtual
{
    private ContextoDeTenant? _contexto;
    private Ator? _ator;

    public ContextoDeTenant Atual => _contexto
        ?? throw new InvalidOperationException(
            "Contexto de tenant não resolvido — o middleware não rodou.");

    public void Definir(ContextoDeTenant contexto, Ator ator)
    {
        _contexto = contexto;
        _ator = ator;
    }

    public Task<Ator> ObterAsync(CancellationToken ct = default) =>
        Task.FromResult(_ator ?? throw new InvalidOperationException("Ator não resolvido."));
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

        // Papéis NÃO vêm no token (ADR-0007 §2): revogação tem de valer em
        // segundos, não no vencimento do JWT. Em produção são resolvidos aqui,
        // com cache curto invalidado por evento de concessão/revogação.
        var papel = http.Request.Headers.TryGetValue("X-Papel", out var papelBruto)
                    && Enum.TryParse<Papel>(papelBruto.ToString(), ignoreCase: true, out var p)
            ? p
            : Papel.Curador;

        var idPessoa = http.Request.Headers.TryGetValue("X-Pessoa", out var pessoaBruta)
                       && Guid.TryParse(pessoaBruta.ToString(), out var gp)
            ? gp
            : Guid.Parse("33333333-3333-3333-3333-333333333333");

        // Unidade é ESCOPO de autorização, não fronteira de isolamento
        // (ADR-0003 §8) — por isso é opcional e só chega por cabeçalho
        // separado, nunca embutida no id de empresa.
        IdDeUnidade? unidade = http.Request.Headers.TryGetValue("X-Unidade", out var unidadeBruta)
                               && Guid.TryParse(unidadeBruta.ToString(), out var gu)
            ? new IdDeUnidade(gu)
            : null;

        var ator = new Ator(idPessoa, empresa,
            [AtribuicaoDePapel.NaEmpresa(papel, DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-1)))],
            Unidade: unidade);

        contexto.Definir(new ContextoDeTenant(empresa, unidade), ator);
        await proximo(http);
    }
}
