using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Jornada.Persistencia.Postgres.Migracoes
{
    /// <inheritdoc />
    public partial class Inicial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "catalogo");

            migrationBuilder.EnsureSchema(
                name: "governanca");

            migrationBuilder.CreateTable(
                name: "item_catalogo",
                schema: "catalogo",
                columns: table => new
                {
                    codigo_publico = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    unidade_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tipo_item = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    codigo = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    nome = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    descricao = table.Column<string>(type: "text", nullable: false, defaultValue: ""),
                    id_pai = table.Column<Guid>(type: "uuid", nullable: true),
                    IdSquad = table.Column<Guid>(type: "uuid", nullable: true),
                    criticidade = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    status_ciclo_vida = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    origem = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    revisao_atual = table.Column<int>(type: "integer", nullable: false),
                    fim_vigencia = table.Column<DateOnly>(type: "date", nullable: true),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    atributos = table.Column<Dictionary<string, string>>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_item_catalogo", x => x.codigo_publico);
                });

            migrationBuilder.CreateTable(
                name: "relacao_ativo",
                schema: "catalogo",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    origem_id = table.Column<Guid>(type: "uuid", nullable: false),
                    destino_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_relacao_ativo", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "sequencial_por_tipo",
                schema: "catalogo",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo_item = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    valor = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sequencial_por_tipo", x => new { x.tenant_id, x.tipo_item });
                });

            migrationBuilder.CreateTable(
                name: "validacao",
                schema: "governanca",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    id_item = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revisao = table.Column<int>(type: "integer", nullable: false),
                    etapa = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    id_submissor = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    id_decisor = table.Column<Guid>(type: "uuid", nullable: true),
                    motivo = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_validacao", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "evidencia",
                schema: "catalogo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    id_item = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    titulo = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    url = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_evidencia", x => new { x.id_item, x.Id });
                    table.ForeignKey(
                        name: "FK_evidencia_item_catalogo_id_item",
                        column: x => x.id_item,
                        principalSchema: "catalogo",
                        principalTable: "item_catalogo",
                        principalColumn: "codigo_publico",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "responsabilidade",
                schema: "catalogo",
                columns: table => new
                {
                    id_item = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    id_pessoa = table.Column<Guid>(type: "uuid", nullable: false),
                    papel = table.Column<string>(type: "text", nullable: false),
                    inicio_vigencia = table.Column<DateOnly>(type: "date", nullable: false),
                    fim_vigencia = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_responsabilidade", x => new { x.id_item, x.Id });
                    table.ForeignKey(
                        name: "FK_responsabilidade_item_catalogo_id_item",
                        column: x => x.id_item,
                        principalSchema: "catalogo",
                        principalTable: "item_catalogo",
                        principalColumn: "codigo_publico",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_item_tenant_pai",
                schema: "catalogo",
                table: "item_catalogo",
                columns: new[] { "tenant_id", "id_pai" });

            migrationBuilder.CreateIndex(
                name: "ix_item_tenant_status",
                schema: "catalogo",
                table: "item_catalogo",
                columns: new[] { "tenant_id", "status_ciclo_vida" });

            migrationBuilder.CreateIndex(
                name: "ix_item_tenant_tipo",
                schema: "catalogo",
                table: "item_catalogo",
                columns: new[] { "tenant_id", "tipo_item" });

            migrationBuilder.CreateIndex(
                name: "ux_item_codigo",
                schema: "catalogo",
                table: "item_catalogo",
                columns: new[] { "tenant_id", "codigo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_relacao_tenant_destino",
                schema: "catalogo",
                table: "relacao_ativo",
                columns: new[] { "tenant_id", "destino_id" });

            migrationBuilder.CreateIndex(
                name: "ix_relacao_tenant_origem",
                schema: "catalogo",
                table: "relacao_ativo",
                columns: new[] { "tenant_id", "origem_id" });

            migrationBuilder.CreateIndex(
                name: "ix_validacao_tenant_ativo",
                schema: "governanca",
                table: "validacao",
                columns: new[] { "tenant_id", "id_item" });

            migrationBuilder.CreateIndex(
                name: "ix_validacao_tenant_status",
                schema: "governanca",
                table: "validacao",
                columns: new[] { "tenant_id", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "evidencia",
                schema: "catalogo");

            migrationBuilder.DropTable(
                name: "relacao_ativo",
                schema: "catalogo");

            migrationBuilder.DropTable(
                name: "responsabilidade",
                schema: "catalogo");

            migrationBuilder.DropTable(
                name: "sequencial_por_tipo",
                schema: "catalogo");

            migrationBuilder.DropTable(
                name: "validacao",
                schema: "governanca");

            migrationBuilder.DropTable(
                name: "item_catalogo",
                schema: "catalogo");
        }
    }
}
