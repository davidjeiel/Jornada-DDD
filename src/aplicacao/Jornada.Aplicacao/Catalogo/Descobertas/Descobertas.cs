using System.Text.Json;
using Jornada.Aplicacao.Acesso;
using Jornada.Aplicacao.Catalogo.Consultas;
using Jornada.Aplicacao.Portas;
using Jornada.Dominio.Acesso;
using Jornada.Dominio.Catalogo;
using Jornada.Dominio.Comum;

namespace Jornada.Aplicacao.Catalogo.Descobertas;

/// <summary>
/// O que uma fonte de descoberta trouxe, lido do JSON colado: a API e as
/// operações de um contrato OpenAPI, ou os repositórios de um inventário Git.
/// Ler não depende do catálogo; é o que a prévia e a importação compartilham.
/// </summary>
public sealed record ConteudoDescoberto(string Titulo, string Versao, string Descricao, IReadOnlyList<ConteudoDescoberto.Item> Itens)
{
    /// <param name="Tipo">api, endpoint ou repositorio.</param>
    /// <param name="Detalhe">O que a prévia mostra ao lado do nome.</param>
    public sealed record Item(string Tipo, string Nome, string Descricao, string Detalhe, IReadOnlyDictionary<string, string> Atributos);

    /// <summary>Os métodos que viram endpoint (<c>integracoes.METODOS</c>). OPTIONS e HEAD ficam de fora.</summary>
    public static readonly IReadOnlyList<string> Metodos = ["GET", "POST", "PUT", "PATCH", "DELETE"];

    public static Resultado<ConteudoDescoberto> Ler(string fonte, string? conteudo)
    {
        JsonElement raiz;
        try
        {
            using var documento = JsonDocument.Parse(conteudo ?? "");
            raiz = documento.RootElement.Clone();
        }
        catch (JsonException e)
        {
            return Resultado<ConteudoDescoberto>.Recusado("CONTEUDO_INVALIDO",
                $"Não consegui ler esse conteúdo como JSON: {e.Message.Split(" Path:")[0]}");
        }

        return fonte switch
        {
            FontesDeDescoberta.OpenApi => DoOpenApi(raiz),
            FontesDeDescoberta.Git => DoInventario(raiz),
            _ => Resultado<ConteudoDescoberto>.Recusado("FONTE_INVALIDA", $"Fonte desconhecida: {fonte}. Use openapi ou git."),
        };
    }

    private static string Texto(JsonElement objeto, string campo) =>
        objeto.ValueKind == JsonValueKind.Object && objeto.TryGetProperty(campo, out var v)
            ? v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : v.ValueKind is JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False ? v.ToString() : ""
            : "";

    private static Resultado<ConteudoDescoberto> DoOpenApi(JsonElement spec)
    {
        if (spec.ValueKind != JsonValueKind.Object)
            return Resultado<ConteudoDescoberto>.Recusado("CONTEUDO_INVALIDO", "O contrato OpenAPI tem de ser um objeto JSON, com info e paths.");

        var info = spec.TryGetProperty("info", out var i) ? i : default;
        var titulo = Texto(info, "title") is { Length: > 0 } t ? t.Trim() : "API sem título";
        var versao = Texto(info, "version") is { Length: > 0 } v ? v : "1.0";
        var itens = new List<Item>
        {
            new("api", titulo, Texto(info, "description") is { Length: > 0 } d ? d : "Descoberta via OpenAPI", $"versão {versao}",
                new Dictionary<string, string> { ["versao"] = versao, ["estilo"] = "REST" }),
        };

        if (spec.TryGetProperty("paths", out var caminhos) && caminhos.ValueKind == JsonValueKind.Object)
            foreach (var rota in caminhos.EnumerateObject())
            {
                if (rota.Value.ValueKind != JsonValueKind.Object) continue;
                foreach (var operacao in rota.Value.EnumerateObject())
                {
                    var metodo = operacao.Name.ToUpperInvariant();
                    if (!Metodos.Contains(metodo)) continue;
                    var resumo = Texto(operacao.Value, "summary");
                    itens.Add(new("endpoint", $"{metodo} {rota.Name}", resumo is { Length: > 0 } ? resumo : "Descoberto via OpenAPI",
                        resumo is { Length: > 0 } ? resumo : "—", new Dictionary<string, string> { ["metodo"] = metodo, ["caminho"] = rota.Name }));
                }
            }
        return Resultado<ConteudoDescoberto>.Ok(new(titulo, versao, itens[0].Descricao, itens));
    }

