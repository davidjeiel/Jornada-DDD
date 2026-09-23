using Jornada.Aplicacao.Portas;
using Jornada.Dominio.Acesso;
using Jornada.Dominio.Catalogo;
using Jornada.Dominio.Comum;

namespace Jornada.Aplicacao.Catalogo.Comandos;

public sealed record ComandoAnexarEvidencia(IdDeAtivo Id, string Tipo, string Titulo, string? Url);

/// <summary>
/// Anexa evidência (ADR, OpenAPI, repositório, documento, link) — o
/// <c>CalculadoraDeQualidade</c> já pontua a dimensão "evidência" desde a
/// primeira versão; só faltava um jeito de gravar uma.
/// </summary>
public sealed class AnexarEvidencia(
    IRepositorioDeAtivos ativos,
    IMetamodelo metamodelo,
    IContextoDeTenantAtual tenant,
    IAtorAtual atorAtual,
    IUnidadeDeTrabalho uow,
    IRelogio relogio)
{
    public async Task<Resultado<bool>> ExecutarAsync(ComandoAnexarEvidencia cmd, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(cmd.Titulo))
            return Resultado<bool>.Recusado("TITULO_OBRIGATORIO", "Título da evidência é obrigatório.");

        var ativo = await ativos.ObterAsync(cmd.Id, ct);
        if (ativo is null)
            return Resultado<bool>.Recusado("ATIVO_NAO_ENCONTRADO", "Ativo não encontrado.");

        var tipo = (await metamodelo.ObterAsync(ct)).Obter(ativo.TipoItem);
        var ator = await atorAtual.ObterAsync(ct);
        var alvo = new AlvoDaAcao(tenant.Atual.Empresa, tipo.Chave, tipo.Bloco, Unidade: tenant.Atual.Unidade);

        if (!PoliticaDeAutorizacao.Pode(ator, Acao.Editar, alvo, relogio.Hoje))
            return Resultado<bool>.Recusado("SEM_PERMISSAO", "Sem permissão para editar este ativo.");

        ativo.AnexarEvidencia(cmd.Tipo, cmd.Titulo, cmd.Url, relogio);
        await uow.ConfirmarAsync(ct);
        return Resultado<bool>.Ok(true);
    }
}
