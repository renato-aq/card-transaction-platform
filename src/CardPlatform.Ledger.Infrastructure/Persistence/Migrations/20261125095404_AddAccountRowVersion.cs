using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CardPlatform.Ledger.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountRowVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // xmin is a PostgreSQL system column: Npgsql skips system columns when generating SQL, so this migration
            // only records the row version in the model snapshot and changes nothing in the database.
            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "accounts",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "xmin",
                table: "accounts");
        }
    }
}
