using Jornada.Dominio.Catalogo;
using Jornada.Dominio.Governanca;
using Jornada.Dominio.Qualidade;
using Jornada.Dominio.Testes.Harness;

namespace Jornada.Dominio.Testes.Casos;

/// <summary>
/// Portado de <c>tests/test_governanca.py</c> (regras de publicação).
/// A função mais importante do produto, agora testável sem banco.
/// </summary>
[Suite("Pré-check de publicação (governanca.pre_check)")]
public sealed class PreCheckTestes
{
    private static ResultadoPreCheck Avaliar(
        Ativo ativo, string tipo = "sistema",
        ContextoDePreCheck? contexto = null,
        ContextoDeQualidade? qualidade = null)
    {
        var t = Cenario.Tipo(tipo);
        var politica = Cenario.Politica(tipo, ativo.Criticidade);
        var ctxQ = qualidade ?? new ContextoDeQualidade(true, false, 0, 1);
        var score = CalculadoraDeQualidade.Avaliar(ativo, t, politica, ctxQ, Cenario.Relogio);
        var ctxP = contexto ?? ContextoDePreCheck.Vazio();
        return PoliticaDePublicacao.Avaliar(ativo, t, politica, score, ctxP, Cenario.Relogio);
    }

    [Teste("ativo sem descrição é bloqueado")]
    public void SemDescricaoBloqueia()
    {
        var r = Avaliar(Cenario.AtivoVazio());
        Verificar.Falso(r.Aprovado);
        Verificar.Contem(r.Codigos, CodigoDeBloqueio.Descricao);
    }

    [Teste("campo obrigatório do tipo vazio é bloqueado")]
    public void CampoObrigatorioVazioBloqueia()
    {
        // "sistema" exige o campo `plataforma`
        var ativo = Ativo.Rascunhar(Cenario.EmpresaA, Cenario.Tipo("sistema"),
            CodigoDeAtivo.De("SIS-0009"), "Sistema Sem Plataforma", Cenario.Relogio,
            descricao: "tem descrição, mas não tem plataforma");
        ativo.AtribuirResponsavel(Guid.NewGuid(), PapelDeOwnership.OwnerTecnico, Cenario.Relogio);

        var r = Avaliar(ativo);
        Verificar.Contem(r.Codigos, CodigoDeBloqueio.Campo);
    }

    [Teste("owner técnico ausente é bloqueado quando o tipo exige")]
    public void OwnerAusenteBloqueia()
    {
        var ativo = Ativo.Rascunhar(Cenario.EmpresaA, Cenario.Tipo("sistema"),
            CodigoDeAtivo.De("SIS-0002"), "Sem Dono", Cenario.Relogio,
            descricao: "descrito",
            atributos: new Dictionary<string, string> { ["plataforma"] = "nuvem" });

        var r = Avaliar(ativo);
        Verificar.Contem(r.Codigos, CodigoDeBloqueio.Owner);
    }

    [Teste("nome duplicado na MESMA empresa é bloqueado")]
    public void NomeDuplicadoBloqueia()
    {
        var r = Avaliar(Cenario.AtivoCompleto(),
            contexto: new ContextoDePreCheck(true, NomeDuplicadoNaEmpresa: true, false, []));
        Verificar.Contem(r.Codigos, CodigoDeBloqueio.Duplicidade);
    }

    [Teste("dependência circular é bloqueada")]
    public void CicloBloqueia()
    {
        var r = Avaliar(Cenario.AtivoCompleto(),
            contexto: new ContextoDePreCheck(true, false, TemDependenciaCircular: true, []));
        Verificar.Contem(r.Codigos, CodigoDeBloqueio.Ciclo);
    }

    [Teste("hierarquia faltando é bloqueada quando o tipo exige pai")]
    public void HierarquiaFaltandoBloqueia()
    {
        // "aplicacao" exige pai do tipo "sistema"
        var ativo = Ativo.Rascunhar(Cenario.EmpresaA, Cenario.Tipo("aplicacao"),
            CodigoDeAtivo.De("APP-0001"), "App Órfã", Cenario.Relogio,
            descricao: "sem sistema pai");
        ativo.AtribuirResponsavel(Guid.NewGuid(), PapelDeOwnership.OwnerTecnico, Cenario.Relogio);

        var r = Avaliar(ativo, "aplicacao",
            contexto: new ContextoDePreCheck(PaiInformado: false, false, false, []));
        Verificar.Contem(r.Codigos, CodigoDeBloqueio.Hierarquia);
    }

