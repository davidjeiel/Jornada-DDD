using Jornada.Aplicacao.Acesso;
using Jornada.Aplicacao.Portas;
using Jornada.Dominio.Acesso;
using Jornada.Dominio.Catalogo;
using Jornada.Dominio.Comum;

namespace Jornada.Aplicacao.Catalogo.Comandos;

/// <param name="Criticidade">
/// Quanto a origem depende do destino. Alta ou crítica faz a descontinuação do
/// destino exigir confirmação (<see cref="DescontinuarAtivo"/>).
/// </param>
/// <param name="Mecanismo">Como a relação acontece: sincrono, assincrono, batch, arquivo ou biblioteca. Opcional.</param>
/// <param name="OrigemDaEvidencia">De onde veio a evidência: manual (padrão), git, openapi, cmdb ou cicd.</param>
public sealed record ComandoRelacionarAtivos(
    IdDeAtivo Origem, IdDeAtivo Destino, string Tipo, Criticidade Criticidade = Criticidade.Media, string? Mecanismo = null,
    string? OrigemDaEvidencia = null);

/// <summary>
/// Grava uma aresta do grafo do catálogo (<c>implementa</c>, <c>expõe</c>,
/// <c>consome</c>, <c>depende_de</c>...). A tabela e as consultas de leitura
/// (<see cref="IRepositorioDeRelacoes"/>) já existiam desde o Grupo 1 — só
/// faltava o caminho de escrita.
///
/// Como <c>servicos.relacionar</c> do painel: o tipo tem de ser um dos
/// conhecidos, a mesma aresta (origem, destino, tipo) não se grava duas vezes,
/// e uma <c>depende_de</c> que fecharia um ciclo é recusada antes de gravar.
/// O valor do resultado diz se a relação foi criada (falso: já existia).
/// </summary>
public sealed class RelacionarAtivos(
    IRepositorioDeAtivos ativos,
    IRepositorioDeRelacoes relacoes,
    IMetamodelo metamodelo,
    IContextoDeTenantAtual tenant,
    IAtorAtual atorAtual,
    IUnidadeDeTrabalho uow,
    ITrilhaDeAuditoria trilha,
    IRelogio relogio)
{
    public async Task<Resultado<bool>> ExecutarAsync(ComandoRelacionarAtivos cmd, CancellationToken ct = default)
    {
        if (cmd.Origem == cmd.Destino)
            return Resultado<bool>.Recusado("CICLO", "Um ativo não pode se relacionar consigo mesmo.");

        string tipo;
        string? mecanismo;
        string origemDaEvidencia;
        try
        {
            tipo = CatalogoDeRelacoes.ConferirTipo(cmd.Tipo);
            mecanismo = CatalogoDeRelacoes.ConferirMecanismo(cmd.Mecanismo);
            origemDaEvidencia = CatalogoDeRelacoes.ConferirOrigem(cmd.OrigemDaEvidencia);
        }
        catch (ErroDeDominio e) { return Resultado<bool>.Recusado(e.Codigo, e.Message); }

        var origem = await ativos.ObterAsync(cmd.Origem, ct);
        if (origem is null)
            return Resultado<bool>.Recusado("ATIVO_NAO_ENCONTRADO", "Ativo de origem não encontrado.");

        var destino = await ativos.ObterAsync(cmd.Destino, ct);
        if (destino is null)
            return Resultado<bool>.Recusado("DESTINO_NAO_ENCONTRADO", "Ativo de destino não encontrado.");

        var tipoDaOrigem = (await metamodelo.ObterAsync(ct)).Obter(origem.TipoItem);
        var ator = await atorAtual.ObterAsync(ct);
        var alvo = await AlvoDoAtivo.ResolverAsync(ativos, tenant.Atual, origem, tipoDaOrigem, ct);

        if (!PoliticaDeAutorizacao.Pode(ator, Acao.Relacionar, alvo, relogio.Hoje))
            return Recusa.Para<bool>(ator, "Sem permissão para relacionar este ativo.");

        // Ida E volta: TemDependenciaCircularAsync (avaliada no pré-check) faz
        // busca em profundidade sobre "depende_de" — uma aresta na direção
        // errada nunca aparece nessa busca. Registrar só o sentido pedido é
        // suficiente e é o que os testes do domínio já esperam.
        var existentes = await relacoes.ListarRelacoesAsync(cmd.Origem, ct);
        if (existentes.Any(r => r.Origem == cmd.Origem && r.Destino == cmd.Destino && r.Tipo == tipo))
            return Resultado<bool>.Ok(false);

        // A aresta origem → destino fecha um ciclo se o destino já chega à origem.
        if (tipo == CatalogoDeRelacoes.DependeDe && await relacoes.TemCaminhoDeDependenciaAsync(cmd.Destino, cmd.Origem, ct))
            return Resultado<bool>.Recusado("CICLO_DE_DEPENDENCIA",
                $"A relação criaria dependência circular: {destino.Codigo} já depende de {origem.Codigo}.");

        await relacoes.RelacionarAsync(cmd.Origem, cmd.Destino, tipo, cmd.Criticidade, mecanismo, origemDaEvidencia, ct);

        // A aresta é gravada na hora (a porta não passa pela unidade de
        // trabalho); o registro de auditoria vai logo depois, na partição da origem.
        await trilha.RegistrarAsync(RegistroDeAuditoria.De(origem, AcoesAuditadas.Relacionar, ator.IdPessoa, relogio,
            depois: new Dictionary<string, string>
            {
                ["destino"] = destino.Codigo.Valor, ["tipo"] = tipo, ["criticidade"] = cmd.Criticidade.Chave(),
                ["mecanismo"] = mecanismo ?? "", ["origem"] = origemDaEvidencia,
            }), ct);
        await uow.ConfirmarAsync(ct);
        return Resultado<bool>.Ok(true);
    }
}
