using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CardPlatform.Authorization.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ChangeOutboxUnpublishedIndexToId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_outbox_messages_unpublished",
                table: "outbox_messages");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_unpublished",
                table: "outbox_messages",
                column: "id",
                filter: "published_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_outbox_messages_unpublished",
                table: "outbox_messages");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_unpublished",
                table: "outbox_messages",
                columns: new[] { "created_at", "id" },
                filter: "published_at IS NULL");
        }
    }
}
