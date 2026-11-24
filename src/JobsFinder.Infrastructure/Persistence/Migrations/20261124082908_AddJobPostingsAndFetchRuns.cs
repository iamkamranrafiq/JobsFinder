using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobsFinder.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddJobPostingsAndFetchRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "job_fetch_runs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Source = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    FailureReason = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FetchedCount = table.Column<int>(type: "integer", nullable: false),
                    InsertedCount = table.Column<int>(type: "integer", nullable: false),
                    DuplicateCount = table.Column<int>(type: "integer", nullable: false),
                    MalformedCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_job_fetch_runs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "job_postings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Source = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ExternalId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Company = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Location = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    IsRemote = table.Column<bool>(type: "boolean", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    ApplyUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    ApplyPath = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    PostedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ContentHash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_job_postings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_job_fetch_runs_StartedAt",
                table: "job_fetch_runs",
                column: "StartedAt");

            migrationBuilder.CreateIndex(
                name: "IX_job_postings_ContentHash",
                table: "job_postings",
                column: "ContentHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_job_postings_Source_ExternalId",
                table: "job_postings",
                columns: new[] { "Source", "ExternalId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "job_fetch_runs");

            migrationBuilder.DropTable(
                name: "job_postings");
        }
    }
}
