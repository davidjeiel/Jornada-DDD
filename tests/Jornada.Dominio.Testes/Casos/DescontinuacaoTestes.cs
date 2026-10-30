using Jornada.Adaptadores.Memoria;
using Jornada.Aplicacao.Catalogo.Comandos;
using Jornada.Aplicacao.Catalogo.Consultas;
using Jornada.Aplicacao.Portas;
using Jornada.Dominio.Acesso;
using Jornada.Dominio.Catalogo;
using Jornada.Dominio.Testes.Harness;

namespace Jornada.Dominio.Testes.Casos;

/// <summary>
/// Saída do catálogo: análise de impacto, descontinuação com confirmação e
/// arquivamento, pelos casos de uso. Portados de <c>tests/test_governanca.py</c>
/// e <c>tests/test_acesso.py</c> do painel-ddd; o nome do teste original vai
/// no fim de cada título.
/// </summary>
[Suite("Ciclo de vida: descontinuar e arquivar (casos de uso)")]
public sealed class DescontinuacaoTestes
{
    [Teste("consumidor crítico bloqueia a descontinuação; com a confirmação, passa (test_descontinuacao_bloqueada_por_consumidor_critico)")]
    public void ConsumidorCriticoBloqueia()
    {
        var m = new Mundo();
        var cap = m.Publicado("capacidade", "CAP-0001", "Conceder crédito");
        var app = m.Publicado("aplicacao", "APP-0001", "Gestor");
        m.Relacionar(app, cap, "implementa", Criticidade.Critica);

        var bloqueado = m.Descontinuar(cap, "obsoleta");
        Verificar.Falso(bloqueado.Sucesso, "sem confirmação, consumidor crítico bloqueia");
        Verificar.Igual("CONFIRMACAO_NECESSARIA", bloqueado.Codigo);
        Verificar.Igual(StatusCicloVida.Publicado, m.Ativo(cap).Status);

        var forcado = m.Descontinuar(cap, "obsoleta", confirmacao: "CAP-0001");
        Verificar.Verdadeiro(forcado.Sucesso, forcado.Mensagem ?? "");
        Verificar.Igual(1, forcado.Valor!.Afetados);
        Verificar.Igual(StatusCicloVida.Descontinuado, m.Ativo(cap).Status);
        Verificar.Igual(Cenario.Relogio.Hoje, m.Ativo(cap).FimVigencia);
    }

    [Teste("confirmação errada não descontinua; o código vale sem diferença de caixa (test_descontinuar_com_consumidor_critico_exige_o_codigo)")]
    public void ConfirmacaoErradaNaoDescontinua()
    {
        var m = new Mundo();
        var cap = m.Publicado("capacidade", "CAP-0001", "Conceder crédito");
        var app = m.Publicado("aplicacao", "APP-0001", "Gestor");
        m.Relacionar(app, cap, "implementa", Criticidade.Alta);

        Verificar.Igual("CONFIRMACAO_NECESSARIA", m.Descontinuar(cap, "obsoleta", "qualquer coisa").Codigo);
        Verificar.Igual(StatusCicloVida.Publicado, m.Ativo(cap).Status, "confirmação errada não descontinua");

        Verificar.Verdadeiro(m.Descontinuar(cap, "obsoleta", " cap-0001 ").Sucesso);
        Verificar.Igual(StatusCicloVida.Descontinuado, m.Ativo(cap).Status);
    }

    [Teste("sem consumidor crítico, descontinua sem pedir o código")]
    public void SemCriticoNaoPedeCodigo()
    {
        var m = new Mundo();
        var cap = m.Publicado("capacidade", "CAP-0001", "Conceder crédito");
        var app = m.Publicado("aplicacao", "APP-0001", "Gestor");
        m.Relacionar(app, cap, "implementa", Criticidade.Media);

        var r = m.Descontinuar(cap, "obsoleta");
        Verificar.Verdadeiro(r.Sucesso, r.Mensagem ?? "");
        Verificar.Igual(1, r.Valor!.Afetados);
    }

    [Teste("o impacto conta só consumidores em vigor, mais críticos primeiro")]
    public void ImpactoContaSoQuemVale()
    {
        var m = new Mundo();
        var cap = m.Publicado("capacidade", "CAP-0001", "Conceder crédito");
        var critico = m.Publicado("aplicacao", "APP-0001", "Gestor");
        var medio = m.Publicado("api", "API-0001", "Crédito API");
        var rascunho = m.Rascunho("aplicacao", "APP-0002", "Protótipo");
        m.Relacionar(medio, cap, "implementa", Criticidade.Media);
        m.Relacionar(critico, cap, "implementa", Criticidade.Critica);
        m.Relacionar(rascunho, cap, "implementa", Criticidade.Critica);   // rascunho não é consumidor
        m.Relacionar(cap, critico, "depende_de", Criticidade.Critica);    // saída não é consumidor

        var impacto = m.Impacto(cap);
        Verificar.Igual(2, impacto.Total);
        Verificar.Igual(1, impacto.Criticos.Count);
        Verificar.Igual("APP-0001", impacto.Consumidores[0].Codigo, "o crítico vem primeiro");
        Verificar.Igual(1, impacto.PorCriticidade["critica"]);
        Verificar.Igual(1, impacto.PorTipo["api"]);
    }

