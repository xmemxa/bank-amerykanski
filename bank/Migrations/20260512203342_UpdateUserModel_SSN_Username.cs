using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace bank.Migrations
{
    /// <inheritdoc />
    public partial class UpdateUserModel_SSN_Username : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Email",
                table: "Users",
                newName: "Username");

            migrationBuilder.AddColumn<string>(
                name: "Address",
                table: "Users",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PhoneNumber",
                table: "Users",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SocialSecurityNumber",
                table: "Users",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_Users_SocialSecurityNumber",
                table: "Users",
                column: "SocialSecurityNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_Username",
                table: "Users",
                column: "Username",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Users_SocialSecurityNumber",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Users_Username",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "Address",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "PhoneNumber",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "SocialSecurityNumber",
                table: "Users");

            migrationBuilder.RenameColumn(
                name: "Username",
                table: "Users",
                newName: "Email");
        }
    }
}
