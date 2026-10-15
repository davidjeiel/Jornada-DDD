using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jornada.Persistencia.Postgres.Migracoes
{
    /// <inheritdoc />
    public partial class RegistrarUltimaRevisaoPublicada : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ultima_revisao_publicada",
                schema: "catalogo",
                table: "item_catalogo",
                type: "integer",
                nullable: true);

            // Preenche os ativos que JÁ existem: publicado, em revisão ou
            // descontinuado só se chega tendo sido publicado (arquivado e
            // em_validacao são ambíguos e ficam nulos). Sem isto, a primeira
            // revisão rejeitada de um ativo antigo o mandaria para rascunho.
            //
            // A tabela tem RLS FORÇADO, que vale até para o dono: sem empresa
            // no contexto, o UPDATE não enxergaria linha nenhuma e não faria
            // nada, em silêncio. O FORCE é suspenso só durante o preenchimento,
            // dentro da transação da migration, e religado em seguida.
            migrationBuilder.Sql("""
                ALTER TABLE catalogo.item_catalogo NO FORCE ROW LEVEL SECURITY;
                UPDATE catalogo.item_catalogo
                   SET ultima_revisao_publicada = revisao_atual
                 WHERE status_ciclo_vida IN ('publicado', 'em_revisao', 'descontinuado')
                   AND revisao_atual > 0;
                ALTER TABLE catalogo.item_catalogo FORCE ROW LEVEL SECURITY;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ultima_revisao_publicada",
                schema: "catalogo",
                table: "item_catalogo");
        }
    }
}
