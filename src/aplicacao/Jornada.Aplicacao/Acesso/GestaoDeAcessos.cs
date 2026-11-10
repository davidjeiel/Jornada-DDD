using Jornada.Aplicacao.Portas;
using Jornada.Dominio.Acesso;
using Jornada.Dominio.Comum;

namespace Jornada.Aplicacao.Acesso;

/// <summary>Um cadastro como a gestão de acessos mostra: com os papéis legíveis e o squad pelo nome.</summary>
public sealed record CadastroLegivel(Pessoa Pessoa, IReadOnlyList<PapelLegivel> Papeis, Squad? Squad);

/// <summary>
/// A gestão de acessos do painel-ddd (<c>/gestao-acessos</c>), só para quem
/// administra: busca de cadastros, correção, concessão e revogação direta,
/// inativação e exclusão de cadastro sem vínculo.
///
/// Uma diferença de propósito: no painel, salvar o cadastro troca TODOS os
/// papéis por um papel na empresa inteira (<c>atualizar_pessoa</c>), e um papel
/// de domínio some sem ninguém ter pedido. Aqui a correção dos dados e os papéis
/// são operações separadas: concede-se e revoga-se cada atribuição.
/// </summary>
public sealed class GestaoDeAcessos(
    IRepositorioDePessoas pessoas,
    IRepositorioDePleitos pleitos,
    IRepositorioDeSquads squads,
    IRepositorioDeAtivos ativos,
    IConsultaDeVinculos vinculos,
    IContextoDeTenantAtual tenant,
    IAtorAtual atorAtual,
    IUnidadeDeTrabalho uow,
    IRelogio relogio)
{
    public const string ProprioCadastro = "PROPRIO_CADASTRO";

    // ──────────────────────────────────────────────────────────── consulta

    /// <summary>Todos os cadastros, ativos primeiro, filtrados por qualquer dado visível (<c>acesso.pessoas</c>).</summary>
    public async Task<Resultado<IReadOnlyList<CadastroLegivel>>> ListarAsync(string? busca = null, CancellationToken ct = default)
    {
        if (await RecusaAsync<IReadOnlyList<CadastroLegivel>>(ct) is { } recusa) return recusa;

        var todosOsSquads = (await squads.ListarAsync(ct)).ToDictionary(s => s.Id);
        var termo = (busca ?? "").Trim();
        var lista = new List<CadastroLegivel>();
        foreach (var p in (await pessoas.ListarAsync(incluirInativas: true, ct)).OrderBy(p => p.Ativa ? 0 : 1))
        {
            var squad = p.IdSquad is { } id ? todosOsSquads.GetValueOrDefault(id) : null;
            if (termo.Length > 0 && !Contem(termo, p.Nome, p.Matricula.Valor, p.Email, p.Unidade.Valor, squad?.Titulo, squad?.Sigla))
                continue;
            lista.Add(await LegivelAsync(p, squad, ct));
        }
        return Resultado<IReadOnlyList<CadastroLegivel>>.Ok(lista);
    }

    /// <summary>O que torna o cadastro parte do histórico. Vazio: pode ser excluído. De <c>interacoes_pessoa</c>.</summary>
    public async Task<Resultado<IReadOnlyList<string>>> VinculosAsync(Guid idPessoa, CancellationToken ct = default)
    {
        if (await RecusaAsync<IReadOnlyList<string>>(ct) is { } recusa) return recusa;
        var pessoa = await pessoas.ObterAsync(idPessoa, ct);
        return pessoa is null
            ? Resultado<IReadOnlyList<string>>.Recusado("PESSOA_NAO_ENCONTRADA", "Cadastro não encontrado.")
            : Resultado<IReadOnlyList<string>>.Ok(await VinculosDeAsync(pessoa, ct));
    }

    // ──────────────────────────────────────────────────────────── escrita

    /// <summary>
    /// Cadastra uma pessoa direto, sem pleito, para ela poder ser responsável
    /// por ativos. Nasce ativa e sem papel nenhum: consulta o catálogo, e os
    /// papéis se concedem à parte. A matrícula é única na empresa.
    /// </summary>
    public async Task<Resultado<CadastroLegivel>> CadastrarAsync(
        string? matricula, string? nome, string? email, string? unidade, Guid? idSquad, CancellationToken ct = default)
    {
        if (await RecusaAsync<CadastroLegivel>(ct) is { } recusa) return recusa;

        DadosDeCadastro dados;
        try { dados = DadosDeCadastro.Conferir(matricula, nome, email, unidade); }
        catch (ErroDeDominio e) { return Resultado<CadastroLegivel>.Recusado(e.Codigo, e.Message); }

        if (await pessoas.ObterPorMatriculaAsync(dados.Matricula, ct) is { } existente)
            return Resultado<CadastroLegivel>.Recusado("MATRICULA_JA_CADASTRADA",
                $"A matrícula {dados.Matricula} já está cadastrada ({existente.Nome}). Corrija esse cadastro em vez de criar outro.");

        Squad? squad = null;
        if (idSquad is { } s)
        {
            squad = await squads.ObterAsync(s, ct);
            if (squad is null) return Resultado<CadastroLegivel>.Recusado("SQUAD_NAO_ENCONTRADA", "Squad não encontrada.");
            if (!squad.Ativa) return Resultado<CadastroLegivel>.Recusado("SQUAD_INATIVA", "A squad está inativa.");
        }

        var pessoa = Pessoa.Cadastrar(tenant.Atual.Empresa, dados, relogio);
        if (squad is not null) pessoa.Corrigir(dados.Nome, dados.Email, dados.Unidade.Valor, squad.Id);
        await pessoas.AdicionarAsync(pessoa, ct);
        await uow.ConfirmarAsync(ct);
        return Resultado<CadastroLegivel>.Ok(await LegivelAsync(pessoa, squad, ct));
    }

    /// <summary>Corrige nome, e-mail, unidade e squad; <paramref name="ativa"/> falso inativa e encerra os papéis.</summary>
    public Task<Resultado<CadastroLegivel>> CorrigirAsync(
        Guid idPessoa, string? nome, string? email, string? unidade, Guid? idSquad, bool ativa, CancellationToken ct = default) =>
        AlterarAsync(idPessoa, ct, async (pessoa, ator) =>
        {
            if (!ativa && pessoa.Id == ator.IdPessoa)
                throw new ErroDeDominio(ProprioCadastro, "O administrador conectado não pode inativar o próprio cadastro.");
            if (idSquad is { } s && await squads.ObterAsync(s, ct) is null)
                throw new ErroDeDominio("SQUAD_NAO_ENCONTRADA", "Squad não encontrada.");

            pessoa.Corrigir(nome, email, unidade, idSquad);
            if (!ativa && pessoa.Ativa) pessoa.Inativar(relogio.Hoje);
            else if (ativa && !pessoa.Ativa) pessoa.Reativar();
        });

    /// <summary>Concede um papel direto, fora de pleito. Ninguém concede a si mesmo, nem o admin.</summary>
    public Task<Resultado<CadastroLegivel>> ConcederAsync(
        Guid idPessoa, string? papel, string? escopoTipo, Guid? escopoId, CancellationToken ct = default) =>
        AlterarAsync(idPessoa, ct, async (pessoa, ator) =>
        {
            var (p, escopo) = await ConferirPapelAsync(papel, escopoTipo, escopoId, ct);
            if (pessoa.Id == ator.IdPessoa)
                throw new ErroDeDominio("SEGREGACAO_DE_FUNCAO", "Segregação de função: ninguém concede acesso a si mesmo.");
            if (!pessoa.Ativa)
                throw new ErroDeDominio("CADASTRO_INATIVO", "Reative o cadastro antes de conceder papel.");
            if (pessoa.Vigentes(relogio.Hoje).Any(a => a.Papel == p && a.EscopoTipo == escopo && a.EscopoId == escopoId))
                throw new ErroDeDominio("PAPEL_JA_CONCEDIDO", "A pessoa já tem este papel neste escopo.");
            pessoa.Conceder(new AtribuicaoDePapel(p, escopo, escopo == TipoDeEscopo.Empresa ? null : escopoId,
                relogio.Hoje, ConcedidoPor: ator.IdPessoa));
        });

    /// <summary>Encerra uma atribuição vigente. O próprio administrador não revoga os próprios papéis.</summary>
    public Task<Resultado<CadastroLegivel>> RevogarAsync(
        Guid idPessoa, string? papel, string? escopoTipo, Guid? escopoId, CancellationToken ct = default) =>
        AlterarAsync(idPessoa, ct, async (pessoa, ator) =>
        {
            var (p, escopo) = await ConferirPapelAsync(papel, escopoTipo, escopoId, ct, conferirDominio: false);
            if (pessoa.Id == ator.IdPessoa)
                throw new ErroDeDominio(ProprioCadastro, "O administrador conectado não revoga os próprios papéis.");
            if (!pessoa.Revogar(p, escopo, escopo == TipoDeEscopo.Empresa ? null : escopoId, relogio.Hoje))
                throw new ErroDeDominio("PAPEL_NAO_CONCEDIDO", "A pessoa não tem este papel vigente neste escopo.");
        });

    /// <summary>Inativa vários cadastros de uma vez; o do administrador conectado fica de fora.</summary>
    public async Task<Resultado<int>> InativarEmLoteAsync(IEnumerable<Guid> ids, CancellationToken ct = default)
    {
        if (await RecusaAsync<int>(ct) is { } recusa) return recusa;
        var ator = await atorAtual.ObterAsync(ct);

        var inativados = 0;
        foreach (var id in ids.Distinct().Where(id => id != ator.IdPessoa))
        {
            if (await pessoas.ObterAsync(id, ct) is not { Ativa: true } pessoa) continue;
            pessoa.Inativar(relogio.Hoje);
            inativados++;
        }
        await uow.ConfirmarAsync(ct);
        return Resultado<int>.Ok(inativados);
    }

    /// <summary>Exclui o cadastro que não tem vínculo nenhum (<c>excluir_pessoa</c>). Com vínculo, inativa-se.</summary>
    public async Task<Resultado<bool>> ExcluirAsync(Guid idPessoa, CancellationToken ct = default)
    {
        if (await RecusaAsync<bool>(ct) is { } recusa) return recusa;
        var ator = await atorAtual.ObterAsync(ct);
        if (idPessoa == ator.IdPessoa)
            return Resultado<bool>.Recusado(ProprioCadastro, "O administrador conectado não pode excluir o próprio cadastro.");

        var pessoa = await pessoas.ObterAsync(idPessoa, ct);
        if (pessoa is null)
            return Resultado<bool>.Recusado("PESSOA_NAO_ENCONTRADA", "Cadastro não encontrado.");

        var vinculos = await VinculosDeAsync(pessoa, ct);
        if (vinculos.Count > 0)
            return Resultado<bool>.Recusado("CADASTRO_COM_VINCULOS",
                $"O cadastro não pode ser excluído: {string.Join(", ", vinculos)}. Inative-o.");

        await pessoas.RemoverAsync(pessoa, ct);
        await uow.ConfirmarAsync(ct);
        return Resultado<bool>.Ok(true);
    }

    // ──────────────────────────────────────────────────────────── apoio

    private async Task<Resultado<CadastroLegivel>> AlterarAsync(
        Guid idPessoa, CancellationToken ct, Func<Pessoa, Ator, Task> alteracao)
    {
        if (await RecusaAsync<CadastroLegivel>(ct) is { } recusa) return recusa;
        var pessoa = await pessoas.ObterAsync(idPessoa, ct);
        if (pessoa is null)
            return Resultado<CadastroLegivel>.Recusado("PESSOA_NAO_ENCONTRADA", "Cadastro não encontrado.");

        try { await alteracao(pessoa, await atorAtual.ObterAsync(ct)); }
        catch (ErroDeDominio e) { return Resultado<CadastroLegivel>.Recusado(e.Codigo, e.Message); }

        await uow.ConfirmarAsync(ct);
        var squad = pessoa.IdSquad is { } s ? await squads.ObterAsync(s, ct) : null;
        return Resultado<CadastroLegivel>.Ok(await LegivelAsync(pessoa, squad, ct));
    }

    /// <summary>Gestão de acessos é do administrador (<c>@exige("administrar")</c>).</summary>
    private async Task<Resultado<T>?> RecusaAsync<T>(CancellationToken ct)
    {
        var ator = await atorAtual.ObterAsync(ct);
        return PoliticaDeAutorizacao.Pode(ator, Acao.Administrar, AlvoDaAcao.NaEmpresa(tenant.Atual.Empresa), relogio.Hoje)
            ? null
            : Recusa.Para<T>(ator, "A gestão de acessos é atribuição do administrador.");
    }

    private async Task<(Papel, TipoDeEscopo)> ConferirPapelAsync(
        string? papel, string? escopoTipo, Guid? escopoId, CancellationToken ct, bool conferirDominio = true)
    {
        if (!PapelExtensoes.TentarDe(papel, out var p))
            throw new ErroDeDominio("PAPEL_INVALIDO", $"Papel desconhecido: {papel}.");
        var escopo = Escopos.De(escopoTipo)
            ?? throw new ErroDeDominio("ESCOPO_INVALIDO", $"Escopo desconhecido: {escopoTipo}. Use empresa, dominio ou squad.");
        if (escopo != TipoDeEscopo.Empresa && escopoId is null)
            throw new ErroDeDominio("ESCOPO_SEM_ALVO", $"O escopo {escopo.ToString().ToLowerInvariant()} exige um alvo.");
        if (conferirDominio && escopo == TipoDeEscopo.Dominio && !await Escopos.EhDominioAsync(ativos, escopoId!.Value, ct))
            throw new ErroDeDominio("ESCOPO_INVALIDO", "O escopo de domínio tem de apontar para um domínio do catálogo.");
        if (conferirDominio && escopo == TipoDeEscopo.Squad && !await Escopos.EhSquadAtivaAsync(squads, escopoId!.Value, ct))
            throw new ErroDeDominio("ESCOPO_INVALIDO", "O escopo de squad tem de apontar para uma squad ativa.");
        return (p, escopo);
    }

    private async Task<IReadOnlyList<string>> VinculosDeAsync(Pessoa pessoa, CancellationToken ct)
    {
        var lista = new List<string>();
        if (pessoa.Atribuicoes.Count > 0) lista.Add("papéis atribuídos");
        if ((await pleitos.ListarAsync(idPessoa: pessoa.Id, ct: ct)).Count > 0) lista.Add("pleitos de acesso");
        lista.AddRange(await vinculos.DaPessoaAsync(pessoa.Id, ct));
        return lista;
    }

    private async Task<CadastroLegivel> LegivelAsync(Pessoa pessoa, Squad? squad, CancellationToken ct)
    {
        var papeis = new List<PapelLegivel>();
        foreach (var a in pessoa.Vigentes(relogio.Hoje).OrderBy(a => a.Papel))
            papeis.Add(new PapelLegivel(a, await Legibilidade.EscopoAsync(ativos, squads, a.EscopoTipo, a.EscopoId, ct),
                PoliticaDeAutorizacao.BlocosDoPapel(a.Papel)));
        return new CadastroLegivel(pessoa, papeis, squad);
    }

    private static bool Contem(string termo, params string?[] campos) =>
        campos.Any(c => c is not null && c.Contains(termo, StringComparison.CurrentCultureIgnoreCase));
}

/// <summary>O escopo de uma concessão, como chega da API.</summary>
internal static class Escopos
{
    public static TipoDeEscopo? De(string? texto) => (texto ?? "").Trim().ToLowerInvariant() switch
    {
        "" or "empresa" or "global" => TipoDeEscopo.Empresa,
        "dominio" => TipoDeEscopo.Dominio,
        "squad" => TipoDeEscopo.Squad,
        _ => null,
    };

    public static async Task<bool> EhSquadAtivaAsync(IRepositorioDeSquads squads, Guid id, CancellationToken ct) =>
        await squads.ObterAsync(id, ct) is { Ativa: true };

    public static async Task<bool> EhDominioAsync(IRepositorioDeAtivos ativos, Guid id, CancellationToken ct) =>
        await ativos.ObterAsync(new Dominio.Catalogo.IdDeAtivo(id), ct) is { } a
        && a.TipoItem.Equals(AlvoDoAtivo.TipoDominio, StringComparison.OrdinalIgnoreCase);
}
