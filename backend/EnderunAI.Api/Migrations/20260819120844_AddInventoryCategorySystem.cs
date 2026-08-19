using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnderunAI.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryCategorySystem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "InventoryCategoryId",
                table: "inventory_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "inventory_categories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
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
                    table.PrimaryKey("PK_inventory_categories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "inventory_attributes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InventoryCategoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IsRequired = table.Column<bool>(type: "boolean", nullable: false),
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
                    table.PrimaryKey("PK_inventory_attributes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_inventory_attributes_inventory_categories_InventoryCategory~",
                        column: x => x.InventoryCategoryId,
                        principalTable: "inventory_categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "inventory_category_units",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InventoryCategoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    Unit = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
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
                    table.PrimaryKey("PK_inventory_category_units", x => x.Id);
                    table.ForeignKey(
                        name: "FK_inventory_category_units_inventory_categories_InventoryCate~",
                        column: x => x.InventoryCategoryId,
                        principalTable: "inventory_categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "inventory_attribute_options",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InventoryAttributeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Value = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Display = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
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
                    table.PrimaryKey("PK_inventory_attribute_options", x => x.Id);
                    table.ForeignKey(
                        name: "FK_inventory_attribute_options_inventory_attributes_InventoryA~",
                        column: x => x.InventoryAttributeId,
                        principalTable: "inventory_attributes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "inventory_item_attribute_values",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InventoryItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    InventoryAttributeId = table.Column<Guid>(type: "uuid", nullable: false),
                    InventoryAttributeOptionId = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.PrimaryKey("PK_inventory_item_attribute_values", x => x.Id);
                    table.ForeignKey(
                        name: "FK_inventory_item_attribute_values_inventory_attribute_options~",
                        column: x => x.InventoryAttributeOptionId,
                        principalTable: "inventory_attribute_options",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_inventory_item_attribute_values_inventory_attributes_Invent~",
                        column: x => x.InventoryAttributeId,
                        principalTable: "inventory_attributes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_inventory_item_attribute_values_inventory_items_InventoryIt~",
                        column: x => x.InventoryItemId,
                        principalTable: "inventory_items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_inventory_items_InventoryCategoryId",
                table: "inventory_items",
                column: "InventoryCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_attribute_options_InventoryAttributeId_Value",
                table: "inventory_attribute_options",
                columns: new[] { "InventoryAttributeId", "Value" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_inventory_attributes_InventoryCategoryId_Code",
                table: "inventory_attributes",
                columns: new[] { "InventoryCategoryId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_inventory_categories_Code",
                table: "inventory_categories",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_inventory_category_units_InventoryCategoryId_Unit",
                table: "inventory_category_units",
                columns: new[] { "InventoryCategoryId", "Unit" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_inventory_item_attribute_values_InventoryAttributeId",
                table: "inventory_item_attribute_values",
                column: "InventoryAttributeId");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_item_attribute_values_InventoryAttributeOptionId",
                table: "inventory_item_attribute_values",
                column: "InventoryAttributeOptionId");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_item_attribute_values_InventoryItemId_InventoryAt~",
                table: "inventory_item_attribute_values",
                columns: new[] { "InventoryItemId", "InventoryAttributeId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_inventory_items_inventory_categories_InventoryCategoryId",
                table: "inventory_items",
                column: "InventoryCategoryId",
                principalTable: "inventory_categories",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_inventory_items_inventory_categories_InventoryCategoryId",
                table: "inventory_items");

            migrationBuilder.DropTable(
                name: "inventory_category_units");

            migrationBuilder.DropTable(
                name: "inventory_item_attribute_values");

            migrationBuilder.DropTable(
                name: "inventory_attribute_options");

            migrationBuilder.DropTable(
                name: "inventory_attributes");

            migrationBuilder.DropTable(
                name: "inventory_categories");

            migrationBuilder.DropIndex(
                name: "IX_inventory_items_InventoryCategoryId",
                table: "inventory_items");

            migrationBuilder.DropColumn(
                name: "InventoryCategoryId",
                table: "inventory_items");
        }
    }
}
