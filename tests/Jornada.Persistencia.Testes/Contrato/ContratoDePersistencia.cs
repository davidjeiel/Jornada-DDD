using Jornada.Aplicacao.Portas;
using Jornada.Dominio.Acesso;
using Jornada.Dominio.Catalogo;
using Jornada.Dominio.Comum;
using Jornada.Dominio.Governanca;
using Jornada.Dominio.Tenancy;
using Jornada.Dominio.Testes.Harness;

namespace Jornada.Persistencia.Testes.Contrato;

/// <summary>
/// O CONTRATO das portas de persistência: o que os casos de uso esperam de
/// qualquer adaptador, escrito uma vez só. Cada adaptador herda esta classe e
/// implementa <see cref="Abrir"/>; os testes são os mesmos para todos.
///
/// Os testes de caso de uso rodam sobre o adaptador em memória. Se ele se
/// comportar diferente do banco real, os testes passam e o defeito aparece em
/// produção. Esta suíte é a ponte entre os dois: o adaptador Cosmos DB e o de
/// memória passam pelos mesmos testes, e nenhum caso de uso percebe a troca.
///
/// Cada teste usa empresas NOVAS (<see cref="IdDeEmpresa.Novo"/>). Assim os
/// testes não dependem de limpar o banco entre um e outro, e o isolamento
/// entre empresas é exercitado de graça em todos eles.
///
/// Fora do contrato, de propósito: no adaptador em memória uma alteração no
/// agregado vale antes do <c>ConfirmarAsync</c> (o objeto é o mesmo do
/// armazém); no Cosmos, só depois. Nenhum caso de uso depende disso hoje.
/// </summary>
public abstract class ContratoDePersistencia
{
    private static readonly RelogioFixo Relogio = RelogioFixo.Em(2026, 10, 21);
    private static readonly CatalogoDeTipos Tipos = CatalogoDeTipos.Padrao();

    /// <summary>Uma "requisição" nova, no contexto da empresa informada.</summary>
    protected abstract Sessao Abrir(IdDeEmpresa empresa);

    // ── ativos: o que entra, sai igual ───────────────────────────────────────

    [Teste("ativo gravado volta com todos os campos numa sessão nova")]
    public async Task AtivoVoltaCompleto()
    {
        var empresa = IdDeEmpresa.Novo();
        var unidade = IdDeUnidade.Novo();
        var pessoa = Guid.NewGuid();
        var squad = Guid.NewGuid();

        var ativo = Ativo.Rascunhar(empresa, Tipos.Obter("sistema"), CodigoDeAtivo.De("SIS-0001"),
            "Core Bancário", Relogio, descricao: "Contas e lançamentos.",
            criticidade: Criticidade.Alta, unidade: unidade,
            atributos: new Dictionary<string, string> { ["plataforma"] = "mainframe" }, idSquad: squad);
        ativo.AtribuirResponsavel(pessoa, PapelDeOwnership.OwnerTecnico, Relogio);
        ativo.AnexarEvidencia("adr", "ADR-0001", "https://exemplo/adr-0001", Relogio);
        await Gravar(empresa, ativo);

        await using var s = Abrir(empresa);
        var lido = await s.Ativos.ObterAsync(ativo.Id) ?? throw Ausente("ativo");

        Verificar.Igual(empresa, lido.Empresa);
        Verificar.Igual(unidade, lido.Unidade);
        Verificar.Igual<Guid?>(squad, lido.IdSquad);
        Verificar.Igual("sistema", lido.TipoItem);
        Verificar.Igual(ativo.Codigo, lido.Codigo);
        Verificar.Igual("Core Bancário", lido.Nome);
        Verificar.Igual("Contas e lançamentos.", lido.Descricao);
        Verificar.Igual(Criticidade.Alta, lido.Criticidade);
        Verificar.Igual(StatusCicloVida.Rascunho, lido.Status);
        Verificar.Igual(ativo.CriadoEm, lido.CriadoEm);
        Verificar.Igual("mainframe", lido.Atributos.GetValueOrDefault("plataforma"));
        Verificar.Igual(null, lido.UltimaRevisaoPublicada);

        var responsavel = lido.Responsaveis.Single();
        Verificar.Igual(pessoa, responsavel.IdPessoa);
        Verificar.Igual(PapelDeOwnership.OwnerTecnico, responsavel.Papel);

        var evidencia = lido.Evidencias.Single();
        Verificar.Igual("ADR-0001", evidencia.Titulo);
        Verificar.Igual("https://exemplo/adr-0001", evidencia.Url);
    }

    [Teste("ativo é encontrado pelo código")]
    public async Task AtivoPorCodigo()
    {
        var empresa = IdDeEmpresa.Novo();
        var ativo = await Gravar(empresa, Novo(empresa, "sistema", "SIS-0007", "Pagamentos"));

        await using var s = Abrir(empresa);
        var lido = await s.Ativos.ObterPorCodigoAsync(CodigoDeAtivo.De("sis-0007"));

        Verificar.Igual(ativo.Id, lido?.Id);
    }

    [Teste("alteração confirmada persiste, inclusive o ciclo de vida")]
    public async Task AlteracaoConfirmadaPersiste()
    {
        var empresa = IdDeEmpresa.Novo();
        var ativo = await Gravar(empresa, Completo(empresa, "SIS-0001", "Core"));

        await using (var s = Abrir(empresa))
        {
            var lido = await s.Ativos.ObterAsync(ativo.Id) ?? throw Ausente("ativo");
            lido.Renomear("Core Bancário", Relogio);
            lido.Submeter("v1", Guid.NewGuid(), Relogio);
            lido.Publicar(Relogio);
            await s.Unidade.ConfirmarAsync();
        }

        await using var nova = Abrir(empresa);
        var depois = await nova.Ativos.ObterAsync(ativo.Id) ?? throw Ausente("ativo");
        Verificar.Igual("Core Bancário", depois.Nome);
        Verificar.Igual(StatusCicloVida.Publicado, depois.Status);
        Verificar.Igual(depois.RevisaoAtual, depois.UltimaRevisaoPublicada,
            "a revisão publicada é o que permite rejeitar uma revisão sem despublicar (ADR-0015)");
    }

