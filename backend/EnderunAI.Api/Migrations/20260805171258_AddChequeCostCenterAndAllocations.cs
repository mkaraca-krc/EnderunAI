using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnderunAI.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddChequeCostCenterAndAllocations : Migration
    {
        /// <summary>
        /// Çekte masraf merkezi ve çek dağılımı.
        ///
        /// cheques.CostCenterCode: ofis kirası gibi projesi olmayan
        /// çeklerin Merkez'e yazılabilmesi için. Boş kalan mevcut
        /// çeklerde fiş yine proje kodunu kullanır; davranış değişmez.
        ///
        /// cheque_allocations: tek çekin birden fazla projeye/Merkeze
        /// bölünmesi. Dağılımı olmayan çek bugünkü gibi tek parça
        /// işlenir — mevcut çeklerin hiçbiri etkilenmez.
        /// </summary>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CostCenterCode",
                table: "cheques",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "cheque_allocations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ChequeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: true),
                    CostCenterCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    SupplierInvoiceId = table.Column<Guid>(type: "uuid", nullable: true),
                    SalesInvoiceId = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.PrimaryKey("PK_cheque_allocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_cheque_allocations_cheques_ChequeId",
                        column: x => x.ChequeId,
                        principalTable: "cheques",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_cheque_allocations_projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_cheque_allocations_sales_invoices_SalesInvoiceId",
                        column: x => x.SalesInvoiceId,
                        principalTable: "sales_invoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_cheque_allocations_supplier_invoices_SupplierInvoiceId",
                        column: x => x.SupplierInvoiceId,
                        principalTable: "supplier_invoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_cheque_allocations_ChequeId",
                table: "cheque_allocations",
                column: "ChequeId");

            migrationBuilder.CreateIndex(
                name: "IX_cheque_allocations_ProjectId",
                table: "cheque_allocations",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_cheque_allocations_SalesInvoiceId",
                table: "cheque_allocations",
                column: "SalesInvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_cheque_allocations_SupplierInvoiceId",
                table: "cheque_allocations",
                column: "SupplierInvoiceId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cheque_allocations");

            migrationBuilder.DropColumn(
                name: "CostCenterCode",
                table: "cheques");
        }
    }
}
