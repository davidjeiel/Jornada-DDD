using Jornada.Adaptadores.Memoria;
using Jornada.Dominio.Tenancy;
using Jornada.Dominio.Testes.Harness;
using Jornada.Persistencia.Testes.Contrato;

namespace Jornada.Persistencia.Testes.Adaptadores;

/// <summary>
/// O adaptador em que os testes de caso de uso confiam. Se ele não cumprir o
/// contrato, aqueles testes provam menos do que parecem.
/// </summary>
[Suite("Contrato de persistência — memória")]
public sealed class ContratoEmMemoria : ContratoDePersistencia
{
    // Um armazém para a suíte inteira, como o singleton da API: as sessões de
    // empresas diferentes compartilham os dados, e só o filtro as separa.
    private readonly ArmazemEmMemoria _armazem = new();

    protected override Sessao Abrir(IdDeEmpresa empresa)
    {
        var repositorio = new RepositorioDeAtivosEmMemoria(_armazem, ContextoDeTenantFixo.Para(empresa));
        return new Sessao(repositorio, repositorio, repositorio, repositorio);
    }
}
