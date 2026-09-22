using Jornada.Aplicacao.Catalogo.Consultas;
using Jornada.Aplicacao.Portas;
using Jornada.Dominio.Acesso;
using Jornada.Dominio.Catalogo;
using Jornada.Dominio.Comum;
using Jornada.Dominio.Governanca;

namespace Jornada.Aplicacao.Catalogo.Comandos;

public sealed record ComandoSubmeterAtivo(IdDeAtivo Id, string Motivo = "");

/// <summary>
/// Submete o ativo para validação. Equivale a <c>servicos.submeter</c>.
///
/// Três coisas acontecem na MESMA transação — gravar a revisão, mudar o estado e
/// enfileirar a notificação. É por isso que Catálogo e Governança nunca devem
/// ser separados em serviços distintos: viraria uma saga com compensação para
/// resolver um problema que não existe (ADR-0006).
/// </summary>
public sealed class SubmeterAtivo(
    IRepositorioDeAtivos ativos,
    AvaliarPreCheck preCheck,
    IMetamodelo metamodelo,
    IContextoDeTenantAtual tenant,
    IAtorAtual atorAtual,
    IUnidadeDeTrabalho uow,
    IRepositorioDeValidacoes validacoes,
    IRelogio relogio)
{
    public sealed record Saida(int Revisao, IReadOnlyList<EtapaValidacao> EtapasAbertas, int Score);

    public async Task<Resultado<Saida>> ExecutarAsync(
        ComandoSubmeterAtivo cmd, CancellationToken ct = default)
    {
        var ativo = await ativos.ObterAsync(cmd.Id, ct);
        if (ativo is null)
            return Resultado<Saida>.Recusado("ATIVO_NAO_ENCONTRADO", "Ativo não encontrado.");

        var tipos = await metamodelo.ObterAsync(ct);
        var tipo = tipos.Obter(ativo.TipoItem);
        var ator = await atorAtual.ObterAsync(ct);
        var contexto = tenant.Atual;

        var alvo = new AlvoDaAcao(contexto.Empresa, tipo.Chave, tipo.Bloco,
                                  Unidade: contexto.Unidade);

        if (!PoliticaDeAutorizacao.Pode(ator, Acao.Submeter, alvo, relogio.Hoje))
            return Resultado<Saida>.Recusado("SEM_PERMISSAO",
                "Sem permissão para submeter este ativo.");

        // O ciclo de vida é verificado antes do pré-check: não adianta dizer que
        // faltam evidências num ativo que já está publicado.
        if (!MaquinaDeCicloDeVida.Permite(ativo.Status, StatusCicloVida.EmValidacao))
            return Resultado<Saida>.Recusado("TRANSICAO_INVALIDA",
                $"Ativo em '{MaquinaDeCicloDeVida.Rotulo(ativo.Status)}' não pode ser submetido.");

        var (checagem, score) = await preCheck.AvaliarAsync(ativo, ct);
        if (!checagem.Aprovado)
        {
            return Resultado<Saida>.Recusado("PRECHECK_REPROVADO",
                string.Join(" | ", checagem.Bloqueios.Select(b => b.Mensagem)));
        }

        ativo.Submeter(cmd.Motivo, ator.IdPessoa, relogio);
        foreach (var etapa in checagem.Politica.Etapas)
        {
            await validacoes.AdicionarAsync(new ValidacaoEmProcesso(
                Guid.CreateVersion7(), ativo.Id, contexto.Empresa, ativo.RevisaoAtual,
                etapa, ator.IdPessoa), ct);
        }
        await uow.ConfirmarAsync(ct);

        return Resultado<Saida>.Ok(new Saida(ativo.RevisaoAtual, checagem.Politica.Etapas, score.Total));
    }
}
