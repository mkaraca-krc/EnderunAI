using EnderunAI.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace EnderunAI.Api.Services.Inventory;

public interface IInventoryCodeService
{
    /// <summary>Şirketin bir sonraki stok kartı kodu (END0010, END0011…).</summary>
    Task<string> NextCodeAsync(Guid companyId, CancellationToken cancellationToken = default);
}

/// <summary>
/// STOK KARTI KODU — TAM OTOMATİK, `END` + 4 HANE DOLGU.
///
/// Kod bir KİMLİK, bir tanım değil. Kullanıcı girmez, düşünmez, bilmek
/// zorunda değildir; fiş ve rapor referansı olarak kullanılır. Ürünü
/// tanımlayan şey AD ve ÖZELLİKLERDİR.
///
/// ═══ BİÇİM NEDEN DEĞİŞTİ (2026-09-13) ═══
///
/// Bu servis önce `100001, 100002…` üretiyordu: ÖNEKSİZ ve DOLGUSUZ.
/// Gerekçesi "kısa numaralar anlamlı kod izlenimi veriyor"du. Ölçüm o
/// gerekçeyi geçersiz kılmadı ama DAHA BÜYÜK bir sorunu gösterdi:
///
///   canlıda 9 kart var ve zaten İKİ biçim taşıyorlar —
///   END0001…END0009 (dolgulu, 7 kayıt) ve END004, END005 (dolgusuz,
///   2 kayıt; o tarihte kod ELLE yazılıyordu, dolgu garantisi yoktu).
///
/// Üretici `100001` üretseydi ilk yeni kartta listede ÜÇÜNCÜ biçim
/// belirecek ve her gün büyüyecekti. Sıralama ve arama üç biçimin
/// arasında bozulur. Mehmet Bey'in kararı: tek biçim, `END` + 4 hane.
///
/// ESKİ KAYITLARA DOKUNULMUYOR. `END004`/`END005` olduğu gibi kalır —
/// kod alanı belge izinin parçası ve geçmişe dönük değiştirmek fiş,
/// rapor ve dış yazışmadaki referansları koparır.
///
/// ═══ NEDEN MEVCUT `IDocumentNumberService` KULLANILMIYOR ═══
///   1. O servis `ÖNEK-YIL-NNNNNN` üretiyor; buradaki biçim farklı.
///   2. Daha önemlisi o sıra YILA BAĞLI (CompanyId+Type+Year). Stok
///      kodu yıl değişince başa dönmemeli — 2027'de açılan kart
///      2026'daki END0010 ile çakışırdı.
///   3. Artırması KİLİTSİZ: okur, artırır, kaydeder. Eşzamanlı iki
///      kart aynı numarayı alabilir ve `(CompanyId, Code)` tekil
///      indeksinde çökerdi.
///
/// BURADAKİ ÇÖZÜM ATOMİK: tek `INSERT … ON CONFLICT DO UPDATE …
/// RETURNING` ifadesi. PostgreSQL satırı kendi kilitler; okuma ile
/// yazma arasında pencere kalmaz. Eşzamanlı çağrılar sıraya girer ve
/// farklı numara alır.
/// </summary>
public sealed class InventoryCodeService(AppDbContext db) : IInventoryCodeService
{
    /// <summary>Tek ön ek. Biçim: <c>END</c> + <see cref="HaneSayisi"/> hane.</summary>
    public const string OnEk = "END";

    /// <summary>
    /// Dolgu hane sayısı. 9999'u aşarsa numara doğal olarak 5 haneye
    /// çıkar (END10000) — dolgu kaybolmaz, genişler. Bugün 9 kart var;
    /// bu sınır yakın değil ama sessiz değil: biçim sondası bunu
    /// ayrıca ölçüyor.
    /// </summary>
    public const int HaneSayisi = 4;

