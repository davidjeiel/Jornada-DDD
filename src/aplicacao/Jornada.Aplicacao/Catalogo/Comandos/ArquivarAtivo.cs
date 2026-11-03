using Jornada.Aplicacao.Acesso;
using Jornada.Aplicacao.Portas;
using Jornada.Dominio.Acesso;
using Jornada.Dominio.Catalogo;
using Jornada.Dominio.Comum;

namespace Jornada.Aplicacao.Catalogo.Comandos;

public sealed record ComandoArquivarAtivo(IdDeAtivo Id);

/// <summary>
/// Estado terminal: o ativo sai das listagens de trabalho e não volta. Vale
/// para um rascunho que não vai adiante e para um ativo já descontinuado;
/// publicado precisa ser descontinuado antes, com a análise de impacto.
/// Equivale a <c>servicos.alterar_status(..., "arquivado")</c>.
///
/// Mesma permissão de descontinuar: tirar do catálogo é uma decisão só.
/// </summary>
public sealed class ArquivarAtivo(
    IRepositorioDeAtivos ativos,
    IMetamodelo metamodelo,
    IContextoDeTenantAtual tenant,
    IAtorAtual atorAtual,
    IUnidadeDeTrabalho uow,
    ITrilhaDeAuditoria trilha,
    IRelogio relogio)
{
    public async Task<Resultado<bool>> ExecutarAsync(ComandoArquivarAtivo cmd, CancellationToken ct = default)
    {
        var ativo = await ativos.ObterAsync(cmd.Id, ct);
        if (ativo is null)
            return Resultado<bool>.Recusado("ATIVO_NAO_ENCONTRADO", "Ativo não encontrado.");

        var tipo = (await metamodelo.ObterAsync(ct)).Obter(ativo.TipoItem);
        var ator = await atorAtual.ObterAsync(ct);
        var alvo = await AlvoDoAtivo.ResolverAsync(ativos, tenant.Atual, ativo, tipo, ct);

        if (!PoliticaDeAutorizacao.Pode(ator, Acao.Descontinuar, alvo, relogio.Hoje))
            return Recusa.Para<bool>(ator, "Sem permissão para arquivar este ativo.");

        if (!MaquinaDeCicloDeVida.Permite(ativo.Status, StatusCicloVida.Arquivado))
        {
            return Resultado<bool>.Recusado("TRANSICAO_INVALIDA",
                $"Só rascunho ou ativo descontinuado pode ser arquivado; este está em '{MaquinaDeCicloDeVida.Rotulo(ativo.Status)}'.");
        }

        var anterior = MaquinaDeCicloDeVida.Rotulo(ativo.Status);
        ativo.Arquivar(relogio);
        await trilha.RegistrarAsync(RegistroDeAuditoria.De(ativo, AcoesAuditadas.Arquivar, ator.IdPessoa, relogio,
            new Dictionary<string, string> { ["status"] = anterior },
            new Dictionary<string, string> { ["status"] = "arquivado" }), ct);
        await uow.ConfirmarAsync(ct);
        return Resultado<bool>.Ok(true);
    }
}
