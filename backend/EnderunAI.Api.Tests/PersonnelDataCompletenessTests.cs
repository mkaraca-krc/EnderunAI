using EnderunAI.Api.Services.HumanResources;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// Personel kartı veri bütünlüğü (H1).
///
/// Korunan fikirler:
/// - Eksiklik kaydı ENGELLEMEZ, uyarır. Canlıda aktif personelin
///   yarısından fazlasında SGK sicil yok; zorunluluk konsaydı bu
///   kayıtların telefonu bile güncellenemezdi.
/// - Eksikler AĞIRLIĞA göre ayrılır. "Telefon yok" ile "SGK sicil yok"
///   aynı listede aynı görünürse ikisi de gürültüye dönüşür.
/// - Kartı olmayan personel bordroya giremez; bu ayrı ve daha sert bir
///   kademedir.
/// </summary>
public sealed class PersonnelDataCompletenessTests
{
    private static readonly Guid Id = Guid.Parse("11111111-0000-0000-0000-000000000001");

    /// <summary>Algoritmaya uyan örnek kimlik numarası.</summary>
    private const string ValidIdentity = "12345678950";

    private static PersonnelDataInput Complete(
        string? identity = ValidIdentity,
        DateTime? birthDate = null,
        string? phone = "5321234567",
        string? sgk = "1234567890123",
        DateTime? employmentStart = null,
        string? jobTitle = "Elektrik Teknisyeni",
        Guid? branchId = null,
        int workLocationType = 1,
        bool hasActiveSiteAssignment = false,
        bool hasSalaryCard = true) =>
        new(
            Id: Id,
            EmployeeNumber: "PRS-001",
            FullName: "Ali Veli",
            IdentityNumber: identity,
            BirthDate: birthDate ?? new DateTime(1990, 5, 1),
            Phone: phone,
            SgkRegistrationNumber: sgk,
            EmploymentStartDate: employmentStart ?? new DateTime(2020, 1, 15),
            JobTitle: jobTitle,
            BranchId: branchId ?? Guid.Parse("22222222-0000-0000-0000-000000000002"),
            WorkLocationType: workLocationType,
            HasActiveSiteAssignment: hasActiveSiteAssignment,
            HasSalaryCard: hasSalaryCard);

    // ---------- Tam kayıt ----------

    [Fact]
    public void CompleteRecord_HasNoIssues()
    {
        var result = PersonnelDataCompletenessCalculator.Evaluate(Complete());

        Assert.Empty(result.Issues);
        Assert.True(result.PayrollReady);
        Assert.True(result.OfficialReady);
        Assert.Equal(100m, result.CompletionRate);
    }

    // ---------- Bordro engeli ----------

    /// <summary>
    /// Ücret kartı yoksa personel bordroya HİÇ giremez; bu en sert
    /// kademe.
    /// </summary>
    [Fact]
    public void MissingSalaryCard_BlocksPayroll()
    {
        var result = PersonnelDataCompletenessCalculator.Evaluate(
            Complete(hasSalaryCard: false));

        var issue = Assert.Single(result.Issues);

        Assert.Equal("salaryCard", issue.Field);
        Assert.Equal(PersonnelDataSeverity.PayrollBlocking, issue.Severity);
        Assert.Equal("Bordroya giremez", issue.SeverityName);
        Assert.False(result.PayrollReady);
        Assert.False(result.OfficialReady);
    }

    // ---------- Resmî bildirim engeli ----------

    [Theory]
    [InlineData("identityNumber")]
    [InlineData("sgkRegistrationNumber")]
    [InlineData("birthDate")]
    [InlineData("employmentStartDate")]
    public void OfficialFields_BlockNotificationButNotPayroll(string field)
    {
        var input = field switch
        {
            "identityNumber" => Complete(identity: null),
            "sgkRegistrationNumber" => Complete(sgk: null),
            "birthDate" => Complete() with { BirthDate = null },
            _ => Complete() with { EmploymentStartDate = null }
        };

        var result = PersonnelDataCompletenessCalculator.Evaluate(input);
        var issue = Assert.Single(result.Issues);

        Assert.Equal(field, issue.Field);
        Assert.Equal(PersonnelDataSeverity.OfficialBlocking, issue.Severity);

        // Bordro yine üretilebilir; engellenen resmî bildirim.
        Assert.True(result.PayrollReady);
        Assert.False(result.OfficialReady);
    }

    /// <summary>
    /// Kayıtlı ama YANLIŞ kimlik numarası, boş olmasından farklı bir
    /// sorundur: numara var gibi görünür, bildirim reddedilir.
    /// </summary>
    [Fact]
    public void StoredInvalidIdentity_IsReportedSeparately()
    {
        var result = PersonnelDataCompletenessCalculator.Evaluate(
            Complete(identity: "11111111111"));

        var issue = Assert.Single(result.Issues);

        Assert.Equal("identityNumber", issue.Field);
        Assert.Contains("doğrulama algoritmasına uymuyor", issue.Reason);
    }

    // ---------- Operasyonel eksik ----------

