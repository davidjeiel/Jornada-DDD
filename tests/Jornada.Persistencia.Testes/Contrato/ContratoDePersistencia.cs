using Jornada.Aplicacao.Portas;
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

        var ativo = Ativo.Rascunhar(empresa, Tipos.Obter("sistema"), CodigoDeAtivo.De("SIS-0001"),
            "Core Bancário", Relogio, descricao: "Contas e lançamentos.",
            criticidade: Criticidade.Alta, unidade: unidade,
            atributos: new Dictionary<string, string> { ["plataforma"] = "mainframe" });
        ativo.AtribuirResponsavel(pessoa, PapelDeOwnership.OwnerTecnico, Relogio);
        ativo.AnexarEvidencia("adr", "ADR-0001", "https://exemplo/adr-0001", Relogio);
        await Gravar(empresa, ativo);

        await using var s = Abrir(empresa);
        var lido = await s.Ativos.ObterAsync(ativo.Id) ?? throw Ausente("ativo");

        Verificar.Igual(empresa, lido.Empresa);
        Verificar.Igual(unidade, lido.Unidade);
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
