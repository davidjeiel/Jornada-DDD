using Jornada.Adaptadores.Memoria;
using Jornada.Aplicacao;
using Jornada.Aplicacao.Catalogo.Comandos;
using Jornada.Aplicacao.Catalogo.Consultas;
using Jornada.Aplicacao.Portas;
using Jornada.Dominio.Acesso;
using Jornada.Dominio.Catalogo;
using Jornada.Dominio.Testes.Harness;

namespace Jornada.Dominio.Testes.Casos;

/// <summary>
/// Portado de <c>tests/test_fluxo.py</c>, <c>test_jornada.py</c> e
/// <c>test_escala.py</c> na parte da ficha: a trilha hierárquica, o caminho até
/// a publicação e as relações (mecanismo, destinos sugeridos, lote).
/// </summary>
[Suite("Ficha do ativo: trilha, caminho até a publicação e relações")]
public sealed class FichaTestes
{
    // ──────────────────────────────────────────────────────────── trilha

    [Teste("a trilha sobe da raiz até o pai direto, sem o próprio ativo")]
    public void TrilhaSobeDaRaizAteOPaiDireto()
    {
        var m = new MundoDaFicha();
        var (dom, sub, ctx, cap) = m.Capacidade();
        var trilha = m.Ficha(cap).Trilha;
        Verificar.Igual($"{dom},{sub},{ctx}", string.Join(",", trilha.Select(a => a.Id)));
        Verificar.Igual("Contrato,Originação,Simulação", string.Join(",", trilha.Select(a => a.Nome)));
    }

    [Teste("a trilha de um ativo raiz é vazia")]
    public void TrilhaDeItemRaizEVazia()
    {
        var m = new MundoDaFicha();
        var (dom, _, _, _) = m.Capacidade();
        Verificar.Vazio(m.Ficha(dom).Trilha);
    }

    // ───────────────────────────────────────────── caminho até a publicação

    [Teste("o checklist mostra o que falta: descrito, mas sem responsáveis nem evidência")]
    public void ChecklistRefleteOQueFalta()
    {
        var m = new MundoDaFicha();
        var (_, _, _, cap) = m.Capacidade();
        var caminho = m.PreCheck(cap).Caminho.ToDictionary(p => p.Chave);

        Verificar.Verdadeiro(caminho["descrever"].Concluido, "descrição e campo obrigatório preenchidos");
        Verificar.Falso(caminho["responsaveis"].Concluido, "faltam o owner negocial e o técnico");
        Verificar.Falso(caminho["evidenciar"].Concluido, "a política de capacidade exige 1 evidência");
        Verificar.Verdadeiro(caminho.Values.Count(p => p.Concluido) < caminho.Count);
        Verificar.Falso(m.PreCheck(cap).Checagem.Aprovado);
    }

    [Teste("o checklist fica completo quando o pré-check aprova")]
    public void ChecklistFicaCompletoQuandoOPreCheckAprova()
    {
        var m = new MundoDaFicha();
        var (_, _, _, cap) = m.Capacidade();
        m.Completar(cap);
        var avaliacao = m.PreCheck(cap);

        Verificar.Verdadeiro(avaliacao.Checagem.Aprovado,
            string.Join("; ", avaliacao.Checagem.Bloqueios.Select(b => b.Mensagem)));
        Verificar.Verdadeiro(avaliacao.Caminho.All(p => p.Concluido));
        Verificar.Igual(5, avaliacao.Caminho.Count);
    }

    // ─────────────────────────────────────────────────────────── relações

    [Teste("o mecanismo e a criticidade chegam ao banco")]
    public void MecanismoChegaAoBanco()
    {
        var m = new MundoDaFicha();
        var (_, _, ctx, _) = m.Capacidade();
        var app = m.Aplicacao();
        Verificar.Verdadeiro(m.Exigir(m.Relacionar(app, ctx, "consome", Criticidade.Alta, "assincrono")), "criada");

        var aresta = m.Repo.ListarRelacoesAsync(app).Result.Single();
        Verificar.Igual("assincrono", aresta.Mecanismo);
        Verificar.Igual(Criticidade.Alta, aresta.Criticidade);
        Verificar.Igual("assincrono", m.Ficha(app).Relacoes.Single().Mecanismo);
    }

    [Teste("tipo de relação e mecanismo desconhecidos são recusados")]
    public void TipoEMecanismoSaoConferidos()
    {
        var m = new MundoDaFicha();
        var (_, _, ctx, cap) = m.Capacidade();
        Verificar.Igual("TIPO_DE_RELACAO_INVALIDO", m.Relacionar(cap, ctx, "inventada").Codigo);
        Verificar.Igual("MECANISMO_INVALIDO", m.Relacionar(cap, ctx, "consome", mecanismo: "pombo").Codigo);
        Verificar.Igual("DESTINO_NAO_ENCONTRADO", m.Relacionar(cap, new IdDeAtivo(Guid.NewGuid()), "consome").Codigo,
            "destino que não existe avisa, em vez de quebrar");
        Verificar.Vazio(m.Repo.ListarRelacoesAsync(cap).Result.Where(r => r.Origem == cap));
    }

