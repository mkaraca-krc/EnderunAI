using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnderunAI.Api.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// SAHA DEPARTMANI — ŞEMA DEĞİL, VERİ GÖÇÜ.
    ///
    /// ── NEDEN GEREKLİ ──
    ///
    /// Canlıda beş departman vardı ve BEŞİ DE OFİS birimi (FİNANS, İNSAN
    /// KAYNAKLARI, MUHASEBE, TEKNİK OFİS, Yönetim). İş gücünün
    /// çoğunluğu ise saha: `Profession = SAHA GÖREVLİSİ` 31 kişi,
    /// ünvan tarafında USTA ×12, KALFA ×9, YARDIMCI ×8, ŞOFÖR, FORMEN,
    /// Elektrik Ustası.
    ///
    /// Yani 79 aktif personelin yarısından çoğunun gideceği bir
    /// departman YOKTU. Sorun veri girilmemesi değil, SEÇENEK
    /// yokluğuydu: kusursuz bir atama ekranı bile onları atayamazdı.
    ///
    /// ── NEDEN TEK BİR "SAHA", NEDEN İNCE BÖLÜNMEDİ ──
    ///
    /// Karar (Mehmet, 2026-09-03): saha personelinin asıl çalışma
    /// birimi PROJE. M3'te proje kanalı o işi görüyor; SAHA departman
    /// kanalı saha geneli duyurular için kalıyor. İhtiyaç çıkarsa sonra
    /// bölünür — bugün bölmek, karşılığı olmayan bir taksonomi kurmak
    /// olurdu.
    ///
    /// ── NEDEN GÖÇ, NEDEN TOHUM (SEED) DEĞİL ──
    ///
    /// Tohum, HER ZAMAN var olması gereken değişmezler içindir ve her
    /// açılışta çalışır. SAHA bir değişmez değil, belirli bir günde
    /// verilmiş bir İŞ KARARI. Göç onu tarihiyle, gerekçesiyle ve geri
    /// alınabilir biçimde kaydediyor.
    ///
    /// ── ŞİRKET BAŞINA BİR TANE ──
    ///
    /// Kimlik gömülmüyor: satır `companies` üzerinden türetiliyor.
    /// Bugün tek şirket var; ikinci şirket açıldığında bu göç yeniden
    /// koşmayacağı için oraya SAHA'yı o günün paketi ekler. Sabit bir
    /// GUID gömülseydi göç ikinci ortamda (test veritabanı dahil)
    /// yanlış şirkete yazardı.
    ///
    /// ── TEKRAR KOŞMAYA DAYANIKLI ──
    ///
    /// `NOT EXISTS` süzgeci sayesinde göç iki kez uygulanırsa ikinci
    /// satır oluşmaz. Göç provası (`goc-provasi`) canlının kopyasında
    /// koşuyor; aynı SQL'in iki ortamda çalışması gerekiyor.
    /// </remarks>
    public partial class SahaDepartmani : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                insert into hr_departments
                    (""Id"", ""CompanyId"", ""Code"", ""Name"",
                     ""IsActive"", ""IsDeleted"", ""CreatedAtUtc"")
                select
                    gen_random_uuid(), c.""Id"", 'SAHA-001', 'SAHA',
                    true, false, timezone('utc', now())
                from companies c
                where c.""IsDeleted"" = false
                  and not exists (
                    select 1 from hr_departments d
                    where d.""CompanyId"" = c.""Id""
                      and upper(d.""Code"") = 'SAHA-001');
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            /*
             * GERİ ALMA KOŞULLU: SAHA'ya atanmış personel varsa satır
             * SİLİNMEZ.
             *
             * Koşulsuz silseydik, geri alma personelleri var olmayan bir
             * departmana bağlı bırakırdı — iki bağlam arasında yabancı
             * anahtar OLMADIĞI için veritabanı buna itiraz etmez, bağ
             * sessizce kırılırdı. Ekranda "(bilinmeyen departman)"
             * görünür, tarihçede ise çözülemeyen bir kimlik kalırdı.
             *
             * Yani bu göçün geri alınması, SAHA kullanılmaya başlandıysa
             * kendiliğinden etkisizdir — ve bu doğru davranıştır:
             * kullanımdaki ana veriyi bir şema geri alması silmemeli.
             */
            migrationBuilder.Sql(@"
                delete from hr_departments d
                where upper(d.""Code"") = 'SAHA-001'
                  and not exists (
                    select 1 from personnel p
                    where p.""DepartmentId"" = d.""Id"")
                  and not exists (
                    select 1 from personnel_department_history h
                    where h.""DepartmentId"" = d.""Id""
                       or h.""PreviousDepartmentId"" = d.""Id"");
            ");
        }
    }
}
