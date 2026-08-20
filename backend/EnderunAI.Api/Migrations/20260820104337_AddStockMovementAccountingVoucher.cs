using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnderunAI.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddStockMovementAccountingVoucher : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AccountingVoucherId",
                table: "stock_movements",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AccountingVoucherId",
                table: "stock_movements");
        }
    }
}
