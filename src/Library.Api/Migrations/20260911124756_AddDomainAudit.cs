using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Library.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddDomainAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // `uuid` não converte para `bigint` (nem via `USING ... ::bigint` — não existe esse cast em
            // Postgres), então a PK é descartada e recriada em vez de alterada no lugar. Linhas existentes
            // ganham um novo `Id` sequencial atribuído pela identity; o valor antigo (Guid) não é preservado
            // em nenhuma coluna, conforme aceito em design.md (tabela sem dados de produção reais).
            migrationBuilder.DropPrimaryKey(
                name: "PK_audit_events",
                table: "audit_events");

            migrationBuilder.DropColumn(
                name: "Id",
                table: "audit_events");

            migrationBuilder.AddColumn<long>(
                    name: "Id",
                    table: "audit_events",
                    type: "bigint",
                    nullable: false)
                .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            migrationBuilder.AddPrimaryKey(
                name: "PK_audit_events",
                table: "audit_events",
                column: "Id");

            migrationBuilder.CreateIndex(
                name: "IX_audit_events_correlation_id",
                table: "audit_events",
                column: "correlation_id");

            migrationBuilder.CreateIndex(
                name: "IX_audit_events_entity_type_entity_id_occurred_at_utc",
                table: "audit_events",
                columns: new[] { "entity_type", "entity_id", "occurred_at_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_audit_events_correlation_id",
                table: "audit_events");

            migrationBuilder.DropIndex(
                name: "IX_audit_events_entity_type_entity_id_occurred_at_utc",
                table: "audit_events");

            migrationBuilder.DropPrimaryKey(
                name: "PK_audit_events",
                table: "audit_events");

            migrationBuilder.DropColumn(
                name: "Id",
                table: "audit_events");

            migrationBuilder.AddColumn<Guid>(
                name: "Id",
                table: "audit_events",
                type: "uuid",
                nullable: false,
                defaultValueSql: "gen_random_uuid()");

            migrationBuilder.AddPrimaryKey(
                name: "PK_audit_events",
                table: "audit_events",
                column: "Id");
        }
    }
}
