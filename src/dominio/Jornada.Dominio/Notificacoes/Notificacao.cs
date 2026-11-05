using Jornada.Dominio.Catalogo;
using Jornada.Dominio.Comum;
using Jornada.Dominio.Tenancy;

namespace Jornada.Dominio.Notificacoes;

/// <summary>Os tipos de aviso (<c>notificacoes.TIPOS</c>): o rótulo e se é urgente.</summary>
public static class TiposDeNotificacao
{
    public const string RevisaoSubmetida = "revisao_submetida";
    public const string SlaVencendo = "sla_vencendo";
    public const string SlaVencido = "sla_vencido";
    public const string RevisaoRejeitada = "revisao_rejeitada";
    public const string AtivoPublicado = "ativo_publicado";
    public const string AcessoSolicitado = "acesso_solicitado";
    public const string AcessoDecidido = "acesso_decidido";

    public sealed record Tipo(string Chave, string Rotulo, bool Urgente);

    public static readonly IReadOnlyList<Tipo> Todos =
    [
        new(RevisaoSubmetida, "Revisão aguardando sua análise", true),
        new(SlaVencendo, "SLA de validação vencendo", true),
        new(SlaVencido, "SLA de validação vencido", true),
        new(RevisaoRejeitada, "Sua revisão foi rejeitada", true),
        new(AtivoPublicado, "Ativo que você consome foi publicado", false),
        new(AcessoSolicitado, "Pleito de acesso aguardando decisão", true),
        new(AcessoDecidido, "Resposta ao seu pleito de acesso", true),
    ];

    public static bool Existe(string chave) => Todos.Any(t => t.Chave == chave);

    public static Tipo Obter(string chave) => Todos.FirstOrDefault(t => t.Chave == chave)
        ?? throw new ErroDeDominio("TIPO_DE_NOTIFICACAO_INVALIDO", $"Tipo de notificação desconhecido: {chave}.");
}

/// <summary>
/// Os canais (<c>notificacoes.CANAIS</c>). Só o <c>app</c>, o sino, está
/// disponível: e-mail e Teams dependem de uma fonte de identidade com endereço
/// confiável, e ficam declarados e desligados até lá.
/// </summary>
public static class CanaisDeNotificacao
{
    public const string App = "app";
    public const string Email = "email";
    public const string Teams = "teams";

    public static readonly IReadOnlyList<(string Chave, string Rotulo)> Todos = [(App, "No catálogo"), (Email, "E-mail"), (Teams, "Teams")];
    public static readonly IReadOnlySet<string> Disponiveis = new HashSet<string> { App };
}

/// <summary>
/// Um aviso para uma pessoa, no padrão outbox: entra na mesma transação do fato
/// que o gerou (atribuir uma etapa e avisar quem vai analisá-la não podem
/// divergir) e um despacho posterior marca <see cref="EnviadaEm"/>.
///
/// <see cref="ChaveUnica"/> impede o mesmo aviso duas vezes: é o que faz o
/// vigia de SLA, que roda de hora em hora, avisar uma vez por dia.
/// </summary>
public sealed class Notificacao
{
    public Guid Id { get; }
    public IdDeEmpresa Empresa { get; }
    public Guid IdPessoa { get; }
    public string Tipo { get; }
    public string Titulo { get; }
    public string Corpo { get; }
    public string? Url { get; }
    public IdDeAtivo? Ativo { get; }
    public Guid? IdValidacao { get; }
    public string? ChaveUnica { get; }
    public string Canal { get; }
    public DateTimeOffset CriadaEm { get; }
    public DateTimeOffset? LidaEm { get; private set; }
    public DateTimeOffset? EnviadaEm { get; private set; }

    private Notificacao(Guid id, IdDeEmpresa empresa, Guid idPessoa, string tipo, string titulo, string corpo, string? url,
        IdDeAtivo? ativo, Guid? idValidacao, string? chaveUnica, string canal, DateTimeOffset criadaEm, DateTimeOffset? lidaEm, DateTimeOffset? enviadaEm)
    {
        Id = id; Empresa = empresa; IdPessoa = idPessoa; Tipo = tipo; Titulo = titulo; Corpo = corpo; Url = url;
        Ativo = ativo; IdValidacao = idValidacao; ChaveUnica = chaveUnica; Canal = canal; CriadaEm = criadaEm; LidaEm = lidaEm; EnviadaEm = enviadaEm;
    }

    public static Notificacao Criar(IdDeEmpresa empresa, Guid idPessoa, string tipo, string titulo, string corpo, string? url,
        IdDeAtivo? ativo, Guid? idValidacao, string? chaveUnica, IRelogio relogio, string canal = CanaisDeNotificacao.App)
    {
        TiposDeNotificacao.Obter(tipo);
        if (idPessoa == Guid.Empty)
            throw new ErroDeDominio("PESSOA_NAO_IDENTIFICADA", "Notificação sem destinatário.");
        return new(Guid.CreateVersion7(), empresa, idPessoa, tipo, titulo, corpo ?? "", url, ativo, idValidacao, chaveUnica, canal,
            relogio.Agora, null, null);
    }

    public bool Lida => LidaEm is not null;

    /// <summary>Marca como lida; a segunda vez não muda nada.</summary>
    public bool MarcarLida(DateTimeOffset agora)
    {
        if (LidaEm is not null) return false;
        LidaEm = agora;
        return true;
    }

    public void MarcarEnviada(DateTimeOffset agora) => EnviadaEm ??= agora;

    /// <summary>Reidratação pela persistência. Não usar em regra de negócio.</summary>
    public static Notificacao Reidratar(Guid id, IdDeEmpresa empresa, Guid idPessoa, string tipo, string titulo, string corpo, string? url,
        IdDeAtivo? ativo, Guid? idValidacao, string? chaveUnica, string canal, DateTimeOffset criadaEm, DateTimeOffset? lidaEm, DateTimeOffset? enviadaEm) =>
        new(id, empresa, idPessoa, tipo, titulo, corpo, url, ativo, idValidacao, chaveUnica, canal, criadaEm, lidaEm, enviadaEm);
}

/// <summary>
/// O que a pessoa quer receber, por tipo e canal (<c>preferencia_notificacao</c>).
/// Sem nada guardado, vale o padrão: todo tipo no sino, nenhum fora dele.
/// </summary>
public sealed record PreferenciasDeNotificacao(Guid IdPessoa, IReadOnlySet<string> Ativas)
{
    public static string Chave(string tipo, string canal) => $"{tipo}:{canal}";

    public static PreferenciasDeNotificacao Padrao(Guid idPessoa) =>
        new(idPessoa, TiposDeNotificacao.Todos.Select(t => Chave(t.Chave, CanaisDeNotificacao.App)).ToHashSet());

    public bool QuerReceber(string tipo, string canal) => Ativas.Contains(Chave(tipo, canal));
}
