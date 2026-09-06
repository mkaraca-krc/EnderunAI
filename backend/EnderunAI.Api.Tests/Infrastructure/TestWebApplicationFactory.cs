using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;

namespace EnderunAI.Api.Tests.Infrastructure;

public sealed class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    public const string TestJwtSecret = "test-only-jwt-secret-never-used-in-production-0123456789";
    public const string TestDatabaseName = "enderun_ai_test";

    public static readonly string TestConnectionString = ResolveTestConnectionString();

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:DefaultConnection", TestConnectionString);
        builder.UseSetting("Jwt:Secret", TestJwtSecret);
        builder.UseSetting("MigrationRecovery:AllowAutomaticDatabaseUpdate", "true");
        builder.UseSetting("Logging:LogLevel:Default", "Warning");
        builder.UseSetting("Logging:LogLevel:Microsoft.EntityFrameworkCore", "Warning");

        Environment.SetEnvironmentVariable("SEED_ADMIN_USERNAME", "test.admin");
        Environment.SetEnvironmentVariable("SEED_ADMIN_PASSWORD", "TestAdmin!2026Secure");
        Environment.SetEnvironmentVariable("SEED_ADMIN_FULLNAME", "Test Admin");
    }

    /// <summary>
    /// Test veritabanını (varsa) düşürür. Program.cs'in kendi başlangıç mantığı
    /// (MigrationRecovery:AllowAutomaticDatabaseUpdate=true) host ilk kez ayağa
    /// kalktığında veritabanını sıfırdan oluşturup migrate edip seed'leyecek —
    /// bu yüzden burada sadece DROP yapılır, host henüz build edilmeden,
    /// ham bir ADO.NET bağlantısıyla 'postgres' maintenance veritabanı üzerinden.
    /// Canlı 'enderun_ai' veritabanına bu metot hiçbir şekilde dokunmaz —
    /// bağlantı adı sabit olarak 'enderun_ai_test'tir.
    /// </summary>
    public static async Task DropTestDatabaseAsync()
    {
        var builder = new NpgsqlConnectionStringBuilder(TestConnectionString)
        {
            Database = "postgres"
        };

        await using var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = $"DROP DATABASE IF EXISTS {TestDatabaseName} WITH (FORCE);";
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// KURAN KAPATIR — TEST VERİTABANI PUBLIC'E AÇIK KALMASIN.
    ///
    /// PostgreSQL'de yeni bir veritabanı ACL'i `NULL` olarak doğar ve
    /// `NULL` "yerleşik varsayılan" demektir: PUBLIC'e CONNECT + TEMP.
    /// Bunu değiştiren bir ayar YOK — ölçüldü (2026-09-06):
    /// `template1`'den PUBLIC CONNECT'i geri almak yeni veritabanını
    /// KORUMUYOR, çünkü şablonun ACL'i kopyalanmıyor.
    ///
    /// NEDEN BURADA: bu fixture her koşuda test veritabanını DÜŞÜRÜP
    /// yeniden kurduruyor. Elle yapılan bir kapatma bir sonraki koşuda
    /// kayboluyor — nitekim kayboldu ve `acik-veritabani-kapisi.sh`
    /// bunu yayından hemen sonra yakaladı. Kapatmayı KURAN yola koymak,
    /// tek kalıcı çözüm.
    ///
    /// SESSİZCE GEÇMEZ: kapatılamazsa istisna yükselir. Açık kalmış bir
    /// veritabanı, "kapattım sanıyordum"dan iyidir.
    /// </summary>
    public static async Task TestVeritabaniniPubliceKapatAsync()
    {
        var builder = new NpgsqlConnectionStringBuilder(TestConnectionString)
        {
            Database = "postgres"
        };

        await using var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText =
            $"REVOKE CONNECT ON DATABASE {TestDatabaseName} FROM PUBLIC;";
        await command.ExecuteNonQueryAsync();
    }

    private static string ResolveTestConnectionString()
    {
        var explicitValue = Environment.GetEnvironmentVariable("TEST_DB_CONNECTION");
        if (!string.IsNullOrWhiteSpace(explicitValue))
            return explicitValue;

        var liveConnection = Environment.GetEnvironmentVariable("DB_CONNECTION");
        if (!string.IsNullOrWhiteSpace(liveConnection))
        {
            return liveConnection.Replace(
                "Database=enderun_ai;",
                $"Database={TestDatabaseName};",
                StringComparison.OrdinalIgnoreCase);
        }

        throw new InvalidOperationException(
            "Test veritabanı bağlantısı bulunamadı. TEST_DB_CONNECTION veya DB_CONNECTION " +
            "ortam değişkenlerinden biri tanımlı olmalı. Canlı veritabanı ASLA kullanılmaz — " +
            "bağlantı adı ayrıca 'enderun_ai_test' olarak sabitlenir.");
    }
}
