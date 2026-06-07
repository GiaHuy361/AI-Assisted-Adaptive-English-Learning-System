using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoreLearningSystem.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSkillMatrixAndWeaknessHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LearnerWeaknessHistories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    LearnerProfileId = table.Column<int>(type: "int", nullable: false),
                    Skill = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Topic = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Level = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IncorrectCount = table.Column<int>(type: "int", nullable: false),
                    OccurrenceCount = table.Column<int>(type: "int", nullable: false),
                    LastOccurredAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    FirstOccurredAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    SourceQuizAttemptId = table.Column<int>(type: "int", nullable: false),
                    LastEventId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LearnerWeaknessHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LearnerWeaknessHistories_LearnerProfiles_LearnerProfileId",
                        column: x => x.LearnerProfileId,
                        principalTable: "LearnerProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "SkillMatrices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    LearnerProfileId = table.Column<int>(type: "int", nullable: false),
                    Skill = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CurrentScore = table.Column<double>(type: "double", nullable: false),
                    MasteryLevel = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    TotalAssessments = table.Column<int>(type: "int", nullable: false),
                    LastAssessmentScore = table.Column<double>(type: "double", nullable: false),
                    LastUpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SkillMatrices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SkillMatrices_LearnerProfiles_LearnerProfileId",
                        column: x => x.LearnerProfileId,
                        principalTable: "LearnerProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "SkillMatrixHistories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    SkillMatrixId = table.Column<int>(type: "int", nullable: false),
                    LearnerProfileId = table.Column<int>(type: "int", nullable: false),
                    Skill = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PreviousScore = table.Column<double>(type: "double", nullable: false),
                    AssessmentScore = table.Column<double>(type: "double", nullable: false),
                    NewScore = table.Column<double>(type: "double", nullable: false),
                    SourceType = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SourceId = table.Column<int>(type: "int", nullable: false),
                    EventId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Reason = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RecordedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SkillMatrixHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SkillMatrixHistories_LearnerProfiles_LearnerProfileId",
                        column: x => x.LearnerProfileId,
                        principalTable: "LearnerProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_LearnerWeaknessHistories_LearnerProfileId_Skill_Topic",
                table: "LearnerWeaknessHistories",
                columns: new[] { "LearnerProfileId", "Skill", "Topic" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SkillMatrices_LearnerProfileId_Skill",
                table: "SkillMatrices",
                columns: new[] { "LearnerProfileId", "Skill" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SkillMatrixHistories_EventId",
                table: "SkillMatrixHistories",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_SkillMatrixHistories_LearnerProfileId",
                table: "SkillMatrixHistories",
                column: "LearnerProfileId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LearnerWeaknessHistories");

            migrationBuilder.DropTable(
                name: "SkillMatrices");

            migrationBuilder.DropTable(
                name: "SkillMatrixHistories");
        }
    }
}
