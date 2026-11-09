using Jornada.Adaptadores.Memoria;
using Jornada.Aplicacao.Catalogo.Comandos;
using Jornada.Dominio.Catalogo;
using Jornada.Dominio.Comum;
using Jornada.Dominio.Testes.Harness;

namespace Jornada.Dominio.Testes.Casos;

/// <summary>
/// Quem responde pelo ativo (<c>servicos.definir_responsavel</c>): um papel tem
/// uma pessoa só, e trocar encerra quem estava, guardando a história.
/// </summary>
[Suite("Responsáveis: trocar substitui, encerrar deixa o papel vago")]
public sealed class ResponsaveisTestes
{
    private static readonly RelogioFixo Amanha = new(Cenario.Relogio.Agora.AddDays(1));

    [Teste("definir outra pessoa no papel encerra quem estava, e a história fica")]
    public void TrocarSubstituiOAnterior()
    {
        var ativo = Cenario.AtivoCompleto();
        var antes = ativo.Responsaveis.Single().IdPessoa;
        var nova = Guid.NewGuid();
        ativo.AtribuirResponsavel(nova, PapelDeOwnership.OwnerTecnico, Amanha);

        var hoje = Amanha.Hoje;
        Verificar.Igual(nova, ativo.Responsaveis.Single(r => r.Vigente(hoje)).IdPessoa, "um papel, uma pessoa");
        var encerrada = ativo.Responsaveis.Single(r => r.IdPessoa == antes);
        Verificar.Igual<DateOnly?>(hoje, encerrada.FimVigencia);
        Verificar.Falso(encerrada.Vigente(hoje), "encerrada hoje já não vale hoje");
    }

    [Teste("papéis diferentes convivem, e definir de novo quem já ocupa não muda nada")]
    public void PapeisDiferentesConvivem()
    {
        var ativo = Cenario.AtivoCompleto();
        var dono = ativo.Responsaveis.Single().IdPessoa;
        ativo.AtribuirResponsavel(Guid.NewGuid(), PapelDeOwnership.Mantenedor, Cenario.Relogio);
        ativo.AtribuirResponsavel(dono, PapelDeOwnership.OwnerTecnico, Amanha);
        Verificar.Igual(2, ativo.Responsaveis.Count(r => r.Vigente(Amanha.Hoje)));
        Verificar.Igual(2, ativo.Responsaveis.Count, "não abriu um registro novo para quem já estava");
    }

    [Teste("encerrar deixa o papel vago; o pré-check volta a pedir o owner")]
    public void EncerrarDeixaOPapelVago()
    {
        var ativo = Cenario.AtivoCompleto();
        var dono = ativo.Responsaveis.Single().IdPessoa;
        ativo.EncerrarResponsavel(dono, PapelDeOwnership.OwnerTecnico, Amanha);
        Verificar.Vazio(ativo.Responsaveis.Where(r => r.Vigente(Amanha.Hoje)));
        Verificar.Falso(ativo.PapeisVigentes(Amanha.Hoje).Contains(PapelDeOwnership.OwnerTecnico));
        Verificar.Lanca<ErroDeDominio>(() => ativo.EncerrarResponsavel(dono, PapelDeOwnership.OwnerTecnico, Amanha), "já encerrada");
    }

    [Teste("trocar no mesmo dia em que definiu também fica na história")]
    public void TrocaNoMesmoDiaFicaNaHistoria()
    {
        var ativo = Cenario.AtivoCompleto();   // owner definido hoje
        var nova = Guid.NewGuid();
        ativo.AtribuirResponsavel(nova, PapelDeOwnership.OwnerTecnico, Cenario.Relogio);
        Verificar.Igual(2, ativo.Responsaveis.Count);
        Verificar.Igual(nova, ativo.Responsaveis.Single(r => r.Vigente(Cenario.Relogio.Hoje)).IdPessoa);
    }

    [Teste("publicado não troca responsável sem abrir revisão")]
    public void PublicadoNaoTrocaResponsavel()
    {
        var ativo = Cenario.AtivoPublicado();
        Verificar.Lanca<ErroDeDominio>(() => ativo.AtribuirResponsavel(Guid.NewGuid(), PapelDeOwnership.OwnerTecnico, Amanha));
        Verificar.Lanca<ErroDeDominio>(() => ativo.EncerrarResponsavel(ativo.Responsaveis.Single().IdPessoa, PapelDeOwnership.OwnerTecnico, Amanha));
    }

    [Teste("o caso de uso de encerrar respeita quem edita e o estado do ativo")]
    public void CasoDeUsoDeEncerrar()
    {
        var m = new MundoDeGovernanca();
        var id = m.SistemaCompleto();
        var dono = m.Ativo(id).Responsaveis.Single().IdPessoa;
        EncerrarResponsavel Caso(Jornada.Dominio.Acesso.Ator quem) =>
            new(m.Repo, m.Metamodelo, m.Tenant, new AtorFixo(quem), m.Repo, m.Repo, Amanha);

        Verificar.Igual("SEM_PERMISSAO", Caso(MundoDeGovernanca.Negocio).ExecutarAsync(new(id, dono, PapelDeOwnership.OwnerTecnico)).Result.Codigo);
        Verificar.Igual("RESPONSAVEL_NAO_ENCONTRADO", Caso(MundoDeGovernanca.Curador).ExecutarAsync(new(id, Guid.NewGuid(), PapelDeOwnership.OwnerTecnico)).Result.Codigo);
        Verificar.Verdadeiro(Caso(MundoDeGovernanca.Curador).ExecutarAsync(new(id, dono, PapelDeOwnership.OwnerTecnico)).Result.Sucesso);
        Verificar.Vazio(m.Ativo(id).Responsaveis.Where(r => r.Vigente(Amanha.Hoje)));
    }
}
