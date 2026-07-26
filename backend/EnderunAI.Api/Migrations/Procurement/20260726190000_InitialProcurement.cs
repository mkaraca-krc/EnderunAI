using System;
using EnderunAI.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnderunAI.Api.Migrations.Procurement;

[DbContext(typeof(ProcurementDbContext))]
[Migration("20260726190000_InitialProcurement")]
public partial class InitialProcurement : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "rfqs",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                PurchaseRequestId = table.Column<Guid>(type: "uuid", nullable: true),
                RfqNumber = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                RfqDateUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                OfferDeadlineUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                Status = table.Column<int>(type: "integer", nullable: false),
                CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
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
                table.PrimaryKey("PK_rfqs", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "rfq_items",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                RfqId = table.Column<Guid>(type: "uuid", nullable: false),
                MaterialId = table.Column<Guid>(type: "uuid", nullable: false),
                Quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                Unit = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                RequiredDateUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
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
                table.PrimaryKey("PK_rfq_items", x => x.Id);
                table.ForeignKey(
                    name: "FK_rfq_items_rfqs_RfqId",
                    column: x => x.RfqId,
                    principalTable: "rfqs",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "supplier_offers",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                RfqId = table.Column<Guid>(type: "uuid", nullable: false),
                SupplierCurrentAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                OfferNumber = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                OfferDateUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                ExchangeRate = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                DiscountRate = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                FreightAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                FreightResponsibility = table.Column<int>(type: "integer", nullable: false),
                PaymentTermDays = table.Column<int>(type: "integer", nullable: false),
                DeliveryTermDays = table.Column<int>(type: "integer", nullable: false),
                AllowsPartialShipment = table.Column<bool>(type: "boolean", nullable: false),
                SupplierPerformanceScore = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
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
                table.PrimaryKey("PK_supplier_offers", x => x.Id);
                table.ForeignKey(
                    name: "FK_supplier_offers_rfqs_RfqId",
                    column: x => x.RfqId,
                    principalTable: "rfqs",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "supplier_offer_items",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                SupplierOfferId = table.Column<Guid>(type: "uuid", nullable: false),
                RfqItemId = table.Column<Guid>(type: "uuid", nullable: false),
                MaterialId = table.Column<Guid>(type: "uuid", nullable: false),
                OfferedQuantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                AvailableStockQuantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                UnitPrice = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                ItemDeliveryDays = table.Column<int>(type: "integer", nullable: false),
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
                table.PrimaryKey("PK_supplier_offer_items", x => x.Id);
                table.ForeignKey(
                    name: "FK_supplier_offer_items_supplier_offers_SupplierOfferId",
                    column: x => x.SupplierOfferId,
                    principalTable: "supplier_offers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "supplier_offer_check_terms",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                SupplierOfferId = table.Column<Guid>(type: "uuid", nullable: false),
                DueDateUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                SequenceNo = table.Column<int>(type: "integer", nullable: false),
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
                table.PrimaryKey("PK_supplier_offer_check_terms", x => x.Id);
                table.ForeignKey(
                    name: "FK_supplier_offer_check_terms_supplier_offers_SupplierOfferId",
                    column: x => x.SupplierOfferId,
                    principalTable: "supplier_offers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_rfqs_CompanyId_RfqNumber",
            table: "rfqs",
            columns: new[] { "CompanyId", "RfqNumber" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_rfq_items_RfqId",
            table: "rfq_items",
            column: "RfqId");

        migrationBuilder.CreateIndex(
            name: "IX_supplier_offers_RfqId_SupplierCurrentAccountId_OfferNumber",
            table: "supplier_offers",
            columns: new[] { "RfqId", "SupplierCurrentAccountId", "OfferNumber" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_supplier_offer_items_SupplierOfferId",
            table: "supplier_offer_items",
            column: "SupplierOfferId");

        migrationBuilder.CreateIndex(
            name: "IX_supplier_offer_check_terms_SupplierOfferId",
            table: "supplier_offer_check_terms",
            column: "SupplierOfferId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "supplier_offer_check_terms");
        migrationBuilder.DropTable(name: "supplier_offer_items");
        migrationBuilder.DropTable(name: "supplier_offers");
        migrationBuilder.DropTable(name: "rfq_items");
        migrationBuilder.DropTable(name: "rfqs");
    }
}
