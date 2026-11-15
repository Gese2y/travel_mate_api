using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AbroApi.Migrations
{
    /// <inheritdoc />
    public partial class Verification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "IdVerifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Front = table.Column<byte[]>(type: "bytea", nullable: false),
                    Back = table.Column<byte[]>(type: "bytea", nullable: false),
                    Selfie = table.Column<byte[]>(type: "bytea", nullable: false),
                    FrontType = table.Column<string>(type: "text", nullable: false),
                    BackType = table.Column<string>(type: "text", nullable: false),
                    SelfieType = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    SubmittedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReviewedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IdVerifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IdVerifications_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IdVerifications_Status_SubmittedAt",
                table: "IdVerifications",
                columns: new[] { "Status", "SubmittedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_IdVerifications_UserId",
                table: "IdVerifications",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IdVerifications");
        }
    }
}