    /// <summary>`END` + hane kalıbı. Sonda ve içe aktarma doğrulaması bunu kullanır.</summary>
    public static readonly System.Text.RegularExpressions.Regex Kalip =
        new($"^{OnEk}[0-9]{{{HaneSayisi},}}$",
            System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// Belge tipi anahtarı. Yıl kolonuna 0 yazılıyor: bu sıra YILA
    /// BAĞLI DEĞİL ve 0 "yıl kırılımı yok" demek. Gerçek bir yılla
    /// karışmaz çünkü hiçbir belge 0 yılında üretilmez.
    /// </summary>
    private const string DocumentType = "INVENTORY_ITEM_CODE";

    public async Task<string> NextCodeAsync(
        Guid companyId, CancellationToken cancellationToken = default)
    {
        if (companyId == Guid.Empty)
            throw new ArgumentException("Şirket bilgisi zorunludur.", nameof(companyId));

        //
        // SIRA, MEVCUT EN BÜYÜK NUMARADAN DEVAM EDER.
        //
        // Sıfırdan başlasaydı ilk üretilen kod END0001 olurdu ve
        // `(CompanyId, Code)` tekil indeksinde ÇÖKERDİ — canlıda o kod
        // zaten var. Tohum YALNIZ satır ilk kez kurulurken okunur;
        // sonrasında sıra kendi kendine artar ve bu sorgu sonucu
        // `ON CONFLICT` dalında yok sayılır.
        //
        // Dolgusuz eski kayıtlar da sayılır: `END005` → 5. Kalıp
        // `^END([0-9]+)$` olduğu için `END` ile başlamayan ya da harf
        // içeren kodlar eşleşmez, `SUBSTRING` null döner ve `MAX`
        // onları atlar.
        //
        const string sql = """
            WITH tohum AS (
                SELECT COALESCE(
                    MAX(CAST(SUBSTRING("Code" FROM '^END([0-9]+)$') AS bigint)), 0) AS enbuyuk
                FROM inventory_items
                WHERE "CompanyId" = @companyId
            )
            INSERT INTO document_number_sequences
                ("Id", "CompanyId", "DocumentType", "Prefix", "Year",
                 "LastNumber", "NumberLength", "IsActive", "IsDeleted", "CreatedAtUtc")
            SELECT
                gen_random_uuid(), @companyId, @documentType, @onEk, 0,
                tohum.enbuyuk + 1, @haneSayisi, true, false, now() AT TIME ZONE 'utc'
            FROM tohum
            ON CONFLICT ("CompanyId", "DocumentType", "Year")
            DO UPDATE SET
                "LastNumber" = document_number_sequences."LastNumber" + 1,
                "UpdatedAtUtc" = now() AT TIME ZONE 'utc'
            RETURNING "LastNumber";
            """;

        var connection = db.Database.GetDbConnection();
        var openedHere = connection.State != System.Data.ConnectionState.Open;

        if (openedHere) await connection.OpenAsync(cancellationToken);

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;

            // Çağıran bir transaction açtıysa ona katıl; ayrı bağlantı
            // açmak "connection is already in a transaction" verirdi.
            if (db.Database.CurrentTransaction is not null)
                command.Transaction = db.Database.CurrentTransaction.GetDbTransaction();

            command.Parameters.Add(new NpgsqlParameter("companyId", companyId));
            command.Parameters.Add(new NpgsqlParameter("documentType", DocumentType));
            command.Parameters.Add(new NpgsqlParameter("onEk", OnEk));
            command.Parameters.Add(new NpgsqlParameter("haneSayisi", HaneSayisi));

            var result = await command.ExecuteScalarAsync(cancellationToken);

            var next = Convert.ToInt64(result);

            // DOLGU BURADA GARANTİ ALTINDA: dizge birleştirme değil,
            // sabit genişlikli biçimlendirme. Eski yolda `ToString()`
            // vardı ve dolgu hiçbir yerde garanti edilmiyordu.
            return OnEk + next.ToString($"D{HaneSayisi}");
        }
        finally
        {
            if (openedHere) await connection.CloseAsync();
        }
    }
}
