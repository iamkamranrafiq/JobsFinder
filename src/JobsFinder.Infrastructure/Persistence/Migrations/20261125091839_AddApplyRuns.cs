using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobsFinder.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddApplyRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "apply_runs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    JobPostingId = table.Column<Guid>(type: "uuid", nullable: false),
                    CandidateProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Reason = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    StepCount = table.Column<int>(type: "integer", nullable: false),
                    CvAttached = table.Column<bool>(type: "boolean", nullable: false),
                    Fields = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_apply_runs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_apply_runs_candidate_profiles_CandidateProfileId",
                        column: x => x.CandidateProfileId,
                        principalTable: "candidate_profiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_apply_runs_job_postings_JobPostingId",
                        column: x => x.JobPostingId,
                        principalTable: "job_postings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "apply_screenshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ApplyRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    Step = table.Column<int>(type: "integer", nullable: false),
                    ContentType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    SizeBytes = table.Column<int>(type: "integer", nullable: false),
                    Content = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_apply_screenshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_apply_screenshots_apply_runs_ApplyRunId",
                        column: x => x.ApplyRunId,
                        principalTable: "apply_runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_apply_runs_CandidateProfileId",
                table: "apply_runs",
                column: "CandidateProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_apply_runs_JobPostingId_StartedAt",
                table: "apply_runs",
                columns: new[] { "JobPostingId", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_apply_screenshots_ApplyRunId_Step",
                table: "apply_screenshots",
                columns: new[] { "ApplyRunId", "Step" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "apply_screenshots");

            migrationBuilder.DropTable(
                name: "apply_runs");
        }
    }
}
