using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnderunAI.Api.Migrations
{
    /// <summary>
    /// GÖREV TÜRÜ VE PERSONEL ATAMASI — TEK GÖÇTE İKİ SÜTUN.
    ///
    /// İKİSİ AYRI GÖÇ OLMADI ÇÜNKÜ AYNI SORUYU CEVAPLIYORLAR: "bu iş ne
    /// ve kim yapacak". Ayrı ayrı çıksalardı, arada kalan sürümde tür
    /// zorunlu ama personel alanı yok olurdu ve ön yüz iki adımda
    /// uyarlanmak zorunda kalırdı.
    ///
    /// TERSİ ALINABİLİR: `Down` iki sütunu da düşürüyor. Geri alma veri
    /// KAYBEDİYOR (tür ve personel ataması gider) ama şema tam olarak
    /// göç öncesi hâline dönüyor — sütunlar bu göçte doğdu, öncesinde
    /// karşılıkları yoktu.
    /// </summary>
    public partial class IsEmriTuruVePersonelAtamasi : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AssignedToPersonnelId",
                table: "WorkTasks",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Kind",
                table: "WorkTasks",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            /*
             * MEVCUT SATIRLARIN TÜRÜ — ÖLÇÜLDÜ, TAHMİN EDİLMEDİ.
             *
             * Canlıda (2026-09-02) toplam İKİ görev var:
             *   GRV-2026-00001  "Test görev"  — SourceModule boş, iptal
             *   GRV-2026-000001 "Sistem doğrulama — masraf merkezi
             *                    kontrolü" — SourceModule 'MANUAL', açık
             *
             * İkisi de İŞ EMRİ. Hatırlatma OLMADIKLARI ölçülebilir:
             * Hızır'ın hatırlatma yolu görevi HER ZAMAN çağırana atar
             * (`AssignedToUserId = context.UserId`), bu iki satırda ise
             * `AssignedToUserId` NULL. Yani hiçbiri o yoldan doğmamış.
             *
             * NEDEN SIFIRDA BIRAKILMADI: `Belirsiz` yazma yollarında
             * reddediliyor. Eski satırlar sıfırda kalsaydı, biri onları
             * güncellemek istediğinde tür seçmek ZORUNDA kalırdı — bu
             * kabul edilebilirdi. Ama liste ve raporlar tür kırılımı
             * gösterdiğinde iki satır "türü yok" kovasına düşerdi ve o
             * kova, gerçekte var olmayan bir durumu temsil ederdi.
             *
             * KOŞUL YAZILI: `where "Kind" = 0` — göç iki kez çalışsa
             * bile sonradan girilmiş türleri EZMEZ.
             */
            migrationBuilder.Sql(
                @"update ""WorkTasks"" set ""Kind"" = 1 where ""Kind"" = 0;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AssignedToPersonnelId",
                table: "WorkTasks");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "WorkTasks");
        }
    }
}
