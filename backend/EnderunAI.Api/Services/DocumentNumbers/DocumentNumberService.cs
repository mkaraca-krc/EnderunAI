using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace EnderunAI.Api.Services.DocumentNumbers;

public sealed class DocumentNumberService(AppDbContext db)
    : IDocumentNumberService
{
    public async Task<string> GenerateAsync(
        Guid companyId,
        string documentType,
        string prefix,
        CancellationToken cancellationToken = default)
    {
        if (companyId == Guid.Empty)
            throw new ArgumentException("Şirket bilgisi zorunludur.", nameof(companyId));

        if (string.IsNullOrWhiteSpace(documentType))
            throw new ArgumentException("Belge tipi zorunludur.", nameof(documentType));

        if (string.IsNullOrWhiteSpace(prefix))
            throw new ArgumentException("Belge ön eki zorunludur.", nameof(prefix));

        var normalizedType = documentType.Trim().ToUpperInvariant();
        var normalizedPrefix = prefix.Trim().ToUpperInvariant();
        var year = DateTime.UtcNow.Year;

        /*
         * AYRI TRANSACTION AÇILMIYOR — GEREKMİYOR.
         *
         * Eskiden burada bir transaction açılıyordu çünkü işlem
         * "oku, yoksa ekle, kaydet" üç adımdı. Artık tek ifade
         * (aşağıdaki upsert) ve tek ifade zaten atomiktir.
         *
         * Çağıran bir transaction açmışsa ona KATILIYORUZ (komuta
         * atanıyor); açmamışsa ifade kendi başına çalışıyor. Kendi
         * transaction'ımızı açıp commit etmeyi unutmak, bu servisin
         * yaptığı her şeyi geri saran bir hataya yol açardı.
         */

        /*
         * ATOMİK ARTIRIM — OKU-SONRA-YAZ YARIŞI KAPATILDI.
         *
         * Önce satır okunuyor, yoksa ekleniyordu. İki eşzamanlı istek
         * ikisi de "yok" görüp ikisi de ekliyor ve biri tekil kısıta
         * takılıp HAM 500 döndürüyordu. Çek paketinin eşzamanlılık
         * testi bunu yakaladı; kusur çeke özel değil — bu servis bütün
         * modüllerin belge numarasını üretiyor.
         *
         * `ON CONFLICT ... DO UPDATE ... RETURNING` tek ifadede hem
         * ekliyor hem artırıyor; iki istek sıraya giriyor ve ikisi de
         * farklı numara alıyor. Kilit ya da yeniden deneme gerekmiyor.
         *
         * DIŞ TRANSACTION'A DA UYUYOR: ifade tek olduğu için katıldığı
         * transaction'ı bozmuyor.
         */
        var connection = db.Database.GetDbConnection();

        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();

        if (db.Database.CurrentTransaction is not null)
            command.Transaction = db.Database.CurrentTransaction.GetDbTransaction();

        command.CommandText = """
            INSERT INTO document_number_sequences
                ("Id", "CompanyId", "DocumentType", "Prefix", "Year",
                 "LastNumber", "NumberLength", "IsActive", "IsDeleted", "CreatedAtUtc")
            VALUES
                (gen_random_uuid(), @companyId, @documentType, @prefix, @year,
                 1, 6, true, false, now())
            ON CONFLICT ("CompanyId", "DocumentType", "Year")
            DO UPDATE SET
                "LastNumber" = document_number_sequences."LastNumber" + 1,
                "Prefix" = EXCLUDED."Prefix",
                "UpdatedAtUtc" = now()
            RETURNING "Prefix", "Year", "LastNumber", "NumberLength";
            """;

        AddParameter(command, "companyId", companyId);
        AddParameter(command, "documentType", normalizedType);
        AddParameter(command, "prefix", normalizedPrefix);
        AddParameter(command, "year", year);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException("Belge numarası üretilemedi.");

        var resultPrefix = reader.GetString(0);
        var resultYear = reader.GetInt32(1);
        var resultNumber = reader.GetInt32(2);
        var resultLength = reader.GetInt32(3);

        return $"{resultPrefix}-{resultYear}-{resultNumber.ToString().PadLeft(resultLength, '0')}";
    }

    private static void AddParameter(System.Data.Common.DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
