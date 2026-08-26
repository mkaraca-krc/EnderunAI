using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// ÖDENEN ÇEK VARSAYILAN LİSTEDE VE TOPLAMDA DURMAZ (ÇEK/1).
///
/// ŞİKAYET: bir çek "Ödendi" göründüğü hâlde o ayın toplam çek
/// tutarından düşmüyor ve listede kalmaya devam ediyordu.
///
/// ÖLÇÜM KÖK NEDENİ GÖSTERDİ: durum veritabanına DOĞRU yazılıyordu
/// (`cheques.Status = 11`, hareket satırı ve dengeli fiş vardı).
/// Hata OKUMA tarafındaydı: liste ucunda durum süzgeci YALNIZ çağıran
/// gönderirse uygulanıyordu ve ekran açılışta hiçbir durum
/// göndermiyordu.
///
/// TESTLER ÇEKLERİ DOĞRUDAN EKLİYOR, durum geçişi akışını
/// çalıştırmıyor: sınanan şey okuma tarafı. Geçiş akışını
/// kullansaydık test, alakasız bir yerde (fiş üretimi, kasa hesabı)
/// kırılabilir ve asıl kuralı sınamaktan uzaklaşırdı.
/// </summary>
[Collection("Integration")]
public sealed class ChequeAcikListeTests(DatabaseFixture fixture)
{
    private sealed record Sahne(Guid CompanyId, HttpClient Client);

    private async Task<Sahne> KurAsync(string ek, params ChequeStatus[] durumlar)
    {
        Guid companyId;

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var proje = await TestDataFactory.CreateProjectAsync(db, $"CEK{ek}");
            companyId = proje.CompanyId;

            var sira = 0;

            foreach (var durum in durumlar)
            {
                sira++;

                db.Cheques.Add(new Cheque
                {
                    CompanyId = companyId,
                    Direction = ChequeDirection.Issued,
                    Status = durum,
                    InternalNumber = $"VCK-{ek}-{sira:000}",
                    ChequeNumber = $"{ek}{sira:000}",
                    BankName = "TEST BANKASI",
                    Amount = 1000m * sira,
                    AmountTry = 1000m * sira,
                    CurrencyCode = "TRY",
                    ExchangeRate = 1m,
                    IssueDate = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
                    DueDate = new DateTime(2026, 8, 25, 0, 0, 0, DateTimeKind.Utc)
                });
            }

            await db.SaveChangesAsync();
        }

        var client = await TestUserFactory.CreateCompanyScopedClientAsync(
            fixture, ek, ["Finans Sorumlusu"], companyId);