    [Teste("confirmar drena os eventos dos agregados (ponto único da outbox)")]
    public async Task ConfirmarDrenaEventos()
    {
        var empresa = IdDeEmpresa.Novo();
        var ativo = Novo(empresa, "sistema", "SIS-0001", "Core");
        Verificar.Verdadeiro(ativo.EventosPendentes.Count > 0, "rascunhar registra AtivoCadastrado");

        await Gravar(empresa, ativo);

        Verificar.Vazio(ativo.EventosPendentes, "evento que fica no agregado nunca chega à outbox");
    }

    // ── ativos: consultas ────────────────────────────────────────────────────

    [Teste("listar ordena por código e filtra por tipo, status e limite")]
    public async Task ListarFiltraEOrdena()
    {
        var empresa = IdDeEmpresa.Novo();
        await Gravar(empresa, Novo(empresa, "sistema", "SIS-0002", "Sistema B"));
        await Gravar(empresa, Novo(empresa, "aplicacao", "APP-0001", "Aplicação A"));
        var publicado = await Gravar(empresa, Publicado(empresa, "SIS-0001", "Sistema A"));

        await using var s = Abrir(empresa);

        Verificar.Igual("APP-0001,SIS-0001,SIS-0002", Codigos(await s.Ativos.ListarAsync()));
        Verificar.Igual("SIS-0001,SIS-0002", Codigos(await s.Ativos.ListarAsync(tipoItem: "Sistema")),
            "o tipo não diferencia maiúsculas");
        Verificar.Igual(publicado.Codigo.Valor,
            Codigos(await s.Ativos.ListarAsync(status: StatusCicloVida.Publicado)));
        Verificar.Igual("APP-0001,SIS-0001", Codigos(await s.Ativos.ListarAsync(limite: 2)));
    }

    [Teste("nome duplicado é detectado no mesmo tipo, sem diferenciar maiúsculas")]
    public async Task NomeDuplicado()
    {
        var empresa = IdDeEmpresa.Novo();
        var ativo = await Gravar(empresa, Novo(empresa, "sistema", "SIS-0001", "Core Bancário"));

        await using var s = Abrir(empresa);

        Verificar.Verdadeiro(await s.Ativos.ExisteNomeDuplicadoAsync("sistema", "core bancário"));
        Verificar.Falso(await s.Ativos.ExisteNomeDuplicadoAsync("aplicacao", "Core Bancário"),
            "o mesmo nome em outro tipo não é duplicado");
        Verificar.Falso(await s.Ativos.ExisteNomeDuplicadoAsync("sistema", "Core Bancário", exceto: ativo.Id),
            "o próprio ativo não conta (edição)");
    }

    [Teste("nome duplicado com acento maiúsculo também é detectado")]
    public async Task NomeDuplicadoComAcento()
    {
        var empresa = IdDeEmpresa.Novo();
        await Gravar(empresa, Novo(empresa, "sistema", "SIS-0001", "Órgão Central"));

        await using var s = Abrir(empresa);

        Verificar.Verdadeiro(await s.Ativos.ExisteNomeDuplicadoAsync("sistema", "órgão central"),
            "a regra é de negócio; não pode depender do locale do banco");
    }

    [Teste("sequencial é contado por empresa e por tipo")]
    public async Task SequencialPorEmpresaETipo()
    {
        var a = IdDeEmpresa.Novo();
        var b = IdDeEmpresa.Novo();

        await using (var s = Abrir(a))
        {
            Verificar.Igual(1, await s.Ativos.ProximoSequencialAsync("sistema"));
            Verificar.Igual(2, await s.Ativos.ProximoSequencialAsync("SISTEMA"));
            Verificar.Igual(1, await s.Ativos.ProximoSequencialAsync("aplicacao"));
        }

        await using var sb = Abrir(b);
        Verificar.Igual(1, await sb.Ativos.ProximoSequencialAsync("sistema"),
            "a numeração de uma empresa não avança a de outra");
    }

    [Teste("trilha vai da raiz até o ativo")]
    public async Task TrilhaDaRaizAoAtivo()
    {
        var empresa = IdDeEmpresa.Novo();
        var sistema = await Gravar(empresa, Novo(empresa, "sistema", "SIS-0001", "Sistema"));
        var aplicacao = await Gravar(empresa, Novo(empresa, "aplicacao", "APP-0001", "Aplicação", sistema.Id));
        var repositorio = await Gravar(empresa, Novo(empresa, "repositorio", "REP-0001", "Repositório", aplicacao.Id));

        await using var s = Abrir(empresa);
        var trilha = await s.Ativos.TrilhaAsync(repositorio.Id);

        Verificar.Igual("SIS-0001,APP-0001,REP-0001", Codigos(trilha));
    }

    // ── ativos: isolamento entre empresas ───────────────────────────────────

    [Teste("empresa não enxerga ativo de outra por nenhuma consulta")]
    public async Task AtivoIsoladoPorEmpresa()
    {
        var a = IdDeEmpresa.Novo();
        var b = IdDeEmpresa.Novo();
        var ativo = await Gravar(a, Novo(a, "sistema", "SIS-0001", "Core Bancário"));

        await using var s = Abrir(b);

        Verificar.Igual(null, await s.Ativos.ObterAsync(ativo.Id));
        Verificar.Igual(null, await s.Ativos.ObterPorCodigoAsync(ativo.Codigo));
        Verificar.Vazio(await s.Ativos.ListarAsync());
        Verificar.Vazio(await s.Ativos.TrilhaAsync(ativo.Id));
        Verificar.Falso(await s.Ativos.ExisteNomeDuplicadoAsync("sistema", "Core Bancário"),
            "o mesmo nome em outra empresa não é duplicado");
    }

