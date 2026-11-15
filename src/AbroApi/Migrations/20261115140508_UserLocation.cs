using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;

#nullable disable

namespace AbroApi.Migrations
{
    /// <inheritdoc />
    public partial class UserLocation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Point>(
                name: "LastLocation",
                table: "Users",
                type: "geography (point)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LocationUpdatedAt",
                table: "Users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_LastLocation",
                table: "Users",
                column: "LastLocation")
                .Annotation("Npgsql:IndexMethod", "gist");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Users_LastLocation",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "LastLocation",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "LocationUpdatedAt",
                table: "Users");
        }
    }
}
