using System.Text;

namespace EnderunAI.Api.Services.EInvoice;

public interface IEInvoiceArchive
{
    /// <summary>
    /// Orijinal XML'i saklar ve göreli yolunu döner. Yol faturaya
    /// yazılır; denetimde belgenin aslına buradan ulaşılır.
    /// </summary>
    Task<string> SaveAsync(string fileName, string xml, CancellationToken cancellationToken);

    /// <summary>Saklanan XML'i okur; dosya yoksa null.</summary>
    Task<string?> ReadAsync(string relativePath, CancellationToken cancellationToken);
}

/// <summary>
/// İçe aktarılan e-fatura XML'lerinin arşivi.
///
/// Belge aslı hiçbir zaman silinmez ve değiştirilmez: sistemdeki
/// tutarlar tartışmaya düşerse kaynağa dönebilmek gerekir. Dosyalar
/// yıl/ay klasörlerine ayrılır, adları çakışmasın diye benzersiz bir
/// önek alır.
/// </summary>
public sealed class EInvoiceArchive : IEInvoiceArchive
{
    /// <summary>
    /// Yüklenen belgelerle aynı kök: yedekleme betiği burayı zaten
    /// yedekliyor, fatura asılları da kapsama girsin.
    /// </summary>
    private const string DefaultRoot = "/var/www/enderun-ai/uploads/e-fatura";

    private readonly string root;

    public EInvoiceArchive(IConfiguration configuration)
    {
        var configured = configuration["EInvoice:ArchivePath"];

        root = string.IsNullOrWhiteSpace(configured) ? DefaultRoot : configured;
    }

    public async Task<string> SaveAsync(
        string fileName, string xml, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var folder = Path.Combine(now.Year.ToString("D4"), now.Month.ToString("D2"));

        Directory.CreateDirectory(Path.Combine(root, folder));

        var safeName = Sanitize(fileName);
        var relativePath = Path.Combine(
            folder, $"{Guid.NewGuid():N}-{safeName}");

        await File.WriteAllTextAsync(
            Path.Combine(root, relativePath), xml, Encoding.UTF8, cancellationToken);

        return relativePath.Replace('\\', '/');
    }

    public async Task<string?> ReadAsync(
        string relativePath, CancellationToken cancellationToken)
    {
        var full = Path.GetFullPath(Path.Combine(root, relativePath));

        // Yol kaçışı (../) ile arşiv dışına çıkılmasın.
        if (!full.StartsWith(Path.GetFullPath(root), StringComparison.Ordinal))
            return null;

        return File.Exists(full)
            ? await File.ReadAllTextAsync(full, Encoding.UTF8, cancellationToken)
            : null;
    }

    /// <summary>
    /// ZIP içindeki dosya adları klasör içerir ve rastgele karakterler
    /// taşıyabilir; dosya adı olarak güvenli hâle getirilir.
    /// </summary>
    private static string Sanitize(string fileName)
    {
        var name = Path.GetFileName(fileName);

        if (string.IsNullOrWhiteSpace(name))
            name = "fatura.xml";

        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name
            .Select(c => invalid.Contains(c) ? '_' : c)
            .ToArray());

        return cleaned.Length > 120 ? cleaned[^120..] : cleaned;
    }
}
