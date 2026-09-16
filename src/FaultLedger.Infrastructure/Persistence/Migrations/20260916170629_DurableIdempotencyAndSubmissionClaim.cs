using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FaultLedger.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class DurableIdempotencyAndSubmissionClaim : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.RenameIndex(
            name: "ux_transfers_idempotency_key",
            table: "transfers",
            newName: "uq_transfers_idempotency_key");

        migrationBuilder.AddColumn<int>(
            name: "fingerprint_version",
            table: "transfers",
            type: "integer",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "request_fingerprint",
            table: "transfers",
            type: "character varying(64)",
            maxLength: 64,
            nullable: true,
            collation: "C");

        migrationBuilder.AddCheckConstraint(
            name: "ck_transfers_request_fingerprint",
            table: "transfers",
            sql: "(request_fingerprint IS NULL AND fingerprint_version IS NULL) OR (request_fingerprint COLLATE \"C\" ~ '^[0-9a-f]{64}$' AND fingerprint_version = 1)");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "ck_transfers_request_fingerprint",
            table: "transfers");

        migrationBuilder.DropColumn(
            name: "fingerprint_version",
            table: "transfers");

        migrationBuilder.DropColumn(
            name: "request_fingerprint",
            table: "transfers");

        migrationBuilder.RenameIndex(
            name: "uq_transfers_idempotency_key",
            table: "transfers",
            newName: "ux_transfers_idempotency_key");
    }
}
