using Jornada.Aplicacao.Portas;
using Jornada.Dominio.Acesso;
using Jornada.Dominio.Comum;

namespace Jornada.Aplicacao.Acesso;

/// <param name="EscopoTipo">empresa (padrão) ou dominio. Squad chega com o cadastro de squads.</param>
public sealed record ComandoDecidirPleito(
    Guid IdPleito, bool Aprovar, string? Resposta = null,
    string? PapelConcedido = null, string? EscopoTipo = null, Guid? EscopoId = null);

/// <summary>
/// Concede ou nega um pleito. Portado de <c>acesso.decidir_solicitacao</c>; as
/// regras moram em <see cref="PleitoDeAcesso.Decidir"/>. Aqui fica o que pede
/// o catálogo: o domínio do escopo tem de existir e ser um domínio.
/// </summary>
public sealed class DecidirPleito(
    IRepositorioDePleitos pleitos,
    IRepositorioDePessoas pessoas,
    IRepositorioDeAtivos ativos,
    IAtorAtual atorAtual,
    IUnidadeDeTrabalho uow,
    IRelogio relogio)
{
    public async Task<Resultado<PleitoLegivel>> ExecutarAsync(ComandoDecidirPleito cmd, CancellationToken ct = default)
    {
        var pleito = await pleitos.ObterAsync(cmd.IdPleito, ct);
        if (pleito is null)
            return Resultado<PleitoLegivel>.Recusado("PLEITO_NAO_ENCONTRADO", "Pleito não encontrado.");

        var ator = await atorAtual.ObterAsync(ct);
        if (!ator.Identificado || ator.ModoLeitura)
            return Recusa.Para<PleitoLegivel>(ator, "");

        Papel? papel = null;
        if (!string.IsNullOrWhiteSpace(cmd.PapelConcedido))
        {
            if (!PapelExtensoes.TentarDe(cmd.PapelConcedido, out var p))
                return Resultado<PleitoLegivel>.Recusado("PAPEL_INVALIDO", $"Papel desconhecido: {cmd.PapelConcedido}.");
            papel = p;
        }

        var escopo = (cmd.EscopoTipo ?? "").Trim().ToLowerInvariant() switch
        {
            "" or "empresa" or "global" => TipoDeEscopo.Empresa,
            "dominio" => TipoDeEscopo.Dominio,
            _ => (TipoDeEscopo?)null,
        };
        if (escopo is null)
            return Resultado<PleitoLegivel>.Recusado("ESCOPO_INVALIDO",
                $"Escopo desconhecido: {cmd.EscopoTipo}. Use empresa ou dominio.");

        if (cmd.Aprovar && escopo == TipoDeEscopo.Dominio && cmd.EscopoId is { } idDominio)
        {
            var dominio = await ativos.ObterAsync(new Dominio.Catalogo.IdDeAtivo(idDominio), ct);
            if (dominio is null || !dominio.TipoItem.Equals(AlvoDoAtivo.TipoDominio, StringComparison.OrdinalIgnoreCase))
                return Resultado<PleitoLegivel>.Recusado("ESCOPO_INVALIDO", "O escopo de domínio tem de apontar para um domínio do catálogo.");
        }

        var pessoa = await pessoas.ObterAsync(pleito.IdPessoa, ct)
            ?? throw new InvalidOperationException($"O pleito {pleito.Id} aponta para uma pessoa que não existe.");

        try
        {
            if (pleito.Decidir(ator, cmd.Aprovar, cmd.Resposta, relogio, papel, escopo.Value, cmd.EscopoId) is { } concedida)
                pessoa.Conceder(concedida);
        }
        catch (ErroDeDominio e)
        {
            return Resultado<PleitoLegivel>.Recusado(e.Codigo, e.Message);
        }

        await uow.ConfirmarAsync(ct);
        return Resultado<PleitoLegivel>.Ok(await Legibilidade.PleitoAsync(ativos, pessoas, pleito, ct));
    }
}
