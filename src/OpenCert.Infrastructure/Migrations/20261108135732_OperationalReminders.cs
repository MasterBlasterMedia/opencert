using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenCert.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class OperationalReminders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CertificateObservedAt",
                table: "Websites",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "NextCheckAt",
                table: "Websites",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<Guid>(
                name: "OrganizationId",
                table: "OutgoingEmails",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RecipientUserId",
                table: "OutgoingEmails",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RenewalDueAt",
                table: "Orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeduplicationKey",
                table: "Notifications",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "CertificateEmails",
                table: "AspNetUsers",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateIndex(
                name: "IX_Websites_NextCheckAt",
                table: "Websites",
                column: "NextCheckAt");

            migrationBuilder.CreateIndex(
                name: "IX_OutgoingEmails_RecipientUserId_CreatedAt",
                table: "OutgoingEmails",
                columns: new[] { "RecipientUserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_RenewalDueAt",
                table: "Orders",
                column: "RenewalDueAt");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_DeduplicationKey",
                table: "Notifications",
                column: "DeduplicationKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Websites_NextCheckAt",
                table: "Websites");

            migrationBuilder.DropIndex(
                name: "IX_OutgoingEmails_RecipientUserId_CreatedAt",
                table: "OutgoingEmails");

            migrationBuilder.DropIndex(
                name: "IX_Orders_RenewalDueAt",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_Notifications_DeduplicationKey",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "CertificateObservedAt",
                table: "Websites");

            migrationBuilder.DropColumn(
                name: "NextCheckAt",
                table: "Websites");

            migrationBuilder.DropColumn(
                name: "OrganizationId",
                table: "OutgoingEmails");

            migrationBuilder.DropColumn(
                name: "RecipientUserId",
                table: "OutgoingEmails");

            migrationBuilder.DropColumn(
                name: "RenewalDueAt",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "DeduplicationKey",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "CertificateEmails",
                table: "AspNetUsers");
        }
    }
}
