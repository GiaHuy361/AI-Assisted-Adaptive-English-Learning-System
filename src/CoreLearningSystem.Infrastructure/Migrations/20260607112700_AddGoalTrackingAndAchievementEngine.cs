using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoreLearningSystem.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddGoalTrackingAndAchievementEngine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {

            migrationBuilder.AddColumn<double>(
                name: "ProgressValue",
                table: "LearnerBadges",
                type: "double",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<string>(
                name: "Reason",
                table: "LearnerBadges",
                type: "varchar(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "SourceEventId",
                table: "LearnerBadges",
                type: "varchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<DateTime>(
                name: "CompletedAt",
                table: "GoalSettings",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "CurrentValue",
                table: "GoalSettings",
                type: "double",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<string>(
                name: "SkillTarget",
                table: "GoalSettings",
                type: "varchar(50)",
                maxLength: 50,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<DateTime>(
                name: "StartDate",
                table: "GoalSettings",
                type: "datetime(6)",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "GoalSettings",
                type: "varchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "TargetLevel",
                table: "GoalSettings",
                type: "varchar(20)",
                maxLength: 20,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<double>(
                name: "TargetValue",
                table: "GoalSettings",
                type: "double",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<string>(
                name: "Unit",
                table: "GoalSettings",
                type: "varchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "GoalSettings",
                type: "datetime(6)",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "AchievementType",
                table: "AchievementBadges",
                type: "varchar(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "Code",
                table: "AchievementBadges",
                type: "varchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "AchievementBadges",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "SkillTarget",
                table: "AchievementBadges",
                type: "varchar(20)",
                maxLength: 20,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<double>(
                name: "Threshold",
                table: "AchievementBadges",
                type: "double",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "AchievementBadges",
                type: "datetime(6)",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.CreateTable(
                name: "GoalProgressHistories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    GoalId = table.Column<int>(type: "int", nullable: false),
                    LearnerProfileId = table.Column<int>(type: "int", nullable: false),
                    SourceEventId = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PreviousValue = table.Column<double>(type: "double", nullable: false),
                    AddedValue = table.Column<double>(type: "double", nullable: false),
                    NewValue = table.Column<double>(type: "double", nullable: false),
                    StatusBefore = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    StatusAfter = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Reason = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RecordedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GoalProgressHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GoalProgressHistories_GoalSettings_GoalId",
                        column: x => x.GoalId,
                        principalTable: "GoalSettings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GoalProgressHistories_LearnerProfiles_LearnerProfileId",
                        column: x => x.LearnerProfileId,
                        principalTable: "LearnerProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_LearnerBadges_LearnerProfileId_BadgeId",
                table: "LearnerBadges",
                columns: new[] { "LearnerProfileId", "BadgeId" },
                unique: true);

            migrationBuilder.DropIndex(
                name: "IX_LearnerBadges_LearnerProfileId",
                table: "LearnerBadges");

            migrationBuilder.CreateIndex(
                name: "IX_AchievementBadges_Code",
                table: "AchievementBadges",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GoalProgressHistories_GoalId_SourceEventId",
                table: "GoalProgressHistories",
                columns: new[] { "GoalId", "SourceEventId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GoalProgressHistories_LearnerProfileId",
                table: "GoalProgressHistories",
                column: "LearnerProfileId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GoalProgressHistories");

            migrationBuilder.DropIndex(
                name: "IX_LearnerBadges_LearnerProfileId_BadgeId",
                table: "LearnerBadges");

            migrationBuilder.DropIndex(
                name: "IX_AchievementBadges_Code",
                table: "AchievementBadges");

            migrationBuilder.DropColumn(
                name: "ProgressValue",
                table: "LearnerBadges");

            migrationBuilder.DropColumn(
                name: "Reason",
                table: "LearnerBadges");

            migrationBuilder.DropColumn(
                name: "SourceEventId",
                table: "LearnerBadges");

            migrationBuilder.DropColumn(
                name: "CompletedAt",
                table: "GoalSettings");

            migrationBuilder.DropColumn(
                name: "CurrentValue",
                table: "GoalSettings");

            migrationBuilder.DropColumn(
                name: "SkillTarget",
                table: "GoalSettings");

            migrationBuilder.DropColumn(
                name: "StartDate",
                table: "GoalSettings");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "GoalSettings");

            migrationBuilder.DropColumn(
                name: "TargetLevel",
                table: "GoalSettings");

            migrationBuilder.DropColumn(
                name: "TargetValue",
                table: "GoalSettings");

            migrationBuilder.DropColumn(
                name: "Unit",
                table: "GoalSettings");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "GoalSettings");

            migrationBuilder.DropColumn(
                name: "AchievementType",
                table: "AchievementBadges");

            migrationBuilder.DropColumn(
                name: "Code",
                table: "AchievementBadges");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "AchievementBadges");

            migrationBuilder.DropColumn(
                name: "SkillTarget",
                table: "AchievementBadges");

            migrationBuilder.DropColumn(
                name: "Threshold",
                table: "AchievementBadges");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "AchievementBadges");

            migrationBuilder.CreateIndex(
                name: "IX_LearnerBadges_LearnerProfileId",
                table: "LearnerBadges",
                column: "LearnerProfileId");
        }
    }
}
