using Jornada.Dominio.Catalogo;
using Jornada.Dominio.Comum;
using Jornada.Dominio.Testes.Harness;

namespace Jornada.Dominio.Testes.Casos;

/// <summary>
/// Portado de <c>tests/test_governanca.py</c> (regras de ciclo de vida).
///
/// No painel-ddd, cada um destes casos exige `create_app`, `seed` e `tmp_path`.
/// Aqui rodam em memória, em microssegundos — que é exatamente o ganho que o
/// ADR-0001 prometeu.
/// </summary>
[Suite("Ciclo de vida (governanca.TRANSICOES)")]
public sealed class CicloDeVidaTestes
{
    [Teste("rascunho vai para em_validacao")]
    public void RascunhoVaiParaEmValidacao() =>
        Verificar.Verdadeiro(
            MaquinaDeCicloDeVida.Permite(StatusCicloVida.Rascunho, StatusCicloVida.EmValidacao),
            "rascunho -> em_validacao");

    [Teste("rascunho NÃO vai direto para publicado")]
    public void RascunhoNaoVaiDiretoParaPublicado() =>
        Verificar.Falso(
            MaquinaDeCicloDeVida.Permite(StatusCicloVida.Rascunho, StatusCicloVida.Publicado),
            "publicar sem passar pela validação");

    [Teste("em_validacao pode voltar para rascunho (rejeição)")]
    public void EmValidacaoVoltaParaRascunho() =>
        Verificar.Verdadeiro(
            MaquinaDeCicloDeVida.Permite(StatusCicloVida.EmValidacao, StatusCicloVida.Rascunho),
            "rejeição devolve para rascunho");

    [Teste("publicado só vai para em_revisao ou descontinuado")]
    public void PublicadoTemDoisDestinos()
    {
        var destinos = MaquinaDeCicloDeVida.DestinosDe(StatusCicloVida.Publicado);
        Verificar.Igual(2, destinos.Count);
        Verificar.Contem(destinos, StatusCicloVida.EmRevisao);
        Verificar.Contem(destinos, StatusCicloVida.Descontinuado);
    }

    [Teste("arquivado é estado terminal")]
    public void ArquivadoEhTerminal() =>
        Verificar.Vazio(MaquinaDeCicloDeVida.DestinosDe(StatusCicloVida.Arquivado),
            "arquivado não tem saída");

    [Teste("descontinuado só pode ser arquivado")]
    public void DescontinuadoSoArquiva()
    {
        var destinos = MaquinaDeCicloDeVida.DestinosDe(StatusCicloVida.Descontinuado);
        Verificar.Igual(1, destinos.Count);
        Verificar.Contem(destinos, StatusCicloVida.Arquivado);
    }

    [Teste("transição inválida nomeia os destinos válidos")]
    public void TransicaoInvalidaExplica()
    {
        var erro = Verificar.Lanca<TransicaoInvalida>(() =>
            MaquinaDeCicloDeVida.Exigir(StatusCicloVida.Arquivado, StatusCicloVida.Publicado));

        Verificar.Verdadeiro(erro.Message.Contains("estado terminal"),
            $"a mensagem deve explicar o porquê; veio: {erro.Message}");
    }

    [Teste("o agregado recusa submeter quando já está publicado")]
    public void AgregadoRecusaSubmeterPublicado()
    {
        var ativo = Cenario.AtivoPublicado();
        Verificar.Lanca<TransicaoInvalida>(
            () => ativo.Submeter("tentativa", Guid.NewGuid(), Cenario.Relogio));
    }

    [Teste("submeter incrementa a revisão e emite AtivoSubmetido")]
    public void SubmeterIncrementaRevisao()
    {
        var ativo = Cenario.AtivoCompleto();
        var autor = Guid.NewGuid();

        ativo.Submeter("primeira versão", autor, Cenario.Relogio);

        Verificar.Igual(StatusCicloVida.EmValidacao, ativo.Status);
        Verificar.Igual(1, ativo.RevisaoAtual);

        var evento = ativo.EventosPendentes.OfType<AtivoSubmetido>().Single();
        Verificar.Igual(autor, evento.IdAutor);
        Verificar.Igual(1, evento.Revisao);
    }

    [Teste("a chave de idempotência do evento distingue revisões")]
    public void ChaveDeIdempotenciaDistingueRevisoes()
    {
        var ativo = Cenario.AtivoCompleto();
        ativo.Submeter("v1", Guid.NewGuid(), Cenario.Relogio);
        var chave1 = ativo.EventosPendentes.OfType<AtivoSubmetido>().Single().ChaveDeIdempotencia;

        ativo.DevolverParaRascunho(Cenario.Relogio);
        ativo.DrenarEventos();
        ativo.Submeter("v2", Guid.NewGuid(), Cenario.Relogio);
        var chave2 = ativo.EventosPendentes.OfType<AtivoSubmetido>().Single().ChaveDeIdempotencia;

        Verificar.Falso(chave1 == chave2,
            "duas submissões do mesmo ativo não podem colidir na outbox");
    }
}
