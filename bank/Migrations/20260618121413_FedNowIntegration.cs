using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace bank.Migrations
{
    /// <inheritdoc />
    public partial class FedNowIntegration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EndToEndId",
                table: "Transactions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalStatus",
                table: "Transactions",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EndToEndId",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "ExternalStatus",
                table: "Transactions");
        }
    }
}
