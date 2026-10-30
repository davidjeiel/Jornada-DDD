using Jornada.Adaptadores.Memoria;
using Jornada.Aplicacao.Portas;
using Jornada.Dominio.Catalogo;
using Jornada.Dominio.Comum;
using Jornada.Dominio.Governanca;
using Jornada.Dominio.Tenancy;
using Jornada.Dominio.Testes.Harness;
using Jornada.Persistencia.Cosmos;
using Jornada.Persistencia.Testes.Contrato;

namespace Jornada.Persistencia.Testes.Adaptadores;

/// <summary>
/// O contrato contra o Cosmos DB (ADR-0016): o emulador local ou uma conta real.
///
///  - <c>JORNADA_TESTES_COSMOS</c>: string de conexão. Sem ela, a suíte é pulada.
///  - <c>JORNADA_TESTES_COSMOS_EMULADOR</c>: <c>true</c> para o emulador
///    (certificado autoassinado, modo gateway).
///
/// Database e containers são criados na primeira sessão, se não existirem.
/// </summary>
[Suite("Contrato de persistência — Cosmos DB")]
public sealed class ContratoCosmos : ContratoDePersistencia
{
    private const string VariavelDeConexao = "JORNADA_TESTES_COSMOS";
    private const string VariavelDoEmulador = "JORNADA_TESTES_COSMOS_EMULADOR";

    private static readonly Lazy<BancoDoCatalogo?> Banco = new(Preparar);

    protected override Sessao Abrir(IdDeEmpresa empresa)
    {
        var banco = Banco.Value
            ?? throw new TestePulado($"defina {VariavelDeConexao} para rodar contra o Cosmos DB");

        var repositorio = new RepositorioCosmos(banco, ContextoDeTenantFixo.Para(empresa));
        return new Sessao(repositorio, repositorio, repositorio, repositorio, repositorio, repositorio);
    }

    // ── além do contrato: o que só este adaptador garante (ADR-0016 §2 e §5) ──

    [Teste("edição concorrente do mesmo ativo é recusada (ETag), e a primeira vale")]
    public async Task EdicaoConcorrenteRecusada()
    {
        var empresa = IdDeEmpresa.Novo();
        var ativo = Ativo.Rascunhar(empresa, CatalogoDeTipos.Padrao().Obter("sistema"),
            CodigoDeAtivo.De("SIS-0001"), "Core", Relogio);
        await using (var s = Abrir(empresa))
        {
            await s.Ativos.AdicionarAsync(ativo);
            await s.Unidade.ConfirmarAsync();
        }

        await using var primeira = Abrir(empresa);
        await using var segunda = Abrir(empresa);
        var lidoPelaPrimeira = await primeira.Ativos.ObterAsync(ativo.Id) ?? throw new FalhaDeVerificacao("sumiu");
        var lidoPelaSegunda = await segunda.Ativos.ObterAsync(ativo.Id) ?? throw new FalhaDeVerificacao("sumiu");

        lidoPelaPrimeira.Renomear("Nome da primeira", Relogio);
        await primeira.Unidade.ConfirmarAsync();

        lidoPelaSegunda.Renomear("Nome da segunda", Relogio);
        var erro = await Capturar(() => segunda.Unidade.ConfirmarAsync());

        Verificar.Igual("CONFLITO_DE_EDICAO", (erro as ErroDeDominio)?.Codigo,
            "sem ETag, a segunda gravação apagaria a primeira em silêncio");
        await using var nova = Abrir(empresa);
        Verificar.Igual("Nome da primeira", (await nova.Ativos.ObterAsync(ativo.Id))?.Nome);
    }

    [Teste("lote é atômico: se o ativo falha, a validação do mesmo lote não entra")]
    public async Task LoteAtomico()
    {
        var empresa = IdDeEmpresa.Novo();
        var ativo = Ativo.Rascunhar(empresa, CatalogoDeTipos.Padrao().Obter("sistema"),
            CodigoDeAtivo.De("SIS-0001"), "Core", Relogio);
        await using (var s = Abrir(empresa))
        {
            await s.Ativos.AdicionarAsync(ativo);
            await s.Unidade.ConfirmarAsync();
        }

        // Mesmo ativo adicionado de novo (criação duplicada → conflito) junto
        // com uma validação nova: as duas operações vão no mesmo lote.
        var validacao = new ValidacaoEmProcesso(
            Guid.CreateVersion7(), ativo.Id, empresa, 1, EtapaValidacao.Tecnica, Guid.NewGuid());
        await using (var s = Abrir(empresa))
        {
            await s.Ativos.AdicionarAsync(ativo);
            await s.Validacoes.AdicionarAsync(validacao);
            Verificar.Verdadeiro(await Capturar(() => s.Unidade.ConfirmarAsync()) is not null,
                "criar o mesmo ativo duas vezes tem de falhar");
        }

        await using var nova = Abrir(empresa);
        Verificar.Igual(null, await nova.Validacoes.ObterAsync(validacao.Id),
            "a validação foi no mesmo lote do ativo que falhou");
    }

    private static readonly RelogioFixo Relogio = RelogioFixo.Em(2026, 10, 21);

    private static async Task<Exception?> Capturar(Func<Task> acao)
    {
        try { await acao(); return null; }
        catch (Exception e) when (e is not FalhaDeVerificacao) { return e; }
    }

    private static BancoDoCatalogo? Preparar()
    {
        var conexao = Environment.GetEnvironmentVariable(VariavelDeConexao);
        if (string.IsNullOrWhiteSpace(conexao)) return null;

        var emulador = string.Equals(Environment.GetEnvironmentVariable(VariavelDoEmulador), "true",
            StringComparison.OrdinalIgnoreCase);
        var banco = BancoDoCatalogo.Conectar(conexao, "jornada-testes", emulador);
        banco.ProvisionarAsync().GetAwaiter().GetResult();
        return banco;
    }
}