    [Teste("gravar ativo de outra empresa é recusado e nada é gravado")]
    public async Task GravarAtivoAlheioRecusado()
    {
        var a = IdDeEmpresa.Novo();
        var b = IdDeEmpresa.Novo();
        var alheio = Novo(a, "sistema", "SIS-0001", "Invasor");

        var recusou = await Falha(async () =>
        {
            await using var s = Abrir(b);
            await s.Ativos.AdicionarAsync(alheio);
            await s.Unidade.ConfirmarAsync();
        });

        Verificar.Verdadeiro(recusou, "a sessão da empresa B não pode escrever em nome da A");
        await using var sa = Abrir(a);
        Verificar.Igual(null, await sa.Ativos.ObterAsync(alheio.Id));
    }

    // ── relações ─────────────────────────────────────────────────────────────

    [Teste("relação aparece para os dois lados")]
    public async Task RelacaoNosDoisLados()
    {
        var empresa = IdDeEmpresa.Novo();
        var origem = await Gravar(empresa, Novo(empresa, "sistema", "SIS-0001", "Origem"));
        var destino = await Gravar(empresa, Novo(empresa, "sistema", "SIS-0002", "Destino"));

        await using var s = Abrir(empresa);
        await s.Relacoes.RelacionarAsync(origem.Id, destino.Id, "consome");

        var esperado = new RelacaoResumo(origem.Id, destino.Id, "consome");
        Verificar.Contem(await s.Relacoes.ListarRelacoesAsync(origem.Id), esperado);
        Verificar.Contem(await s.Relacoes.ListarRelacoesAsync(destino.Id), esperado);
    }

    [Teste("relação guarda a criticidade da dependência; sem ela, vale média")]
    public async Task RelacaoGuardaCriticidade()
    {
        var empresa = IdDeEmpresa.Novo();
        var app = await Gravar(empresa, Novo(empresa, "aplicacao", "APP-0001", "Gestor"));
        var cap = await Gravar(empresa, Novo(empresa, "capacidade", "CAP-0001", "Conceder crédito"));
        var outra = await Gravar(empresa, Novo(empresa, "capacidade", "CAP-0002", "Cobrar"));

        await using (var s = Abrir(empresa))
        {
            await s.Relacoes.RelacionarAsync(app.Id, cap.Id, "implementa", Criticidade.Critica);
            await s.Relacoes.RelacionarAsync(app.Id, outra.Id, "implementa");
        }

        await using var nova = Abrir(empresa);
        var relacoes = await nova.Relacoes.ListarRelacoesAsync(app.Id);
        Verificar.Igual(Criticidade.Critica, relacoes.Single(r => r.Destino == cap.Id).Criticidade);
        Verificar.Igual(Criticidade.Media, relacoes.Single(r => r.Destino == outra.Id).Criticidade);
    }

    [Teste("dependência circular é detectada, inclusive indireta")]
    public async Task DependenciaCircular()
    {
        var empresa = IdDeEmpresa.Novo();
        var x = await Gravar(empresa, Novo(empresa, "sistema", "SIS-0001", "X"));
        var y = await Gravar(empresa, Novo(empresa, "sistema", "SIS-0002", "Y"));
        var z = await Gravar(empresa, Novo(empresa, "sistema", "SIS-0003", "Z"));

        await using var s = Abrir(empresa);
        await s.Relacoes.RelacionarAsync(x.Id, y.Id, "depende_de");
        await s.Relacoes.RelacionarAsync(y.Id, z.Id, "depende_de");
        Verificar.Falso(await s.Relacoes.TemDependenciaCircularAsync(x.Id), "x → y → z não é ciclo");

        await s.Relacoes.RelacionarAsync(z.Id, x.Id, "depende_de");
        Verificar.Verdadeiro(await s.Relacoes.TemDependenciaCircularAsync(x.Id), "x → y → z → x");
    }

    [Teste("caminho de dependência: só por depende_de, no sentido das arestas, e só nesta empresa")]
    public async Task CaminhoDeDependencia()
    {
        var empresa = IdDeEmpresa.Novo();
        var x = await Gravar(empresa, Novo(empresa, "sistema", "SIS-0001", "X"));
        var y = await Gravar(empresa, Novo(empresa, "sistema", "SIS-0002", "Y"));
        var z = await Gravar(empresa, Novo(empresa, "sistema", "SIS-0003", "Z"));

        await using var s = Abrir(empresa);
        await s.Relacoes.RelacionarAsync(x.Id, y.Id, "depende_de");
        await s.Relacoes.RelacionarAsync(y.Id, z.Id, "consome");
        Verificar.Verdadeiro(await s.Relacoes.TemCaminhoDeDependenciaAsync(x.Id, y.Id));
        Verificar.Falso(await s.Relacoes.TemCaminhoDeDependenciaAsync(y.Id, x.Id), "a aresta tem sentido");
        Verificar.Falso(await s.Relacoes.TemCaminhoDeDependenciaAsync(x.Id, z.Id), "consome não é dependência");

        await s.Relacoes.RelacionarAsync(y.Id, z.Id, "depende_de");
        Verificar.Verdadeiro(await s.Relacoes.TemCaminhoDeDependenciaAsync(x.Id, z.Id), "x → y → z");

        await using var deFora = Abrir(IdDeEmpresa.Novo());
        Verificar.Falso(await deFora.Relacoes.TemCaminhoDeDependenciaAsync(x.Id, z.Id), "as arestas de outra empresa não contam");
    }

    [Teste("relação guarda o mecanismo; sem ele, fica nulo")]
    public async Task RelacaoGuardaOMecanismo()
    {
        var empresa = IdDeEmpresa.Novo();
        var app = await Gravar(empresa, Novo(empresa, "aplicacao", "APP-0001", "Motor"));
        var api = await Gravar(empresa, Novo(empresa, "api", "API-0001", "API"));
        var bd = await Gravar(empresa, Novo(empresa, "base_dados", "BDD-0001", "Base"));

        await using (var s = Abrir(empresa))
        {
            await s.Relacoes.RelacionarAsync(app.Id, api.Id, "consome", Criticidade.Alta, "assincrono");
            await s.Relacoes.RelacionarAsync(app.Id, bd.Id, "persiste_em");
        }

        await using var nova = Abrir(empresa);
        var arestas = (await nova.Relacoes.ListarRelacoesAsync(app.Id)).ToDictionary(r => r.Tipo);
        Verificar.Igual("assincrono", arestas["consome"].Mecanismo);
        Verificar.Igual(Criticidade.Alta, arestas["consome"].Criticidade);
        Verificar.Igual(null, arestas["persiste_em"].Mecanismo);
    }