    private static Resultado<ConteudoDescoberto> DoInventario(JsonElement lista)
    {
        if (lista.ValueKind != JsonValueKind.Array)
            return Resultado<ConteudoDescoberto>.Recusado("CONTEUDO_INVALIDO",
                "O inventário Git é uma lista JSON: [{\"nome\": \"...\", \"url\": \"...\", \"linguagem\": \"...\"}].");

        var itens = new List<Item>();
        foreach (var repo in lista.EnumerateArray())
        {
            var nome = Texto(repo, "nome").Trim();
            if (nome.Length == 0)
                return Resultado<ConteudoDescoberto>.Recusado("CONTEUDO_INVALIDO", "Todo repositório do inventário precisa de nome.");
            var atributos = new Dictionary<string, string>
            {
                ["url"] = Texto(repo, "url"),
                ["branch_principal"] = Texto(repo, "branch") is { Length: > 0 } b ? b : "main",
                ["linguagem"] = Texto(repo, "linguagem"),
                ["ultima_atividade"] = Texto(repo, "ultima_atividade"),
            };
            var detalhe = atributos["linguagem"] is { Length: > 0 } l ? l : atributos["url"] is { Length: > 0 } u ? u : "—";
            itens.Add(new("repositorio", nome, Texto(repo, "descricao") is { Length: > 0 } d ? d : "Descoberto no provedor Git", detalhe, atributos));
        }
        return Resultado<ConteudoDescoberto>.Ok(new($"{itens.Count} repositório(s)", "", "", itens));
    }
}

/// <summary>As fontes de descoberta (<c>integracoes.FONTES</c>).</summary>
public static class FontesDeDescoberta
{
    public const string OpenApi = "openapi";
    public const string Git = "git";

    public sealed record Fonte(string Chave, string Rotulo, string Ajuda, string TipoDoPai);

    public static readonly IReadOnlyList<Fonte> Todas =
    [
        new(OpenApi, "Contrato OpenAPI", "JSON do contrato. Cria a API e um endpoint por operação.", "aplicacao"),
        new(Git, "Inventário Git", "Lista JSON: [{\"nome\": \"...\", \"url\": \"...\", \"linguagem\": \"...\"}]. Cria um repositório por item.", "aplicacao"),
    ];
}

