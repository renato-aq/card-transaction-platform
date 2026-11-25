using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CardPlatform.Ledger.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddHoldsAccountPerCurrencyIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ux_accounts_holds_currency",
                table: "accounts",
                column: "currency",
                unique: true,
                filter: "type = 'Holds'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_accounts_holds_currency",
                table: "accounts");
        }
    }
}
