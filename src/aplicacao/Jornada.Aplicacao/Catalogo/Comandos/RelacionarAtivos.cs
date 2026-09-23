using Jornada.Aplicacao.Portas;
using Jornada.Dominio.Acesso;
using Jornada.Dominio.Catalogo;
using Jornada.Dominio.Comum;

namespace Jornada.Aplicacao.Catalogo.Comandos;

public sealed record ComandoRelacionarAtivos(IdDeAtivo Origem, IdDeAtivo Destino, string Tipo);

/// <summary>
/// Grava uma aresta do grafo do catálogo (<c>implementa</c>, <c>expõe</c>,
/// <c>consome</c>, <c>depende_de</c>...). A tabela e as consultas de leitura
/// (<see cref="IRepositorioDeRelacoes"/>) já existiam desde o Grupo 1 — só
/// faltava o caminho de escrita.
/// </summary>
public sealed class RelacionarAtivos(
    IRepositorioDeAtivos ativos,
    IRepositorioDeRelacoes relacoes,
    IMetamodelo metamodelo,
    IContextoDeTenantAtual tenant,
    IAtorAtual atorAtual,
    IRelogio relogio)
{
    public async Task<Resultado<bool>> ExecutarAsync(ComandoRelacionarAtivos cmd, CancellationToken ct = default)
    {
        if (cmd.Origem == cmd.Destino)
            return Resultado<bool>.Recusado("CICLO", "Um ativo não pode se relacionar consigo mesmo.");

        if (string.IsNullOrWhiteSpace(cmd.Tipo))
            return Resultado<bool>.Recusado("TIPO_OBRIGATORIO", "Tipo de relação é obrigatório.");

        var origem = await ativos.ObterAsync(cmd.Origem, ct);
        if (origem is null)
            return Resultado<bool>.Recusado("ATIVO_NAO_ENCONTRADO", "Ativo de origem não encontrado.");

        var destino = await ativos.ObterAsync(cmd.Destino, ct);
        if (destino is null)
            return Resultado<bool>.Recusado("DESTINO_NAO_ENCONTRADO", "Ativo de destino não encontrado.");

        var tipoDaOrigem = (await metamodelo.ObterAsync(ct)).Obter(origem.TipoItem);
        var ator = await atorAtual.ObterAsync(ct);
        var alvo = new AlvoDaAcao(tenant.Atual.Empresa, tipoDaOrigem.Chave, tipoDaOrigem.Bloco,
                                  Unidade: tenant.Atual.Unidade);

        if (!PoliticaDeAutorizacao.Pode(ator, Acao.Relacionar, alvo, relogio.Hoje))
            return Resultado<bool>.Recusado("SEM_PERMISSAO", "Sem permissão para relacionar este ativo.");

        // Ida E volta: TemDependenciaCircularAsync (avaliada no pré-check) faz
        // busca em profundidade sobre "depende_de" — uma aresta na direção
        // errada nunca aparece nessa busca. Registrar só o sentido pedido é
        // suficiente e é o que os testes do domínio já esperam.
        await relacoes.RelacionarAsync(cmd.Origem, cmd.Destino, cmd.Tipo.Trim().ToLowerInvariant(), ct);
        return Resultado<bool>.Ok(true);
    }
}
