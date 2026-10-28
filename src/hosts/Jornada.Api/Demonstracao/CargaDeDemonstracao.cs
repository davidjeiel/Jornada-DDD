using Jornada.Aplicacao;
using Jornada.Aplicacao.Catalogo.Comandos;
using Jornada.Aplicacao.Portas;
using Jornada.Dominio.Acesso;
using Jornada.Dominio.Catalogo;
using Jornada.Dominio.Comum;
using Jornada.Dominio.Tenancy;

namespace Jornada.Api.Demonstracao;

/// <summary>
/// Carga de demonstração: o cenário piloto do painel-ddd (<c>catalogo/seed.py</c>),
/// os domínios Contrato e Garantias com os ativos técnicos que os sustentam,
/// gravado em cada empresa de teste.
///
/// Tudo passa pelos CASOS DE USO, com a pessoa certa em cada passo: o curador
/// cadastra e submete, o admin decide (segregação de função). Uma carga que
/// gravasse direto no repositório publicaria o que o rito real recusaria, e a
/// demonstração mentiria sobre o produto.
///
/// Só roda com <c>Demonstracao:Carregar=true</c> e só numa empresa sem nenhum
/// ativo: reiniciar a API com o Cosmos não duplica o catálogo.
/// </summary>
public sealed class CargaDeDemonstracao(
    IServiceScopeFactory escopos, IRelogio relogio, ILogger<CargaDeDemonstracao> log)
{
    /// <summary>Uma pessoa de teste. Os ids são os de <c>frontend/src/usuarios.ts</c>.</summary>
    private sealed record Pessoa(Guid Id, Papel Papel);

    private sealed record EmpresaDeTeste(
        IdDeEmpresa Id, IdDeUnidade Unidade,
        Pessoa Admin, Pessoa Curador, Pessoa Arquiteto, Pessoa TechLead, Pessoa Negocio);

    private static readonly EmpresaDeTeste[] Empresas =
    [
        new(new IdDeEmpresa(Guid.Parse("11111111-1111-1111-1111-111111111111")),
            new IdDeUnidade(Guid.Parse("a0000000-0000-0000-0000-000000000001")),
            Admin:     new(Guid.Parse("10000000-0000-0000-0000-000000000001"), Papel.Admin),
            Curador:   new(Guid.Parse("20000000-0000-0000-0000-000000000001"), Papel.Curador),
            Arquiteto: new(Guid.Parse("30000000-0000-0000-0000-000000000001"), Papel.Arquiteto),
            TechLead:  new(Guid.Parse("40000000-0000-0000-0000-000000000001"), Papel.TechLead),
            Negocio:   new(Guid.Parse("50000000-0000-0000-0000-000000000001"), Papel.Negocio)),
        new(new IdDeEmpresa(Guid.Parse("22222222-2222-2222-2222-222222222222")),
            new IdDeUnidade(Guid.Parse("b0000000-0000-0000-0000-000000000002")),
            Admin:     new(Guid.Parse("10000000-0000-0000-0000-000000000002"), Papel.Admin),
            Curador:   new(Guid.Parse("20000000-0000-0000-0000-000000000002"), Papel.Curador),
            Arquiteto: new(Guid.Parse("30000000-0000-0000-0000-000000000002"), Papel.Arquiteto),
            TechLead:  new(Guid.Parse("40000000-0000-0000-0000-000000000002"), Papel.TechLead),
            Negocio:   new(Guid.Parse("50000000-0000-0000-0000-000000000002"), Papel.Negocio)),
    ];

    public async Task CarregarAsync(CancellationToken ct = default)
    {
        foreach (var empresa in Empresas)
        {
            var vazia = await ComoAsync(empresa, empresa.Curador, async sp =>
                (await sp.GetRequiredService<IRepositorioDeAtivos>().ListarAsync(limite: 1, ct: ct)).Count == 0);

            if (!vazia)
            {
                log.LogInformation("Carga de demonstração: empresa {Empresa} já tem ativos, nada a fazer.", empresa.Id.Valor);
                continue;
            }

            var resumo = await CarregarEmpresaAsync(empresa, ct);
            log.LogInformation(
                "Carga de demonstração: empresa {Empresa} com {Ativos} ativos, {Publicados} publicados e {Fila} na fila de validação.",
                empresa.Id.Valor, resumo.Ativos, resumo.Publicados, resumo.NaFila);
        }
    }

    private sealed record Resumo(int Ativos, int Publicados, int NaFila);

    private async Task<Resumo> CarregarEmpresaAsync(EmpresaDeTeste e, CancellationToken ct)
    {
        var cadastrados = 0;

        async Task<IdDeAtivo> Novo(string tipo, string nome, string descricao, string criticidade = "media",
                                   IdDeAtivo? pai = null, Dictionary<string, string>? atributos = null)
        {
            cadastrados++;
            return Exigir(await ComoAsync(e, e.Curador, sp => sp.GetRequiredService<CadastrarAtivo>().ExecutarAsync(
                new ComandoCadastrarAtivo(tipo, nome, descricao, CriticidadeExtensoes.De(criticidade), pai, atributos), ct)),
                $"cadastrar {nome}");
        }

        // ────────────────────────────────────────────────── domínio: Contrato
        var domCtr = await Novo("dominio", "Administração do Contrato",
            "Ciclo de vida do contrato de financiamento, da proposta aprovada até a quitação.", "alta",
            atributos: new() { ["visao"] = "Sustentar a originação e a manutenção dos contratos de crédito imobiliário.",
                               ["forum"] = "Fórum de Arquitetura Corporativa" });
        var subOrig = await Novo("subdominio", "Originação", "Formalização e emissão do contrato.", "alta", domCtr,
            new() { ["classificacao"] = "core" });
        var subManut = await Novo("subdominio", "Manutenção contratual", "Alterações, portabilidade e quitação.",
            pai: domCtr, atributos: new() { ["classificacao"] = "suporte" });

        var ctxSim = await Novo("contexto", "Simulação", "Cálculo de condições antes da contratação.", "alta", subOrig,
            new() { ["linguagem_ubiqua"] = "Simulação, Cenário, Prazo, Taxa", ["estrategia_integracao"] = "serviço aberto" });
        var ctxGestao = await Novo("contexto", "Gestão de Contratos", "Estado e eventos do contrato vigente.", "critica", subManut,
            new() { ["linguagem_ubiqua"] = "Contrato, Aditivo, Parcela", ["estrategia_integracao"] = "cliente-fornecedor" });

        var capSim = await Novo("capacidade", "Simular Financiamento",
            "Permite calcular condições de financiamento considerando produto, cliente, garantias e políticas vigentes.",
            "alta", ctxSim,
            new() { ["resultado_esperado"] = "Proposta simulada com parcelas, CET e prazo.",
                    ["processo_negocio"] = "Originação de crédito" });
        var capPort = await Novo("capacidade", "Registrar Portabilidade",
            "Recebe e formaliza a portabilidade de contrato de outra instituição.", "media", ctxGestao,
            new() { ["resultado_esperado"] = "Contrato portado com saldo devedor conciliado.",
                    ["processo_negocio"] = "Manutenção contratual" });

        // ───────────────────────────────────────────────── domínio: Garantias
        var domGar = await Novo("dominio", "Garantias",
            "Constituição, avaliação e liberação de garantias vinculadas ao contrato.", "alta",
            atributos: new() { ["visao"] = "Reduzir risco de crédito com garantias válidas e rastreáveis.",
                               ["forum"] = "Fórum de Risco e Arquitetura" });
        var subImob = await Novo("subdominio", "Garantia imobiliária", "Imóveis dados em alienação fiduciária.", "alta", domGar,
            new() { ["classificacao"] = "core" });
        var ctxImob = await Novo("contexto", "Garantia Imobiliária", "Avaliação, registro em cartório e baixa de garantia.",
            "alta", subImob,
            new() { ["linguagem_ubiqua"] = "Imóvel, Laudo, Matrícula, Baixa", ["estrategia_integracao"] = "camada anticorrupção" });
        var capAval = await Novo("capacidade", "Avaliar Imóvel",
            "Registra laudo de avaliação e valor aceito para constituição da garantia.", "alta", ctxImob,
            new() { ["resultado_esperado"] = "Laudo aprovado e valor de garantia definido.",
                    ["processo_negocio"] = "Constituição de garantia" });

        // ──────────────────────────────────────────────────── ativos técnicos
        var sisLegado = await Novo("sistema", "SIACI - Core de Contratos", "Núcleo COBOL de contratos, ativo desde 1987.",
            "critica", atributos: new() { ["plataforma"] = "mainframe", ["situacao"] = "em modernização" });
        var sisDigital = await Novo("sistema", "Plataforma Digital de Crédito", "Camada distribuída de originação digital.",
            "alta", atributos: new() { ["plataforma"] = "nuvem", ["situacao"] = "estratégico" });

        var appSim = await Novo("aplicacao", "Motor de Simulação", "Serviço de cálculo de condições de financiamento.",
            "alta", sisDigital, new() { ["linguagem"] = "Java 21 / Spring Boot",
                                        ["repositorio_url"] = "https://git.exemplo.com.br/credito/motor-simulacao" });
        var appCtr = await Novo("aplicacao", "Gestor de Contratos", "Programas COBOL de manutenção contratual.",
            "critica", sisLegado, new() { ["linguagem"] = "COBOL / CICS / DB2",
                                          ["repositorio_url"] = "https://git.exemplo.com.br/legado/siaci-core" });
        var appGar = await Novo("aplicacao", "Portal de Garantias", "Cadastro e acompanhamento de garantias.",
            "alta", sisDigital, new() { ["linguagem"] = "Python / FastAPI",
                                        ["repositorio_url"] = "https://git.exemplo.com.br/garantias/calculo-habitacao" });

        var repoSim = await Novo("repositorio", "motor-simulacao", "Código do motor de cálculo.", pai: appSim,
            atributos: new() { ["url"] = "https://git.exemplo.com.br/credito/motor-simulacao" });
        // Fica em rascunho, sem owner: é a pendência real que a demonstração mostra.
        await Novo("repositorio", "siaci-core", "Fontes COBOL do core de contratos.", "critica", appCtr,
            new() { ["url"] = "https://git.exemplo.com.br/legado/siaci-core" });
        var repoGar = await Novo("repositorio", "calculo-habitacao", "Rotinas de cálculo de garantia habitacional.",
            pai: appGar, atributos: new() { ["url"] = "https://git.exemplo.com.br/garantias/calculo-habitacao" });

        var apiSim = await Novo("api", "API Simulação", "Expõe o cálculo de condições para canais digitais.", "alta", appSim,
            new() { ["versao"] = "2.3", ["contrato_url"] = "https://api.exemplo.com.br/simulacao/openapi.json" });
        var apiCtr = await Novo("api", "API Contratos", "Consulta e manutenção de contratos vigentes.", "critica", appCtr,
            new() { ["versao"] = "3.0", ["contrato_url"] = "https://api.exemplo.com.br/contratos/openapi.json" });
        var apiGar = await Novo("api", "API Garantias", "Registro e consulta de garantias.", "alta", appGar,
            new() { ["versao"] = "1.4", ["contrato_url"] = "https://api.exemplo.com.br/garantias/openapi.json" });

        var endSim = await Novo("endpoint", "POST /simulacoes", "Cria uma simulação de financiamento.", pai: apiSim,
            atributos: new() { ["metodo"] = "POST", ["caminho"] = "/simulacoes" });
        await Novo("endpoint", "GET /contratos/{id}", "Consulta contrato por identificador.", "alta", apiCtr,
            new() { ["metodo"] = "GET", ["caminho"] = "/contratos/{id}" });
        await Novo("endpoint", "POST /garantias/{id}/laudos", "Anexa laudo de avaliação à garantia.", pai: apiGar,
            atributos: new() { ["metodo"] = "POST", ["caminho"] = "/garantias/{id}/laudos" });

        var bdCtr = await Novo("base_dados", "DB2 Contratos", "Base transacional do core de contratos.", "critica", sisLegado,
            new() { ["tecnologia"] = "IBM DB2 z/OS" });
        var objCtr = await Novo("objeto_dado", "TB_CONTRATO", "Tabela mestre de contratos.", "critica", bdCtr,
            new() { ["classificacao_dado"] = "restrito" });

        var evt = await Novo("evento", "ContratoFormalizado", "Publicado quando o contrato é assinado e registrado.",
            "alta", ctxGestao, new() { ["mecanismo"] = "assincrono" });

        // ──────────────────────────────────────────────────────── ownership
        // No piloto cada domínio tinha a sua pessoa de negócio e cada squad o seu
        // tech lead; aqui há uma pessoa por papel em cada empresa de teste.
        var negocial = PapelDeOwnership.OwnerNegocial;
        var tecnico = PapelDeOwnership.OwnerTecnico;
        (IdDeAtivo Ativo, Pessoa Pessoa, PapelDeOwnership Papel)[] responsaveis =
        [
            (domCtr, e.Negocio, negocial), (subOrig, e.Negocio, negocial), (subManut, e.Negocio, negocial),
            (ctxSim, e.Negocio, negocial), (ctxSim, e.TechLead, tecnico),
            (ctxGestao, e.Negocio, negocial), (ctxGestao, e.TechLead, tecnico),
            (capSim, e.Negocio, negocial), (capSim, e.TechLead, tecnico),
            (capPort, e.Negocio, negocial), (capPort, e.TechLead, tecnico),
            (domGar, e.Negocio, negocial), (subImob, e.Negocio, negocial),
            (ctxImob, e.Negocio, negocial), (ctxImob, e.TechLead, tecnico),
            (capAval, e.Negocio, negocial), (capAval, e.TechLead, tecnico),
            (sisLegado, e.Arquiteto, tecnico), (sisDigital, e.TechLead, tecnico),
            (appSim, e.TechLead, tecnico), (appCtr, e.TechLead, tecnico), (appGar, e.TechLead, tecnico),
            (repoSim, e.TechLead, tecnico), (repoGar, e.TechLead, tecnico),
            (apiSim, e.TechLead, tecnico), (apiCtr, e.TechLead, tecnico), (apiGar, e.TechLead, tecnico),
            (bdCtr, e.Arquiteto, tecnico), (evt, e.TechLead, tecnico),
        ];
        foreach (var (ativo, pessoa, papel) in responsaveis)
        {
            Exigir(await ComoAsync(e, e.Curador, sp => sp.GetRequiredService<AtribuirResponsavel>().ExecutarAsync(
                new ComandoAtribuirResponsavel(ativo, pessoa.Id, papel), ct)), $"responsável de {ativo}");
        }

        // ────────────────────────────────────────────────────────── relações
        // Criticidade da dependência como no seed do painel: é ela que faz a
        // descontinuação de uma capacidade pedir o código de confirmação.
        const Criticidade A = Criticidade.Alta, C = Criticidade.Critica, M = Criticidade.Media;
        (IdDeAtivo Origem, IdDeAtivo Destino, string Tipo, Criticidade Criticidade)[] relacoes =
        [
            (appSim, capSim, "implementa", A), (appCtr, capPort, "implementa", C), (appGar, capAval, "implementa", A),
            (apiSim, capSim, "implementa", A), (apiCtr, capPort, "implementa", C), (apiGar, capAval, "implementa", A),
            (apiSim, endSim, "expoe", M), (appSim, apiGar, "consome", A), (appCtr, objCtr, "persiste_em", C),
            (ctxSim, ctxImob, "depende_de", A), (ctxGestao, ctxImob, "depende_de", C),
            (appCtr, evt, "produz", A), (appGar, evt, "consome", M),
        ];
        foreach (var (origem, destino, tipo, criticidade) in relacoes)
        {
            Exigir(await ComoAsync(e, e.Curador, sp => sp.GetRequiredService<RelacionarAtivos>().ExecutarAsync(
                new ComandoRelacionarAtivos(origem, destino, tipo, criticidade), ct)), $"relação {tipo}");
        }

        // ───────────────────────────────────────────────────────── evidências
        (IdDeAtivo Ativo, string Tipo, string Titulo, string? Url)[] evidencias =
        [
            (domCtr, "documento", "Ata do fórum de governança", null),
            (domGar, "documento", "Política corporativa de garantias", null),
            (ctxSim, "adr", "ADR-014 separação simulação/contratação", null),
            (ctxGestao, "adr", "ADR-021 estratégia de portabilidade", null),
            (ctxImob, "adr", "ADR-009 camada anticorrupção de cartório", null),
            (capSim, "documento", "Manual de política de crédito", null),
            (capAval, "documento", "Norma de avaliação de imóveis", null),
            (capPort, "documento", "Fluxo de portabilidade", null),
            (apiSim, "openapi", "Contrato OpenAPI 2.3", "https://api.exemplo.com.br/simulacao/openapi.json"),
            (apiCtr, "openapi", "Contrato OpenAPI 3.0", "https://api.exemplo.com.br/contratos/openapi.json"),
            // API crítica exige duas evidências: com elas, entra na fila de validação.
            (apiCtr, "adr", "ADR-030 versionamento do contrato de contratos", null),
            (apiGar, "openapi", "Contrato OpenAPI 1.4", null),
            (repoSim, "repositorio", "Repositório Git", "https://git.exemplo.com.br/credito/motor-simulacao"),
            (repoGar, "repositorio", "Repositório Git", "https://git.exemplo.com.br/garantias/calculo-habitacao"),
            (appSim, "documento", "Diagrama de implantação", null),
            (appCtr, "documento", "Inventário de programas COBOL", null),
            (appGar, "documento", "Diagrama de contexto", null),
            (sisDigital, "documento", "Visão de arquitetura", null),
            (bdCtr, "documento", "Dicionário de dados", null),
            (evt, "documento", "Esquema Avro do evento", null),
        ];
        foreach (var (ativo, tipo, titulo, url) in evidencias)
        {
            Exigir(await ComoAsync(e, e.Curador, sp => sp.GetRequiredService<AnexarEvidencia>().ExecutarAsync(
                new ComandoAnexarEvidencia(ativo, tipo, titulo, url), ct)), $"evidência {titulo}");
        }

        // ──────────────────────────────────── publicação da base já governada
        IdDeAtivo[] publicados =
        [
            domCtr, subOrig, subManut, ctxSim, ctxGestao, capSim, domGar, subImob, ctxImob, capAval,
            sisLegado, sisDigital, appSim, appCtr, appGar, repoSim, repoGar, apiSim, apiGar,
            bdCtr, objCtr, evt, endSim,
        ];
        foreach (var ativo in publicados)
        {
            await SubmeterAsync(e, ativo, "Carga inicial do piloto", ct);

            var abertas = await ComoAsync(e, e.Admin, async sp =>
                (await sp.GetRequiredService<IRepositorioDeValidacoes>().ListarAsync(ativo, ct))
                    .Where(v => v.Status == StatusDaValidacao.Aberta).Select(v => v.Id).ToArray());

            // Um escopo por decisão: cada uma relê o ativo, como uma requisição faria.
            foreach (var id in abertas)
            {
                Exigir(await ComoAsync(e, e.Admin, sp => sp.GetRequiredService<DecidirValidacao>().ExecutarAsync(
                    new ComandoDecidirValidacao(id, true, "Aprovado na carga assistida"), ct)), "decisão");
            }
        }

        // ─────────────────────── fila de validação em aberto, para decidir ao vivo
        await SubmeterAsync(e, apiCtr, "Publicação da versão 3.0 do contrato", ct);
        await SubmeterAsync(e, capPort, "Nova capacidade de portabilidade", ct);

        return new Resumo(cadastrados, publicados.Length, NaFila: 2);
    }

    private async Task SubmeterAsync(EmpresaDeTeste e, IdDeAtivo ativo, string motivo, CancellationToken ct) =>
        Exigir(await ComoAsync(e, e.Curador, sp => sp.GetRequiredService<SubmeterAtivo>().ExecutarAsync(
            new ComandoSubmeterAtivo(ativo, motivo), ct)), $"submeter {ativo}");

    /// <summary>
    /// Roda <paramref name="acao"/> num escopo novo, como se fosse uma requisição
    /// daquela pessoa naquela empresa: mesmo contexto de tenant, mesmo ator.
    /// </summary>
    private async Task<T> ComoAsync<T>(EmpresaDeTeste e, Pessoa pessoa, Func<IServiceProvider, Task<T>> acao)
    {
        await using var escopo = escopos.CreateAsyncScope();
        var contexto = escopo.ServiceProvider.GetRequiredService<ContextoDaRequisicao>();
        var ator = new Ator(pessoa.Id, e.Id,
            [AtribuicaoDePapel.NaEmpresa(pessoa.Papel, relogio.Hoje.AddYears(-1))], Unidade: e.Unidade);
        contexto.Definir(new ContextoDeTenant(e.Id, e.Unidade), ator);
        return await acao(escopo.ServiceProvider);
    }

    /// <summary>Recusa na carga é bug da carga: falha alto, com o motivo do caso de uso.</summary>
    private static T Exigir<T>(Resultado<T> r, string passo) =>
        r.Sucesso
            ? r.Valor!
            : throw new InvalidOperationException($"Carga de demonstração falhou em '{passo}': {r.Codigo} {r.Mensagem}");
}
