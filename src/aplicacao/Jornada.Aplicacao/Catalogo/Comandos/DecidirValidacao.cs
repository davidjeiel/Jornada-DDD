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
    IRepositorioDeRevisoes revisoes,
    ITrilhaDeAuditoria trilha,
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

        // Uma etapa só vale para a revisão que está em validação agora. Etapa de
        // revisão antiga que ficou aberta (dado anterior a esta regra) não decide nada.
        if (validacao.Revisao != ativo.RevisaoAtual || ativo.Status != StatusCicloVida.EmValidacao)
            return Resultado<bool>.Recusado("VALIDACAO_OBSOLETA",
                $"Esta etapa é da revisão {validacao.Revisao}, que não está mais em validação.");

        var tipo = (await metamodelo.ObterAsync(ct)).Obter(ativo.TipoItem);
        var ator = await atorAtual.ObterAsync(ct);
        var contexto = tenant.Atual;
        var alvo = new AlvoDaAcao(contexto.Empresa, tipo.Chave, tipo.Bloco, Unidade: contexto.Unidade);
        var autorizacao = PoliticaDeAutorizacao.PodeDecidir(
            ator, validacao.Etapa, validacao.IdSubmissor, ativo, alvo, relogio.Hoje);
        if (!autorizacao.Permitido)
            return Resultado<bool>.Recusado("SEM_PERMISSAO", autorizacao.Motivo);

        // Só as etapas DESTA revisão contam. Antes, a busca trazia também as de
        // revisões anteriores — e uma reprovação antiga impedia para sempre a
        // publicação de qualquer revisão nova do mesmo ativo.
        var daRevisao = (await validacoes.ListarAsync(ativo.Id, ct))
            .Where(v => v.Revisao == validacao.Revisao)
            .ToArray();

        validacao.Decidir(cmd.Aprovada, ator.IdPessoa, cmd.Motivo);
        await trilha.RegistrarAsync(RegistroDeAuditoria.De(ativo, AcoesAuditadas.Validar, ator.IdPessoa, relogio,
            depois: new Dictionary<string, string>
            {
                ["revisao"] = validacao.Revisao.ToString(),
                ["etapa"] = validacao.Etapa.ToString().ToLowerInvariant(),
                ["decisao"] = cmd.Aprovada ? "aprovada" : "reprovada",
                ["motivo"] = cmd.Motivo ?? "",
            }), ct);

        if (!cmd.Aprovada)
        {
            // Reprovada uma etapa, a revisão inteira volta para ajuste: as demais
            // etapas abertas dela são canceladas, como no painel-ddd.
            foreach (var irma in daRevisao.Where(v => v.Id != validacao.Id && v.Status == StatusDaValidacao.Aberta))
                irma.Cancelar($"Revisão {validacao.Revisao} reprovada na etapa {validacao.Etapa.ToString().ToLowerInvariant()}.");

            ativo.Rejeitar(relogio);
        }
        else if (daRevisao.All(v => v.Id == validacao.Id || v.Status == StatusDaValidacao.Aprovada))
        {
            ativo.Publicar(relogio);

            // Revisão submetida antes do ADR-0017 não tem retrato: publica do mesmo jeito.
            if (await revisoes.ObterAsync(ativo.Id, validacao.Revisao, ct) is { } retrato)
                retrato.MarcarPublicada(relogio);
            await trilha.RegistrarAsync(RegistroDeAuditoria.De(ativo, AcoesAuditadas.Publicar, ator.IdPessoa, relogio,
                depois: new Dictionary<string, string> { ["revisao"] = validacao.Revisao.ToString() }), ct);
        }

        await uow.ConfirmarAsync(ct);
        return Resultado<bool>.Ok(true);
    }
}