    [Teste("destino descontinuado aparece como fora de vigência")]
    public async Task DestinoForaDeVigencia()
    {
        var empresa = IdDeEmpresa.Novo();
        var origem = await Gravar(empresa, Novo(empresa, "sistema", "SIS-0001", "Origem"));
        var vigente = await Gravar(empresa, Publicado(empresa, "SIS-0002", "Vigente"));
        var antigo = Publicado(empresa, "SIS-0003", "Antigo");
        antigo.Descontinuar("substituído", Relogio);
        await Gravar(empresa, antigo);

        await using var s = Abrir(empresa);
        await s.Relacoes.RelacionarAsync(origem.Id, vigente.Id, "depende_de");
        await s.Relacoes.RelacionarAsync(origem.Id, antigo.Id, "depende_de");

        var nomes = await s.Relacoes.NomesDeDestinosForaDeVigenciaAsync(origem.Id);
        Verificar.Igual("Antigo", string.Join(",", nomes));
    }

    [Teste("implementações de uma capacidade são contadas")]
    public async Task ContaImplementacoes()
    {
        var empresa = IdDeEmpresa.Novo();
        var capacidade = await Gravar(empresa, Novo(empresa, "capacidade", "CAP-0001", "Abrir conta"));
        var app1 = await Gravar(empresa, Novo(empresa, "aplicacao", "APP-0001", "App 1"));
        var app2 = await Gravar(empresa, Novo(empresa, "aplicacao", "APP-0002", "App 2"));

        await using var s = Abrir(empresa);
        await s.Relacoes.RelacionarAsync(app1.Id, capacidade.Id, "implementa");
        await s.Relacoes.RelacionarAsync(app2.Id, capacidade.Id, "implementa");
        await s.Relacoes.RelacionarAsync(app2.Id, app1.Id, "depende_de");

        Verificar.Igual(2, await s.Relacoes.ContarImplementacoesAsync(capacidade.Id));
    }

    [Teste("empresa não enxerga relação de outra")]
    public async Task RelacaoIsoladaPorEmpresa()
    {
        var a = IdDeEmpresa.Novo();
        var b = IdDeEmpresa.Novo();
        var app = await Gravar(a, Novo(a, "aplicacao", "APP-0001", "App"));
        var capacidade = await Gravar(a, Novo(a, "capacidade", "CAP-0001", "Capacidade"));

        await using (var sa = Abrir(a))
        {
            await sa.Relacoes.RelacionarAsync(app.Id, capacidade.Id, "implementa");
            await sa.Relacoes.RelacionarAsync(capacidade.Id, app.Id, "depende_de");
            await sa.Relacoes.RelacionarAsync(app.Id, capacidade.Id, "depende_de");
        }

        await using var s = Abrir(b);
        Verificar.Vazio(await s.Relacoes.ListarRelacoesAsync(app.Id));
        Verificar.Igual(0, await s.Relacoes.ContarImplementacoesAsync(capacidade.Id));
        Verificar.Falso(await s.Relacoes.TemDependenciaCircularAsync(app.Id),
            "o ciclo existe na empresa A, não na B");
    }

    // ── validações ───────────────────────────────────────────────────────────

    [Teste("validação gravada volta igual e a decisão confirmada persiste")]
    public async Task ValidacaoEDecisaoPersistem()
    {
        var empresa = IdDeEmpresa.Novo();
        var ativo = await Gravar(empresa, Novo(empresa, "sistema", "SIS-0001", "Core"));
        var submissor = Guid.NewGuid();
        var decisor = Guid.NewGuid();
        var validacao = await GravarValidacao(empresa, ativo, EtapaValidacao.Tecnica, submissor);

        await using (var s = Abrir(empresa))
        {
            var lida = await s.Validacoes.ObterAsync(validacao.Id) ?? throw Ausente("validação");
            Verificar.Igual(ativo.Id, lida.Ativo);
            Verificar.Igual(empresa, lida.Empresa);
            Verificar.Igual(1, lida.Revisao);
            Verificar.Igual(EtapaValidacao.Tecnica, lida.Etapa);
            Verificar.Igual(submissor, lida.IdSubmissor);
            Verificar.Igual(StatusDaValidacao.Aberta, lida.Status);

            lida.Decidir(aprovada: false, decisor, "falta evidência");
            await s.Unidade.ConfirmarAsync();
        }

        await using var nova = Abrir(empresa);
        var decidida = await nova.Validacoes.ObterAsync(validacao.Id) ?? throw Ausente("validação");
        Verificar.Igual(StatusDaValidacao.Reprovada, decidida.Status);
        Verificar.Igual(decisor, decidida.IdDecisor);
        Verificar.Igual("falta evidência", decidida.Motivo);
    }

    [Teste("cancelamento de etapa persiste")]
    public async Task CancelamentoPersiste()
    {
        var empresa = IdDeEmpresa.Novo();
        var ativo = await Gravar(empresa, Novo(empresa, "sistema", "SIS-0001", "Core"));
        var validacao = await GravarValidacao(empresa, ativo, EtapaValidacao.Arquitetural);

        await using (var s = Abrir(empresa))
        {
            var lida = await s.Validacoes.ObterAsync(validacao.Id) ?? throw Ausente("validação");
            lida.Cancelar("outra etapa reprovou");
            await s.Unidade.ConfirmarAsync();
        }

        await using var nova = Abrir(empresa);
        var cancelada = await nova.Validacoes.ObterAsync(validacao.Id) ?? throw Ausente("validação");
        Verificar.Igual(StatusDaValidacao.Cancelada, cancelada.Status);
        Verificar.Igual("outra etapa reprovou", cancelada.Motivo);
    }

