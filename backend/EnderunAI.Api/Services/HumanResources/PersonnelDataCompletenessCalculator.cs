namespace EnderunAI.Api.Services.HumanResources;

/// <summary>
/// Eksik alanın hangi süreci engellediği.
///
/// Sıralama bilinçli: "telefon yok" ile "SGK sicil yok" aynı listede
/// aynı ağırlıkta görünürse ikisi de gürültüye dönüşür. Biri
/// rahatsızlık, öteki resmî bildirimi imkânsız kılıyor.
/// </summary>
public enum PersonnelDataSeverity
{
    /// <summary>Bu personel bordroya giremez.</summary>
    PayrollBlocking = 0,

    /// <summary>Bordro üretilir ama resmî bildirim yapılamaz.</summary>
    OfficialBlocking = 1,

    /// <summary>Süreci durdurmaz; kayıt eksiktir.</summary>
    Operational = 2
}

/// <param name="HasSalaryCard">Dönem sonunda yürürlükte bir ücret kartı
/// var mı.</param>
/// <param name="HasActiveSiteAssignment">Açık şantiye ataması var mı —
/// görev yeri "şantiye" seçilmişse aranır.</param>
public sealed record PersonnelDataInput(
    Guid Id,
    string EmployeeNumber,
    string FullName,
    string? IdentityNumber,
    DateTime? BirthDate,
    string? Phone,
    string? SgkRegistrationNumber,
    DateTime? EmploymentStartDate,
    string? JobTitle,
    Guid? BranchId,
    int WorkLocationType,
    bool HasActiveSiteAssignment,
    bool HasSalaryCard);

public sealed record PersonnelDataIssue(
    string Field,
    string Label,
    PersonnelDataSeverity Severity,
    string SeverityName,
    string Reason);

/// <param name="CompletionRate">Bakılan alanların yüzde kaçı dolu.</param>
public sealed record PersonnelDataCompleteness(
    Guid PersonnelId,
    string EmployeeNumber,
    string FullName,
    IReadOnlyList<PersonnelDataIssue> Issues,
    bool PayrollReady,
    bool OfficialReady,
    decimal CompletionRate);

/// <param name="ByField">Alan başına eksik personel sayısı — hangi
/// alanın toplu tamamlamaya değdiğini gösterir.</param>
public sealed record PersonnelDataCompletenessSummary(
    int Total,
    int PayrollReadyCount,
    int OfficialReadyCount,
    int CompleteCount,
    IReadOnlyDictionary<string, int> ByField,
    IReadOnlyList<PersonnelDataCompleteness> Items);

/// <summary>
/// Personel kartı veri bütünlüğü.
///
/// Saf ve veritabanısız.
///
/// KARAR: eksik alan kaydı ENGELLEMEZ, uyarır. Canlıda 79 aktif
/// personelin 50'sinde SGK sicil yok; zorunlu yapılsaydı bu kayıtların
/// telefonunu güncellemek bile imkânsız hale gelirdi. Engelleme yerine
/// eksikliğin NEYE mal olduğunu söylüyoruz.
///
/// Tek istisna T.C. kimlik numarası: boş bırakılabilir ama YANLIŞ
/// girilemez (bkz. <see cref="TurkishIdentityNumber"/>). Boş alan
/// eksiktir; yanlış alan sessiz bir hatadır.
/// </summary>
public static class PersonnelDataCompletenessCalculator
{
    /// <summary>Görev yeri: atanmadı.</summary>
    private const int WorkLocationUnassigned = 0;

    /// <summary>Görev yeri: şantiye.</summary>
    private const int WorkLocationSite = 2;

