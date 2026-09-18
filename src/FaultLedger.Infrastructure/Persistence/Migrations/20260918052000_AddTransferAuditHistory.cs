using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FaultLedger.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddTransferAuditHistory : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "transfer_audit_events",
            columns: table => new
            {
                event_id = table.Column<Guid>(type: "uuid", nullable: false),
                transfer_id = table.Column<Guid>(type: "uuid", nullable: false),
                previous_state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                new_state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                reason = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                source = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                provider_reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                transition_version = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_transfer_audit_events", x => x.event_id);
                table.CheckConstraint("ck_transfer_audit_new_state", "new_state IN ('Created', 'ReadyToSubmit', 'Submitting', 'Accepted', 'Failed', 'Unknown', 'Completed', 'ManualReview')");
                table.CheckConstraint("ck_transfer_audit_occurred_at", "isfinite(occurred_at)");
                table.CheckConstraint("ck_transfer_audit_previous_state", "previous_state IS NULL OR previous_state IN ('Created', 'ReadyToSubmit', 'Submitting', 'Accepted', 'Failed', 'Unknown', 'Completed', 'ManualReview')");
                table.CheckConstraint("ck_transfer_audit_reason", "length(reason) BETWEEN 1 AND 128");
                table.CheckConstraint("ck_transfer_audit_source", "length(source) BETWEEN 1 AND 64");
                table.CheckConstraint("ck_transfer_audit_transition_version", "transition_version >= 0");
                table.ForeignKey(
                    name: "fk_transfer_audit_events_transfers_transfer_id",
                    column: x => x.transfer_id,
                    principalTable: "transfers",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "ix_transfer_audit_transfer_time",
            table: "transfer_audit_events",
            columns: new[] { "transfer_id", "occurred_at", "event_id" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "transfer_audit_events");
    }
}