    [Teste("validações são listadas por ativo, com as abertas primeiro")]
    public async Task ListarValidacoes()
    {
        var empresa = IdDeEmpresa.Novo();
        var ativo = await Gravar(empresa, Novo(empresa, "sistema", "SIS-0001", "Core"));
        var outro = await Gravar(empresa, Novo(empresa, "sistema", "SIS-0002", "Outro"));
        var decidida = await GravarValidacao(empresa, ativo, EtapaValidacao.Negocial);
        var aberta = await GravarValidacao(empresa, ativo, EtapaValidacao.Tecnica);
        await GravarValidacao(empresa, outro, EtapaValidacao.Tecnica);

        await using (var s = Abrir(empresa))
        {
            var lida = await s.Validacoes.ObterAsync(decidida.Id) ?? throw Ausente("validação");
            lida.Decidir(aprovada: true, Guid.NewGuid(), null);
            await s.Unidade.ConfirmarAsync();
        }

        await using var nova = Abrir(empresa);
        var doAtivo = await nova.Validacoes.ListarAsync(ativo.Id);

        Verificar.Igual(2, doAtivo.Count);
        Verificar.Igual(aberta.Id, doAtivo[0].Id, "a fila de quem decide começa pelas abertas");
        Verificar.Igual(3, (await nova.Validacoes.ListarAsync()).Count);
    }

    [Teste("empresa não enxerga validação de outra")]
    public async Task ValidacaoIsoladaPorEmpresa()
    {
        var a = IdDeEmpresa.Novo();
        var b = IdDeEmpresa.Novo();
        var ativo = await Gravar(a, Novo(a, "sistema", "SIS-0001", "Core"));
        var validacao = await GravarValidacao(a, ativo, EtapaValidacao.Tecnica);

        await using var s = Abrir(b);
        Verificar.Igual(null, await s.Validacoes.ObterAsync(validacao.Id));
        Verificar.Vazio(await s.Validacoes.ListarAsync());
        Verificar.Vazio(await s.Validacoes.ListarAsync(ativo.Id));
    }

    // ── apoio ────────────────────────────────────────────────────────────────

    private static Ativo Novo(IdDeEmpresa empresa, string tipo, string codigo, string nome,
        IdDeAtivo? pai = null) =>
        Ativo.Rascunhar(empresa, Tipos.Obter(tipo), CodigoDeAtivo.De(codigo), nome, Relogio, idPai: pai);

    /// <summary>Sistema com o mínimo para ser submetido: descrição, campo obrigatório e owner técnico.</summary>
    private static Ativo Completo(IdDeEmpresa empresa, string codigo, string nome)
    {
        var ativo = Ativo.Rascunhar(empresa, Tipos.Obter("sistema"), CodigoDeAtivo.De(codigo), nome, Relogio,
            descricao: "Descrição.", atributos: new Dictionary<string, string> { ["plataforma"] = "nuvem" });
        ativo.AtribuirResponsavel(Guid.NewGuid(), PapelDeOwnership.OwnerTecnico, Relogio);
        return ativo;
    }

    private static Ativo Publicado(IdDeEmpresa empresa, string codigo, string nome)
    {
        var ativo = Completo(empresa, codigo, nome);
        ativo.Submeter("v1", Guid.NewGuid(), Relogio);
        ativo.Publicar(Relogio);
        return ativo;
    }

    // ── revisões e auditoria (ADR-0017) ──────────────────────────────────────

    [Teste("retrato da revisão volta igual, e a data de publicação é gravada depois")]
    public async Task RetratoDaRevisao()
    {
        var empresa = IdDeEmpresa.Novo();
        var ativo = Completo(empresa, "SIS-0001", "Core Bancário");
        ativo.Submeter("v1", Guid.NewGuid(), Relogio);
        await Gravar(empresa, ativo);

        var autor = Guid.NewGuid();
        var retrato = RevisaoDoAtivo.Retratar(ativo, [], "primeira versão", autor, Relogio);
        await using (var s = Abrir(empresa))
        {
            await s.Revisoes.AdicionarAsync(retrato);
            await s.Unidade.ConfirmarAsync();
        }

        await using (var s = Abrir(empresa))
        {
            var lido = await s.Revisoes.ObterAsync(ativo.Id, 1) ?? throw Ausente("revisão");
            Verificar.Igual(retrato.Conteudo, lido.Conteudo);
            Verificar.Igual(retrato.Hash, lido.Hash);
            Verificar.Verdadeiro(lido.Integra, "o hash confere depois da viagem ao banco");
            Verificar.Igual("primeira versão", lido.Motivo);
            Verificar.Igual(autor, lido.IdAutor);
            Verificar.Igual(null, lido.PublicadaEm);

            lido.MarcarPublicada(Relogio);
            await s.Unidade.ConfirmarAsync();
        }

        await using var nova = Abrir(empresa);
        var publicada = (await nova.Revisoes.ListarAsync(ativo.Id)).Single();
        Verificar.Igual(Relogio.Agora, publicada.PublicadaEm);
        Verificar.Igual(retrato.Hash, publicada.Hash, "publicar não mexe no conteúdo");
        Verificar.Igual(null, await nova.Revisoes.ObterAsync(ativo.Id, 2));
    }

    [Teste("o mesmo número de revisão não é gravado duas vezes")]
    public async Task RevisaoNaoDuplica()
    {
        var empresa = IdDeEmpresa.Novo();
        var ativo = Completo(empresa, "SIS-0001", "Core Bancário");
        ativo.Submeter("v1", Guid.NewGuid(), Relogio);
        await Gravar(empresa, ativo);

        await using (var s = Abrir(empresa))
        {
            await s.Revisoes.AdicionarAsync(RevisaoDoAtivo.Retratar(ativo, [], "v1", Guid.NewGuid(), Relogio));
            await s.Unidade.ConfirmarAsync();
        }

        ErroDeDominio? erro = null;
        try
        {
            await using var s = Abrir(empresa);
            await s.Revisoes.AdicionarAsync(RevisaoDoAtivo.Retratar(ativo, [], "de novo", Guid.NewGuid(), Relogio));
            await s.Unidade.ConfirmarAsync();
        }
        catch (ErroDeDominio e) { erro = e; }

        Verificar.Igual("CONFLITO_DE_EDICAO", erro?.Codigo);
        await using var nova = Abrir(empresa);
        Verificar.Igual("v1", (await nova.Revisoes.ObterAsync(ativo.Id, 1))!.Motivo, "a primeira continua valendo");
    }