    [Teste("evidência mínima não atendida é bloqueada")]
    public void EvidenciaMinimaBloqueia()
    {
        // "aplicacao" exige evidencia_minima = 1
        var ativo = Ativo.Rascunhar(Cenario.EmpresaA, Cenario.Tipo("aplicacao"),
            CodigoDeAtivo.De("APP-0002"), "App", Cenario.Relogio,
            descricao: "descrita", idPai: IdDeAtivo.Novo());
        ativo.AtribuirResponsavel(Guid.NewGuid(), PapelDeOwnership.OwnerTecnico, Cenario.Relogio);

        var r = Avaliar(ativo, "aplicacao");
        Verificar.Contem(r.Codigos, CodigoDeBloqueio.Evidencia);
    }

    [Teste("API crítica exige 2 evidências e score 80 — política por criticidade")]
    public void ApiCriticaEhMaisExigente()
    {
        var normal = Cenario.Politica("api", Criticidade.Media);
        var critica = Cenario.Politica("api", Criticidade.Critica);

        Verificar.Igual(1, normal.EvidenciaMinima);
        Verificar.Igual(2, critica.EvidenciaMinima);
        Verificar.Igual(70, normal.ScoreMinimo);
        Verificar.Igual(80, critica.ScoreMinimo);
        Verificar.Igual(24, critica.SlaHoras);
        Verificar.Igual(3, critica.Etapas.Count);
    }

    [Teste("relação com ativo fora de vigência é ALERTA, não bloqueio")]
    public void RelacaoForaDeVigenciaEhAlerta()
    {
        var r = Avaliar(Cenario.AtivoCompleto(),
            contexto: new ContextoDePreCheck(true, false, false, ["Sistema Legado"]));

        Verificar.NaoContem(r.Codigos, CodigoDeBloqueio.Ciclo);
        Verificar.Verdadeiro(
            r.Alertas.Any(a => a.Contains("Sistema Legado")),
            "deve alertar sem impedir a publicação");
    }

    [Teste("ativo completo passa no pré-check")]
    public void AtivoCompletoPassa()
    {
        var r = Avaliar(Cenario.AtivoCompleto());
        Verificar.Verdadeiro(r.Aprovado,
            $"esperava aprovado; bloqueios: {string.Join(" | ", r.Bloqueios)}");
        Verificar.Vazio(r.Bloqueios);
    }

    [Teste("os códigos de bloqueio têm chave estável para a API")]
    public void CodigosTemChaveEstavel()
    {
        Verificar.Igual("SCORE_ABAIXO_DO_MINIMO", CodigoDeBloqueio.Score.Chave());
        Verificar.Igual("EVIDENCIA_INSUFICIENTE", CodigoDeBloqueio.Evidencia.Chave());
        Verificar.Igual("OWNER_NAO_DEFINIDO", CodigoDeBloqueio.Owner.Chave());
    }

    [Teste("o checklist deriva do pré-check, não reimplementa regra")]
    public void ChecklistDerivaDoPreCheck()
    {
        var r = Avaliar(Cenario.AtivoVazio());
        var caminho = CaminhoDePublicacao.De(r);

        Verificar.Igual(5, caminho.Count);

        var descrever = caminho.Single(p => p.Chave == "descrever");
        Verificar.Falso(descrever.Concluido, "falta descrição");
        Verificar.Verdadeiro(descrever.Impedimentos.Count > 0);

        var todosImpedimentos = caminho.SelectMany(p => p.Impedimentos).Select(b => b.Codigo);
        foreach (var codigo in r.Codigos)
            Verificar.Contem(todosImpedimentos, codigo,
                "todo bloqueio do pré-check tem de aparecer em algum passo");
    }
}
