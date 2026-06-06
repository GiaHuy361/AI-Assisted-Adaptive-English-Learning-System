using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoreLearningSystem.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UpgradeQuizToPackageModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "TimeLimit",
                table: "Quizzes",
                newName: "DurationMinutes");

            migrationBuilder.AddColumn<string>(
                name: "Level",
                table: "Quizzes",
                type: "varchar(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Level",
                table: "Quizzes");

            migrationBuilder.RenameColumn(
                name: "DurationMinutes",
                table: "Quizzes",
                newName: "TimeLimit");
        }
    }
}