    [Teste("auditoria volta do mais recente para o mais antigo, com limite, por ativo e por empresa")]
    public async Task TrilhaDeAuditoria()
    {
        var empresa = IdDeEmpresa.Novo();
        var ativo = await Gravar(empresa, Novo(empresa, "sistema", "SIS-0001", "Core"));
        var outro = await Gravar(empresa, Novo(empresa, "sistema", "SIS-0002", "Outro"));
        var pessoa = Guid.NewGuid();

        RegistroDeAuditoria Registro(IdDeAtivo id, string acao, int minutos) => new(
            Guid.CreateVersion7(), empresa, id, acao, pessoa, Relogio.Agora.AddMinutes(minutos),
            new Dictionary<string, string> { ["nome"] = "antes" }, new Dictionary<string, string> { ["nome"] = "depois" });

        await using (var s = Abrir(empresa))
        {
            await s.Trilha.RegistrarAsync(Registro(ativo.Id, "criar", 0));
            await s.Trilha.RegistrarAsync(Registro(ativo.Id, "alterar", 1));
            await s.Trilha.RegistrarAsync(Registro(ativo.Id, "submeter", 2));
            await s.Trilha.RegistrarAsync(Registro(outro.Id, "criar", 3));
            await s.Unidade.ConfirmarAsync();
        }

        await using var nova = Abrir(empresa);
        var ultimos = await nova.Trilha.ListarAsync(ativo.Id, limite: 2);
        Verificar.Igual("submeter,alterar", string.Join(",", ultimos.Select(r => r.Acao)));
        Verificar.Igual(pessoa, ultimos[0].IdPessoa);
        Verificar.Igual(Relogio.Agora.AddMinutes(2), ultimos[0].Em);
        Verificar.Igual("antes", ultimos[0].Antes!["nome"]);
        Verificar.Igual("depois", ultimos[0].Depois!["nome"]);
        Verificar.Igual(3, (await nova.Trilha.ListarAsync(ativo.Id)).Count, "só os do ativo pedido");

        await using var deFora = Abrir(IdDeEmpresa.Novo());
        Verificar.Vazio(await deFora.Trilha.ListarAsync(ativo.Id));
    }

    // ── pessoas e pleitos (ADR-0018) ─────────────────────────────────────────

    [Teste("pessoa volta com o cadastro e os papéis, por id e por matrícula")]
    public async Task PessoaVoltaCompleta()
    {
        var empresa = IdDeEmpresa.Novo();
        var dominio = Guid.NewGuid();
        var quemConcedeu = Guid.NewGuid();
        var pessoa = Pessoa.Cadastrar(empresa, DadosDeCadastro.Conferir("c123456", "Davi Souza", "davi@empresa.com", "0427"), Relogio);
        pessoa.Conceder(AtribuicaoDePapel.NaEmpresa(Papel.TechLead, Relogio.Hoje));
        pessoa.Conceder(new AtribuicaoDePapel(Papel.Negocio, TipoDeEscopo.Dominio, dominio, Relogio.Hoje,
            Relogio.Hoje.AddDays(30), quemConcedeu));
        await using (var s = Abrir(empresa))
        {
            await s.Pessoas.AdicionarAsync(pessoa);
            await s.Unidade.ConfirmarAsync();
        }

        await using var nova = Abrir(empresa);
        var lida = await nova.Pessoas.ObterAsync(pessoa.Id) ?? throw Ausente("pessoa");
        Verificar.Igual("C123456", lida.Matricula.Valor);
        Verificar.Igual("Davi Souza", lida.Nome);
        Verificar.Igual("davi@empresa.com", lida.Email);
        Verificar.Igual("0427", lida.Unidade.Valor);
        Verificar.Verdadeiro(lida.Ativa);
        Verificar.Igual(pessoa.CadastradaEm, lida.CadastradaEm);
        Verificar.Igual(2, lida.Atribuicoes.Count);
        var escopada = lida.Atribuicoes.Single(a => a.EscopoTipo == TipoDeEscopo.Dominio);
        Verificar.Igual(Papel.Negocio, escopada.Papel);
        Verificar.Igual<Guid?>(dominio, escopada.EscopoId);
        Verificar.Igual<DateOnly?>(Relogio.Hoje.AddDays(30), escopada.FimVigencia);
        Verificar.Igual<Guid?>(quemConcedeu, escopada.ConcedidoPor);

        Verificar.Igual(pessoa.Id, (await nova.Pessoas.ObterPorMatriculaAsync(Matricula.De("C123456")))?.Id, "pela matrícula");
        await using var deFora = Abrir(IdDeEmpresa.Novo());
        Verificar.Igual(null, await deFora.Pessoas.ObterAsync(pessoa.Id), "outra empresa não enxerga a pessoa");
        Verificar.Igual(null, await deFora.Pessoas.ObterPorMatriculaAsync(Matricula.De("C123456")));
    }

    [Teste("a matrícula é única na empresa, e só nela")]
    public async Task MatriculaUnicaPorEmpresa()
    {
        var empresa = IdDeEmpresa.Novo();
        Pessoa Nova(IdDeEmpresa e, string nome) =>
            Pessoa.Cadastrar(e, DadosDeCadastro.Conferir("C654321", nome, "x@empresa.com", "0001"), Relogio);

        await using (var s = Abrir(empresa))
        {
            await s.Pessoas.AdicionarAsync(Nova(empresa, "Eva Lima"));
            await s.Unidade.ConfirmarAsync();
        }
        await using (var s = Abrir(empresa))
        {
            Verificar.Verdadeiro(await Falha(async () =>
            {
                await s.Pessoas.AdicionarAsync(Nova(empresa, "Eva Lima Duplicada"));
                await s.Unidade.ConfirmarAsync();
            }), "a mesma matrícula duas vezes na mesma empresa");
        }

        var outra = IdDeEmpresa.Novo();
        await using (var s = Abrir(outra))
        {
            await s.Pessoas.AdicionarAsync(Nova(outra, "Eva Lima"));
            await s.Unidade.ConfirmarAsync();
        }
        await using var nova = Abrir(empresa);
        Verificar.Igual("Eva Lima", (await nova.Pessoas.ListarAsync()).Single().Nome, "a da outra empresa não aparece");
    }

