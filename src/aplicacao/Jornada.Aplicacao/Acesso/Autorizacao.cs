using Jornada.Aplicacao.Portas;
using Jornada.Dominio.Acesso;
using Jornada.Dominio.Catalogo;
using Jornada.Dominio.Tenancy;

namespace Jornada.Aplicacao.Acesso;

/// <summary>
/// O alvo da autorização, com o domínio do ativo resolvido. Portado de
/// <c>acesso._dominio_do_item</c>: sobe a hierarquia até achar o domínio. Sem
/// isto, um papel concedido "no domínio Crédito" não valia em ativo nenhum.
/// </summary>
public static class AlvoDoAtivo
{
    public const string TipoDominio = "dominio";

    /// <summary>Alvo de uma ação sobre um ativo que já existe.</summary>
    public static async Task<AlvoDaAcao> ResolverAsync(
        IRepositorioDeAtivos ativos, ContextoDeTenant contexto, Ativo ativo, TipoDeAtivo tipo, CancellationToken ct) =>
        new(contexto.Empresa, tipo.Chave, tipo.Bloco,
            IdDominio: await DominioAsync(ativos, ativo.Id, ct), IdSquad: ativo.IdSquad, Unidade: contexto.Unidade);

    /// <summary>Alvo do cadastro: o domínio é o do pai. Um domínio novo não tem domínio, e só papel da empresa o cria.</summary>
    public static async Task<AlvoDaAcao> DoCadastroAsync(
        IRepositorioDeAtivos ativos, ContextoDeTenant contexto, TipoDeAtivo tipo, IdDeAtivo? idPai, CancellationToken ct) =>
        new(contexto.Empresa, tipo.Chave, tipo.Bloco,
            IdDominio: idPai is { } pai ? await DominioAsync(ativos, pai, ct) : null, Unidade: contexto.Unidade);

    /// <summary>O squad informado tem de existir e estar ativo. Nulo: sem squad, sempre vale.</summary>
    public static async Task<(string Codigo, string Mensagem)?> ConferirSquadAsync(
        IRepositorioDeSquads squads, Guid? idSquad, CancellationToken ct)
    {
        if (idSquad is not { } id) return null;
        var squad = await squads.ObterAsync(id, ct);
        if (squad is null) return ("SQUAD_NAO_ENCONTRADA", "Squad não encontrada.");
        if (!squad.Ativa) return ("SQUAD_INATIVA", $"A squad {squad.Sigla} está inativa e não recebe ativos novos.");
        return null;
    }

    /// <summary>A que domínio o ativo pertence. A trilha que desenha o breadcrumb responde também pelo escopo.</summary>
    public static async Task<Guid?> DominioAsync(IRepositorioDeAtivos ativos, IdDeAtivo id, CancellationToken ct)
    {
        var trilha = await ativos.TrilhaAsync(id, ct);
        return trilha.FirstOrDefault(a => a.TipoItem.Equals(TipoDominio, StringComparison.OrdinalIgnoreCase))?.Id.Valor;
    }
}

/// <summary>
/// A recusa de autorização com o código que diz o motivo. "Quem é você?" e
/// "você está só lendo" têm saída própria: a tela manda ao perfil em vez de
/// dizer que falta papel (<c>web.exige</c> do painel).
/// </summary>
public static class Recusa
{
    public const string PessoaNaoIdentificada = "PESSOA_NAO_IDENTIFICADA";
    public const string ModoLeitura = "MODO_LEITURA";
    public const string SemPermissao = "SEM_PERMISSAO";

    public static Resultado<T> Para<T>(Ator ator, string mensagem) =>
        !ator.Identificado
            ? Resultado<T>.Recusado(PessoaNaoIdentificada, "Escolha quem é você antes de agir no catálogo.")
            : ator.ModoLeitura
                ? Resultado<T>.Recusado(ModoLeitura, PoliticaDeAutorizacao.MensagemDoModoLeitura)
                : Resultado<T>.Recusado(SemPermissao, mensagem);
}
