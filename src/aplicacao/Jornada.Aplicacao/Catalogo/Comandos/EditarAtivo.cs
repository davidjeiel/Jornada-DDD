using Jornada.Aplicacao.Portas;
using Jornada.Dominio.Acesso;
using Jornada.Dominio.Catalogo;
using Jornada.Dominio.Comum;

namespace Jornada.Aplicacao.Catalogo.Comandos;

public sealed record ComandoEditarAtivo(
    IdDeAtivo Id,
    string Nome,
    string Descricao,
    Criticidade Criticidade,
    IReadOnlyDictionary<string, string>? Atributos);

/// <summary>
/// Fecha a fatia funcional: até aqui só era possível cadastrar. Editar é a
/// mesma autorização de cadastrar (<c>Acao.Editar</c>, mesmo bloco do tipo) —
/// quem escreve no bloco pode corrigir o que já escreveu.
/// </summary>
public sealed class EditarAtivo(
    IRepositorioDeAtivos ativos,
    IMetamodelo metamodelo,
    IContextoDeTenantAtual tenant,
    IAtorAtual atorAtual,
    IUnidadeDeTrabalho uow,
    ITrilhaDeAuditoria trilha,
    IRelogio relogio)
{
    public async Task<Resultado<bool>> ExecutarAsync(ComandoEditarAtivo cmd, CancellationToken ct = default)
    {
        var ativo = await ativos.ObterAsync(cmd.Id, ct);
        if (ativo is null)
            return Resultado<bool>.Recusado("ATIVO_NAO_ENCONTRADO", "Ativo não encontrado.");

        var tipo = (await metamodelo.ObterAsync(ct)).Obter(ativo.TipoItem);
        var ator = await atorAtual.ObterAsync(ct);
        var alvo = new AlvoDaAcao(tenant.Atual.Empresa, tipo.Chave, tipo.Bloco, Unidade: tenant.Atual.Unidade);

        if (!PoliticaDeAutorizacao.Pode(ator, Acao.Editar, alvo, relogio.Hoje))
            return Resultado<bool>.Recusado("SEM_PERMISSAO", "Sem permissão para editar este ativo.");

        // Publicado é imutável: mudar exige abrir revisão (POST .../revisoes).
        if (!ativo.Editavel)
            return Resultado<bool>.Recusado("ATIVO_NAO_EDITAVEL", ativo.MotivoDeNaoEditavel);

        var antes = Campos(ativo, cmd.Atributos?.Keys);

        ativo.Renomear(cmd.Nome, relogio);
        ativo.Descrever(cmd.Descricao, relogio);
        ativo.ReclassificarCriticidade(cmd.Criticidade, relogio);

        if (cmd.Atributos is not null)
            foreach (var (chave, valor) in cmd.Atributos)
                ativo.DefinirAtributo(chave, valor, relogio);

        // Só o que mudou, nos dois lados, como o "antes/depois" do painel.
        var depois = Campos(ativo, cmd.Atributos?.Keys);
        var mudou = depois.Keys.Where(k => antes.GetValueOrDefault(k) != depois[k]).ToArray();
        if (mudou.Length > 0)
        {
            await trilha.RegistrarAsync(RegistroDeAuditoria.De(ativo, AcoesAuditadas.Alterar, ator.IdPessoa, relogio,
                mudou.ToDictionary(k => k, k => antes.GetValueOrDefault(k) ?? ""),
                mudou.ToDictionary(k => k, k => depois[k])), ct);
        }

        await uow.ConfirmarAsync(ct);
        return Resultado<bool>.Ok(true);
    }

    private static Dictionary<string, string> Campos(Ativo ativo, IEnumerable<string>? atributos)
    {
        var campos = new Dictionary<string, string>
        {
            ["nome"] = ativo.Nome,
            ["descricao"] = ativo.Descricao,
            ["criticidade"] = ativo.Criticidade.Chave(),
        };
        foreach (var chave in atributos ?? [])
            campos[$"atributo:{chave}"] = ativo.Atributos.GetValueOrDefault(chave) ?? "";
        return campos;
    }
}