    [Teste("pessoas são listadas por nome, e as inativas só quando pedidas")]
    public async Task PessoasPorNome()
    {
        var empresa = IdDeEmpresa.Novo();
        await using (var s = Abrir(empresa))
        {
            foreach (var (m, nome, ativa) in new[] { ("C000003", "Carla Dias", true), ("C000001", "Ana Ferreira", true), ("C000002", "Bruno Alves", false) })
                await s.Pessoas.AdicionarAsync(Pessoa.Reidratar(Guid.CreateVersion7(), empresa,
                    DadosDeCadastro.Conferir(m, nome, "x@empresa.com", "0001"), ativa, Relogio.Agora, []));
            await s.Unidade.ConfirmarAsync();
        }

        await using var nova = Abrir(empresa);
        Verificar.Igual("Ana Ferreira,Carla Dias", string.Join(",", (await nova.Pessoas.ListarAsync()).Select(p => p.Nome)));
        Verificar.Igual(3, (await nova.Pessoas.ListarAsync(incluirInativas: true)).Count);
    }

    [Teste("pleito volta completo; a fila põe pendentes primeiro, do mais antigo ao mais novo")]
    public async Task PleitosVoltamEmOrdem()
    {
        var empresa = IdDeEmpresa.Novo();
        var relogio = RelogioFixo.Em(2026, 10, 21);
        var decisor = new Ator(Guid.NewGuid(), empresa, [AtribuicaoDePapel.NaEmpresa(Papel.Admin, relogio.Hoje)]);
        var ana = Pessoa.Cadastrar(empresa, DadosDeCadastro.Conferir("C000001", "Ana Ferreira", "ana@empresa.com", "0001"), relogio);
        var beto = Pessoa.Cadastrar(empresa, DadosDeCadastro.Conferir("C000002", "Beto Alves", "beto@empresa.com", "0001"), relogio);

        PleitoDeAcesso primeiro, segundo, terceiro;
        await using (var s = Abrir(empresa))
        {
            await s.Pessoas.AdicionarAsync(ana);
            await s.Pessoas.AdicionarAsync(beto);
            primeiro = PleitoDeAcesso.Abrir(ana, Papel.Negocio, "Respondo por Crédito.", relogio);
            relogio.Avancar(TimeSpan.FromMinutes(1));
            segundo = PleitoDeAcesso.Abrir(beto, Papel.TechLead, "", relogio);
            relogio.Avancar(TimeSpan.FromMinutes(1));
            terceiro = PleitoDeAcesso.Abrir(beto, Papel.Arquiteto, "", relogio);
            foreach (var p in new[] { terceiro, primeiro, segundo })
                await s.Pleitos.AdicionarAsync(p);
            await s.Unidade.ConfirmarAsync();
        }

        // Decidir numa sessão nova grava o pleito e a pessoa com o papel, juntos.
        await using (var s = Abrir(empresa))
        {
            var lido = await s.Pleitos.ObterAsync(primeiro.Id) ?? throw Ausente("pleito");
            var pessoa = await s.Pessoas.ObterAsync(ana.Id) ?? throw Ausente("pessoa");
            pessoa.Conceder(lido.Decidir(decisor, true, "Bem-vinda.", relogio, escopoTipo: TipoDeEscopo.Dominio, escopoId: Guid.NewGuid())!);
            await s.Unidade.ConfirmarAsync();
        }

        await using var nova = Abrir(empresa);
        var fila = await nova.Pleitos.ListarAsync();
        Verificar.Igual($"{segundo.Id},{terceiro.Id},{primeiro.Id}", string.Join(",", fila.Select(p => p.Id)),
            "pendentes primeiro, quem espera há mais tempo na frente");

        var decidido = fila.Last();
        Verificar.Igual(StatusDoPleito.Aprovado, decidido.Status);
        Verificar.Igual("Respondo por Crédito.", decidido.Justificativa);
        Verificar.Igual<Papel?>(Papel.Negocio, decidido.PapelConcedido);
        Verificar.Igual<TipoDeEscopo?>(TipoDeEscopo.Dominio, decidido.EscopoTipo);
        Verificar.Igual<Guid?>(decisor.IdPessoa, decidido.DecididoPor);
        Verificar.Igual("Bem-vinda.", decidido.Resposta);
        Verificar.Igual(Papel.Negocio, (await nova.Pessoas.ObterAsync(ana.Id))!.Atribuicoes.Single().Papel, "a concessão foi junto");

        Verificar.Igual(2, (await nova.Pleitos.ListarAsync(StatusDoPleito.Pendente)).Count);
        Verificar.Igual(2, (await nova.Pleitos.ListarAsync(idPessoa: beto.Id)).Count);
        await using var deFora = Abrir(IdDeEmpresa.Novo());
        Verificar.Vazio(await deFora.Pleitos.ListarAsync(), "outra empresa não enxerga os pleitos");
    }