        return new Sahne(companyId, client);
    }

    private static async Task<List<JsonElement>> ListeAsync(Sahne s, string ekSorgu = "")
    {
        var cevap = await s.Client.GetAsync(
            $"/api/cheques?companyId={s.CompanyId}{ekSorgu}");

        Assert.Equal(HttpStatusCode.OK, cevap.StatusCode);

        return (await cevap.Content.ReadFromJsonAsync<JsonElement>())
            .EnumerateArray().ToList();
    }

    private static int Durum(JsonElement satir) => satir.GetProperty("status").GetInt32();

    /// <summary>
    /// ŞİKAYETİN KENDİSİ: ödenen çek varsayılan listede YOK.
    ///
    /// Açık çek (Verilen) duruyor. Bu test kırmızıya dönerse hata
    /// aynen geri gelmiş demektir.
    /// </summary>
    [Fact]
    public async Task OdenenCek_VarsayilanListedeGorunmez()
    {
        var s = await KurAsync(
            $"o{DateTime.UtcNow:ffffff}", ChequeStatus.Issued, ChequeStatus.Paid);

        var satirlar = await ListeAsync(s);

        Assert.Single(satirlar);
        Assert.Equal((int)ChequeStatus.Issued, Durum(satirlar[0]));
        Assert.DoesNotContain(satirlar, x => Durum(x) == (int)ChequeStatus.Paid);
    }

    /// <summary>
    /// KAPANMIŞ SAYILAN HER DURUM VARSAYILAN LİSTEDEN ÇIKAR.
    ///
    /// Tahsil edilen, karşılıksız çıkan, iade edilen ve ertelenen çek
    /// de kapanmıştır. `Bounced` kararla kapanmış sayıldı: alacak
    /// cariye döndü ve orada izleniyor — açık bırakılsaydı aynı alacak
    /// iki yerde görünürdü.
    /// </summary>
    [Fact]
    public async Task KapanmisDurumlar_VarsayilanListedenCikar()
    {
        var s = await KurAsync(
            $"k{DateTime.UtcNow:ffffff}",
            ChequeStatus.Issued,
            ChequeStatus.Paid,
            ChequeStatus.Collected,
            ChequeStatus.Bounced,
            ChequeStatus.Returned,
            ChequeStatus.Replaced);

        var satirlar = await ListeAsync(s);

        Assert.Single(satirlar);
        Assert.Equal((int)ChequeStatus.Issued, Durum(satirlar[0]));
    }

    /// <summary>
    /// FAKTORİNGDEKİ ÇEK AÇIK KALIR.
    ///
    /// Parası kırdırma anında alınmış olsa da çek hâlâ tedavülde ve
    /// rücu riski taşıyor. `CashFlowService` onu beklenen tahsilattan
    /// ÇIKARIYOR ve bu bir çelişki değil: nakit akışı "ne kadar para
    /// gelecek" diye sorar, çek defteri "hangi çekler hâlâ canlı" diye.
    /// </summary>
    [Fact]
    public async Task FaktoringdekiCek_AcikKalir()
    {
        var s = await KurAsync(
            $"f{DateTime.UtcNow:ffffff}",
            ChequeStatus.Portfolio, ChequeStatus.AtBank,
            ChequeStatus.AtFactoring, ChequeStatus.Paid);

        var satirlar = await ListeAsync(s);

        Assert.Equal(3, satirlar.Count);
        Assert.Contains(satirlar, x => Durum(x) == (int)ChequeStatus.AtFactoring);
    }

    /// <summary>
    /// ÖDENEN ÇEK SİLİNMİYOR, GİZLENMİYOR — GEÇMİŞ ERİŞİLEBİLİR (K1).
    ///
    /// İki yol da çalışmalı: durum süzgeci ve `includeClosed`.
    /// Yalnız biri olsaydı "ödenmiş çekleri göster" isteyen kullanıcı
    /// tek bir yoldan geçmek zorunda kalırdı.
    /// </summary>
    [Fact]
    public async Task OdenenCek_DurumSuzgeciVeBayrakIleGorulebilir()
    {
        var s = await KurAsync(
            $"g{DateTime.UtcNow:ffffff}", ChequeStatus.Issued, ChequeStatus.Paid);

        var durumla = await ListeAsync(s, $"&status={(int)ChequeStatus.Paid}");
        Assert.Single(durumla);
        Assert.Equal((int)ChequeStatus.Paid, Durum(durumla[0]));

        var bayrakla = await ListeAsync(s, "&includeClosed=true");
        Assert.Equal(2, bayrakla.Count);
        Assert.Contains(bayrakla, x => Durum(x) == (int)ChequeStatus.Paid);
    }

    /// <summary>
    /// TOPLAM BAYRAĞI SATIRDA GELİYOR — EKRAN KENDİ KURALINI YAZMASIN.
    ///
    /// ÇEK/1'in kök nedeni iki ayrı karar yeriydi: sunucu listeye neyi
    /// koyacağına, ekran neyi toplayacağına AYRI karar veriyordu.
    /// Bayrak sunucudan geldiği sürece ayrışma imkânsız.
    ///
    /// Ödenen çek toplama GİRER (kullanıcı onu açıkça istediğinde
    /// toplamını da görmeli); iptal edilen GİRMEZ ama satırı listede
    /// kalır — gizlemek yok saymak değildir.
    /// </summary>
    [Fact]
    public async Task ToplamBayragi_SatirdaGelir()
    {
        var s = await KurAsync(
            $"t{DateTime.UtcNow:ffffff}",
            ChequeStatus.Issued, ChequeStatus.Paid, ChequeStatus.Voided);

        var hepsi = await ListeAsync(s, "&includeClosed=true&includeVoided=true");

        Assert.Equal(3, hepsi.Count);

        foreach (var satir in hepsi)
        {
            var girer = satir.GetProperty("countsTowardTotals").GetBoolean();

            Assert.Equal(Durum(satir) != (int)ChequeStatus.Voided, girer);
        }
    }

    /// <summary>
    /// ÖZET UCU ÖDENEN ÇEKİ KENDİ KUTUSUNDA GÖSTERİR.
    ///
    /// Özet zaten durum kırılımlı ve ay süzgeci taşımıyor; ödenen çek
    /// "açık" kutularına karışmamalı. Bu test özetin liste kuralından
    /// etkilenmediğini sabitliyor — ikisi ayrı sorulara cevap veriyor.
    /// </summary>
    [Fact]
    public async Task Ozet_OdenenCekiAyriKutudaGosterir()
    {
        var s = await KurAsync(
            $"z{DateTime.UtcNow:ffffff}", ChequeStatus.Issued, ChequeStatus.Paid);

        var cevap = await s.Client.GetAsync($"/api/cheques/summary?companyId={s.CompanyId}");
        Assert.Equal(HttpStatusCode.OK, cevap.StatusCode);

        var ozet = await cevap.Content.ReadFromJsonAsync<JsonElement>();

        // Verilen (açık) 1000, Ödenen 2000 — kurulum sırasına göre.
        Assert.Equal(1000m, ozet.GetProperty("issuedOpenAmount").GetDecimal());
        Assert.Equal(2000m, ozet.GetProperty("issuedPaidAmount").GetDecimal());
    }
}
