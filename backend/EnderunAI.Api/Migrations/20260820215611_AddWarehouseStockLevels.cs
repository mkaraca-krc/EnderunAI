using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnderunAI.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddWarehouseStockLevels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            /*
             * S8 — ASGARİ/AZAMİ KARTTAN DEPOYA TAŞINDI.
             *
             * VERİ KAYBI YOK, ÖLÇÜLDÜ: taşımadan önce canlıda
             *   select count(*) from inventory_items where "MinimumStock" > 0  ->  0
             *   "MaximumStock" dolu 9 kart, hepsinin değeri 0,0000
             * yani iki kolon da hiç kullanılmamıştı. Bu yüzden veri
             * taşıyan bir adım YAZILMADI: taşınacak bir şey yok ve
             * uydurma bir eşik üretmek, kimsenin koymadığı bir kararı
             * geriye yüklerdi.
             *
             * Seviye artık `warehouse_stock_levels` satırında; satırın
             * VARLIĞI takibin kendisidir.
             */
            migrationBuilder.DropColumn(
                name: "MaximumStock",
                table: "inventory_items");

            migrationBuilder.DropColumn(
                name: "MinimumStock",
                table: "inventory_items");

            migrationBuilder.CreateTable(
                name: "warehouse_stock_levels",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uuid", nullable: false),
                    InventoryItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    MinimumQuantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    MaximumQuantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_warehouse_stock_levels", x => x.Id);
                    table.ForeignKey(
                        name: "FK_warehouse_stock_levels_inventory_items_InventoryItemId",
                        column: x => x.InventoryItemId,
                        principalTable: "inventory_items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_warehouse_stock_levels_warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_warehouse_stock_levels_InventoryItemId",
                table: "warehouse_stock_levels",
                column: "InventoryItemId");

            migrationBuilder.CreateIndex(
                name: "IX_warehouse_stock_levels_WarehouseId",
                table: "warehouse_stock_levels",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_warehouse_stock_levels_WarehouseId_InventoryItemId",
                table: "warehouse_stock_levels",
                columns: new[] { "WarehouseId", "InventoryItemId" },
                unique: true,
                filter: "\"IsDeleted\" = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "warehouse_stock_levels");

            migrationBuilder.AddColumn<decimal>(
                name: "MaximumStock",
                table: "inventory_items",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MinimumStock",
                table: "inventory_items",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);
        }
    }
}