    [Teste("pessoa excluída some e libera a matrícula")]
    public async Task PessoaExcluidaLiberaAMatricula()
    {
        var empresa = IdDeEmpresa.Novo();
        Pessoa Nova() => Pessoa.Cadastrar(empresa, DadosDeCadastro.Conferir("C555555", "Ivo Prado", "ivo@empresa.com", "0001"), Relogio);
        var primeira = Nova();
        await using (var s = Abrir(empresa))
        {
            await s.Pessoas.AdicionarAsync(primeira);
            await s.Unidade.ConfirmarAsync();
        }
        await using (var s = Abrir(empresa))
        {
            await s.Pessoas.RemoverAsync(await s.Pessoas.ObterAsync(primeira.Id) ?? throw Ausente("pessoa"));
            await s.Unidade.ConfirmarAsync();
        }

        var segunda = Nova();
        await using (var s = Abrir(empresa))
        {
            Verificar.Igual(null, await s.Pessoas.ObterAsync(primeira.Id));
            Verificar.Igual(null, await s.Pessoas.ObterPorMatriculaAsync(Matricula.De("C555555")));
            await s.Pessoas.AdicionarAsync(segunda);
            await s.Unidade.ConfirmarAsync();
        }
        await using var nova = Abrir(empresa);
        Verificar.Igual(segunda.Id, (await nova.Pessoas.ObterPorMatriculaAsync(Matricula.De("C555555")))?.Id);
    }

    [Teste("squad volta igual, por id e por sigla; trocar a sigla libera a antiga")]
    public async Task SquadETrocaDeSigla()
    {
        var empresa = IdDeEmpresa.Novo();
        var cred = Squad.Criar(empresa, "cred", "Squad Crédito", "Contratos de crédito");
        await using (var s = Abrir(empresa))
        {
            await s.Squads.AdicionarAsync(cred);
            await s.Unidade.ConfirmarAsync();
        }

        await using (var s = Abrir(empresa))
        {
            var lida = await s.Squads.ObterPorSiglaAsync("CRED") ?? throw Ausente("squad");
            Verificar.Igual(cred.Id, lida.Id);
            Verificar.Igual("Squad Crédito", lida.Titulo);
            Verificar.Igual("Contratos de crédito", lida.Descricao);
            Verificar.Verdadeiro(lida.Ativa);
            lida.Alterar("CREDITO", "Squad Crédito", "Contratos de crédito");
            lida.Inativar();
            await s.Unidade.ConfirmarAsync();
        }

        await using (var s = Abrir(empresa))
        {
            Verificar.Igual(null, await s.Squads.ObterPorSiglaAsync("CRED"), "a sigla antiga não aponta mais para o squad");
            var renomeada = await s.Squads.ObterPorSiglaAsync("credito") ?? throw Ausente("squad renomeado");
            Verificar.Falso(renomeada.Ativa);
            await s.Squads.AdicionarAsync(Squad.Criar(empresa, "CRED", "Novo Crédito", "Reaproveita a sigla"));
            await s.Unidade.ConfirmarAsync();
        }

        await using (var s = Abrir(empresa))
        {
            Verificar.Verdadeiro(await Falha(async () =>
            {
                await s.Squads.AdicionarAsync(Squad.Criar(empresa, "CREDITO", "Repetido", "x"));
                await s.Unidade.ConfirmarAsync();
            }), "a sigla nova está reservada");
        }

        await using var nova = Abrir(empresa);
        Verificar.Igual("Novo Crédito,Squad Crédito", string.Join(",", (await nova.Squads.ListarAsync()).Select(q => q.Titulo)),
            "ativos primeiro");
        await using var deFora = Abrir(IdDeEmpresa.Novo());
        Verificar.Vazio(await deFora.Squads.ListarAsync());
    }

    [Teste("vínculos da pessoa: responsabilidade, revisão, validação e auditoria, só nesta empresa")]
    public async Task VinculosDaPessoa()
    {
        var empresa = IdDeEmpresa.Novo();
        var dona = Guid.NewGuid();
        var autora = Guid.NewGuid();
        var ativo = Ativo.Rascunhar(empresa, Tipos.Obter("sistema"), CodigoDeAtivo.De("SIS-0001"), "Core", Relogio,
            atributos: new Dictionary<string, string> { ["plataforma"] = "nuvem" });
        ativo.AtribuirResponsavel(dona, PapelDeOwnership.OwnerTecnico, Relogio);
        await using (var s = Abrir(empresa))
        {
            await s.Ativos.AdicionarAsync(ativo);
            await s.Trilha.RegistrarAsync(RegistroDeAuditoria.De(ativo, "criar", autora, Relogio));
            await s.Unidade.ConfirmarAsync();
        }

        await using var nova = Abrir(empresa);
        Verificar.Contem(await nova.Vinculos.DaPessoaAsync(dona), "responsabilidades em ativos");
        Verificar.Contem(await nova.Vinculos.DaPessoaAsync(autora), "eventos de auditoria");
        Verificar.Vazio(await nova.Vinculos.DaPessoaAsync(Guid.NewGuid()));
        await using var deFora = Abrir(IdDeEmpresa.Novo());
        Verificar.Vazio(await deFora.Vinculos.DaPessoaAsync(dona), "o vínculo de outra empresa não conta");
    }

    private async Task<Ativo> Gravar(IdDeEmpresa empresa, Ativo ativo)
    {
        await using var s = Abrir(empresa);
        await s.Ativos.AdicionarAsync(ativo);
        await s.Unidade.ConfirmarAsync();
        return ativo;
    }

    private async Task<ValidacaoEmProcesso> GravarValidacao(
        IdDeEmpresa empresa, Ativo ativo, EtapaValidacao etapa, Guid? submissor = null)
    {
        var validacao = new ValidacaoEmProcesso(
            Guid.CreateVersion7(), ativo.Id, empresa, 1, etapa, submissor ?? Guid.NewGuid());

        await using var s = Abrir(empresa);
        await s.Validacoes.AdicionarAsync(validacao);
        await s.Unidade.ConfirmarAsync();
        return validacao;
    }

    private static string Codigos(IEnumerable<Ativo> ativos) =>
        string.Join(",", ativos.Select(a => a.Codigo.Valor));

    /// <summary>Verdadeiro se a ação lançou. Falha de verificação não conta como recusa.</summary>
    private static async Task<bool> Falha(Func<Task> acao)
    {
        try
        {
            await acao();
            return false;
        }
        catch (Exception e) when (e is not FalhaDeVerificacao and not TestePulado)
        {
            return true;
        }
    }

    private static FalhaDeVerificacao Ausente(string oque) =>
        new($"{oque} gravado não foi encontrado numa sessão nova");
}
