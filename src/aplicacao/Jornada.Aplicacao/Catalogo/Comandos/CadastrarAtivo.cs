using Jornada.Aplicacao.Portas;
using Jornada.Dominio.Acesso;
using Jornada.Dominio.Catalogo;
using Jornada.Dominio.Comum;

namespace Jornada.Aplicacao.Catalogo.Comandos;

/// <summary>
/// Repare no que este comando NÃO tem: um campo <c>TenantId</c> e um campo
/// <c>Usuario</c>.
///
/// No <c>api.py</c> atual, <c>POST /itens</c> lê <c>corpo.get("usuario")</c> —
/// quem chama declara quem é. Numa API pública multiempresa isso é falsificação
/// de identidade por design. Aqui os dois vêm do token, pelas portas
/// (ADR-0003, ADR-0009).
/// </summary>
public sealed record ComandoCadastrarAtivo(
    string TipoItem,
    string Nome,
    string Descricao = "",
    Criticidade Criticidade = Criticidade.Media,
    IdDeAtivo? IdPai = null,
    IReadOnlyDictionary<string, string>? Atributos = null);

public sealed class CadastrarAtivo(
    IRepositorioDeAtivos ativos,
    IMetamodelo metamodelo,
    IContextoDeTenantAtual tenant,
    IAtorAtual atorAtual,
    IUnidadeDeTrabalho uow,
    IRelogio relogio)
{
    public async Task<Resultado<IdDeAtivo>> ExecutarAsync(
        ComandoCadastrarAtivo cmd, CancellationToken ct = default)
    {
        var tipos = await metamodelo.ObterAsync(ct);
        if (!tipos.Existe(cmd.TipoItem))
            return Resultado<IdDeAtivo>.Recusado("TIPO_DESCONHECIDO",
                $"Tipo de ativo desconhecido: {cmd.TipoItem}");

        var tipo = tipos.Obter(cmd.TipoItem);
        var ator = await atorAtual.ObterAsync(ct);
        var contexto = tenant.Atual;

        // Autorização ANTES de qualquer escrita. O alvo carrega o bloco do tipo,
        // que é o que impede alguém de negócio cadastrar um endpoint.
        var alvo = new AlvoDaAcao(contexto.Empresa, tipo.Chave, tipo.Bloco,
                                  Unidade: contexto.Unidade);

        if (!PoliticaDeAutorizacao.Pode(ator, Acao.Cadastrar, alvo, relogio.Hoje))
            return Resultado<IdDeAtivo>.Recusado("SEM_PERMISSAO",
                $"Sem permissão para cadastrar ativos do bloco '{tipo.Bloco}'.");

        // Hierarquia: regra de aplicação, não constraint de banco (docs/MODELO.md).
        if (tipo.Pai is not null && cmd.IdPai is { } idPai)
        {
            var pai = await ativos.ObterAsync(idPai, ct);
            if (pai is null)
                return Resultado<IdDeAtivo>.Recusado("PAI_NAO_ENCONTRADO", "Item pai não encontrado.");

            if (!pai.TipoItem.Equals(tipo.Pai, StringComparison.OrdinalIgnoreCase))
                return Resultado<IdDeAtivo>.Recusado("HIERARQUIA_INVALIDA",
                    $"{tipo.Rotulo} deve ser filho de um item do tipo '{tipo.Pai}', " +
                    $"e não de '{pai.TipoItem}'.");
        }

        var sequencial = await ativos.ProximoSequencialAsync(tipo.Chave, ct);
        var codigo = CodigoDeAtivo.Gerar(tipo.Prefixo, sequencial);

        var ativo = Ativo.Rascunhar(
            contexto.Empresa, tipo, codigo, cmd.Nome, relogio,
            cmd.Descricao, cmd.Criticidade, cmd.IdPai, contexto.Unidade, cmd.Atributos);

        await ativos.AdicionarAsync(ativo, ct);
        await uow.ConfirmarAsync(ct);

        return Resultado<IdDeAtivo>.Ok(ativo.Id);
    }
}
