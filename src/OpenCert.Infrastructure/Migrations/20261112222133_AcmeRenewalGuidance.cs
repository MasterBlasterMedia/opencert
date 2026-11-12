using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenCert.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AcmeRenewalGuidance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "NextRenewalInfoAt",
                table: "Orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RenewalInfoCheckedAt",
                table: "Orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RenewalInfoError",
                table: "Orders",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RenewalInfoFailures",
                table: "Orders",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "RenewalSource",
                table: "Orders",
                type: "text",
                nullable: false,
                defaultValue: "Validity");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RenewalWindowEnd",
                table: "Orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RenewalWindowStart",
                table: "Orders",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NextRenewalInfoAt",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "RenewalInfoCheckedAt",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "RenewalInfoError",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "RenewalInfoFailures",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "RenewalSource",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "RenewalWindowEnd",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "RenewalWindowStart",
                table: "Orders");
        }
    }
}
