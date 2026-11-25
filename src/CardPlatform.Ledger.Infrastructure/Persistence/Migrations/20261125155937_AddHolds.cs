using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CardPlatform.Ledger.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddHolds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "holds",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    card_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    placement_transaction_id = table.Column<Guid>(type: "uuid", nullable: false),
                    completion_transaction_id = table.Column<Guid>(type: "uuid", nullable: true),
                    // xmin is a PostgreSQL system column: Npgsql skips it when generating the CREATE TABLE statement;
                    // it is only recorded in the model as the row version (ADR 0006).
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    currency = table.Column<string>(type: "char(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_holds", x => x.id);
                    table.ForeignKey(
                        name: "fk_holds_accounts_card_account_id",
                        column: x => x.card_account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_holds_transactions_completion_transaction_id",
                        column: x => x.completion_transaction_id,
                        principalTable: "transactions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_holds_transactions_placement_transaction_id",
                        column: x => x.placement_transaction_id,
                        principalTable: "transactions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_holds_card_account_id",
                table: "holds",
                column: "card_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_holds_completion_transaction_id",
                table: "holds",
                column: "completion_transaction_id");

            migrationBuilder.CreateIndex(
                name: "ix_holds_placement_transaction_id",
                table: "holds",
                column: "placement_transaction_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "holds");
        }
    }
}
