using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace FaultLedger.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class InitialTransferPersistence : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "transfers",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                client_reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                idempotency_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, collation: "C"),
                amount = table.Column<decimal>(type: "numeric(28,8)", precision: 28, scale: 8, nullable: false),
                currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                provider_reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                version = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_transfers", x => x.id);
                table.CheckConstraint("ck_transfers_amount", "amount > 0 AND amount <= 99999999999999999999.99999999");
                table.CheckConstraint("ck_transfers_client_reference", "client_reference COLLATE \"C\" ~ '^[A-Za-z0-9_.:-]{1,100}$'");
                table.CheckConstraint("ck_transfers_currency", "currency COLLATE \"C\" ~ '^[A-Z]{3}$'");
                table.CheckConstraint("ck_transfers_id", "id <> '00000000-0000-0000-0000-000000000000'::uuid");
                table.CheckConstraint("ck_transfers_idempotency_key", "idempotency_key COLLATE \"C\" ~ '^[A-Za-z0-9_.:-]{1,128}$'");
                table.CheckConstraint("ck_transfers_provider_reference", "(state IN ('Accepted', 'Completed') AND provider_reference IS NOT NULL AND provider_reference COLLATE \"C\" ~ '^[A-Za-z0-9_.:-]{1,100}$') OR (state NOT IN ('Accepted', 'Completed') AND provider_reference IS NULL)");
                table.CheckConstraint("ck_transfers_state_version", "(state = 'Created' AND version = 0) OR (state = 'ReadyToSubmit' AND version = 1) OR (state = 'Submitting' AND version = 2) OR (state IN ('Accepted', 'Failed', 'Unknown') AND version = 3) OR (state IN ('Completed', 'ManualReview') AND version = 4)");
                table.CheckConstraint("ck_transfers_timestamps", "updated_at >= created_at AND isfinite(created_at) AND isfinite(updated_at)");
            });

        migrationBuilder.CreateIndex(
            name: "ux_transfers_idempotency_key",
            table: "transfers",
            column: "idempotency_key",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "transfers");
    }
}
