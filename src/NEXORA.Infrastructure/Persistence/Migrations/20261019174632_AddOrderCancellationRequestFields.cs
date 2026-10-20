using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NEXORA.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderCancellationRequestFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "cancel_decision_note",
                table: "orders",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "cancel_request_id",
                table: "orders",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "cancel_request_reason",
                table: "orders",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "cancel_request_status",
                table: "orders",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "cancel_requested_at",
                table: "orders",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "cancel_reviewed_at",
                table: "orders",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "cancel_reviewed_by",
                table: "orders",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_orders_cancel_pending_review",
                table: "orders",
                columns: new[] { "cancel_requested_at", "id" },
                filter: "[cancel_request_status] = 'PENDING'");

            migrationBuilder.CreateIndex(
                name: "IX_orders_cancel_reviewed_by",
                table: "orders",
                column: "cancel_reviewed_by");

            migrationBuilder.CreateIndex(
                name: "ux_orders_cancel_request_id",
                table: "orders",
                column: "cancel_request_id",
                unique: true,
                filter: "[cancel_request_id] IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_orders_cancel_request_consistent",
                table: "orders",
                sql: "([cancel_request_status] IS NULL AND [cancel_request_reason] IS NULL AND [cancel_request_id] IS NULL AND [cancel_requested_at] IS NULL AND [cancel_reviewed_by] IS NULL AND [cancel_reviewed_at] IS NULL AND [cancel_decision_note] IS NULL)\nOR\n([cancel_request_status] = 'PENDING' AND [cancel_request_reason] IS NOT NULL AND LEN(LTRIM(RTRIM([cancel_request_reason]))) > 0 AND [cancel_request_id] IS NOT NULL AND [cancel_requested_at] IS NOT NULL AND [cancel_reviewed_by] IS NULL AND [cancel_reviewed_at] IS NULL AND [cancel_decision_note] IS NULL)\nOR\n([cancel_request_status] = 'APPROVED' AND [cancel_request_reason] IS NOT NULL AND LEN(LTRIM(RTRIM([cancel_request_reason]))) > 0 AND [cancel_request_id] IS NOT NULL AND [cancel_requested_at] IS NOT NULL AND [cancel_reviewed_by] IS NOT NULL AND [cancel_reviewed_at] IS NOT NULL AND [status] = 'CANCELLED')\nOR\n([cancel_request_status] = 'REJECTED' AND [cancel_request_reason] IS NOT NULL AND LEN(LTRIM(RTRIM([cancel_request_reason]))) > 0 AND [cancel_request_id] IS NOT NULL AND [cancel_requested_at] IS NOT NULL AND [cancel_reviewed_by] IS NOT NULL AND [cancel_reviewed_at] IS NOT NULL AND [cancel_decision_note] IS NOT NULL AND LEN(LTRIM(RTRIM([cancel_decision_note]))) > 0)");

            migrationBuilder.AddForeignKey(
                name: "FK_orders_users_cancel_reviewed_by",
                table: "orders",
                column: "cancel_reviewed_by",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_orders_users_cancel_reviewed_by",
                table: "orders");

            migrationBuilder.DropIndex(
                name: "ix_orders_cancel_pending_review",
                table: "orders");

            migrationBuilder.DropIndex(
                name: "IX_orders_cancel_reviewed_by",
                table: "orders");

            migrationBuilder.DropIndex(
                name: "ux_orders_cancel_request_id",
                table: "orders");

            migrationBuilder.DropCheckConstraint(
                name: "ck_orders_cancel_request_consistent",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "cancel_decision_note",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "cancel_request_id",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "cancel_request_reason",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "cancel_request_status",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "cancel_requested_at",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "cancel_reviewed_at",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "cancel_reviewed_by",
                table: "orders");
        }
    }
}
