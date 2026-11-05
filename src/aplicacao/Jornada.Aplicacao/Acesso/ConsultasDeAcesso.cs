using Jornada.Aplicacao.Portas;
using Jornada.Dominio.Acesso;
using Jornada.Dominio.Catalogo;
using Jornada.Dominio.Comum;

namespace Jornada.Aplicacao.Acesso;

/// <summary>Uma atribuição de papel como a tela mostra: com o escopo pelo nome e os blocos em que o papel escreve.</summary>
public sealed record PapelLegivel(AtribuicaoDePapel Atribuicao, string EscopoRotulo, IReadOnlyList<string> Blocos);

/// <summary>Um pleito com quem pediu, para a fila e para o perfil.</summary>
public sealed record PleitoLegivel(PleitoDeAcesso Pleito, Pessoa? Pessoa, string? EscopoRotulo);

internal static class Legibilidade
{
    /// <summary>"dominio #1" não diz nada a quem lê; o nome do domínio, sim. De <c>acesso.rotulo_escopo</c>.</summary>
    public static async Task<string> EscopoAsync(
        IRepositorioDeAtivos ativos, IRepositorioDeSquads squads, TipoDeEscopo tipo, Guid? id, CancellationToken ct)
    {
        if (tipo == TipoDeEscopo.Empresa || id is null) return "todo o catálogo";
        if (tipo == TipoDeEscopo.Dominio)
        {
            var dominio = await ativos.ObterAsync(new IdDeAtivo(id.Value), ct);
            return dominio is null ? $"domínio {id.Value.ToString()[..8]}" : $"domínio {dominio.Nome}";
        }
        if (tipo == TipoDeEscopo.Squad)
        {
            var squad = await squads.ObterAsync(id.Value, ct);
            return squad is null ? $"squad {id.Value.ToString()[..8]}" : $"squad {squad.Sigla}";
        }
        return $"{tipo.ToString().ToLowerInvariant()} {id.Value.ToString()[..8]}";
    }

    public static async Task<PleitoLegivel> PleitoAsync(
        IRepositorioDeAtivos ativos, IRepositorioDeSquads squads, IRepositorioDePessoas pessoas, PleitoDeAcesso pleito, CancellationToken ct) =>
        new(pleito, await pessoas.ObterAsync(pleito.IdPessoa, ct),
            pleito.EscopoTipo is { } tipo ? await EscopoAsync(ativos, squads, tipo, pleito.EscopoId, ct) : null);
}

/// <summary>
/// "Seu perfil": quem você é, os papéis que valem agora, o que eles deixam
/// fazer e os seus pleitos. Portado da rota <c>/perfil</c> do painel-ddd.
/// </summary>
public sealed class ConsultarPerfil(
    IRepositorioDePessoas pessoas,
    IRepositorioDePleitos pleitos,
    IRepositorioDeAtivos ativos,
    IRepositorioDeSquads squads,
    IAtorAtual atorAtual,
    IRelogio relogio)
{
    public sealed record Saida(
        Ator Ator,
        Pessoa? Pessoa,
        IReadOnlyList<PapelLegivel> Papeis,
        IReadOnlyList<PleitoLegivel> Pleitos,
        IReadOnlySet<string> BlocosQueEscreve,
        bool PodeConceder,
        bool PodeConcederAdmin,
        bool PodeAdministrar);

    public async Task<Saida> ExecutarAsync(CancellationToken ct = default)
    {
        var ator = await atorAtual.ObterAsync(ct);
        var hoje = relogio.Hoje;
        var pessoa = ator.Identificado ? await pessoas.ObterAsync(ator.IdPessoa, ct) : null;

        var papeis = new List<PapelLegivel>();
        foreach (var a in ator.Atribuicoes.Where(a => a.Vigente(hoje)).OrderBy(a => a.Papel))
            papeis.Add(new PapelLegivel(a, await Legibilidade.EscopoAsync(ativos, squads, a.EscopoTipo, a.EscopoId, ct),
                PoliticaDeAutorizacao.BlocosDoPapel(a.Papel)));

        var meus = new List<PleitoLegivel>();
        if (pessoa is not null)
            foreach (var p in (await pleitos.ListarAsync(idPessoa: pessoa.Id, ct: ct)).OrderByDescending(p => p.CriadoEm))
                meus.Add(await Legibilidade.PleitoAsync(ativos, squads, pessoas, p, ct));

        // Em modo de leitura nada escreve, nem a concessão: o menu de quem
        // concede some junto, como o pode() dos templates do painel.
        return new Saida(ator, pessoa, papeis, meus,
            PoliticaDeAutorizacao.BlocosQueEscreve(ator, hoje),
            PoliticaDeAutorizacao.PodeConceder(ator, null, hoje).Permitido,
            PoliticaDeAutorizacao.PodeConceder(ator, Papel.Admin, hoje).Permitido,
            ator.Identificado && ator.Atribuicoes.Any(a => a.Vigente(hoje) && a.Papel == Papel.Admin));
    }
}

/// <summary>A fila de pleitos, para quem concede. Portado da rota <c>/acessos</c>.</summary>
public sealed class ListarPleitos(
    IRepositorioDePleitos pleitos,
    IRepositorioDePessoas pessoas,
    IRepositorioDeAtivos ativos,
    IRepositorioDeSquads squads,
    IAtorAtual atorAtual,
    IRelogio relogio)
{
    public sealed record Saida(IReadOnlyList<PleitoLegivel> Pleitos, int Pendentes, bool PodeConcederAdmin);

    public async Task<Resultado<Saida>> ExecutarAsync(StatusDoPleito? status = null, CancellationToken ct = default)
    {
        var ator = await atorAtual.ObterAsync(ct);
        var alcada = PoliticaDeAutorizacao.PodeConceder(ator, null, relogio.Hoje);
        if (!alcada.Permitido)
            return Recusa.Para<Saida>(ator, alcada.Motivo);

        var lista = new List<PleitoLegivel>();
        foreach (var p in await pleitos.ListarAsync(status, ct: ct))
            lista.Add(await Legibilidade.PleitoAsync(ativos, squads, pessoas, p, ct));

        return Resultado<Saida>.Ok(new Saida(lista, lista.Count(p => p.Pleito.Status == StatusDoPleito.Pendente),
            PoliticaDeAutorizacao.PodeConceder(ator, Papel.Admin, relogio.Hoje).Permitido));
    }
}

/// <summary>
/// As pessoas ativas da empresa. É o seletor "quem é você" do perfil do
/// painel, que vale até a identidade corporativa entrar (ADR-0007).
/// </summary>
public sealed class ListarPessoas(IRepositorioDePessoas pessoas)
{
    public Task<IReadOnlyList<Pessoa>> ExecutarAsync(CancellationToken ct = default) => pessoas.ListarAsync(ct: ct);
}
