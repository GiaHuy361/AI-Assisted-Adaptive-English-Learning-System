using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoreLearningSystem.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMaxScoreAndQuestionScore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "MaxScore",
                table: "Quizzes",
                type: "double",
                nullable: false,
                defaultValue: 10.0);

            migrationBuilder.AddColumn<double>(
                name: "Score",
                table: "Questions",
                type: "double",
                nullable: false,
                defaultValue: 1.0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MaxScore",
                table: "Quizzes");

            migrationBuilder.DropColumn(
                name: "Score",
                table: "Questions");
        }
    }
}
