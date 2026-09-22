using Jornada.Dominio.Catalogo;
using Jornada.Dominio.Qualidade;
using Jornada.Dominio.Testes.Harness;

namespace Jornada.Dominio.Testes.Casos;

/// <summary>Portado de <c>catalogo/qualidade.py</c> — pesos e descontos.</summary>
[Suite("Score de qualidade (qualidade.avaliar)")]
public sealed class QualidadeTestes
{
    private static ScoreDeQualidade Avaliar(
        Ativo ativo, string tipo = "sistema", ContextoDeQualidade? ctx = null) =>
        CalculadoraDeQualidade.Avaliar(
            ativo, Cenario.Tipo(tipo), Cenario.Politica(tipo, ativo.Criticidade),
            ctx ?? new ContextoDeQualidade(true, false, 0, 1), Cenario.Relogio);

    [Teste("os pesos somam 1.0")]
    public void PesosSomamUm()
    {
        var soma = ScoreDeQualidade.PesoCompletude + ScoreDeQualidade.PesoConsistencia
                 + ScoreDeQualidade.PesoOwnership + ScoreDeQualidade.PesoEvidencia
                 + ScoreDeQualidade.PesoTemporalidade;
        Verificar.Verdadeiro(Math.Abs(soma - 1.0) < 1e-9, $"soma dos pesos = {soma}");
    }

    [Teste("score 100 em tudo dá total 100")]
    public void TudoPerfeitoDaCem()
    {
        var s = new ScoreDeQualidade(100, 100, 100, 100, 100, []);
        Verificar.Igual(100, s.Total);
    }

    [Teste("ponderação: 50/100/100/100/100 dá 85")]
    public void PonderacaoConfere()
    {
        // 50*0.30 + 100*0.20 + 100*0.20 + 100*0.15 + 100*0.15 = 15 + 70 = 85
        var s = new ScoreDeQualidade(50, 100, 100, 100, 100, []);
        Verificar.Igual(85, s.Total);
    }

    [Teste("ativo vazio tem completude baixa e lista pendências")]
    public void AtivoVazioTemPendencias()
    {
        var s = Avaliar(Cenario.AtivoVazio());
        Verificar.Igual(0, s.Completude, "nem descrição nem plataforma preenchidas");
        Verificar.Verdadeiro(s.Pendencias.Any(p => p.Contains("Descrição")));
    }

    [Teste("ativo completo tem completude 100")]
    public void AtivoCompletoTemCompletudeCem() =>
        Verificar.Igual(100, Avaliar(Cenario.AtivoCompleto()).Completude);

    [Teste("pai ausente desconta 50 da consistência")]
    public void PaiAusenteDesconta()
    {
        var ativo = Ativo.Rascunhar(Cenario.EmpresaA, Cenario.Tipo("aplicacao"),
            CodigoDeAtivo.De("APP-0003"), "App", Cenario.Relogio, descricao: "x");

        var s = Avaliar(ativo, "aplicacao", new ContextoDeQualidade(false, false, 0, 0));
        Verificar.Igual(50, s.Consistencia);
        Verificar.Verdadeiro(s.Pendencias.Any(p => p.Contains("Vincular")));
    }

    [Teste("pai fora de vigência desconta 30")]
    public void PaiForaDeVigenciaDesconta()
    {
        var ativo = Ativo.Rascunhar(Cenario.EmpresaA, Cenario.Tipo("aplicacao"),
            CodigoDeAtivo.De("APP-0004"), "App", Cenario.Relogio, descricao: "x");

        var s = Avaliar(ativo, "aplicacao", new ContextoDeQualidade(true, true, 0, 0));
        Verificar.Igual(70, s.Consistencia);
    }

    [Teste("relações órfãs descontam 10 cada, com teto de 40")]
    public void OrfasDescontamComTeto()
    {
        var ativo = Cenario.AtivoCompleto();

        Verificar.Igual(80, Avaliar(ativo, ctx: new ContextoDeQualidade(true, false, 2, 1)).Consistencia);
        Verificar.Igual(60, Avaliar(ativo, ctx: new ContextoDeQualidade(true, false, 9, 1)).Consistencia,
            "9 órfãs bateriam 90 de desconto, mas o teto é 40");
    }

