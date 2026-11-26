using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CardPlatform.Ledger.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEntriesAccountTransactionIdIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_entries_account_id",
                table: "entries");

            migrationBuilder.CreateIndex(
                name: "ix_entries_account_id_transaction_id_id",
                table: "entries",
                columns: new[] { "account_id", "transaction_id", "id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_entries_account_id_transaction_id_id",
                table: "entries");

            migrationBuilder.CreateIndex(
                name: "ix_entries_account_id",
                table: "entries",
                column: "account_id");
        }
    }
}
