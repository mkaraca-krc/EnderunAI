using EnderunAI.Api.Models;
using EnderunAI.Api.Services.Isg;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// Taşeron evrakının geçerlilik kuralları.
///
/// Asıl güvence SGK BORCU YOKTUR yazısının üç aylık kanuni süresi:
/// kullanıcı bitiş tarihi girmese bile belge "süresiz" görünmemeli.
/// Görünseydi asıl işveren, süresi çoktan dolmuş bir yazıya güvenerek
/// müteselsil sorumluluk altında kalırdı.
/// </summary>
public sealed class SubcontractorDocumentTests
{
    private static SubcontractorDocument Build(
        SubcontractorDocumentType type,
        DateOnly issueDate,
        DateOnly? validUntil = null) =>
        new()
        {
            DocumentType = type,
            IssueDate = issueDate,
            ValidUntil = validUntil,
            Title = "Test"
        };

    [Fact]
    public void SocialSecurityClearance_ExpiresThreeMonthsAfterIssueWhenNoEndGiven()
    {
        var document = Build(
            SubcontractorDocumentType.SocialSecurityClearance,
            new DateOnly(2026, 3, 15));

        Assert.Equal(new DateOnly(2026, 6, 15), document.EffectiveValidUntil);
    }

    /// <summary>
    /// Elle girilen bitiş tarihi kanuni süreyi EZER: kurum bazen daha
    /// kısa süreli yazı verir ve o zaman kısası geçerlidir.
    /// </summary>
    [Fact]
    public void SocialSecurityClearance_RespectsExplicitEndDate()
    {
        var document = Build(
            SubcontractorDocumentType.SocialSecurityClearance,
            new DateOnly(2026, 3, 15),
            validUntil: new DateOnly(2026, 4, 30));

        Assert.Equal(new DateOnly(2026, 4, 30), document.EffectiveValidUntil);
    }

    /// <summary>
    /// Diğer belge türlerinde bitiş tarihi yoksa süresiz sayılır —
    /// uydurma bir son kullanma tarihi türetilmez.
    /// </summary>
    [Theory]
    [InlineData(SubcontractorDocumentType.Contract)]
    [InlineData(SubcontractorDocumentType.SignatureCircular)]
    [InlineData(SubcontractorDocumentType.TaxCertificate)]
    [InlineData(SubcontractorDocumentType.Other)]
    public void OtherTypes_StayOpenEndedWhenNoEndGiven(
        SubcontractorDocumentType type)
    {
        var document = Build(type, new DateOnly(2026, 3, 15));

        Assert.Null(document.EffectiveValidUntil);
    }

    /// <summary>
    /// Üç ayı geçmiş bir SGK yazısı, bitiş tarihi girilmemiş olsa bile
    /// SÜRESİ DOLMUŞ sayılır. Bu testin asıl işi: birisi ileride
    /// EffectiveValidUntil'i kaldırırsa belge sessizce "süresiz"e
    /// dönmesin.
    /// </summary>
    [Fact]
    public void SocialSecurityClearance_IsExpiredAfterThreeMonths()
    {
        var document = Build(
            SubcontractorDocumentType.SocialSecurityClearance,
            new DateOnly(2026, 1, 10));

        var status = IsgValidityCalculator.Evaluate(
            document.EffectiveValidUntil, new DateOnly(2026, 5, 1));

        Assert.Equal(IsgValidityStatus.Expired, status);
    }

    /// <summary>
    /// Yenileme uyarısı İSG belgeleriyle aynı eşikten (30 gün) geçer;
    /// kural iki yere kopyalanmadı.
    /// </summary>
    [Fact]
    public void SocialSecurityClearance_WarnsWithinTheSharedThreshold()
    {
        var document = Build(
            SubcontractorDocumentType.SocialSecurityClearance,
            new DateOnly(2026, 1, 10));

        // Bitiş 10 Nisan; 20 Mart'ta 21 gün kalmış.
        var status = IsgValidityCalculator.Evaluate(
            document.EffectiveValidUntil, new DateOnly(2026, 3, 20));

        Assert.Equal(IsgValidityStatus.ExpiringSoon, status);
        Assert.Equal(21, IsgValidityCalculator.DaysRemaining(
            document.EffectiveValidUntil, new DateOnly(2026, 3, 20)));
    }

    /// <summary>
    /// Bitiş günü DAHİL geçerlidir: 10 Nisan'da biten yazı 10 Nisan'da
    /// hâlâ geçerlidir.
    /// </summary>
    [Fact]
    public void ValidityIncludesTheExpiryDayItself()
    {
        var document = Build(
            SubcontractorDocumentType.SocialSecurityClearance,
            new DateOnly(2026, 1, 10));

        var status = IsgValidityCalculator.Evaluate(
            document.EffectiveValidUntil, new DateOnly(2026, 4, 10));

        Assert.NotEqual(IsgValidityStatus.Expired, status);
    }
}