    [Theory]
    [InlineData("phone")]
    [InlineData("jobTitle")]
    [InlineData("branchId")]
    public void OperationalFields_BlockNothing(string field)
    {
        var input = field switch
        {
            "phone" => Complete(phone: null),
            "jobTitle" => Complete(jobTitle: null),
            _ => Complete() with { BranchId = null }
        };

        var result = PersonnelDataCompletenessCalculator.Evaluate(input);

        Assert.Single(result.Issues);
        Assert.Equal(PersonnelDataSeverity.Operational, result.Issues[0].Severity);
        Assert.True(result.PayrollReady);
        Assert.True(result.OfficialReady);
    }

    [Fact]
    public void UnassignedWorkLocation_IsAnOperationalIssue()
    {
        var result = PersonnelDataCompletenessCalculator.Evaluate(
            Complete(workLocationType: 0));

        Assert.Equal("workLocation", Assert.Single(result.Issues).Field);
    }

    /// <summary>
    /// Görev yeri şantiye seçilmiş ama açık ataması yoksa kayıt yarım
    /// kalmıştır — seçim yapılmış, karşılığı girilmemiş.
    /// </summary>
    [Fact]
    public void SiteWorkLocationWithoutAssignment_IsReported()
    {
        var result = PersonnelDataCompletenessCalculator.Evaluate(
            Complete(workLocationType: 2, hasActiveSiteAssignment: false));

        Assert.Contains("açık bir şantiye ataması yok",
            Assert.Single(result.Issues).Reason);
    }

    [Fact]
    public void SiteWorkLocationWithAssignment_IsFine()
    {
        var result = PersonnelDataCompletenessCalculator.Evaluate(
            Complete(workLocationType: 2, hasActiveSiteAssignment: true));

        Assert.Empty(result.Issues);
    }

    // ---------- Sıralama ve oran ----------

    /// <summary>Ağır olan üstte: kullanıcı önce yanmakta olanı görmeli.</summary>
    [Fact]
    public void Issues_AreOrderedBySeverity()
    {
        var result = PersonnelDataCompletenessCalculator.Evaluate(
            Complete(phone: null, sgk: null, hasSalaryCard: false));

        Assert.Equal(
            new[]
            {
                PersonnelDataSeverity.PayrollBlocking,
                PersonnelDataSeverity.OfficialBlocking,
                PersonnelDataSeverity.Operational
            },
            result.Issues.Select(x => x.Severity));
    }

    [Fact]
    public void CompletionRate_FallsWithEachMissingField()
    {
        var one = PersonnelDataCompletenessCalculator.Evaluate(Complete(phone: null));
        var two = PersonnelDataCompletenessCalculator.Evaluate(
            Complete(phone: null, jobTitle: null));

        Assert.True(one.CompletionRate < 100m);
        Assert.True(two.CompletionRate < one.CompletionRate);
    }

    [Fact]
    public void EmptyRecord_HasZeroCompletion()
    {
        var result = PersonnelDataCompletenessCalculator.Evaluate(
            Complete(
                identity: null, phone: null, sgk: null, jobTitle: null,
                workLocationType: 0, hasSalaryCard: false)
            with
            { BirthDate = null, EmploymentStartDate = null, BranchId = null });

        Assert.Equal(9, result.Issues.Count);
        Assert.Equal(0m, result.CompletionRate);
    }

    // ---------- Özet ----------

    [Fact]
    public void Summary_CountsReadinessSeparately()
    {
        var summary = PersonnelDataCompletenessCalculator.Summarize(
        [
            Complete(),
            Complete(sgk: null) with { Id = Guid.NewGuid() },
            Complete(hasSalaryCard: false) with { Id = Guid.NewGuid() }
        ]);

        Assert.Equal(3, summary.Total);
        Assert.Equal(2, summary.PayrollReadyCount);
        Assert.Equal(1, summary.OfficialReadyCount);
        Assert.Equal(1, summary.CompleteCount);
    }

    /// <summary>
    /// Alan başına sayım, hangi alanın toplu tamamlamaya değdiğini
    /// gösteriyor.
    /// </summary>
    [Fact]
    public void Summary_CountsMissingFields()
    {
        var summary = PersonnelDataCompletenessCalculator.Summarize(
        [
            Complete(phone: null),
            Complete(phone: null) with { Id = Guid.NewGuid() },
            Complete(sgk: null) with { Id = Guid.NewGuid() }
        ]);

        Assert.Equal(2, summary.ByField["phone"]);
        Assert.Equal(1, summary.ByField["sgkRegistrationNumber"]);
        Assert.False(summary.ByField.ContainsKey("jobTitle"));
    }

    /// <summary>En eksik kayıt başta: toplu tamamlama listesi böyle işe yarar.</summary>
    [Fact]
    public void Summary_ListsTheMostIncompleteFirst()
    {
        var summary = PersonnelDataCompletenessCalculator.Summarize(
        [
            Complete(),
            Complete(phone: null, sgk: null, jobTitle: null)
                with { Id = Guid.NewGuid(), FullName = "Eksik Kayıt" }
        ]);

        Assert.Equal("Eksik Kayıt", summary.Items[0].FullName);
    }

    [Fact]
    public void Summary_OfNobody_IsEmptyNotBroken()
    {
        var summary = PersonnelDataCompletenessCalculator.Summarize([]);

        Assert.Equal(0, summary.Total);
        Assert.Empty(summary.Items);
        Assert.Empty(summary.ByField);
    }
}
