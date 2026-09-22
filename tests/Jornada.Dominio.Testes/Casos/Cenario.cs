using Jornada.Dominio.Acesso;
using Jornada.Dominio.Catalogo;
using Jornada.Dominio.Comum;
using Jornada.Dominio.Governanca;
using Jornada.Dominio.Tenancy;

namespace Jornada.Dominio.Testes.Casos;

/// <summary>
/// Construtores de cenário. Equivale a <c>tests/apoio.py</c> do painel-ddd —
/// mas sem banco, sem app e sem tmp_path.
/// </summary>
public static class Cenario
{
    /// <summary>Tempo é parâmetro, não acidente: toda data do teste sai daqui.</summary>
    public static readonly RelogioFixo Relogio = RelogioFixo.Em(2026, 9, 22);

    public static readonly IdDeEmpresa EmpresaA = IdDeEmpresa.De("11111111-1111-1111-1111-111111111111");
    public static readonly IdDeEmpresa EmpresaB = IdDeEmpresa.De("22222222-2222-2222-2222-222222222222");

    public static readonly CatalogoDeTipos Tipos = CatalogoDeTipos.Padrao();
    public static readonly PoliticasDeGovernanca Politicas = PoliticasDeGovernanca.Padrao();

    public static TipoDeAtivo Tipo(string chave) => Tipos.Obter(chave);

    public static PoliticaDeGovernanca Politica(string tipo, Criticidade c = Criticidade.Media) =>
        Politicas.Para(tipo, c);

    /// <summary>Sistema em rascunho, sem nada preenchido. O pior caso.</summary>
    public static Ativo AtivoVazio(string tipo = "sistema", IdDeEmpresa? empresa = null) =>
        Ativo.Rascunhar(empresa ?? EmpresaA, Tipo(tipo),
            CodigoDeAtivo.De("SIS-0001"), "Sistema X", Relogio);

    /// <summary>Sistema pronto para publicar: descrição, campo obrigatório e owner técnico.</summary>
    public static Ativo AtivoCompleto(IdDeEmpresa? empresa = null)
    {
        var ativo = Ativo.Rascunhar(
            empresa ?? EmpresaA, Tipo("sistema"), CodigoDeAtivo.De("SIS-0001"),
            "Core Bancário", Relogio,
            descricao: "Sistema central de contas e lançamentos.",
            atributos: new Dictionary<string, string> { ["plataforma"] = "mainframe" });

        ativo.AtribuirResponsavel(Guid.NewGuid(), PapelDeOwnership.OwnerTecnico, Relogio);
        ativo.DrenarEventos();
        return ativo;
    }

    public static Ativo AtivoPublicado()
    {
        var ativo = AtivoCompleto();
        ativo.Submeter("v1", Guid.NewGuid(), Relogio);
        ativo.Publicar(Relogio);
        ativo.DrenarEventos();
        return ativo;
    }

    /// <summary>Ator com um papel válido em toda a empresa.</summary>
    public static Ator Ator(Papel papel, IdDeEmpresa? empresa = null, Guid? id = null) =>
        new(id ?? Guid.NewGuid(), empresa ?? EmpresaA,
            [AtribuicaoDePapel.NaEmpresa(papel, Relogio.Hoje.AddDays(-30))]);

    public static Ator AtorSemPapel(IdDeEmpresa? empresa = null) =>
        new(Guid.NewGuid(), empresa ?? EmpresaA, []);

    public static AlvoDaAcao Alvo(string tipo, IdDeEmpresa? empresa = null)
    {
        var t = Tipo(tipo);
        return new AlvoDaAcao(empresa ?? EmpresaA, t.Chave, t.Bloco);
    }
}
