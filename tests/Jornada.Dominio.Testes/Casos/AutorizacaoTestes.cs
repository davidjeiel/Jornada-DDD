using Jornada.Dominio.Acesso;
using Jornada.Dominio.Catalogo;
using Jornada.Dominio.Governanca;
using Jornada.Dominio.Testes.Harness;

namespace Jornada.Dominio.Testes.Casos;

/// <summary>
/// Portado de <c>tests/test_acesso.py</c> (458 linhas — a maior suíte do
/// painel-ddd). A matriz real é 6 papéis × 4 escopos × 11 ações, cruzada com os
/// blocos de tipo. Sem banco, dá para cobrir de verdade (ADR-0007 §3).
/// </summary>
[Suite("Autorização (acesso.pode / pode_decidir)")]
public sealed class AutorizacaoTestes
{
    private static readonly DateOnly Hoje = Cenario.Relogio.Hoje;

    [Teste("curador cadastra em qualquer bloco")]
    public void CuradorCadastraEmQualquerBloco()
    {
        var curador = Cenario.Ator(Papel.Curador);
        Verificar.Verdadeiro(
            PoliticaDeAutorizacao.Pode(curador, Acao.Cadastrar, Cenario.Alvo("dominio"), Hoje),
            "curador na Estrutura DDD");
        Verificar.Verdadeiro(
            PoliticaDeAutorizacao.Pode(curador, Acao.Cadastrar, Cenario.Alvo("api"), Hoje),
            "curador nos Ativos técnicos");
    }

    [Teste("negócio NÃO cadastra ativo técnico (BLOCOS_POR_PAPEL)")]
    public void NegocioNaoCadastraAtivoTecnico()
    {
        var negocio = Cenario.Ator(Papel.Negocio);
        Verificar.Verdadeiro(
            PoliticaDeAutorizacao.Pode(negocio, Acao.Cadastrar, Cenario.Alvo("dominio"), Hoje),
            "negócio na Estrutura DDD: permitido");
        Verificar.Falso(
            PoliticaDeAutorizacao.Pode(negocio, Acao.Cadastrar, Cenario.Alvo("api"), Hoje),
            "o papel autoriza a AÇÃO, mas não o OBJETO");
    }

    [Teste("tech_lead NÃO redesenha a hierarquia de domínios")]
    public void TechLeadNaoMexeEmDominio()
    {
        var tech = Cenario.Ator(Papel.TechLead);
        Verificar.Verdadeiro(
            PoliticaDeAutorizacao.Pode(tech, Acao.Cadastrar, Cenario.Alvo("api"), Hoje));
        Verificar.Falso(
            PoliticaDeAutorizacao.Pode(tech, Acao.Cadastrar, Cenario.Alvo("dominio"), Hoje));
    }

    [Teste("consulta não escreve em lugar nenhum")]
    public void ConsultaNaoEscreve()
    {
        var consulta = Cenario.Ator(Papel.Consulta);
        foreach (var tipo in new[] { "dominio", "api", "sistema", "capacidade" })
            Verificar.Falso(
                PoliticaDeAutorizacao.Pode(consulta, Acao.Cadastrar, Cenario.Alvo(tipo), Hoje),
                $"consulta não cadastra {tipo}");
    }

    [Teste("só admin administra")]
    public void SoAdminAdministra()
    {
        foreach (var papel in new[] { Papel.Curador, Papel.Arquiteto, Papel.TechLead,
                                      Papel.Negocio, Papel.Consulta })
        {
            Verificar.Falso(
                PoliticaDeAutorizacao.Pode(Cenario.Ator(papel), Acao.Administrar,
                    AlvoDaAcao.NaEmpresa(Cenario.EmpresaA), Hoje),
                $"{papel} não administra");
        }
        Verificar.Verdadeiro(
            PoliticaDeAutorizacao.Pode(Cenario.Ator(Papel.Admin), Acao.Administrar,
                AlvoDaAcao.NaEmpresa(Cenario.EmpresaA), Hoje));
    }

    [Teste("ISOLAMENTO: papel da Empresa A não alcança ativo da Empresa B")]
    public void PapelNaoAtravessaEmpresa()
    {
        var adminDaA = Cenario.Ator(Papel.Admin, Cenario.EmpresaA);
        Verificar.Falso(
            PoliticaDeAutorizacao.Pode(adminDaA, Acao.Cadastrar,
                Cenario.Alvo("sistema", Cenario.EmpresaB), Hoje),
            "nem admin atravessa a fronteira de empresa (ADR-0003)");
    }