    [Teste("capacidade sem implementação desconta 20")]
    public void CapacidadeSemImplementacaoDesconta()
    {
        var cap = Ativo.Rascunhar(Cenario.EmpresaA, Cenario.Tipo("capacidade"),
            CodigoDeAtivo.De("CAP-0001"), "Abrir conta", Cenario.Relogio,
            descricao: "x", idPai: IdDeAtivo.Novo(),
            atributos: new Dictionary<string, string> { ["resultado_esperado"] = "conta aberta" });

        var com = Avaliar(cap, "capacidade", new ContextoDeQualidade(true, false, 0, 1));
        var sem = Avaliar(cap, "capacidade", new ContextoDeQualidade(true, false, 0, 0));

        Verificar.Igual(100, com.Consistencia);
        Verificar.Igual(80, sem.Consistencia);
    }

    [Teste("consistência nunca fica negativa")]
    public void ConsistenciaNuncaNegativa()
    {
        var cap = Ativo.Rascunhar(Cenario.EmpresaA, Cenario.Tipo("capacidade"),
            CodigoDeAtivo.De("CAP-0002"), "Cap", Cenario.Relogio, descricao: "x");

        var s = Avaliar(cap, "capacidade", new ContextoDeQualidade(false, false, 9, 0));
        Verificar.Verdadeiro(s.Consistencia >= 0, $"consistência = {s.Consistencia}");
    }

    [Teste("ownership: 1 de 2 owners exigidos dá 50")]
    public void OwnershipParcial()
    {
        // "contexto" exige owner negocial E técnico
        var ctx = Ativo.Rascunhar(Cenario.EmpresaA, Cenario.Tipo("contexto"),
            CodigoDeAtivo.De("CTX-0001"), "Contexto", Cenario.Relogio,
            descricao: "x", idPai: IdDeAtivo.Novo());
        ctx.AtribuirResponsavel(Guid.NewGuid(), PapelDeOwnership.OwnerNegocial, Cenario.Relogio);

        Verificar.Igual(50, Avaliar(ctx, "contexto").Ownership);
    }

    [Teste("tipo sem owner exigido: sem responsável vale 70, não 0")]
    public void SemOwnerExigidoVale70()
    {
        var end = Ativo.Rascunhar(Cenario.EmpresaA, Cenario.Tipo("endpoint"),
            CodigoDeAtivo.De("END-0001"), "GET /contas", Cenario.Relogio,
            descricao: "x", idPai: IdDeAtivo.Novo());

        Verificar.Igual(70, Avaliar(end, "endpoint").Ownership,
            "o cadastro não está errado, só menos rastreável");
    }

    [Teste("evidência: sem mínimo exigido, nenhuma evidência vale 80")]
    public void SemMinimoNenhumaEvidenciaVale80()
    {
        var sis = Cenario.AtivoCompleto();   // "sistema" tem evidencia_minima = 0
        Verificar.Igual(80, Avaliar(sis).Evidencia);

        sis.AnexarEvidencia("adr", "ADR-001", "https://exemplo/adr-001", Cenario.Relogio);
        Verificar.Igual(100, Avaliar(sis).Evidencia);
    }

    [Teste("temporalidade cai com a idade do cadastro")]
    public void TemporalidadeCaiComIdade()
    {
        // "sistema": periodicidade de revisão = 365 dias
        var relogio = RelogioDe(2026, 9, 22);
        var ativo = Cenario.AtivoCompleto();

        Verificar.Igual(100, Avaliar(ativo).Temporalidade, "cadastro de hoje");

        var antigo = AtivoComIdade(dias: 400);
        Verificar.Igual(60, Avaliar(antigo).Temporalidade, "400 dias: entre 365 e 730");

        var antiquissimo = AtivoComIdade(dias: 900);
        Verificar.Igual(20, Avaliar(antiquissimo).Temporalidade, "900 dias: acima do dobro");

        _ = relogio;
    }

    private static Jornada.Dominio.Comum.RelogioFixo RelogioDe(int a, int m, int d) =>
        Jornada.Dominio.Comum.RelogioFixo.Em(a, m, d);

    /// <summary>Cria um ativo "envelhecido": o relógio anda depois da criação.</summary>
    private static Ativo AtivoComIdade(int dias)
    {
        var passado = Jornada.Dominio.Comum.RelogioFixo.Em(2026, 9, 22);
        passado.Avancar(TimeSpan.FromDays(-dias));

        var ativo = Ativo.Rascunhar(Cenario.EmpresaA, Cenario.Tipo("sistema"),
            CodigoDeAtivo.De("SIS-0100"), "Antigo", passado,
            descricao: "descrito",
            atributos: new Dictionary<string, string> { ["plataforma"] = "mainframe" });
        ativo.AtribuirResponsavel(Guid.NewGuid(), PapelDeOwnership.OwnerTecnico, passado);
        return ativo;
    }
}
