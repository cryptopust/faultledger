using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FaultLedger.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class DurableProviderInbox : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "provider_inbox",
            columns: table => new
            {
                inbox_id = table.Column<Guid>(type: "uuid", nullable: false),
                provider_event_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, collation: "C"),
                provider_operation_correlation = table.Column<Guid>(type: "uuid", nullable: false),
                event_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                provider_reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                evidence = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                normalized_payload = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                payload_version = table.Column<int>(type: "integer", nullable: false),
                processing_status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                attempt_count = table.Column<int>(type: "integer", nullable: false),
                last_error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                transfer_id = table.Column<Guid>(type: "uuid", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_provider_inbox", x => x.inbox_id);
                table.CheckConstraint("ck_provider_inbox_attempt_count", "attempt_count >= 0");
                table.CheckConstraint("ck_provider_inbox_event_type", "event_type IN ('Accepted', 'Completed', 'Rejected', 'Failed')");
                table.CheckConstraint("ck_provider_inbox_payload_version", "payload_version = 1");
                table.CheckConstraint("ck_provider_inbox_processing_status", "processing_status IN ('Pending', 'Processing', 'Processed', 'Failed')");
            });

        migrationBuilder.CreateIndex(
            name: "ix_provider_inbox_operation_correlation",
            table: "provider_inbox",
            column: "provider_operation_correlation");

        migrationBuilder.CreateIndex(
            name: "ix_provider_inbox_processing",
            table: "provider_inbox",
            columns: new[] { "processing_status", "received_at" });

        migrationBuilder.CreateIndex(
            name: "uq_provider_inbox_provider_event_id",
            table: "provider_inbox",
            column: "provider_event_id",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "provider_inbox");
    }
}