    [Teste("descontinuar exige motivo e permissão")]
    public void ExigeMotivoEPermissao()
    {
        var m = new Mundo();
        var cap = m.Publicado("capacidade", "CAP-0001", "Conceder crédito");

        Verificar.Igual("MOTIVO_OBRIGATORIO", m.Descontinuar(cap, "  ").Codigo);
        Verificar.Igual("SEM_PERMISSAO", m.Descontinuar(cap, "obsoleta", quem: Cenario.Ator(Papel.Negocio)).Codigo);
        Verificar.Igual(StatusCicloVida.Publicado, m.Ativo(cap).Status);
    }

    [Teste("rascunho não se descontinua: arquiva-se")]
    public void RascunhoArquivaNaoDescontinua()
    {
        var m = new Mundo();
        var id = m.Rascunho("sistema", "SIS-0001", "Core");

        Verificar.Igual("TRANSICAO_INVALIDA", m.Descontinuar(id, "não vai adiante").Codigo);
        Verificar.Verdadeiro(m.Arquivar(id).Sucesso);
        Verificar.Igual(StatusCicloVida.Arquivado, m.Ativo(id).Status);
    }

    [Teste("publicado só arquiva depois de descontinuado; arquivado é terminal")]
    public void PublicadoArquivaDepoisDeDescontinuado()
    {
        var m = new Mundo();
        var id = m.Publicado("sistema", "SIS-0001", "Core");

        Verificar.Igual("TRANSICAO_INVALIDA", m.Arquivar(id).Codigo);
        Verificar.Verdadeiro(m.Descontinuar(id, "substituído").Sucesso);
        Verificar.Verdadeiro(m.Arquivar(id).Sucesso);
        Verificar.Igual("TRANSICAO_INVALIDA", m.Arquivar(id).Codigo, "arquivado não vai a lugar nenhum");
    }

    [Teste("hierarquia inválida é recusada no cadastro (test_hierarquia_invalida_e_recusada)")]
    public void HierarquiaInvalida()
    {
        var m = new Mundo();
        var dominio = m.Rascunho("dominio", "DOM-0001", "Crédito");

        // Capacidade é filha de contexto, não de domínio.
        var r = new CadastrarAtivo(m.Repo, m.Metamodelo, m.Tenant, new AtorFixo(Mundo.Curador), m.Repo, m.Repo, Cenario.Relogio)
            .ExecutarAsync(new ComandoCadastrarAtivo("capacidade", "Capacidade solta", IdPai: dominio)).Result;

        Verificar.Falso(r.Sucesso);
        Verificar.Igual("HIERARQUIA_INVALIDA", r.Codigo);
    }

    private sealed class Mundo
    {
        public static readonly Ator Curador = Cenario.Ator(Papel.Curador);

        private readonly ArmazemEmMemoria _armazem = new();
        public ContextoDeTenantFixo Tenant { get; } = ContextoDeTenantFixo.Para(Cenario.EmpresaA);
        public MetamodeloPadrao Metamodelo { get; } = new();

        public RepositorioDeAtivosEmMemoria Repo => new(_armazem, Tenant);

        public Ativo Ativo(IdDeAtivo id) => Repo.ObterAsync(id).Result!;

        public IdDeAtivo Rascunho(string tipo, string codigo, string nome)
        {
            var ativo = Dominio.Catalogo.Ativo.Rascunhar(Cenario.EmpresaA, Cenario.Tipo(tipo),
                CodigoDeAtivo.De(codigo), nome, Cenario.Relogio);
            Repo.AdicionarAsync(ativo).Wait();
            return ativo.Id;
        }

        /// <summary>Publicado direto no agregado, como o UPDATE dos testes do painel: o rito não é o assunto aqui.</summary>
        public IdDeAtivo Publicado(string tipo, string codigo, string nome)
        {
            var id = Rascunho(tipo, codigo, nome);
            Ativo(id).Submeter("v1", Guid.NewGuid(), Cenario.Relogio);
            Ativo(id).Publicar(Cenario.Relogio);
            return id;
        }

        public void Relacionar(IdDeAtivo origem, IdDeAtivo destino, string tipo, Criticidade criticidade) =>
            Repo.RelacionarAsync(origem, destino, tipo, criticidade).Wait();

        public AnalisarImpacto.Saida Impacto(IdDeAtivo id) =>
            new AnalisarImpacto(Repo, Repo).ExecutarAsync(id).Result.Valor!;

        public Aplicacao.Resultado<DescontinuarAtivo.Saida> Descontinuar(
            IdDeAtivo id, string motivo, string? confirmacao = null, Ator? quem = null) =>
            new DescontinuarAtivo(Repo, new AnalisarImpacto(Repo, Repo), Metamodelo, Tenant,
                    new AtorFixo(quem ?? Curador), Repo, Repo, Cenario.Relogio)
                .ExecutarAsync(new ComandoDescontinuarAtivo(id, motivo, confirmacao)).Result;

        public Aplicacao.Resultado<bool> Arquivar(IdDeAtivo id) =>
            new ArquivarAtivo(Repo, Metamodelo, Tenant, new AtorFixo(Curador), Repo, Repo, Cenario.Relogio)
                .ExecutarAsync(new ComandoArquivarAtivo(id)).Result;
    }
}
