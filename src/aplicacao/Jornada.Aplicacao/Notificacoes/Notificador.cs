using Jornada.Aplicacao.Portas;
using Jornada.Dominio.Acesso;
using Jornada.Dominio.Catalogo;
using Jornada.Dominio.Comum;
using Jornada.Dominio.Governanca;
using Jornada.Dominio.Notificacoes;

namespace Jornada.Aplicacao.Notificacoes;

/// <summary>
/// Quem enfileira os avisos (<c>notificacoes.registrar</c> e <c>para_muitos</c>)
/// e quem diz a quem avisar (<c>acesso.quem_pode_decidir</c> e
/// <c>quem_concede</c>). NÃO confirma: o aviso entra na transação de quem chamou.
/// </summary>
public sealed class Notificador(
    IRepositorioDeNotificacoes notificacoes,
    IRepositorioDePreferencias preferencias,
    IRepositorioDePessoas pessoas,
    IContextoDeTenantAtual tenant,
    IRelogio relogio)
{
    /// <summary>
    /// Enfileira um aviso. Falso quando a pessoa desligou o tipo, ou quando a
    /// chave única já foi usada: é o que impede repetir o mesmo alerta.
    /// </summary>
    public async Task<bool> RegistrarAsync(
        Guid idPessoa, string tipo, string titulo, string corpo, string? url = null,
        IdDeAtivo? ativo = null, Guid? idValidacao = null, string? chaveUnica = null, CancellationToken ct = default)
    {
        if (!(await preferencias.ObterAsync(idPessoa, ct)).QuerReceber(tipo, CanaisDeNotificacao.App)) return false;
        if (chaveUnica is not null && await notificacoes.ExisteChaveAsync(chaveUnica, ct)) return false;
        await notificacoes.AdicionarAsync(Notificacao.Criar(tenant.Atual.Empresa, idPessoa, tipo, titulo, corpo, url,
            ativo, idValidacao, chaveUnica, relogio), ct);
        return true;
    }

    /// <summary>O mesmo aviso para várias pessoas; a chave de cada uma é <c>sufixo:pessoa</c>.</summary>
    public async Task<int> ParaMuitosAsync(
        IEnumerable<Guid> destinatarios, string tipo, string titulo, string corpo, string? url = null,
        IdDeAtivo? ativo = null, Guid? idValidacao = null, string? sufixoChave = null, Guid? excluir = null, CancellationToken ct = default)
    {
        var enviadas = 0;
        foreach (var id in destinatarios.Distinct())
        {
            if (id == excluir) continue;
            if (await RegistrarAsync(id, tipo, titulo, corpo, url, ativo, idValidacao,
                    sufixoChave is null ? null : $"{sufixoChave}:{id}", ct))
                enviadas++;
        }
        return enviadas;
    }

    /// <summary>
    /// Quem pode decidir a etapa: a mesma regra da decisão, aplicada a cada
    /// pessoa ativa. Quem submeteu fica de fora pela segregação de função.
    /// </summary>
    public async Task<IReadOnlyList<Guid>> QuemPodeDecidirAsync(
        ValidacaoEmProcesso validacao, Ativo ativo, AlvoDaAcao alvo, CancellationToken ct = default)
    {
        var hoje = relogio.Hoje;
        return (await pessoas.ListarAsync(ct: ct))
            .Where(p => PoliticaDeAutorizacao.PodeDecidir(p.ComoAtor(hoje), validacao.Etapa, validacao.IdSubmissor, ativo, alvo, hoje).Permitido)
            .Select(p => p.Id)
            .ToArray();
    }

    /// <summary>Quem pode conceder o papel pleiteado: curador ou admin, e só admin para admin.</summary>
    public async Task<IReadOnlyList<Guid>> QuemConcedeAsync(Papel papel, CancellationToken ct = default)
    {
        var hoje = relogio.Hoje;
        return (await pessoas.ListarAsync(ct: ct))
            .Where(p => PoliticaDeAutorizacao.PodeConceder(p.ComoAtor(hoje), papel, hoje).Permitido)
            .Select(p => p.Id)
            .ToArray();
    }
}
