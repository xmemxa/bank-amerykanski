using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace bank.Migrations
{
    /// <inheritdoc />
    public partial class AddJuniorAccountFeatures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TransferRequestJson",
                table: "Transactions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DailyLimit",
                table: "Cards",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PerTransactionLimit",
                table: "Cards",
                type: "numeric",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TransferRequestJson",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "DailyLimit",
                table: "Cards");

            migrationBuilder.DropColumn(
                name: "PerTransactionLimit",
                table: "Cards");
        }
    }
}