    [Teste("a mesma relação não se grava duas vezes")]
    public void RelacaoRepetidaNaoDuplica()
    {
        var m = new MundoDaFicha();
        var (_, _, ctx, _) = m.Capacidade();
        var app = m.Aplicacao();
        Verificar.Verdadeiro(m.Exigir(m.Relacionar(app, ctx, "consome")));
        Verificar.Falso(m.Exigir(m.Relacionar(app, ctx, "consome")), "a segunda é ignorada");
        Verificar.Verdadeiro(m.Exigir(m.Relacionar(app, ctx, "depende_de")), "outro tipo é outra relação");
        Verificar.Igual(2, m.Repo.ListarRelacoesAsync(app).Result.Count);
    }

    [Teste("dependência que fecharia um ciclo é recusada antes de gravar")]
    public void CicloRecusadoAntesDeGravar()
    {
        var m = new MundoDaFicha();
        var (dom, sub, ctx, _) = m.Capacidade();
        m.Exigir(m.Relacionar(dom, sub, "depende_de"));
        m.Exigir(m.Relacionar(sub, ctx, "depende_de"));
        Verificar.Igual("CICLO_DE_DEPENDENCIA", m.Relacionar(ctx, dom, "depende_de").Codigo, "ctx → dom fecharia dom → sub → ctx → dom");
        Verificar.Vazio(m.Repo.ListarRelacoesAsync(ctx).Result.Where(r => r.Origem == ctx), "nada foi gravado");
        Verificar.Verdadeiro(m.Exigir(m.Relacionar(ctx, dom, "consome")), "outro tipo não é dependência");
    }

    [Teste("os destinos sugeridos cobrem todas as relações, com tipos que existem")]
    public void DestinosSugeridosCobremTodasAsRelacoes()
    {
        // A lista que filtra a busca de destino não pode deixar uma relação sem sugestão.
        var tipos = CatalogoDeTipos.Padrao();
        foreach (var relacao in CatalogoDeRelacoes.Tipos)
        {
            Verificar.Verdadeiro(relacao.DestinosSugeridos.Count > 0, relacao.Chave);
            foreach (var destino in relacao.DestinosSugeridos)
                Verificar.Verdadeiro(tipos.Existe(destino), $"{relacao.Chave} → {destino}");
        }
        Verificar.Igual("implementa,expoe,consome,produz,depende_de,persiste_em",
            string.Join(",", CatalogoDeRelacoes.Tipos.Select(t => t.Chave)));
    }

    // ──────────────────────────────────────────────────────────────── lote

    [Teste("o lote cria várias, ignora as que já existem e grava o mecanismo")]
    public void LoteCriaVariasEIgnoraAsExistentes()
    {
        var m = new MundoDaFicha();
        var app = m.Aplicacao();
        var apis = new[] { m.Api(app, "API Um"), m.Api(app, "API Dois"), m.Api(app, "API Três") };

        var primeiro = m.Exigir(m.Lote(app, apis, "expoe", "sincrono"));
        var repetido = m.Exigir(m.Lote(app, apis, "expoe"));
        Verificar.Igual(3, primeiro.Criadas);
        Verificar.Igual(0, repetido.Criadas);
        Verificar.Igual(3, repetido.Ignoradas);
        Verificar.Igual("sincrono", string.Join(",", m.Repo.ListarRelacoesAsync(app).Result
            .Where(r => r.Origem == app).Select(r => r.Mecanismo).Distinct()));
    }

    [Teste("o lote ignora o próprio ativo, e o erro de um destino não para os outros")]
    public void LoteIgnoraOProprioAtivo()
    {
        var m = new MundoDaFicha();
        var (_, _, _, cap) = m.Capacidade();
        var app = m.Aplicacao();
        var r = m.Exigir(m.Lote(app, [app, cap, new IdDeAtivo(Guid.NewGuid())], "implementa"));
        Verificar.Igual(1, r.Criadas);
        Verificar.Igual(1, r.Ignoradas);
        Verificar.Igual("DESTINO_NAO_ENCONTRADO", r.Erros.Single().Codigo);
    }

    [Teste("o lote recusa por inteiro o que vale para todos: sem permissão, tipo inválido")]
    public void LoteRecusaPorInteiro()
    {
        var m = new MundoDaFicha();
        var (_, _, _, cap) = m.Capacidade();
        var app = m.Aplicacao();
        Verificar.Igual("TIPO_DE_RELACAO_INVALIDO", m.Lote(app, [cap], "inventada").Codigo);
        Verificar.Igual("SEM_PERMISSAO", m.Lote(app, [cap], "implementa", quem: MundoDeGovernanca.Negocio).Codigo);
    }
}