/// <summary>
/// A prévia e a importação (<c>integracoes.plano_*</c> e <c>importar_*</c>). A
/// prévia diz o que a importação faria e não grava nada; a importação cria em
/// rascunho, com origem automática, o que ainda não existe. A semântica
/// (domínio, capacidade, criticidade, dono) fica para uma pessoa, na triagem.
/// </summary>
public sealed class ImportarDescobertas(
    IRepositorioDeAtivos ativos,
    IMetamodelo metamodelo,
    IContextoDeTenantAtual tenant,
    IAtorAtual atorAtual,
    IUnidadeDeTrabalho uow,
    ITrilhaDeAuditoria trilha,
    IRelogio relogio)
{
    /// <param name="Acao">criar, manter (já existe) ou atualizar (repositório que já existe e recebe os atributos novos).</param>
    public sealed record AcaoDoPlano(string Acao, string Tipo, string Nome, string Detalhe);

    public sealed record Plano(string Fonte, string Titulo, string Versao, IReadOnlyList<AcaoDoPlano> Acoes, IdDeAtivo? IdPai)
    {
        public int Criar => Acoes.Count(a => a.Acao == "criar");
        public int Manter => Acoes.Count(a => a.Acao != "criar");
    }

    public sealed record Saida(int Criados, int Atualizados, int Mantidos, IReadOnlyList<IdDeAtivo> Novos);

    public async Task<Resultado<Plano>> PlanejarAsync(string fonte, string? conteudo, IdDeAtivo? idPai, CancellationToken ct = default)
    {
        var preparo = await PrepararAsync(fonte, conteudo, idPai, ct);
        if (!preparo.Sucesso) return Resultado<Plano>.Recusado(preparo.Codigo!, preparo.Mensagem!);
        var (lido, tipos, _) = preparo.Valor!;

        var acoes = new List<AcaoDoPlano>();
        foreach (var item in lido.Itens)
        {
            var existe = await ativos.ObterPorNomeAsync(item.Tipo, item.Nome, ct) is not null;
            acoes.Add(new(existe ? (item.Tipo == "repositorio" ? "atualizar" : "manter") : "criar",
                tipos.Obter(item.Tipo).Rotulo, item.Nome, item.Detalhe));
        }
        return Resultado<Plano>.Ok(new Plano(fonte, lido.Titulo, lido.Versao, acoes, idPai));
    }

    public async Task<Resultado<Saida>> ImportarAsync(string fonte, string? conteudo, IdDeAtivo? idPai, CancellationToken ct = default)
    {
        var preparo = await PrepararAsync(fonte, conteudo, idPai, ct);
        if (!preparo.Sucesso) return Resultado<Saida>.Recusado(preparo.Codigo!, preparo.Mensagem!);
        var (lido, tipos, pai) = preparo.Valor!;
        var ator = await atorAtual.ObterAsync(ct);

        int criados = 0, atualizados = 0, mantidos = 0;
        var novos = new List<IdDeAtivo>();
        IdDeAtivo? idDaApi = null;

        foreach (var item in lido.Itens)
        {
            var tipo = tipos.Obter(item.Tipo);
            if (await ativos.ObterPorNomeAsync(item.Tipo, item.Nome, ct) is { } existente)
            {
                if (item.Tipo == "api") idDaApi = existente.Id;
                if (item.Tipo == "repositorio" && existente.Status is StatusCicloVida.Rascunho or StatusCicloVida.EmRevisao)
                {
                    foreach (var (chave, valor) in item.Atributos.Where(a => a.Value.Length > 0))
                        existente.DefinirAtributo(chave, valor, relogio);
                    await Auditar(existente, ator, fonte, ct);
                    atualizados++;
                }
                else mantidos++;
                continue;
            }

            // A API fica sob a aplicação escolhida; o endpoint, sob a API do mesmo contrato.
            var paiDoItem = item.Tipo == "endpoint" ? idDaApi : pai?.Id;
            var codigo = CodigoDeAtivo.Gerar(tipo.Prefixo, await ativos.ProximoSequencialAsync(tipo.Chave, ct));
            var novo = Ativo.Rascunhar(tenant.Atual.Empresa, tipo, codigo, item.Nome, relogio, item.Descricao, Criticidade.Media,
                paiDoItem, tenant.Atual.Unidade, item.Atributos.Where(a => a.Value.Length > 0).ToDictionary(),
                OrigemDoAtivo.Automatica, pai?.IdSquad, ator.IdPessoa);

            if (item.Tipo == "api")
            {
                novo.AnexarEvidencia("openapi", $"Contrato OpenAPI {lido.Versao}", null, relogio);
                idDaApi = novo.Id;
            }
            if (item.Tipo == "repositorio" && item.Atributos["url"] is { Length: > 0 } url)
                novo.AnexarEvidencia("repositorio", "URL do repositório", url, relogio);

            await ativos.AdicionarAsync(novo, ct);
            await trilha.RegistrarAsync(RegistroDeAuditoria.De(novo, AcoesAuditadas.Criar, ator.IdPessoa, relogio,
                depois: new Dictionary<string, string> { ["codigo"] = codigo.Valor, ["nome"] = novo.Nome, ["origem"] = fonte }), ct);
            novos.Add(novo.Id);
            criados++;
        }

        await uow.ConfirmarAsync(ct);
        return Resultado<Saida>.Ok(new Saida(criados, atualizados, mantidos, novos));
    }

    private Task Auditar(Ativo ativo, Ator ator, string fonte, CancellationToken ct) =>
        trilha.RegistrarAsync(RegistroDeAuditoria.De(ativo, AcoesAuditadas.Importar, ator.IdPessoa, relogio,
            depois: new Dictionary<string, string> { ["fonte"] = fonte }), ct);

    /// <summary>Lê o conteúdo, confere o pai e quem importa. A prévia exige o mesmo que a importação.</summary>
    private async Task<Resultado<(ConteudoDescoberto Lido, CatalogoDeTipos Tipos, Ativo? Pai)>> PrepararAsync(
        string fonte, string? conteudo, IdDeAtivo? idPai, CancellationToken ct)
    {
        var lido = ConteudoDescoberto.Ler(fonte, conteudo);
        if (!lido.Sucesso) return Resultado<(ConteudoDescoberto, CatalogoDeTipos, Ativo?)>.Recusado(lido.Codigo!, lido.Mensagem!);

        var tipos = await metamodelo.ObterAsync(ct);
        var tipoDoPai = FontesDeDescoberta.Todas.Single(f => f.Chave == fonte).TipoDoPai;
        Ativo? pai = null;
        if (idPai is { } id)
        {
            pai = await ativos.ObterAsync(id, ct);
            if (pai is null)
                return Resultado<(ConteudoDescoberto, CatalogoDeTipos, Ativo?)>.Recusado("PAI_NAO_ENCONTRADO", "Item pai não encontrado.");
            if (!pai.TipoItem.Equals(tipoDoPai, StringComparison.OrdinalIgnoreCase))
                return Resultado<(ConteudoDescoberto, CatalogoDeTipos, Ativo?)>.Recusado("HIERARQUIA_INVALIDA",
                    $"O que esta fonte descobre fica sob uma {tipos.Obter(tipoDoPai).Rotulo.ToLowerInvariant()}, e não sob {pai.TipoItem}.");
        }

        var ator = await atorAtual.ObterAsync(ct);
        var tipoPrincipal = tipos.Obter(lido.Valor!.Itens.FirstOrDefault()?.Tipo ?? (fonte == FontesDeDescoberta.Git ? "repositorio" : "api"));
        var alvo = await AlvoDoAtivo.DoCadastroAsync(ativos, tenant.Atual, tipoPrincipal, idPai, ct) with { IdSquad = pai?.IdSquad };
        if (!PoliticaDeAutorizacao.Pode(ator, Acao.Importar, alvo, relogio.Hoje))
            return Recusa.Para<(ConteudoDescoberto, CatalogoDeTipos, Ativo?)>(ator, "Importar descobertas é de tech lead, curador ou administrador.");

        return Resultado<(ConteudoDescoberto, CatalogoDeTipos, Ativo?)>.Ok((lido.Valor!, tipos, pai));
    }
}

