using Jornada.Aplicacao.Portas;
using Jornada.Dominio.Acesso;
using Jornada.Dominio.Catalogo;
using Jornada.Dominio.Comum;
using Jornada.Dominio.Governanca;

namespace Jornada.Aplicacao.Catalogo.Comandos;

public sealed record ComandoDecidirValidacao(Guid IdValidacao, bool Aprovada, string? Motivo = null);

public sealed class DecidirValidacao(
    IRepositorioDeValidacoes validacoes,
    IRepositorioDeAtivos ativos,
    IMetamodelo metamodelo,
    IContextoDeTenantAtual tenant,
    IAtorAtual atorAtual,
    IUnidadeDeTrabalho uow,
    IRelogio relogio)
{
    public async Task<Resultado<bool>> ExecutarAsync(
        ComandoDecidirValidacao cmd, CancellationToken ct = default)
    {
        var validacao = await validacoes.ObterAsync(cmd.IdValidacao, ct);
        if (validacao is null)
            return Resultado<bool>.Recusado("VALIDACAO_NAO_ENCONTRADA", "Validação não encontrada.");
        if (validacao.Status != StatusDaValidacao.Aberta)
            return Resultado<bool>.Recusado("VALIDACAO_JA_DECIDIDA", "Esta validação já foi decidida.");

        var ativo = await ativos.ObterAsync(validacao.Ativo, ct);
        if (ativo is null)
            return Resultado<bool>.Recusado("ATIVO_NAO_ENCONTRADO", "Ativo não encontrado.");

        var tipo = (await metamodelo.ObterAsync(ct)).Obter(ativo.TipoItem);
        var ator = await atorAtual.ObterAsync(ct);
        var contexto = tenant.Atual;
        var alvo = new AlvoDaAcao(contexto.Empresa, tipo.Chave, tipo.Bloco, Unidade: contexto.Unidade);
        var autorizacao = PoliticaDeAutorizacao.PodeDecidir(
            ator, validacao.Etapa, validacao.IdSubmissor, ativo, alvo, relogio.Hoje);
        if (!autorizacao.Permitido)
            return Resultado<bool>.Recusado("SEM_PERMISSAO", autorizacao.Motivo);

        validacao.Decidir(cmd.Aprovada, ator.IdPessoa, cmd.Motivo);
        if (!cmd.Aprovada)
        {
            ativo.DevolverParaRascunho(relogio);
        }
        else
        {
            var doAtivo = await validacoes.ListarAsync(ativo.Id, ct);
            if (doAtivo.All(v => v.Status == StatusDaValidacao.Aprovada))
                ativo.Publicar(relogio);
        }

        await uow.ConfirmarAsync(ct);
        return Resultado<bool>.Ok(true);
    }
}
