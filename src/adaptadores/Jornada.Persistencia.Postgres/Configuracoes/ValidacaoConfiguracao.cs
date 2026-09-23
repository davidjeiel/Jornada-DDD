using Jornada.Aplicacao.Portas;
using Jornada.Dominio.Catalogo;
using Jornada.Dominio.Governanca;
using Jornada.Dominio.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jornada.Persistencia.Postgres.Configuracoes;

/// <summary>
/// Mapeamento de <see cref="ValidacaoEmProcesso"/> para <c>governanca.validacao</c>.
///
/// O EF materializa esta classe pelo ÚNICO construtor público que ela tem — os
/// nomes dos parâmetros (<c>id</c>, <c>ativo</c>, <c>empresa</c>...) batem com
/// os das propriedades, então não precisa de construtor vazio nem de
/// configuração extra de binding. <c>Status</c>, <c>IdDecisor</c> e
/// <c>Motivo</c> são preenchidos depois, por reflexão sobre os setters
/// privados — o mesmo mecanismo que já vale para os agregados do domínio.
/// </summary>
public sealed class ValidacaoConfiguracao : IEntityTypeConfiguration<ValidacaoEmProcesso>
{
    public void Configure(EntityTypeBuilder<ValidacaoEmProcesso> e)
    {
        e.ToTable("validacao", "governanca");
        e.HasKey(v => v.Id);

        e.Property(v => v.Id).HasColumnName("id");

        e.Property(v => v.Empresa)
            .HasColumnName("tenant_id")
            .HasConversion(v => v.Valor, v => new IdDeEmpresa(v))
            .IsRequired();

        e.Property(v => v.Ativo)
            .HasColumnName("id_item")
            .HasConversion(v => v.Valor, v => new IdDeAtivo(v))
            .IsRequired();

        e.Property(v => v.Revisao).HasColumnName("revisao");

        e.Property(v => v.Etapa)
            .HasColumnName("etapa")
            .HasConversion<string>()
            .HasMaxLength(20);

        e.Property(v => v.IdSubmissor).HasColumnName("id_submissor");

        e.Property(v => v.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(20);

        e.Property(v => v.IdDecisor).HasColumnName("id_decisor");
        e.Property(v => v.Motivo).HasColumnName("motivo");

        // tenant_id sempre à esquerda (ADR-0003): senão o predicado do RLS
        // degrada o plano em silêncio.
        e.HasIndex(v => new { v.Empresa, v.Ativo }).HasDatabaseName("ix_validacao_tenant_ativo");
        e.HasIndex(v => new { v.Empresa, v.Status }).HasDatabaseName("ix_validacao_tenant_status");
    }
}
