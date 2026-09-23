using Jornada.Dominio.Catalogo;
using Jornada.Dominio.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jornada.Persistencia.Postgres.Configuracoes;

/// <summary>
/// Mapeamento do agregado <see cref="Ativo"/> para <c>catalogo.item_catalogo</c>.
///
/// Reconhece as decisões do ADR-0004 sobre o schema atual:
///  - toda unicidade é POR EMPRESA (hoje `codigo` é UNIQUE global, o que
///    quebraria assim que duas empresas usassem o mesmo código);
///  - todo índice composto começa por tenant_id, senão o predicado do RLS
///    degrada o plano em silêncio;
///  - `atributos` vira JSONB com índice GIN, não TEXT com JSON dentro;
///  - datas viram timestamptz, não TEXT.
/// </summary>
public sealed class AtivoConfiguracao : IEntityTypeConfiguration<Ativo>
{
    public void Configure(EntityTypeBuilder<Ativo> e)
    {
        e.ToTable("item_catalogo", "catalogo");
        e.HasKey(a => a.Id);

        e.Property(a => a.Id)
            .HasColumnName("codigo_publico")
            .HasConversion(v => v.Valor, v => new IdDeAtivo(v));

        e.Property(a => a.Empresa)
            .HasColumnName("tenant_id")
            .HasConversion(v => v.Valor, v => new IdDeEmpresa(v))
            .IsRequired();

        e.Property(a => a.Unidade)
            .HasColumnName("unidade_id")
            .HasConversion(
                v => v!.Value.Valor,
                v => new IdDeUnidade(v));

        e.Property(a => a.Codigo)
            .HasColumnName("codigo")
            .HasConversion(v => v.Valor, v => CodigoDeAtivo.De(v))
            .HasMaxLength(24)
            .IsRequired();

        e.Property(a => a.TipoItem).HasColumnName("tipo_item").HasMaxLength(60).IsRequired();
        e.Property(a => a.Nome).HasColumnName("nome").HasMaxLength(200).IsRequired();
        e.Property(a => a.Descricao).HasColumnName("descricao").HasDefaultValue("");

        e.Property(a => a.IdPai)
            .HasColumnName("id_pai")
            .HasConversion(v => v!.Value.Valor, v => new IdDeAtivo(v));

        e.Property(a => a.Criticidade)
            .HasColumnName("criticidade")
            .HasConversion(v => v.Chave(), v => CriticidadeExtensoes.De(v))
            .HasMaxLength(10);

        e.Property(a => a.Status)
            .HasColumnName("status_ciclo_vida")
            .HasConversion(
                v => MaquinaDeCicloDeVida.Rotulo(v),
                v => Enum.Parse<StatusCicloVida>(v.Replace("_", ""), ignoreCase: true))
            .HasMaxLength(20);

        e.Property(a => a.Origem)
            .HasColumnName("origem")
            .HasConversion<string>()
            .HasMaxLength(12);

        e.Property(a => a.RevisaoAtual).HasColumnName("revisao_atual");
        e.Property(a => a.FimVigencia).HasColumnName("fim_vigencia");
        e.Property(a => a.CriadoEm).HasColumnName("criado_em");
        e.Property(a => a.AtualizadoEm).HasColumnName("atualizado_em");

        // `atributos` como JSONB: deixa de ser blob opaco e passa a ser
        // consultável e facetável (ADR-0004, ADR-0005).
        e.Property<Dictionary<string, string>>("_atributos")
            .HasColumnName("atributos")
            .HasColumnType("jsonb")
            .HasField("_atributos")
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        e.OwnsMany<Responsabilidade>("_responsaveis", r =>
        {
            r.ToTable("responsabilidade", "catalogo");
            r.WithOwner().HasForeignKey("id_item");
            r.Property(x => x.IdPessoa).HasColumnName("id_pessoa");
            r.Property(x => x.Papel).HasColumnName("papel").HasConversion<string>();
            r.Property(x => x.InicioVigencia).HasColumnName("inicio_vigencia");
            r.Property(x => x.FimVigencia).HasColumnName("fim_vigencia");
        });

        e.OwnsMany<Evidencia>("_evidencias", ev =>
        {
            ev.ToTable("evidencia", "catalogo");
            ev.WithOwner().HasForeignKey("id_item");
            // Sem ValueGeneratedNever, o EF assume que Guid = chave gerada pelo
            // BANCO (convenção padrão para Guid), ignora o valor que
            // Ativo.AnexarEvidencia já gerou (Guid.CreateVersion7()) e espera
            // ler de volta um valor via RETURNING — que nunca vem, porque a
            // coluna não tem generator nenhum. Resultado: INSERT "afeta 0
            // linhas" (DbUpdateConcurrencyException), embora a linha exista.
            ev.Property(x => x.Id).ValueGeneratedNever();
            ev.Property(x => x.Tipo).HasColumnName("tipo").HasMaxLength(30);
            ev.Property(x => x.Titulo).HasColumnName("titulo").HasMaxLength(200);
            ev.Property(x => x.Url).HasColumnName("url");
        });

        e.Ignore(a => a.EventosPendentes);

        // Sem isto o EF tenta descobrir ESTAS propriedades computadas como
        // navegação por convenção (são IReadOnlyList<TEntidade>) e entra em
        // conflito com o OwnsMany acima, que já mapeia os CAMPOS de apoio
        // (_responsaveis, _evidencias) diretamente.
        e.Ignore(a => a.Responsaveis);
        e.Ignore(a => a.Evidencias);

        // ── Índices: tenant_id SEMPRE à esquerda (ADR-0003) ──────────────────
        e.HasIndex(a => new { a.Empresa, a.Codigo }).IsUnique().HasDatabaseName("ux_item_codigo");
        e.HasIndex(a => new { a.Empresa, a.TipoItem }).HasDatabaseName("ix_item_tenant_tipo");
        e.HasIndex(a => new { a.Empresa, a.Status }).HasDatabaseName("ix_item_tenant_status");
        e.HasIndex(a => new { a.Empresa, a.IdPai }).HasDatabaseName("ix_item_tenant_pai");
    }
}
