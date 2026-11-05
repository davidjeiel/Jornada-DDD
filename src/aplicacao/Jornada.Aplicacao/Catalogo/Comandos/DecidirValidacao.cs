using Jornada.Aplicacao.Acesso;
using Jornada.Aplicacao.Notificacoes;
using Jornada.Aplicacao.Portas;
using Jornada.Dominio.Acesso;
using Jornada.Dominio.Catalogo;
using Jornada.Dominio.Comum;
using Jornada.Dominio.Governanca;
using Jornada.Dominio.Notificacoes;

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
    IRepositorioDeRelacoes relacoes,
    Notificador notificador,
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
        var alvo = await AlvoDoAtivo.ResolverAsync(ativos, contexto, ativo, tipo, ct);
        var autorizacao = PoliticaDeAutorizacao.PodeDecidir(
            ator, validacao.Etapa, validacao.IdSubmissor, ativo, alvo, relogio.Hoje);
        if (!autorizacao.Permitido)
            return Recusa.Para<bool>(ator, autorizacao.Motivo);

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

            // Quem submeteu fica sabendo, com o parecer (servicos._avisar_autor_da_rejeicao).
            var etapa = validacao.Etapa.ToString().ToLowerInvariant();
            await notificador.RegistrarAsync(validacao.IdSubmissor, TiposDeNotificacao.RevisaoRejeitada,
                $"Revisão de {ativo.Nome} rejeitada na etapa {etapa}",
                string.IsNullOrWhiteSpace(cmd.Motivo) ? "Sem parecer registrado." : cmd.Motivo!,
                $"/ativos/{ativo.Id}", ativo.Id, validacao.Id, $"{TiposDeNotificacao.RevisaoRejeitada}:{validacao.Id}", ct);
        }
        else if (daRevisao.All(v => v.Id == validacao.Id || v.Status == StatusDaValidacao.Aprovada))
        {
            ativo.Publicar(relogio);

            // Revisão submetida antes do ADR-0017 não tem retrato: publica do mesmo jeito.
            if (await revisoes.ObterAsync(ativo.Id, validacao.Revisao, ct) is { } retrato)
                retrato.MarcarPublicada(relogio);
            await trilha.RegistrarAsync(RegistroDeAuditoria.De(ativo, AcoesAuditadas.Publicar, ator.IdPessoa, relogio,
                depois: new Dictionary<string, string> { ["revisao"] = validacao.Revisao.ToString() }), ct);
            await AvisarConsumidoresAsync(ativo, ator.IdPessoa, ct);
        }

        await uow.ConfirmarAsync(ct);
        return Resultado<bool>.Ok(true);
    }

    /// <summary>
    /// Avisa os responsáveis pelos ativos que consomem ou dependem do que acabou
    /// de publicar (servicos._avisar_consumidores). Um aviso por pessoa por dia.
    /// </summary>
    private async Task AvisarConsumidoresAsync(Ativo ativo, Guid quemDecidiu, CancellationToken ct)
    {
        var hoje = relogio.Hoje;
        var destinatarios = new List<Guid>();
        foreach (var aresta in (await relacoes.ListarRelacoesAsync(ativo.Id, ct)).Where(r => r.Destino == ativo.Id))
            if (await ativos.ObterAsync(aresta.Origem, ct) is { } consumidor)
                destinatarios.AddRange(consumidor.Responsaveis.Where(r => r.Vigente(hoje)).Select(r => r.IdPessoa));

        await notificador.ParaMuitosAsync(destinatarios, TiposDeNotificacao.AtivoPublicado,
            $"{ativo.Nome} publicou uma nova revisão",
            $"{ativo.Codigo.Valor} é consumido por um ativo sob sua responsabilidade.",
            $"/ativos/{ativo.Id}", ativo.Id, null, $"{TiposDeNotificacao.AtivoPublicado}:{ativo.Id}:{hoje:yyyy-MM-dd}", quemDecidiu, ct);
    }
}
