using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PersonalPlatform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddThemePreference : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "theme",
                schema: "app",
                table: "user_profiles",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddCheckConstraint(
                name: "ck_user_profiles_theme",
                schema: "app",
                table: "user_profiles",
                sql: "theme BETWEEN 0 AND 2");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_user_profiles_theme",
                schema: "app",
                table: "user_profiles");

            migrationBuilder.DropColumn(
                name: "theme",
                schema: "app",
                table: "user_profiles");
        }
    }
}
