using Jornada.Aplicacao.Catalogo.Consultas;
using Jornada.Aplicacao.Portas;
using Jornada.Dominio.Acesso;
using Jornada.Dominio.Catalogo;
using Jornada.Dominio.Comum;

namespace Jornada.Aplicacao.Catalogo.Comandos;

/// <param name="Confirmacao">
/// O código do ativo, digitado por quem descontinua. Só é exigido quando há
/// consumidor de alta criticidade; nos demais casos é ignorado.
/// </param>
public sealed record ComandoDescontinuarAtivo(IdDeAtivo Id, string Motivo, string? Confirmacao = null);

/// <summary>
/// Tira um ativo de vigência. Equivale a <c>servicos.descontinuar</c> mais a
/// confirmação em duas etapas da rota <c>/ativo/&lt;id&gt;/descontinuar</c>.
///
/// No painel-ddd são dois mecanismos: o serviço bloqueia se houver consumidor
/// crítico (a menos que receba <c>forcar</c>), e a tela exige que se digite o
/// código antes de mandar <c>forcar</c>. Aqui é um só: digitar o código É o
/// "forçar". Assim um cliente da API não contorna a confirmação mandando só a
/// bandeira.
///
/// As relações não são encerradas uma a uma, como no painel: elas moram na
/// partição de quem aponta (ADR-0016) e encerrá-las seria um lote por
/// consumidor, sem atomicidade. Uma relação com um dos lados fora de vigência
/// simplesmente deixa de contar (ver <see cref="AnalisarImpacto"/> e o alerta
/// do pré-check), e como descontinuado não volta a publicado, o efeito é o mesmo.
/// </summary>
public sealed class DescontinuarAtivo(
    IRepositorioDeAtivos ativos,
    AnalisarImpacto impacto,
    IMetamodelo metamodelo,
    IContextoDeTenantAtual tenant,
    IAtorAtual atorAtual,
    IUnidadeDeTrabalho uow,
    ITrilhaDeAuditoria trilha,
    IRelogio relogio)
{
    /// <summary>Descontinuado, com quantos consumidores foram afetados.</summary>
    public sealed record Saida(int Afetados);

    public async Task<Resultado<Saida>> ExecutarAsync(ComandoDescontinuarAtivo cmd, CancellationToken ct = default)
    {
        var ativo = await ativos.ObterAsync(cmd.Id, ct);
        if (ativo is null)
            return Resultado<Saida>.Recusado("ATIVO_NAO_ENCONTRADO", "Ativo não encontrado.");

        var tipo = (await metamodelo.ObterAsync(ct)).Obter(ativo.TipoItem);
        var ator = await atorAtual.ObterAsync(ct);
        var alvo = new AlvoDaAcao(tenant.Atual.Empresa, tipo.Chave, tipo.Bloco, Unidade: tenant.Atual.Unidade);

        if (!PoliticaDeAutorizacao.Pode(ator, Acao.Descontinuar, alvo, relogio.Hoje))
            return Resultado<Saida>.Recusado("SEM_PERMISSAO", "Sem permissão para descontinuar este ativo.");

        if (!MaquinaDeCicloDeVida.Permite(ativo.Status, StatusCicloVida.Descontinuado))
        {
            return Resultado<Saida>.Recusado("TRANSICAO_INVALIDA",
                $"Só ativo publicado ou em revisão pode ser descontinuado; este está em '{MaquinaDeCicloDeVida.Rotulo(ativo.Status)}'.");
        }

        if (string.IsNullOrWhiteSpace(cmd.Motivo))
            return Resultado<Saida>.Recusado("MOTIVO_OBRIGATORIO", "Informe o motivo da descontinuação.");

        var analise = (await impacto.ExecutarAsync(cmd.Id, ct)).Valor!;
        var criticos = analise.Criticos.Count;
        if (criticos > 0 && !Confirmou(cmd.Confirmacao, ativo.Codigo))
        {
            return Resultado<Saida>.Recusado("CONFIRMACAO_NECESSARIA",
                $"{criticos} consumidor(es) de alta criticidade dependem deste ativo. " +
                $"Digite o código {ativo.Codigo.Valor} para confirmar a descontinuação.");
        }

        ativo.Descontinuar(cmd.Motivo.Trim(), relogio);
        await trilha.RegistrarAsync(RegistroDeAuditoria.De(ativo, AcoesAuditadas.Descontinuar, ator.IdPessoa, relogio,
            depois: new Dictionary<string, string>
            {
                ["motivo"] = cmd.Motivo.Trim(), ["impactados"] = analise.Total.ToString(),
            }), ct);
        await uow.ConfirmarAsync(ct);
        return Resultado<Saida>.Ok(new Saida(analise.Total));
    }

    private static bool Confirmou(string? digitado, CodigoDeAtivo codigo) =>
        string.Equals((digitado ?? "").Trim(), codigo.Valor, StringComparison.OrdinalIgnoreCase);
}
