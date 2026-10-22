using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PersonalPlatform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddQuitHabits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "quit_habits",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    color = table.Column<string>(type: "character(7)", fixedLength: true, maxLength: 7, nullable: false),
                    quit_date = table.Column<DateOnly>(type: "date", nullable: false),
                    archived_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_quit_habits", x => x.id);
                    table.UniqueConstraint("ak_quit_habits_id_owner_id", x => new { x.id, x.owner_id });
                    table.ForeignKey(
                        name: "fk_quit_habits_asp_net_users_owner_id",
                        column: x => x.owner_id,
                        principalSchema: "app",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "quit_habit_relapses",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quit_habit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_quit_habit_relapses", x => x.id);
                    table.ForeignKey(
                        name: "fk_quit_habit_relapses_asp_net_users_owner_id",
                        column: x => x.owner_id,
                        principalSchema: "app",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_quit_habit_relapses_quit_habits_quit_habit_id_owner_id",
                        columns: x => new { x.quit_habit_id, x.owner_id },
                        principalSchema: "app",
                        principalTable: "quit_habits",
                        principalColumns: new[] { "id", "owner_id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_quit_habit_relapses_owner_id",
                schema: "app",
                table: "quit_habit_relapses",
                column: "owner_id");

            migrationBuilder.CreateIndex(
                name: "ix_quit_habit_relapses_quit_habit_id_date",
                schema: "app",
                table: "quit_habit_relapses",
                columns: new[] { "quit_habit_id", "date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_quit_habit_relapses_quit_habit_id_owner_id",
                schema: "app",
                table: "quit_habit_relapses",
                columns: new[] { "quit_habit_id", "owner_id" });

            migrationBuilder.CreateIndex(
                name: "ix_quit_habits_owner_id_archived_at",
                schema: "app",
                table: "quit_habits",
                columns: new[] { "owner_id", "archived_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "quit_habit_relapses",
                schema: "app");

            migrationBuilder.DropTable(
                name: "quit_habits",
                schema: "app");
        }
    }
}
