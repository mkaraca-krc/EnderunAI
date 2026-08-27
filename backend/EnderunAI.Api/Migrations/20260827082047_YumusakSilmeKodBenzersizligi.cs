using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnderunAI.Api.Migrations
{
    /// <inheritdoc />
    public partial class YumusakSilmeKodBenzersizligi : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            /*
             * YUMUŞAK SİLİNEN KAYDIN KODU REHİN KALMAZ (Kural 49).
             *
             * Bu dokuz tabloda kodu KULLANICI seçiyor ve kod bir
             * EŞLEŞTİRME ANAHTARI DEĞİL: hiçbir sorgu, rapor, belge
             * ya da dış bütünleşme bu kodla eşleştirme yapmıyor
             * (ölçüldü). Depo hiyerarşisinde konum tamamen `Id`
             * üzerinden bağlı — metin olarak saklayan tek bir yer
             * yok.
             *
             * Eşleştirme anahtarı OLANLAR bilerek dışarıda bırakıldı:
             * muhasebe hesabı (koddan ebeveyn türetiliyor), proje,
             * mühendislik pozisyonu, stok kalemi, depo, şirket, kasa,
             * şube, cari. Onlarda kod kalıcı olarak rezervedir.
             *
             * DÜŞÜRMELER KOŞULLU (`IF EXISTS`): `DropIndex` indeksin
             * var olduğunu VARSAYAR. HR bölgesinde tam bu varsayım
             * yüzünden "42704: index does not exist" alındı — model
             * bir ad bekliyordu, fizikte başka ad vardı. Bu dokuz
             * kalemin fiziksel adları tek tek ÖLÇÜLDÜ ve uyuyor, ama
             * varsayıma dayanmak yerine koşullu düşürmek bedelsiz.
             *
             * TEK İŞLEM: EF Core her göçü kendi işleminde koşturur ve
             * burada bunu bozan bir şey yok (`CREATE INDEX
             * CONCURRENTLY` KULLANILMADI — işlem içinde çalışmaz).
             * Ortada başarısız olursa tamamı geri alınır; indeksleri
             * düşürülmüş ama yenileri kurulmamış bir canlı kalmaz.
             */
            migrationBuilder.Sql(
                "DROP INDEX IF EXISTS \"IX_warehouse_zones_WarehouseId_Code\";");
            migrationBuilder.Sql(
                "DROP INDEX IF EXISTS \"IX_warehouse_shelves_WarehouseZoneId_Code\";");
            migrationBuilder.Sql(
                "DROP INDEX IF EXISTS \"IX_warehouse_shelf_levels_WarehouseShelfId_Code\";");
            migrationBuilder.Sql(
                "DROP INDEX IF EXISTS \"IX_inventory_categories_Code\";");
            migrationBuilder.Sql(
                "DROP INDEX IF EXISTS \"IX_inventory_attributes_InventoryCategoryId_Code\";");
            migrationBuilder.Sql(
                "DROP INDEX IF EXISTS \"IX_hr_shift_definitions_CompanyId_Code\";");
            migrationBuilder.Sql(
                "DROP INDEX IF EXISTS \"IX_document_categories_CompanyId_Code\";");

migrationBuilder.CreateIndex(
                name: "IX_warehouse_zones_WarehouseId_Code",
                table: "warehouse_zones",
                columns: new[] { "WarehouseId", "Code" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_warehouse_shelves_WarehouseZoneId_Code",
                table: "warehouse_shelves",
                columns: new[] { "WarehouseZoneId", "Code" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_warehouse_shelf_levels_WarehouseShelfId_Code",
                table: "warehouse_shelf_levels",
                columns: new[] { "WarehouseShelfId", "Code" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_categories_Code",
                table: "inventory_categories",
                column: "Code",
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_attributes_InventoryCategoryId_Code",
                table: "inventory_attributes",
                columns: new[] { "InventoryCategoryId", "Code" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_hr_shift_definitions_CompanyId_Code",
                table: "hr_shift_definitions",
                columns: new[] { "CompanyId", "Code" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_document_categories_CompanyId_Code",
                table: "document_categories",
                columns: new[] { "CompanyId", "Code" },
                unique: true,
                filter: "\"IsDeleted\" = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DROP INDEX IF EXISTS \"IX_warehouse_zones_WarehouseId_Code\";");
            migrationBuilder.Sql(
                "DROP INDEX IF EXISTS \"IX_warehouse_shelves_WarehouseZoneId_Code\";");
            migrationBuilder.Sql(
                "DROP INDEX IF EXISTS \"IX_warehouse_shelf_levels_WarehouseShelfId_Code\";");
            migrationBuilder.Sql(
                "DROP INDEX IF EXISTS \"IX_inventory_categories_Code\";");
            migrationBuilder.Sql(
                "DROP INDEX IF EXISTS \"IX_inventory_attributes_InventoryCategoryId_Code\";");
            migrationBuilder.Sql(
                "DROP INDEX IF EXISTS \"IX_hr_shift_definitions_CompanyId_Code\";");
            migrationBuilder.Sql(
                "DROP INDEX IF EXISTS \"IX_document_categories_CompanyId_Code\";");

migrationBuilder.CreateIndex(
                name: "IX_warehouse_zones_WarehouseId_Code",
                table: "warehouse_zones",
                columns: new[] { "WarehouseId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_warehouse_shelves_WarehouseZoneId_Code",
                table: "warehouse_shelves",
                columns: new[] { "WarehouseZoneId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_warehouse_shelf_levels_WarehouseShelfId_Code",
                table: "warehouse_shelf_levels",
                columns: new[] { "WarehouseShelfId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_inventory_categories_Code",
                table: "inventory_categories",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_inventory_attributes_InventoryCategoryId_Code",
                table: "inventory_attributes",
                columns: new[] { "InventoryCategoryId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_hr_shift_definitions_CompanyId_Code",
                table: "hr_shift_definitions",
                columns: new[] { "CompanyId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_document_categories_CompanyId_Code",
                table: "document_categories",
                columns: new[] { "CompanyId", "Code" },
                unique: true);
        }
    }
}
