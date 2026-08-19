using EnderunAI.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace EnderunAI.Api.Services.Inventory;

public interface IInventoryCodeService
{
    /// <summary>Şirketin bir sonraki stok kartı kodu (100001, 100002…).</summary>
    Task<string> NextCodeAsync(Guid companyId, CancellationToken cancellationToken = default);
}

/// <summary>
/// STOK KARTI KODU — TAM OTOMATİK, ANLAMSIZ SIRA NUMARASI.
///
/// Kod bir KİMLİK, bir tanım değil. Kullanıcı girmez, düşünmez, bilmek
/// zorunda değildir; fiş ve rapor referansı olarak kullanılır. Ürünü
/// tanımlayan şey AD ve ÖZELLİKLERDİR.
///
/// NEDEN MEVCUT `IDocumentNumberService` KULLANILMIYOR:
///   1. O servis `ÖNEK-YIL-NNNNNN` üretiyor; burada ön ek ve anlam
///      İSTENMİYOR.
///   2. Daha önemlisi o sıra YILA BAĞLI (CompanyId+Type+Year). Stok
///      kodu yıl değişince başa dönmemeli — 2027'de açılan kart
///      2026'daki 100001 ile çakışırdı.
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
    /// <summary>
    /// İlk kod 100001. Beş haneli değil ALTI haneli başlaması bilinçli:
    /// kısa numaralar "anlamlı kod" izlenimi veriyor ve kullanıcılar
    /// ezberlemeye çalışıyor.
    /// </summary>
    private const long StartAt = 100_000;

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

        const string sql = """
            INSERT INTO document_number_sequences
                ("Id", "CompanyId", "DocumentType", "Prefix", "Year",
                 "LastNumber", "NumberLength", "IsActive", "IsDeleted", "CreatedAtUtc")
            VALUES
                (gen_random_uuid(), @companyId, @documentType, '', 0,
                 @startAt + 1, 6, true, false, now() AT TIME ZONE 'utc')
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
            command.Parameters.Add(new NpgsqlParameter("startAt", StartAt));

            var result = await command.ExecuteScalarAsync(cancellationToken);

            var next = Convert.ToInt64(result);

            return next.ToString();
        }
        finally
        {
            if (openedHere) await connection.CloseAsync();
        }
    }
}
