using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FaultLedger.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class UnknownOutcomeReconciliation : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "ck_transfers_state_version",
            table: "transfers");

        migrationBuilder.AddCheckConstraint(
            name: "ck_transfers_state_version",
            table: "transfers",
            sql: "(state = 'Created' AND version = 0) OR (state = 'ReadyToSubmit' AND version = 1) OR (state = 'Submitting' AND version = 2) OR (state IN ('Accepted', 'Failed') AND version IN (3, 4)) OR (state = 'Unknown' AND version = 3) OR (state = 'Completed' AND version IN (4, 5)) OR (state = 'ManualReview' AND version = 4)");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "ck_transfers_state_version",
            table: "transfers");

        migrationBuilder.AddCheckConstraint(
            name: "ck_transfers_state_version",
            table: "transfers",
            sql: "(state = 'Created' AND version = 0) OR (state = 'ReadyToSubmit' AND version = 1) OR (state = 'Submitting' AND version = 2) OR (state IN ('Accepted', 'Failed', 'Unknown') AND version = 3) OR (state IN ('Completed', 'ManualReview') AND version = 4)");
    }
}
