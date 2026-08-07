using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnderunAI.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddStockMovementSubcontractorLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SubcontractorContractId",
                table: "stock_movements",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_stock_movements_SubcontractorContractId_MovementDate",
                table: "stock_movements",
                columns: new[] { "SubcontractorContractId", "MovementDate" });

            migrationBuilder.AddForeignKey(
                name: "FK_stock_movements_subcontractor_contracts_SubcontractorContra~",
                table: "stock_movements",
                column: "SubcontractorContractId",
                principalTable: "subcontractor_contracts",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_stock_movements_subcontractor_contracts_SubcontractorContra~",
                table: "stock_movements");

            migrationBuilder.DropIndex(
                name: "IX_stock_movements_SubcontractorContractId_MovementDate",
                table: "stock_movements");

            migrationBuilder.DropColumn(
                name: "SubcontractorContractId",
                table: "stock_movements");
        }
    }
}
