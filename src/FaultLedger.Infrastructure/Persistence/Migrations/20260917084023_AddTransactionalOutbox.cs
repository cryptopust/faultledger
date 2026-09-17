using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FaultLedger.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddTransactionalOutbox : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "outbox_messages",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                aggregate_id = table.Column<Guid>(type: "uuid", nullable: false),
                aggregate_version = table.Column<long>(type: "bigint", nullable: false),
                event_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                schema_version = table.Column<int>(type: "integer", nullable: false),
                payload = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                attempt_count = table.Column<int>(type: "integer", nullable: false),
                next_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                claim_id = table.Column<Guid>(type: "uuid", nullable: true),
                claimed_by = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                claimed_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                last_error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_outbox_messages", x => x.id);
                table.CheckConstraint("ck_outbox_attempt_count", "attempt_count >= 0");
                table.CheckConstraint("ck_outbox_event_type", "event_type = 'TransferCompleted'");
                table.CheckConstraint("ck_outbox_lease_consistency", "(claimed_until IS NULL AND claimed_by IS NULL AND claim_id IS NULL) OR (claimed_until IS NOT NULL AND claimed_by IS NOT NULL AND claim_id IS NOT NULL)");
                table.CheckConstraint("ck_outbox_schema_version", "schema_version = 1");
            });

        migrationBuilder.CreateIndex(
            name: "ix_outbox_due",
            table: "outbox_messages",
            columns: new[] { "published_at", "next_attempt_at", "claimed_until", "created_at" });

        migrationBuilder.CreateIndex(
            name: "uq_outbox_aggregate_event",
            table: "outbox_messages",
            columns: new[] { "aggregate_id", "event_type" },
            unique: true);

        migrationBuilder.CreateTable(
            name: "simulated_consumer_events",
            columns: table => new
            {
                event_id = table.Column<Guid>(type: "uuid", nullable: false),
                event_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                schema_version = table.Column<int>(type: "integer", nullable: false),
                aggregate_id = table.Column<Guid>(type: "uuid", nullable: false),
                aggregate_version = table.Column<long>(type: "bigint", nullable: false),
                payload = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                first_received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                last_received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                receipt_count = table.Column<int>(type: "integer", nullable: false),
                logical_effect_count = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_simulated_consumer_events", x => x.event_id);
                table.CheckConstraint("ck_simulated_consumer_counts",
                    "receipt_count >= 1 AND logical_effect_count = 1");
                table.CheckConstraint("ck_simulated_consumer_schema_version", "schema_version = 1");
            });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "simulated_consumer_events");

        migrationBuilder.DropTable(
            name: "outbox_messages");
    }
}