    public static PersonnelDataCompleteness Evaluate(PersonnelDataInput input)
    {
        var issues = new List<PersonnelDataIssue>();

        if (!input.HasSalaryCard)
        {
            issues.Add(Issue(
                "salaryCard", "Ücret kartı", PersonnelDataSeverity.PayrollBlocking,
                "Ücret kartı olmayan personel bordroya giremez."));
        }

        if (string.IsNullOrWhiteSpace(input.IdentityNumber))
        {
            issues.Add(Issue(
                "identityNumber", "T.C. kimlik no",
                PersonnelDataSeverity.OfficialBlocking,
                "SGK bildirimi kimlik numarası olmadan yapılamaz."));
        }
        else if (!TurkishIdentityNumber.IsValid(input.IdentityNumber))
        {
            issues.Add(Issue(
                "identityNumber", "T.C. kimlik no",
                PersonnelDataSeverity.OfficialBlocking,
                "Kayıtlı kimlik numarası doğrulama algoritmasına uymuyor; " +
                "bu numarayla yapılan bildirim reddedilir."));
        }

        if (string.IsNullOrWhiteSpace(input.SgkRegistrationNumber))
        {
            issues.Add(Issue(
                "sgkRegistrationNumber", "SGK sicil no",
                PersonnelDataSeverity.OfficialBlocking,
                "SGK bildirimi ve prim tahakkuku sicil numarasına bağlı."));
        }

        if (input.BirthDate is null)
        {
            issues.Add(Issue(
                "birthDate", "Doğum tarihi",
                PersonnelDataSeverity.OfficialBlocking,
                "Emeklilik ve genç/yaşlı çalışan istisnaları doğum tarihine bağlı."));
        }

        if (input.EmploymentStartDate is null)
        {
            issues.Add(Issue(
                "employmentStartDate", "İşe giriş tarihi",
                PersonnelDataSeverity.OfficialBlocking,
                "Kıdem, yıllık izin hak edişi ve SGK giriş bildirimi " +
                "işe giriş tarihine bağlı."));
        }

        if (string.IsNullOrWhiteSpace(input.Phone))
        {
            issues.Add(Issue(
                "phone", "Telefon", PersonnelDataSeverity.Operational,
                "Sahayla iletişim ve acil durum kaydı için gerekli."));
        }

        if (string.IsNullOrWhiteSpace(input.JobTitle))
        {
            issues.Add(Issue(
                "jobTitle", "Ünvan", PersonnelDataSeverity.Operational,
                "Ünvansız personel organizasyon ve maliyet raporlarında " +
                "sınıflandırılamıyor."));
        }

        if (input.BranchId is null)
        {
            issues.Add(Issue(
                "branchId", "Şube", PersonnelDataSeverity.Operational,
                "Şube, veri kapsamı ve maliyet dağıtımında kullanılıyor."));
        }

        if (input.WorkLocationType == WorkLocationUnassigned)
        {
            issues.Add(Issue(
                "workLocation", "Görev yeri", PersonnelDataSeverity.Operational,
                "Görev yeri atanmamış: merkez mi şantiye mi belli değil."));
        }
        else if (input.WorkLocationType == WorkLocationSite &&
                 !input.HasActiveSiteAssignment)
        {
            issues.Add(Issue(
                "workLocation", "Görev yeri", PersonnelDataSeverity.Operational,
                "Görev yeri şantiye seçilmiş ama açık bir şantiye ataması yok."));
        }

        // Toplam bakılan alan sayısı sabit: oran karşılaştırılabilir
        // olsun diye eksik sayısından değil, kontrol sayısından üretiliyor.
        const int checkedFields = 9;

        var rate = decimal.Round(
            (decimal)(checkedFields - issues.Count) / checkedFields * 100m, 1);

        return new PersonnelDataCompleteness(
            PersonnelId: input.Id,
            EmployeeNumber: input.EmployeeNumber,
            FullName: input.FullName,
            Issues: issues
                .OrderBy(x => (int)x.Severity)
                .ThenBy(x => x.Label, StringComparer.CurrentCulture)
                .ToList(),
            PayrollReady: issues.All(
                x => x.Severity != PersonnelDataSeverity.PayrollBlocking),
            OfficialReady: issues.All(
                x => x.Severity == PersonnelDataSeverity.Operational),
            CompletionRate: rate < 0m ? 0m : rate);
    }

    public static PersonnelDataCompletenessSummary Summarize(
        IReadOnlyCollection<PersonnelDataInput> personnel)
    {
        var items = personnel.Select(Evaluate).ToList();

        var byField = items
            .SelectMany(x => x.Issues)
            .GroupBy(x => x.Field)
            .ToDictionary(g => g.Key, g => g.Count());

        return new PersonnelDataCompletenessSummary(
            Total: items.Count,
            PayrollReadyCount: items.Count(x => x.PayrollReady),
            OfficialReadyCount: items.Count(x => x.OfficialReady),
            CompleteCount: items.Count(x => x.Issues.Count == 0),
            ByField: byField,
            // En eksik olan başta: toplu tamamlama listesi bu sırayla
            // işe yarar.
            Items: items
                .OrderBy(x => x.CompletionRate)
                .ThenBy(x => x.FullName, StringComparer.CurrentCulture)
                .ToList());
    }

    public static string SeverityName(PersonnelDataSeverity severity) => severity switch
    {
        PersonnelDataSeverity.PayrollBlocking => "Bordroya giremez",
        PersonnelDataSeverity.OfficialBlocking => "Resmî bildirim engeli",
        _ => "Eksik bilgi"
    };

    private static PersonnelDataIssue Issue(
        string field, string label, PersonnelDataSeverity severity, string reason) =>
        new(field, label, severity, SeverityName(severity), reason);
}