/// <summary>Um catálogo em memória com a cadeia Contrato › Originação › Simulação › capacidade.</summary>
internal sealed class MundoDaFicha
{
    private readonly ArmazemEmMemoria _armazem = new();
    private readonly ContextoDeTenantFixo _tenant = ContextoDeTenantFixo.Para(Cenario.EmpresaA);
    private readonly MetamodeloPadrao _meta = new();
    private readonly AtorFixo _curador = new(MundoDeGovernanca.Curador);

    public RepositorioDeAtivosEmMemoria Repo => new(_armazem, _tenant);

    public (IdDeAtivo Dom, IdDeAtivo Sub, IdDeAtivo Ctx, IdDeAtivo Cap) Capacidade()
    {
        var dom = Cadastrar("dominio", "Contrato", null, new() { ["visao"] = "Contratos de crédito" });
        var sub = Cadastrar("subdominio", "Originação", dom, new() { ["classificacao"] = "core" });
        var ctx = Cadastrar("contexto", "Simulação", sub, new() { ["linguagem_ubiqua"] = "Simulação, Taxa" });
        var cap = Cadastrar("capacidade", "Simular Financiamento", ctx, new() { ["resultado_esperado"] = "Proposta simulada" });
        return (dom, sub, ctx, cap);
    }

    public IdDeAtivo Aplicacao()
    {
        var sis = Cadastrar("sistema", "Plataforma de Crédito", null, new() { ["plataforma"] = "nuvem" });
        return Cadastrar("aplicacao", "Motor de Simulação", sis, new() { ["linguagem"] = "Java" });
    }

    public IdDeAtivo Api(IdDeAtivo app, string nome) =>
        Cadastrar("api", nome, app, new() { ["contrato_url"] = "https://api.exemplo/openapi.json" });

    /// <summary>Owners negocial e técnico e uma evidência: o que a capacidade precisa para publicar.</summary>
    public void Completar(IdDeAtivo id)
    {
        foreach (var papel in new[] { PapelDeOwnership.OwnerNegocial, PapelDeOwnership.OwnerTecnico })
            Exigir(new AtribuirResponsavel(Repo, _meta, _tenant, _curador, Repo, Repo, Cenario.Relogio)
                .ExecutarAsync(new ComandoAtribuirResponsavel(id, Guid.NewGuid(), papel)).Result);
        Exigir(new AnexarEvidencia(Repo, _meta, _tenant, _curador, Repo, Repo, Cenario.Relogio)
            .ExecutarAsync(new ComandoAnexarEvidencia(id, "documento", "Manual de crédito", null)).Result);
        Exigir(Relacionar(Aplicacao(), id, "implementa"));
    }

    public ObterVisaoDoAtivo.Saida Ficha(IdDeAtivo id) => Exigir(new ObterVisaoDoAtivo(Repo, Repo).ExecutarAsync(id).Result);

    public AvaliarPreCheck.Saida PreCheck(IdDeAtivo id) =>
        Exigir(new AvaliarPreCheck(Repo, Repo, _meta, new PoliticasPadrao(), Cenario.Relogio).ExecutarAsync(id).Result);

    private RelacionarAtivos Relacionador(Ator? quem = null) =>
        new(Repo, Repo, _meta, _tenant, quem is null ? _curador : new AtorFixo(quem), Repo, Repo, Cenario.Relogio);

    public Resultado<bool> Relacionar(IdDeAtivo origem, IdDeAtivo destino, string tipo,
                                      Criticidade criticidade = Criticidade.Media, string? mecanismo = null) =>
        Relacionador().ExecutarAsync(new ComandoRelacionarAtivos(origem, destino, tipo, criticidade, mecanismo)).Result;

    public Resultado<RelacionarEmLote.Saida> Lote(IdDeAtivo origem, IdDeAtivo[] destinos, string tipo,
                                                  string? mecanismo = null, Ator? quem = null) =>
        new RelacionarEmLote(Relacionador(quem))
            .ExecutarAsync(new ComandoRelacionarEmLote(origem, destinos, tipo, Criticidade.Media, mecanismo)).Result;

    private IdDeAtivo Cadastrar(string tipo, string nome, IdDeAtivo? pai, Dictionary<string, string> atributos) =>
        Exigir(new CadastrarAtivo(Repo, _meta, _tenant, _curador, Repo, Repo, new RepositorioDeAcessoEmMemoria(_armazem, _tenant), Cenario.Relogio)
            .ExecutarAsync(new ComandoCadastrarAtivo(tipo, nome, $"Descrição completa de {nome}.", Criticidade.Media, pai, atributos)).Result);

    public T Exigir<T>(Resultado<T> r) =>
        r.Sucesso ? r.Valor! : throw new FalhaDeVerificacao($"passo do cenário recusado: {r.Codigo} — {r.Mensagem}");
}