/// <summary>
/// A bandeja e a triagem (<c>servicos.descobertas</c> e <c>triar</c>). Na
/// bandeja, o que a máquina trouxe e ninguém olhou: origem automática, em
/// rascunho. Aceitar só tira da bandeja (vira cadastro humano, sem mexer no
/// ciclo de vida); descartar arquiva, guardando a trilha. Cadastro humano não
/// é tocado.
/// </summary>
public sealed class TriarDescobertas(
    IRepositorioDeAtivos ativos,
    IMetamodelo metamodelo,
    IContextoDeTenantAtual tenant,
    IAtorAtual atorAtual,
    IUnidadeDeTrabalho uow,
    ITrilhaDeAuditoria trilha,
    IRelogio relogio)
{
    public const int Limite = 200;
    public const string Aceitar = "aceitar";
    public const string Descartar = "descartar";

    /// <param name="Ignorados">Os que não eram descoberta pendente, ou que a pessoa não pode triar.</param>
    public sealed record Saida(string Acao, int Tratados, int Ignorados);

    public async Task<IReadOnlyList<Ativo>> BandejaAsync(CancellationToken ct = default) =>
        (await ativos.BuscarAsync(new FiltroDoCatalogo(Status: StatusCicloVida.Rascunho, Origem: OrigemDoAtivo.Automatica), BuscarNoCatalogo.Teto, ct))
            .OrderByDescending(a => a.AtualizadoEm).ThenBy(a => a.Codigo.Valor, StringComparer.Ordinal)
            .Take(Limite)
            .ToArray();

    public async Task<Resultado<Saida>> TriarAsync(IReadOnlyCollection<IdDeAtivo> ids, string? acao, CancellationToken ct = default)
    {
        var escolhida = (acao ?? "").Trim().ToLowerInvariant();
        if (escolhida is not (Aceitar or Descartar))
            return Resultado<Saida>.Recusado("ACAO_DE_TRIAGEM_INVALIDA", $"Ação de triagem inválida: {acao}. Use aceitar ou descartar.");

        var ator = await atorAtual.ObterAsync(ct);
        if (!ator.Identificado || ator.ModoLeitura) return Recusa.Para<Saida>(ator, "");
        var tipos = await metamodelo.ObterAsync(ct);

        int tratados = 0, ignorados = 0, semPermissao = 0;
        foreach (var id in ids.Distinct())
        {
            var ativo = await ativos.ObterAsync(id, ct);
            if (ativo is null || ativo.Origem != OrigemDoAtivo.Automatica || ativo.Status != StatusCicloVida.Rascunho)
            {
                ignorados++;
                continue;
            }
            var alvo = await AlvoDoAtivo.ResolverAsync(ativos, tenant.Atual, ativo, tipos.Obter(ativo.TipoItem), ct);
            if (!PoliticaDeAutorizacao.Pode(ator, Acao.Importar, alvo, relogio.Hoje))
            {
                semPermissao++;
                continue;
            }

            if (escolhida == Aceitar) ativo.AceitarDescoberta(relogio);
            else ativo.Arquivar(relogio);
            await trilha.RegistrarAsync(RegistroDeAuditoria.De(ativo,
                escolhida == Aceitar ? AcoesAuditadas.TriagemAceitar : AcoesAuditadas.TriagemDescartar, ator.IdPessoa, relogio), ct);
            tratados++;
        }

        if (tratados == 0 && semPermissao > 0)
            return Recusa.Para<Saida>(ator, "Triar descobertas é de tech lead, curador ou administrador.");
        if (tratados > 0) await uow.ConfirmarAsync(ct);
        return Resultado<Saida>.Ok(new Saida(escolhida, tratados, ignorados + semPermissao));
    }
}
