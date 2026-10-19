using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NEXORA.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCheckoutRequestId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "checkout_request_id",
                table: "orders",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ux_orders_checkout_request_id",
                table: "orders",
                column: "checkout_request_id",
                unique: true,
                filter: "[checkout_request_id] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_orders_checkout_request_id",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "checkout_request_id",
                table: "orders");
        }
    }
}
