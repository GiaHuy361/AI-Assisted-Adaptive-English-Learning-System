using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoreLearningSystem.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemoveBadgeTypeAndRequiredCount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BadgeType",
                table: "AchievementBadges");

            migrationBuilder.DropColumn(
                name: "RequiredCount",
                table: "AchievementBadges");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BadgeType",
                table: "AchievementBadges",
                type: "varchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "General")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "RequiredCount",
                table: "AchievementBadges",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }
    }
}
