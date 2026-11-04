using Jornada.Aplicacao.Portas;
using Jornada.Dominio.Acesso;
using Jornada.Dominio.Comum;

namespace Jornada.Aplicacao.Acesso;

/// <summary>
/// Os squads da empresa. Portado das rotas <c>/squads</c> do painel-ddd, que
/// exigem <c>conceder</c>: quem concede papel é quem organiza os times.
/// </summary>
public sealed class CadastroDeSquads(
    IRepositorioDeSquads squads,
    IContextoDeTenantAtual tenant,
    IAtorAtual atorAtual,
    IUnidadeDeTrabalho uow,
    IRelogio relogio)
{
    /// <summary>Ativas primeiro, filtradas por título, sigla ou descrição. Leitura aberta: o squad aparece no cadastro da pessoa.</summary>
    public async Task<IReadOnlyList<Squad>> ListarAsync(string? busca = null, CancellationToken ct = default)
    {
        var termo = (busca ?? "").Trim();
        return (await squads.ListarAsync(ct))
            .Where(s => termo.Length == 0
                        || s.Titulo.Contains(termo, StringComparison.CurrentCultureIgnoreCase)
                        || s.Sigla.Contains(termo, StringComparison.CurrentCultureIgnoreCase)
                        || s.Descricao.Contains(termo, StringComparison.CurrentCultureIgnoreCase))
            .ToArray();
    }

    public Task<Resultado<Squad>> CriarAsync(string? sigla, string? titulo, string? descricao, CancellationToken ct = default) =>
        EscreverAsync(ct, async () =>
        {
            var squad = Squad.Criar(tenant.Atual.Empresa, sigla, titulo, descricao);
            await ExigirSiglaLivreAsync(squad.Sigla, null, ct);
            await squads.AdicionarAsync(squad, ct);
            return squad;
        });

    public Task<Resultado<Squad>> AlterarAsync(Guid id, string? sigla, string? titulo, string? descricao, CancellationToken ct = default) =>
        EscreverAsync(ct, async () =>
        {
            var squad = await ObterAsync(id, ct);
            await ExigirSiglaLivreAsync(Squad.NormalizarSigla(sigla), id, ct);
            squad.Alterar(sigla, titulo, descricao);
            return squad;
        });

    /// <summary>Inativa sem apagar: os vínculos existentes são preservados.</summary>
    public Task<Resultado<Squad>> InativarAsync(Guid id, CancellationToken ct = default) =>
        EscreverAsync(ct, async () =>
        {
            var squad = await ObterAsync(id, ct);
            squad.Inativar();
            return squad;
        });

    private async Task<Squad> ObterAsync(Guid id, CancellationToken ct) =>
        await squads.ObterAsync(id, ct) ?? throw new ErroDeDominio("SQUAD_NAO_ENCONTRADA", "Squad não encontrada.");

    private async Task ExigirSiglaLivreAsync(string sigla, Guid? exceto, CancellationToken ct)
    {
        if (await squads.ObterPorSiglaAsync(sigla, ct) is { } outra && outra.Id != exceto)
            throw new ErroDeDominio("SIGLA_DUPLICADA", "Já existe uma squad com essa sigla.");
    }

    private async Task<Resultado<Squad>> EscreverAsync(CancellationToken ct, Func<Task<Squad>> escrita)
    {
        var ator = await atorAtual.ObterAsync(ct);
        var alcada = PoliticaDeAutorizacao.PodeConceder(ator, null, relogio.Hoje);
        if (!alcada.Permitido)
            return Recusa.Para<Squad>(ator, "O cadastro de squads é atribuição de curador ou administrador.");

        Squad squad;
        try { squad = await escrita(); }
        catch (ErroDeDominio e) { return Resultado<Squad>.Recusado(e.Codigo, e.Message); }

        await uow.ConfirmarAsync(ct);
        return Resultado<Squad>.Ok(squad);
    }
}
