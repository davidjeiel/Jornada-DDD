using Jornada.Aplicacao.Portas;
using Jornada.Dominio.Acesso;
using Jornada.Dominio.Catalogo;
using Jornada.Dominio.Comum;

namespace Jornada.Aplicacao.Catalogo.Comandos;

public sealed record ComandoAbrirRevisao(IdDeAtivo Id);

/// <summary>
/// Abre revisão de um ativo publicado — o único caminho para alterá-lo.
/// Equivale a <c>servicos.abrir_revisao</c>. Depois de aberta, o ativo aceita
/// edição e volta ao rito pela submissão.
///
/// A autorização é <see cref="Acao.Revisar"/>, no bloco do tipo: quem
/// responde pelo negócio revisa a estrutura DDD; quem responde pela técnica,
/// os ativos técnicos.
/// </summary>
public sealed class AbrirRevisao(
    IRepositorioDeAtivos ativos,
    IMetamodelo metamodelo,
    IContextoDeTenantAtual tenant,
    IAtorAtual atorAtual,
    IUnidadeDeTrabalho uow,
    IRelogio relogio)
{
    public async Task<Resultado<bool>> ExecutarAsync(ComandoAbrirRevisao cmd, CancellationToken ct = default)
    {
        var ativo = await ativos.ObterAsync(cmd.Id, ct);
        if (ativo is null)
            return Resultado<bool>.Recusado("ATIVO_NAO_ENCONTRADO", "Ativo não encontrado.");

        var tipo = (await metamodelo.ObterAsync(ct)).Obter(ativo.TipoItem);
        var ator = await atorAtual.ObterAsync(ct);
        var alvo = new AlvoDaAcao(tenant.Atual.Empresa, tipo.Chave, tipo.Bloco, Unidade: tenant.Atual.Unidade);

        if (!PoliticaDeAutorizacao.Pode(ator, Acao.Revisar, alvo, relogio.Hoje))
            return Resultado<bool>.Recusado("SEM_PERMISSAO", "Sem permissão para revisar este ativo.");

        // Explícito, e não "Permite(status, EmRevisao)": em_validacao também
        // chega a em_revisao, mas só pela rejeição de uma revisão.
        if (ativo.Status != StatusCicloVida.Publicado)
        {
            return Resultado<bool>.Recusado("TRANSICAO_INVALIDA",
                $"Só ativo publicado pode ter revisão aberta; este está em '{MaquinaDeCicloDeVida.Rotulo(ativo.Status)}'.");
        }

        ativo.AbrirRevisao(relogio);
        await uow.ConfirmarAsync(ct);
        return Resultado<bool>.Ok(true);
    }
}
