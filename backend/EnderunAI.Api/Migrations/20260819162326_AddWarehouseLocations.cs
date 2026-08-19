using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnderunAI.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddWarehouseLocations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "WarehouseShelfId",
                table: "inventory_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "WarehouseShelfLevelId",
                table: "inventory_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "WarehouseZoneId",
                table: "inventory_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "warehouse_zones",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("PK_warehouse_zones", x => x.Id);
                    table.ForeignKey(
                        name: "FK_warehouse_zones_warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "warehouse_shelves",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WarehouseZoneId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("PK_warehouse_shelves", x => x.Id);
                    table.ForeignKey(
                        name: "FK_warehouse_shelves_warehouse_zones_WarehouseZoneId",
                        column: x => x.WarehouseZoneId,
                        principalTable: "warehouse_zones",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "warehouse_shelf_levels",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WarehouseShelfId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("PK_warehouse_shelf_levels", x => x.Id);
                    table.ForeignKey(
                        name: "FK_warehouse_shelf_levels_warehouse_shelves_WarehouseShelfId",
                        column: x => x.WarehouseShelfId,
                        principalTable: "warehouse_shelves",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "warehouse_category_locations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uuid", nullable: false),
                    InventoryCategoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    WarehouseZoneId = table.Column<Guid>(type: "uuid", nullable: false),
                    WarehouseShelfId = table.Column<Guid>(type: "uuid", nullable: true),
                    WarehouseShelfLevelId = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.PrimaryKey("PK_warehouse_category_locations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_warehouse_category_locations_inventory_categories_Inventory~",
                        column: x => x.InventoryCategoryId,
                        principalTable: "inventory_categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_warehouse_category_locations_warehouse_shelf_levels_Warehou~",
                        column: x => x.WarehouseShelfLevelId,
                        principalTable: "warehouse_shelf_levels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_warehouse_category_locations_warehouse_shelves_WarehouseShe~",
                        column: x => x.WarehouseShelfId,
                        principalTable: "warehouse_shelves",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_warehouse_category_locations_warehouse_zones_WarehouseZoneId",
                        column: x => x.WarehouseZoneId,
                        principalTable: "warehouse_zones",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_warehouse_category_locations_warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_inventory_items_WarehouseShelfId",
                table: "inventory_items",
                column: "WarehouseShelfId");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_items_WarehouseShelfLevelId",
                table: "inventory_items",
                column: "WarehouseShelfLevelId");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_items_WarehouseZoneId",
                table: "inventory_items",
                column: "WarehouseZoneId");

            migrationBuilder.CreateIndex(
                name: "IX_warehouse_category_locations_InventoryCategoryId",
                table: "warehouse_category_locations",
                column: "InventoryCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_warehouse_category_locations_WarehouseId_InventoryCategoryId",
                table: "warehouse_category_locations",
                columns: new[] { "WarehouseId", "InventoryCategoryId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_warehouse_category_locations_WarehouseShelfId",
                table: "warehouse_category_locations",
                column: "WarehouseShelfId");

            migrationBuilder.CreateIndex(
                name: "IX_warehouse_category_locations_WarehouseShelfLevelId",
                table: "warehouse_category_locations",
                column: "WarehouseShelfLevelId");

            migrationBuilder.CreateIndex(
                name: "IX_warehouse_category_locations_WarehouseZoneId",
                table: "warehouse_category_locations",
                column: "WarehouseZoneId");

            migrationBuilder.CreateIndex(
                name: "IX_warehouse_shelf_levels_WarehouseShelfId_Code",
                table: "warehouse_shelf_levels",
                columns: new[] { "WarehouseShelfId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_warehouse_shelves_WarehouseZoneId_Code",
                table: "warehouse_shelves",
                columns: new[] { "WarehouseZoneId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_warehouse_zones_WarehouseId_Code",
                table: "warehouse_zones",
                columns: new[] { "WarehouseId", "Code" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_inventory_items_warehouse_shelf_levels_WarehouseShelfLevelId",
                table: "inventory_items",
                column: "WarehouseShelfLevelId",
                principalTable: "warehouse_shelf_levels",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_inventory_items_warehouse_shelves_WarehouseShelfId",
                table: "inventory_items",
                column: "WarehouseShelfId",
                principalTable: "warehouse_shelves",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_inventory_items_warehouse_zones_WarehouseZoneId",
                table: "inventory_items",
                column: "WarehouseZoneId",
                principalTable: "warehouse_zones",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_inventory_items_warehouse_shelf_levels_WarehouseShelfLevelId",
                table: "inventory_items");

            migrationBuilder.DropForeignKey(
                name: "FK_inventory_items_warehouse_shelves_WarehouseShelfId",
                table: "inventory_items");

            migrationBuilder.DropForeignKey(
                name: "FK_inventory_items_warehouse_zones_WarehouseZoneId",
                table: "inventory_items");

            migrationBuilder.DropTable(
                name: "warehouse_category_locations");

            migrationBuilder.DropTable(
                name: "warehouse_shelf_levels");

            migrationBuilder.DropTable(
                name: "warehouse_shelves");

            migrationBuilder.DropTable(
                name: "warehouse_zones");

            migrationBuilder.DropIndex(
                name: "IX_inventory_items_WarehouseShelfId",
                table: "inventory_items");

            migrationBuilder.DropIndex(
                name: "IX_inventory_items_WarehouseShelfLevelId",
                table: "inventory_items");

            migrationBuilder.DropIndex(
                name: "IX_inventory_items_WarehouseZoneId",
                table: "inventory_items");

            migrationBuilder.DropColumn(
                name: "WarehouseShelfId",
                table: "inventory_items");

            migrationBuilder.DropColumn(
                name: "WarehouseShelfLevelId",
                table: "inventory_items");

            migrationBuilder.DropColumn(
                name: "WarehouseZoneId",
                table: "inventory_items");
        }
    }
}
