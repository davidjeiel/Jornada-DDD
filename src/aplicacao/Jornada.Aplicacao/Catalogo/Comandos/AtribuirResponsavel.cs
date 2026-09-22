using Jornada.Aplicacao.Portas;
using Jornada.Dominio.Acesso;
using Jornada.Dominio.Catalogo;
using Jornada.Dominio.Comum;

namespace Jornada.Aplicacao.Catalogo.Comandos;

public sealed record ComandoAtribuirResponsavel(
    IdDeAtivo Id, Guid IdPessoa, PapelDeOwnership Papel);

/// <summary>
/// Define quem responde pelo ativo. Equivale a <c>servicos.definir_responsavel</c>.
///
/// Fecha o ciclo da fatia vertical: sem owner, o pré-check bloqueia a submissão —
/// e é justamente isso que se quer provar de ponta a ponta.
/// </summary>
public sealed class AtribuirResponsavel(
    IRepositorioDeAtivos ativos,
    IMetamodelo metamodelo,
    IContextoDeTenantAtual tenant,
    IAtorAtual atorAtual,
    IUnidadeDeTrabalho uow,
    IRelogio relogio)
{
    public async Task<Resultado<bool>> ExecutarAsync(
        ComandoAtribuirResponsavel cmd, CancellationToken ct = default)
    {
        var ativo = await ativos.ObterAsync(cmd.Id, ct);
        if (ativo is null)
            return Resultado<bool>.Recusado("ATIVO_NAO_ENCONTRADO", "Ativo não encontrado.");

        var tipo = (await metamodelo.ObterAsync(ct)).Obter(ativo.TipoItem);
        var ator = await atorAtual.ObterAsync(ct);
        var alvo = new AlvoDaAcao(tenant.Atual.Empresa, tipo.Chave, tipo.Bloco,
                                  Unidade: tenant.Atual.Unidade);

        if (!PoliticaDeAutorizacao.Pode(ator, Acao.Editar, alvo, relogio.Hoje))
            return Resultado<bool>.Recusado("SEM_PERMISSAO",
                "Sem permissão para editar este ativo.");

        ativo.AtribuirResponsavel(cmd.IdPessoa, cmd.Papel, relogio);
        await uow.ConfirmarAsync(ct);
        return Resultado<bool>.Ok(true);
    }
}
