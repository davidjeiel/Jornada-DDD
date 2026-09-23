using Jornada.Aplicacao.Portas;
using Jornada.Dominio.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Jornada.Persistencia.Postgres;

/// <summary>
/// Só existe para o `dotnet ef migrations add` conseguir instanciar o
/// <see cref="ContextoDeDados"/> sem o container de DI da API — a ferramenta
/// roda fora do <c>Program.cs</c> e não sabe resolver <see cref="IContextoDeTenantAtual"/>.
///
/// A connection string aqui NUNCA é usada de verdade: gerar/aplicar migration
/// não faz nenhuma consulta filtrada por tenant, só DDL. Aponta para
/// catalogo_migrador (dono do schema, ADR-0003) por documentação — é ele quem
/// roda migration em qualquer ambiente real.
/// </summary>
public sealed class ContextoDeDadosFabricaDeDesign : IDesignTimeDbContextFactory<ContextoDeDados>
{
    public ContextoDeDados CreateDbContext(string[] args)
    {
        var opcoes = new DbContextOptionsBuilder<ContextoDeDados>()
            .UseNpgsql("Host=localhost;Port=5432;Database=catalogo;Username=catalogo_migrador;Password=dev_migrador")
            .Options;

        return new ContextoDeDados(opcoes, new ContextoDeTenantDeDesign());
    }

    private sealed class ContextoDeTenantDeDesign : IContextoDeTenantAtual
    {
        public ContextoDeTenant Atual { get; } = new(IdDeEmpresa.Novo());
    }
}