    [Teste("papel com escopo de domínio só vale naquele domínio")]
    public void EscopoDeDominioLimita()
    {
        var dominioA = Guid.NewGuid();
        var dominioB = Guid.NewGuid();
        var ator = new Ator(Guid.NewGuid(), Cenario.EmpresaA,
            [AtribuicaoDePapel.NoDominio(Papel.Curador, dominioA, Hoje.AddDays(-10))]);

        var tipo = Cenario.Tipo("sistema");
        var alvoA = new AlvoDaAcao(Cenario.EmpresaA, tipo.Chave, tipo.Bloco, IdDominio: dominioA);
        var alvoB = new AlvoDaAcao(Cenario.EmpresaA, tipo.Chave, tipo.Bloco, IdDominio: dominioB);

        Verificar.Verdadeiro(PoliticaDeAutorizacao.Pode(ator, Acao.Cadastrar, alvoA, Hoje));
        Verificar.Falso(PoliticaDeAutorizacao.Pode(ator, Acao.Cadastrar, alvoB, Hoje));
    }

    [Teste("papel vencido não vale mais")]
    public void PapelVencidoNaoVale()
    {
        var ator = new Ator(Guid.NewGuid(), Cenario.EmpresaA,
        [
            new AtribuicaoDePapel(Papel.Admin, TipoDeEscopo.Empresa, null,
                Hoje.AddDays(-60), Hoje.AddDays(-1)),
        ]);
        Verificar.Falso(
            PoliticaDeAutorizacao.Pode(ator, Acao.Administrar,
                AlvoDaAcao.NaEmpresa(Cenario.EmpresaA), Hoje),
            "atribuição expirou ontem");
    }

    [Teste("SEGREGAÇÃO DE FUNÇÃO: quem submeteu não decide, nem sendo admin")]
    public void QuemSubmeteuNaoDecide()
    {
        var pessoa = Guid.NewGuid();
        var admin = Cenario.Ator(Papel.Admin, id: pessoa);
        var ativo = Cenario.AtivoCompleto();

        var decisao = PoliticaDeAutorizacao.PodeDecidir(
            admin, EtapaValidacao.Tecnica, quemSubmeteu: pessoa, ativo,
            Cenario.Alvo("sistema"), Hoje);

        Verificar.Falso(decisao.Permitido, "nem admin contorna a segregação de função");
        Verificar.Verdadeiro(decisao.Motivo.Contains("Segregação"),
            $"o motivo deve dizer o porquê; veio: {decisao.Motivo}");
    }

    [Teste("etapa técnica exige tech_lead")]
    public void EtapaTecnicaExigeTechLead()
    {
        var ativo = Cenario.AtivoCompleto();
        var outro = Guid.NewGuid();

        var negocio = PoliticaDeAutorizacao.PodeDecidir(
            Cenario.Ator(Papel.Negocio), EtapaValidacao.Tecnica, outro, ativo,
            Cenario.Alvo("sistema"), Hoje);
        Verificar.Falso(negocio.Permitido, "negócio não decide etapa técnica");
        Verificar.Verdadeiro(negocio.Motivo.Contains("techlead"),
            $"o motivo deve nomear o papel exigido; veio: {negocio.Motivo}");

        var tech = PoliticaDeAutorizacao.PodeDecidir(
            Cenario.Ator(Papel.TechLead), EtapaValidacao.Tecnica, outro, ativo,
            Cenario.Alvo("sistema"), Hoje);
        Verificar.Verdadeiro(tech.Permitido);
    }

    [Teste("owner técnico decide a etapa técnica do próprio ativo")]
    public void OwnerDecideAEtapaDele()
    {
        var pessoa = Guid.NewGuid();
        var ativo = Cenario.AtivoVazio();
        ativo.AtribuirResponsavel(pessoa, PapelDeOwnership.OwnerTecnico, Cenario.Relogio);

        // Sem NENHUM papel na empresa — só o ownership do ativo.
        var ator = new Ator(pessoa, Cenario.EmpresaA, []);

        var decisao = PoliticaDeAutorizacao.PodeDecidir(
            ator, EtapaValidacao.Tecnica, quemSubmeteu: Guid.NewGuid(), ativo,
            Cenario.Alvo("sistema"), Hoje);

        Verificar.Verdadeiro(decisao.Permitido,
            "quem responde pelo ativo responde pela etapa dele");
    }

    [Teste("blocos_que_escreve reflete o papel")]
    public void BlocosQueEscreve()
    {
        Verificar.Igual(2, PoliticaDeAutorizacao.BlocosQueEscreve(
            Cenario.Ator(Papel.Curador), Hoje).Count);

        var negocio = PoliticaDeAutorizacao.BlocosQueEscreve(Cenario.Ator(Papel.Negocio), Hoje);
        Verificar.Igual(1, negocio.Count);
        Verificar.Contem(negocio, CatalogoDeTipos.BlocoEstruturaDdd);

        Verificar.Vazio(PoliticaDeAutorizacao.BlocosQueEscreve(
            Cenario.Ator(Papel.Consulta), Hoje), "consulta não escreve em bloco nenhum");
    }

    [Teste("ator sem papel nenhum não faz nada")]
    public void AtorSemPapelNaoFazNada()
    {
        var ator = Cenario.AtorSemPapel();
        foreach (var acao in Enum.GetValues<Acao>())
            Verificar.Falso(
                PoliticaDeAutorizacao.Pode(ator, acao, Cenario.Alvo("sistema"), Hoje),
                $"sem papel não pode {acao}");
    }
}
