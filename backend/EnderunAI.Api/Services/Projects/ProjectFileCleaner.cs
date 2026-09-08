namespace EnderunAI.Api.Services.Projects;

public interface IProjectFileCleaner
{
    /// <summary>
    /// Projenin yüklenmiş dosyalarını diskten kaldırır. Proje dosyaları
    /// tek bir proje klasörü altında tutulduğu için klasör tümüyle silinir.
    /// Dönüş: silinecek bir şey bulunup silindiyse true.
    /// </summary>
    bool DeleteProjectFiles(Guid projectId);
}

/// <summary>
/// Proje dosyalarının fiziksel temizliği. Yol
/// <c>ProjectDocumentsController.StorageRoot</c> ile aynı köke bakar;
/// proje dosyaları UploadService'ten geçmediği için ayrı bir temizleyici
/// gerekiyor.
///
/// Silme, veritabanı işlemi başarıyla tamamlandıktan sonra çağrılır:
/// dosya silme geri alınamaz, transaction geri alınabilir.
/// </summary>
public sealed class ProjectFileCleaner(
    IConfiguration configuration,
    ILogger<ProjectFileCleaner> logger) : IProjectFileCleaner
{
    // VARSAYILAN KÖK BURADA YAZMIYOR (SIZINTI/1 · Kural 79).
    //
    // Bu sınıf kökün İKİNCİ OKUYUCUSUYDU ve kendi varsayılanını
    // taşıyordu. Yazıcı (ProjectDocumentsController) ile silici
    // arasında en tehlikeli ayrışma budur: biri bir dizine yazarken
    // öteki başka bir dizinde siler.
    //
    // Fail-closed kapı silme yolunda AYRICA doğru: test
    // veritabanına bağlı bir süreç, canlı proje dosyalarını
    // silmeye hiç kalkışmasın.
    public bool DeleteProjectFiles(Guid projectId)
    {
        var root = Upload.YazmaKokleri.ProjeDosyalari(configuration);
        var projectFolder = Path.Combine(root, projectId.ToString());

        // Kök dizinin kendisinin silinmesine hiçbir koşulda izin verilmez.
        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var normalizedFolder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(projectFolder));

        if (normalizedFolder == normalizedRoot ||
            !normalizedFolder.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            logger.LogWarning(
                "Proje dosya klasörü kök dizinin dışında hesaplandı, silme atlandı: {Folder}",
                normalizedFolder);
            return false;
        }

        if (!Directory.Exists(normalizedFolder))
            return false;

        try
        {
            Directory.Delete(normalizedFolder, recursive: true);
            return true;
        }
        catch (Exception ex)
        {
            // Veritabanı kaydı çoktan silindi; dosya artığı kalması silmeyi
            // başarısız saymamızı gerektirmez, ama iz bırakmalı.
            logger.LogError(
                ex, "Proje dosyaları silinemedi: {ProjectId} ({Folder})", projectId, normalizedFolder);
            return false;
        }
    }
}
