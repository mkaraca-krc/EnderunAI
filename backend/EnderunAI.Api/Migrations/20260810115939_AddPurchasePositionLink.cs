using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnderunAI.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPurchasePositionLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "EngineeringPositionId",
                table: "rfq_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "EngineeringPositionId",
                table: "purchase_request_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "EngineeringPositionId",
                table: "purchase_order_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_rfq_items_EngineeringPositionId",
                table: "rfq_items",
                column: "EngineeringPositionId");

            migrationBuilder.CreateIndex(
                name: "IX_purchase_request_items_EngineeringPositionId",
                table: "purchase_request_items",
                column: "EngineeringPositionId");

            migrationBuilder.CreateIndex(
                name: "IX_purchase_order_items_EngineeringPositionId",
                table: "purchase_order_items",
                column: "EngineeringPositionId");

            migrationBuilder.AddForeignKey(
                name: "FK_purchase_order_items_engineering_positions_EngineeringPosit~",
                table: "purchase_order_items",
                column: "EngineeringPositionId",
                principalTable: "engineering_positions",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_purchase_request_items_engineering_positions_EngineeringPos~",
                table: "purchase_request_items",
                column: "EngineeringPositionId",
                principalTable: "engineering_positions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_rfq_items_engineering_positions_EngineeringPositionId",
                table: "rfq_items",
                column: "EngineeringPositionId",
                principalTable: "engineering_positions",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_purchase_order_items_engineering_positions_EngineeringPosit~",
                table: "purchase_order_items");

            migrationBuilder.DropForeignKey(
                name: "FK_purchase_request_items_engineering_positions_EngineeringPos~",
                table: "purchase_request_items");

            migrationBuilder.DropForeignKey(
                name: "FK_rfq_items_engineering_positions_EngineeringPositionId",
                table: "rfq_items");

            migrationBuilder.DropIndex(
                name: "IX_rfq_items_EngineeringPositionId",
                table: "rfq_items");

            migrationBuilder.DropIndex(
                name: "IX_purchase_request_items_EngineeringPositionId",
                table: "purchase_request_items");

            migrationBuilder.DropIndex(
                name: "IX_purchase_order_items_EngineeringPositionId",
                table: "purchase_order_items");

            migrationBuilder.DropColumn(
                name: "EngineeringPositionId",
                table: "rfq_items");

            migrationBuilder.DropColumn(
                name: "EngineeringPositionId",
                table: "purchase_request_items");

            migrationBuilder.DropColumn(
                name: "EngineeringPositionId",
                table: "purchase_order_items");
        }
    }
}
