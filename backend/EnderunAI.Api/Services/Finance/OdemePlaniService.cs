using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Models.Finance;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Services.Finance;

/// <summary>
/// HAFTALIK ÖDEME PLANI SERVİSİ (ÖP/1a).
///
/// KARARLAR BURADA DEĞİL: K2/K3/K4/K6/K8/K10 mantığı
/// <see cref="OdemePlaniKurallari"/> içinde saf fonksiyonlar hâlinde.
/// Servis onları ÇAĞIRIR, kopyalamaz — ikinci bir kopya zamanla
/// ayrışır ve hangisinin koruduğu ölçülemez hâle gelir (Kural 25).
/// </summary>
public sealed class OdemePlaniService(AppDbContext db, IOdemeSatirKilidi kilit)
{
    // ═══════════════════════════════════════════════════════════════
    // D1 — HAFTANIN TASLAĞI
    // ═══════════════════════════════════════════════════════════════

    /// <summary>Verilen tarihin içinde bulunduğu haftanın pazartesisi.</summary>
    public static DateTime HaftaninPazartesisi(DateTime tarih)
    {
        var fark = ((int)tarih.DayOfWeek + 6) % 7;   // Pazartesi = 0
        return tarih.Date.AddDays(-fark);
    }

    /// <summary>
    /// Haftanın taslak planını oluşturur (D1).
    ///
    /// HAZIR GELENLER: gelecek hafta vadesi dolan çekler + geçen
    /// haftanın plan dışı ödemeleri (K5) + devirler (K8).
    ///
    /// SİSTEM BAŞKA HİÇBİR SATIRI ÖNERMEZ — listeyi muhasebeci kurar.
    /// Tek istisna çekler, çünkü çekte vade verisi sağlam.
    ///
    /// AYNI HAFTA İÇİN İKİNCİ PLAN AÇILMAZ: kısmi benzersiz indeks
    /// (silinmişler hariç) bunu veritabanı düzeyinde de engelliyor.
    /// </summary>
    public async Task<OdemePlani> HaftalikTaslakOlusturAsync(
        Guid companyId, DateTime haftaBaslangici, Guid? kullaniciId,
        CancellationToken cancellationToken)
    {
        var pazartesi = HaftaninPazartesisi(haftaBaslangici);

        var mevcut = await db.OdemePlanlari
            .FirstOrDefaultAsync(
                x => x.CompanyId == companyId && x.HaftaBaslangici == pazartesi,
                cancellationToken);

        if (mevcut is not null) return mevcut;

        var plan = new OdemePlani
        {
            CompanyId = companyId,
            HaftaBaslangici = pazartesi,
            OdemeGunu = pazartesi.AddDays(4),   // cuma
            Durum = OdemePlaniDurumu.Taslak,
            HazirlayanUserId = kullaniciId,
            CreatedByUserId = kullaniciId
        };

        db.OdemePlanlari.Add(plan);

        await VadesiGelenCekleriEkleAsync(plan, cancellationToken);
        await DevirleriEkleAsync(plan, kullaniciId, cancellationToken);
        await PlanDisiOdemeleriIsaretleAsync(plan, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        return plan;
    }

    /// <summary>
    /// Gelecek hafta vadesi dolan VERİLEN çekler plana düşer.
    ///
    /// İPTAL VE ÖDENMİŞ ÇEK GİRMEZ: iptal edilmiş çek bir yükümlülük
    /// değil, ödenmiş çeğin parası zaten çıkmış.
    /// </summary>
    private async Task VadesiGelenCekleriEkleAsync(
        OdemePlani plan, CancellationToken cancellationToken)
    {
        var bas = plan.HaftaBaslangici;
        var son = bas.AddDays(7);

        var cekler = await db.Cheques
            .Where(x => x.CompanyId == plan.CompanyId
                && x.Direction == ChequeDirection.Issued
                && x.Status == ChequeStatus.Issued
                && x.DueDate >= bas && x.DueDate < son)
            .ToListAsync(cancellationToken);

        var oncelik = 0;

        foreach (var cek in cekler)
        {
            if (cek.CurrentAccountId is not { } cari) continue;

            plan.Satirlar.Add(new OdemePlaniSatiri
            {
                CurrentAccountId = cari,
                OnerilenTutar = cek.Amount,
                Yontem = OdemeYontemi.Cek,
                CekVadesi = cek.DueDate,
                Oncelik = ++oncelik,
                CashAccountId = cek.CashAccountId,
                Aciklama = $"Vadesi dolan çek: {cek.ChequeNumber}",
                Karar = OdemeSatirKarari.Bekliyor
            });
        }
    }

    /// <summary>
    /// DEVİRLER (K8): onaylanmış ama ödenmemiş satırlar kaybolmaz.
    ///
    /// ÜÇ HAFTAYI AŞAN ONAY DÜŞER: satır "Bekliyor"a döner ve
    /// yeniden onaya gelir. Eski onayla bugün para çıkmamalı.
    /// </summary>
    private async Task DevirleriEkleAsync(
        OdemePlani plan, Guid? kullaniciId, CancellationToken cancellationToken)
    {
        var simdi = DateTime.UtcNow;

        var devredecekler = await db.OdemePlaniSatirlari
            .Include(x => x.OdemePlani)
            .Where(x => x.OdemePlani.CompanyId == plan.CompanyId
                && x.OdemePlani.HaftaBaslangici < plan.HaftaBaslangici
                && (x.Karar == OdemeSatirKarari.Onaylandi
                    || x.Karar == OdemeSatirKarari.Kismi)
                && x.OdemeDurumu != OdemeSatirOdemeDurumu.Odendi)
            .ToListAsync(cancellationToken);

        foreach (var eski in devredecekler)
        {
            var onayGecerli = eski.KararAnUtc is { } an
                && OdemePlaniKurallari.OnayGecerliMi(an, simdi);

            var yeni = new OdemePlaniSatiri
            {
                CurrentAccountId = eski.CurrentAccountId,
                OnerilenTutar = (eski.OnaylananTutar ?? eski.OnerilenTutar)
                    - eski.OdenenTutar,
                Yontem = eski.Yontem,
                CekVadesi = eski.CekVadesi,
                Oncelik = eski.Oncelik,
                CashAccountId = eski.CashAccountId,
                Aciklama = eski.Aciklama,
                DevrededenSatirId = eski.Id,
                DevirHaftaSayisi = eski.DevirHaftaSayisi + 1,
                CreatedByUserId = kullaniciId,
                SupplierInvoiceId = eski.SupplierInvoiceId
            };

            if (onayGecerli)
            {
                // Onay taşınıyor — anlık görüntüsüyle birlikte.
                yeni.Karar = eski.Karar;
                yeni.KararVerenUserId = eski.KararVerenUserId;
                yeni.KararAnUtc = eski.KararAnUtc;
                yeni.OnaylananTutar = yeni.OnerilenTutar;
                yeni.OnayliCurrentAccountId = eski.OnayliCurrentAccountId;
                yeni.OnayliTutar = yeni.OnerilenTutar;
                yeni.OnayliYontem = eski.OnayliYontem;
                yeni.OnayliCekVadesi = eski.OnayliCekVadesi;
                yeni.OnayliOncelik = eski.OnayliOncelik;
                yeni.OnayliCashAccountId = eski.OnayliCashAccountId;
            }
            else
            {
                // ONAY DÜŞTÜ — yeniden onaya gelecek.
                yeni.Karar = OdemeSatirKarari.Bekliyor;
            }

            plan.Satirlar.Add(yeni);
        }
    }

    /// <summary>
    /// PLAN DIŞI ÖDEMELER (K5): geçen haftanınkiler bu planın
    /// başında listelenir. Acil ödeme yasak değil, görünmez olması
    /// yasak.
    /// </summary>
    private async Task PlanDisiOdemeleriIsaretleAsync(
        OdemePlani plan, CancellationToken cancellationToken)
    {
        var oncekiHafta = plan.HaftaBaslangici.AddDays(-7);

        var odemeler = await db.PlanDisiOdemeler
            .Where(x => x.CompanyId == plan.CompanyId
                && x.ListelendigiHafta == null
                && x.OdemeTarihi >= oncekiHafta
                && x.OdemeTarihi < plan.HaftaBaslangici)
            .ToListAsync(cancellationToken);

        foreach (var odeme in odemeler)
            odeme.ListelendigiHafta = plan.HaftaBaslangici;
    }

    // ═══════════════════════════════════════════════════════════════
    // B1/B2 — BAKİYE ANLIK GÖRÜNTÜSÜ
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Hesabın güncel bakiyesini HAREKETLERDEN hesaplar.
    ///
    /// Bu sistemde bakiye SAKLANMIYOR (ölçüldü): `OpeningBalance` +
    /// girişler − çıkışlar. Pahalı olduğu için yalnız AÇIK İSTEKLE
    /// çağrılıyor (B2), ekran her açılışta değil.
    /// </summary>
    public async Task<decimal> BakiyeHesaplaAsync(
        Guid cashAccountId, CancellationToken cancellationToken)
    {
        var hesap = await db.CashAccounts
            .FirstOrDefaultAsync(x => x.Id == cashAccountId, cancellationToken)
            ?? throw new KeyNotFoundException("Kasa/banka hesabı bulunamadı.");

        var giren = await db.CashTransactions
            .Where(x => x.CashAccountId == cashAccountId
                && x.Direction == CashTransactionDirection.In)
            .SumAsync(x => (decimal?)x.Amount, cancellationToken) ?? 0m;

        var cikan = await db.CashTransactions
            .Where(x => x.CashAccountId == cashAccountId
                && x.Direction == CashTransactionDirection.Out)
            .SumAsync(x => (decimal?)x.Amount, cancellationToken) ?? 0m;

        return hesap.OpeningBalance + giren - cikan;
    }

    /// <summary>
    /// Plana bakiye yazar ya da günceller (B1/B2).
    ///
    /// PLAN GÖSTERDİĞİNİ SAKLAR: bakiye ister hesaplansın ister elle
    /// girilsin, kayda geçen ekranda GÖRÜLEN sayıdır. Onay bir sayıya
    /// bakılarak verilir; yeniden kurulamayan onay denetlenemez.
    /// </summary>
    public async Task BakiyeYazAsync(
        Guid planId, Guid cashAccountId, decimal tutar, BakiyeKaynagi kaynak,
        Guid? kullaniciId, CancellationToken cancellationToken)
    {
        var kayit = await db.OdemePlaniHesapBakiyeleri
            .FirstOrDefaultAsync(
                x => x.OdemePlaniId == planId && x.CashAccountId == cashAccountId,
                cancellationToken);

        if (kayit is null)
        {
            kayit = new OdemePlaniHesapBakiyesi
            {
                OdemePlaniId = planId,
                CashAccountId = cashAccountId,
                CreatedByUserId = kullaniciId
            };
            db.OdemePlaniHesapBakiyeleri.Add(kayit);
        }

        kayit.GosterilenBakiye = tutar;
        kayit.Kaynak = kaynak;
        kayit.OlcumAnUtc = DateTime.UtcNow;
        kayit.OlcenUserId = kullaniciId;
        kayit.UpdatedAtUtc = DateTime.UtcNow;
        kayit.UpdatedByUserId = kullaniciId;

        await db.SaveChangesAsync(cancellationToken);
    }

    // ═══════════════════════════════════════════════════════════════
    // D2/D3 — ONAYA SUNMA VE SATIR KARARI
    // ═══════════════════════════════════════════════════════════════

    public async Task OnayaSunAsync(
        Guid planId, Guid? kullaniciId, CancellationToken cancellationToken)
    {
        var plan = await PlanGetirAsync(planId, cancellationToken);

        if (plan.Durum != OdemePlaniDurumu.Taslak)
            throw new InvalidOperationException(
                "Yalnız taslak plan onaya sunulabilir.");

        plan.Durum = OdemePlaniDurumu.Onayda;
        plan.OnayaSunulmaAnUtc = DateTime.UtcNow;
        plan.HazirlayanUserId ??= kullaniciId;
        plan.UpdatedAtUtc = DateTime.UtcNow;
        plan.UpdatedByUserId = kullaniciId;

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// SATIR KARARI (K1) — onay bu seviyede verilir.
    ///
    /// K4: hazırlayan ya da satırı son değiştiren kişi onaylayamaz.
    /// K2: onaylanan değerlerin anlık görüntüsü satıra yazılır.
    /// </summary>
    public async Task SatirKararVerAsync(
        Guid satirId, OdemeSatirKarari karar, decimal? onaylananTutar,
        DateTime? cekVadesi, int? oncelik, Guid onaylayanUserId,
        CancellationToken cancellationToken)
    {
        /*
         * ONAY DA KİLİTLİ (S6 taraması).
         *
         * İki eşzamanlı onay: ikincisi birincinin K2 anlık
         * görüntüsünü EZER. Sonuç tek bir onay olur ama
         * `KararVerenUserId` SON YAZANI gösterir — yani "bu ödemeyi
         * kim onayladı" sorusunun cevabı yanlış olur. Onayın
         * denetlenebilirliği bu alana dayanıyor.
         */
        var kendiIslemi = db.Database.CurrentTransaction is null;
        var islem = kendiIslemi
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;

        try
        {
        await kilit.KilitleAsync(satirId, cancellationToken);

        var satir = await db.OdemePlaniSatirlari
            .Include(x => x.OdemePlani)
            .FirstOrDefaultAsync(x => x.Id == satirId, cancellationToken)
            ?? throw new KeyNotFoundException("Plan satırı bulunamadı.");

        await db.Entry(satir).ReloadAsync(cancellationToken);

        if (satir.OdemePlani.Durum != OdemePlaniDurumu.Onayda)
            throw new InvalidOperationException(
                "Karar yalnız onaydaki planda verilebilir.");

        // K4 — KOD DÜZEYİNDE, AYAR DEĞİL.
        if (!OdemePlaniKurallari.OnaylayabilirMi(
                onaylayanUserId,
                satir.OdemePlani.HazirlayanUserId,
                satir.UpdatedByUserId ?? satir.CreatedByUserId))
        {
            throw new InvalidOperationException(
                "Hazırlayan kendi hazırladığı satırı onaylayamaz. " +
                "Onayı başka bir yetkili vermelidir.");
        }

        satir.Karar = karar;
        satir.KararVerenUserId = onaylayanUserId;
        satir.KararAnUtc = DateTime.UtcNow;

        if (oncelik is { } yeniOncelik) satir.Oncelik = yeniOncelik;
        if (cekVadesi is { } vade) satir.CekVadesi = vade;

        if (karar is OdemeSatirKarari.Onaylandi or OdemeSatirKarari.Kismi)
        {
            satir.OnaylananTutar = onaylananTutar ?? satir.OnerilenTutar;

            // K2 — ANLIK GÖRÜNTÜ. Öncelik DAHİL (K7).
            satir.OnayliCurrentAccountId = satir.CurrentAccountId;
            satir.OnayliTutar = satir.OnaylananTutar;
            satir.OnayliYontem = satir.Yontem;
            satir.OnayliCekVadesi = satir.CekVadesi;
            satir.OnayliOncelik = satir.Oncelik;
            satir.OnayliCashAccountId = satir.CashAccountId;
        }
        else
        {
            // Reddedilen satırın onay görüntüsü OLMAZ.
            satir.OnaylananTutar = null;
            satir.OnayliCurrentAccountId = null;
            satir.OnayliTutar = null;
            satir.OnayliYontem = null;
            satir.OnayliCekVadesi = null;
            satir.OnayliOncelik = null;
            satir.OnayliCashAccountId = null;
        }

        await db.SaveChangesAsync(cancellationToken);
        if (islem is not null) await islem.CommitAsync(cancellationToken);
        }
        catch
        {
            if (islem is not null) await islem.RollbackAsync(cancellationToken);
            throw;
        }
        finally
        {
            if (islem is not null) await islem.DisposeAsync();
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // D4 — UYGULAMA
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// SATIRI ÖDENMİŞ İŞARETLER (D4 · K11).
    ///
    /// SİSTEM KENDİ BAŞINA ÖDEME YAPMAZ: bu metot bir insanın
    /// kararıyla çağrılır. Para, bir insan bakmadan çıkmaz.
    ///
    /// ÜÇ KAPI SIRAYLA:
    ///   K8 — onay üç haftayı aştıysa satır yeniden onaya döner,
    ///   K2 — onaydan sonra değişen satır ÖDENMEZ,
    ///   K3 — onaylanandan fazla ödenemez.
    ///
    /// SIRA ÖNEMLİ (Kural 43): kalıcı retler geçici retten önce.
    /// </summary>
    public async Task SatirOdemeKaydetAsync(
        Guid satirId, decimal odenenTutar, Guid? kullaniciId,
        CancellationToken cancellationToken)
    {
        /*
         * TEK İŞLEM + SATIR KİLİDİ (S6).
         *
         * ÖNCEKİ HÂLİ YARIŞA AÇIKTI: satır okunuyor, kontroller
         * BELLEKTEKİ kopyaya uygulanıyor, sonra yazılıyor. İki
         * eşzamanlı istek K2'yi "onaylandığı gibi" geçiyor, K3'ü
         * KENDİ payına geçiyor ve toplamda ONAYLANANDAN FAZLA ödeme
         * yazılıyordu. K3 tek başına yetmiyor çünkü sınır BAYAT
         * `OdenenTutar` üzerinden hesaplanıyordu.
         *
         * Saf kural sondalarının hepsi (S1–S4) bu deliği göremedi:
         * kurallar doğruydu, delik ARALARINDAYDI — okuma ile yazma
         * arasında (Kural 54).
         *
         * KİLİT ÖNCE, OKUMA SONRA: kilitten önce okumak, kilidi
         * beklerken bayatlamış bir kopya üzerinde karar vermek olurdu.
         */
        /*
         * HATA, İŞLEM KAPANDIKTAN SONRA FIRLATILIYOR.
         *
         * İlk yazımda K2/K8 dallarında "kaydet → commit → throw"
         * yapıyordum; `catch` bloğu da zaten commit edilmiş işlemi
         * geri almaya çalışıp `"This NpgsqlTransaction has completed"`
         * fırlatıyor ve ASIL MESAJI eziyordu. Testler "Tutar" ya da
         * "Öncelik" arıyor, yerine altyapı hatası buluyordu.
         *
         * Karar sonucu bir DEĞİŞKENDE taşınıyor; işlem düzgün kapanıp
         * kaynaklar bırakıldıktan sonra fırlatılıyor.
         */
        string? kalicRet = null;

        var kendiIslemi = db.Database.CurrentTransaction is null;

        var islem = kendiIslemi
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;

        try
        {
            await kilit.KilitleAsync(satirId, cancellationToken);

            // TAZE OKUMA — kilit alındıktan SONRA.
            var satir = await db.OdemePlaniSatirlari
                .Include(x => x.OdemePlani)
                .FirstOrDefaultAsync(x => x.Id == satirId, cancellationToken)
                ?? throw new KeyNotFoundException("Plan satırı bulunamadı.");

            await db.Entry(satir).ReloadAsync(cancellationToken);

            if (satir.Karar is not (OdemeSatirKarari.Onaylandi or OdemeSatirKarari.Kismi))
                throw new InvalidOperationException(
                    "Onaylanmamış satır için ödeme kaydedilemez.");

            // ── K8: YAŞLANMA (kalıcı ret) ─────────────────────────
            if (satir.KararAnUtc is { } kararAn
                && !OdemePlaniKurallari.OnayGecerliMi(kararAn, DateTime.UtcNow))
            {
                satir.Karar = OdemeSatirKarari.Bekliyor;
                satir.OnayliTutar = null;
                await db.SaveChangesAsync(cancellationToken);

                kalicRet =
                    $"Bu satırın onayı {OdemePlaniKurallari.OnayGecerlilikHaftasi} " +
                    "haftadan eski; onay düştü ve satır yeniden onaya döndü. " +
                    "Eski onayla bugün para çıkamaz.";
            }

            // ── K2: ONAYDAN SONRA DEĞİŞTİ Mİ (kalıcı ret) ─────────
            if (kalicRet is null)
            {
                var degisenler = OdemePlaniKurallari.DegisenOnayAlanlari(satir);

                if (degisenler.Count > 0)
                {
                    satir.Karar = OdemeSatirKarari.Bekliyor;
                    await db.SaveChangesAsync(cancellationToken);

                    kalicRet =
                        "Satır onaylandıktan sonra değişmiş, ödeme yapılmadı. " +
                        "Değişen alanlar: " + string.Join(", ", degisenler) +
                        ". Satır yeniden onaya döndü.";
                }
            }

            if (kalicRet is null)
            {
                // ── K3: ÖDENEN ≤ ONAYLANAN (geçici ret) ───────────
                //
                // TAZE `OdenenTutar` ÜZERİNDEN: kilit sayesinde bu
                // değer artık bayat olamaz. S6'nın kapattığı delik
                // tam burası.
                var onaylanan = satir.OnayliTutar ?? 0m;

                if (OdemePlaniKurallari.OdemeSiniriAsiliyorMu(
                        onaylanan, satir.OdenenTutar, odenenTutar))
                {
                    throw new InvalidOperationException(
                        $"Onaylanan tutar {Formatting.TurkishFormat.Amount(onaylanan)}, " +
                        $"halihazır ödenen {Formatting.TurkishFormat.Amount(satir.OdenenTutar)}. " +
                        "Bu ödeme sınırı aşıyor. Az ödemek serbest, çok ödemek değil.");
                }

                satir.OdenenTutar += odenenTutar;

                satir.OdemeDurumu =
                    decimal.Round(satir.OdenenTutar, 2) >= decimal.Round(onaylanan, 2)
                        ? OdemeSatirOdemeDurumu.Odendi
                        : OdemeSatirOdemeDurumu.KismenOdendi;

                satir.UpdatedAtUtc = DateTime.UtcNow;
                satir.UpdatedByUserId = kullaniciId;

                await db.SaveChangesAsync(cancellationToken);
            }

            if (islem is not null) await islem.CommitAsync(cancellationToken);
        }
        catch
        {
            if (islem is not null && db.Database.CurrentTransaction is not null)
                await islem.RollbackAsync(cancellationToken);
            throw;
        }
        finally
        {
            if (islem is not null) await islem.DisposeAsync();
        }

        // KALICI RET — işlem kapandı, kayıt yerinde, mesaj net.
        if (kalicRet is not null)
            throw new InvalidOperationException(kalicRet);
    }

    // ═══════════════════════════════════════════════════════════════
    // D5 — KAPANIŞ
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// PLANI KAPATIR (D5 · K10).
    ///
    /// SEBEPSİZ SATIR VARKEN PLAN KAPANMAZ: onaylanmış ama ödenmemiş
    /// ya da kısmen ödenmiş her satır sebep taşımak zorunda. Bir
    /// tedarikçinin sessizce aç bırakıldığı ancak böyle görünür.
    /// </summary>
    public async Task KapatAsync(
        Guid planId, Guid? kullaniciId, CancellationToken cancellationToken)
    {
        /*
         * K10 KONTROLÜ TAZE OKUMAYLA (S6 taraması).
         *
         * Kapatma PLAN seviyesinde, ödeme SATIR seviyesinde. İkisini
         * tek kilitle korumak için planın bütün satırlarını
         * kilitlemek gerekirdi — kilidin kapsamını gereğinden çok
         * genişletmek ve ölü kilitlenme riskini davet etmek olurdu.
         *
         * Bunun yerine satırlar kapatma anında VERİTABANINDAN taze
         * okunuyor: araya giren bir ödeme ya da karar değişikliği
         * K10 kontrolüne dahil olur. Yarış tamamen kapanmaz —
         * kontrol ile yazma arasında hâlâ bir aralık var — ama o
         * aralıkta olabilecek en kötü şey, kapanmış bir planda
         * sebebi sonradan girilmiş bir satır kalmasıdır; para
         * hareketi değil, kayıt eksikliği.
         */
        var plan = await db.OdemePlanlari
            .Include(x => x.Satirlar)
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == planId, cancellationToken)
            ?? throw new KeyNotFoundException("Ödeme planı bulunamadı.");

        var sebepsizler = plan.Satirlar
            .Where(OdemePlaniKurallari.KapanisSebebiGerekliMi)
            .Where(x => x.KapanisSebebi is null)
            .ToList();

        if (sebepsizler.Count > 0)
        {
            throw new InvalidOperationException(
                $"{sebepsizler.Count} satır kapanış sebebi taşımıyor. " +
                "Onaylanmış ama ödenmemiş her satır sebep ister; " +
                "sebepsiz satır varken plan kapanmaz.");
        }

        var eksikAciklama = plan.Satirlar
            .Where(x => x.KapanisSebebi is { } s
                && OdemePlaniKurallari.KapanisAciklamasiGerekliMi(s)
                && string.IsNullOrWhiteSpace(x.KapanisAciklamasi))
            .ToList();

        if (eksikAciklama.Count > 0)
            throw new InvalidOperationException(
                "\"Diğer\" sebebi seçilen satırlarda açıklama zorunludur.");

        var izlenen = await db.OdemePlanlari
            .FirstAsync(x => x.Id == planId, cancellationToken);

        izlenen.Durum = OdemePlaniDurumu.Kapandi;
        izlenen.KapanmaAnUtc = DateTime.UtcNow;
        izlenen.UpdatedAtUtc = DateTime.UtcNow;
        izlenen.UpdatedByUserId = kullaniciId;

        await db.SaveChangesAsync(cancellationToken);
    }

    // ═══════════════════════════════════════════════════════════════
    // K6 — İKİ AYRI BÜTÇE SAYISI
    // ═══════════════════════════════════════════════════════════════

    public sealed record HesapButcesi(
        Guid CashAccountId, decimal NakitCikis, decimal GosterilenBakiye,
        decimal Fark, BakiyeKaynagi? BakiyeKaynagi);

    public sealed record VadeYukumlulugu(int Yil, int Ay, decimal Tutar);

    public sealed record ButceOzeti(
        IReadOnlyList<HesapButcesi> HesapBazindaNakit,
        IReadOnlyList<VadeYukumlulugu> GelecekYukumlulukler);

    /// <summary>
    /// K6: BU CUMA ÇIKACAK NAKİT ile BU CUMA YARATILAN GELECEK
    /// YÜKÜMLÜLÜK — İKİ AYRI SAYI, TOPLANMAZ.
    ///
    /// K9: yetmezlik onay anında görünür. Fark eksiyse ekran AÇIKÇA
    /// uyarır ama ENGELLEMEZ — GM yine onaylayabilir, ama görmeden
    /// onaylamış olmaz.
    /// </summary>
    public async Task<ButceOzeti> ButceOzetiAsync(
        Guid planId, CancellationToken cancellationToken)
    {
        var plan = await PlanGetirAsync(planId, cancellationToken);

        var bakiyeler = await db.OdemePlaniHesapBakiyeleri
            .Where(x => x.OdemePlaniId == planId)
            .ToListAsync(cancellationToken);

        var onayli = plan.Satirlar
            .Where(x => x.Karar is OdemeSatirKarari.Onaylandi
                or OdemeSatirKarari.Kismi)
            .ToList();

        var nakit = onayli
            .Where(x => OdemePlaniKurallari.NakitCikisiMi(x.Yontem))
            .GroupBy(x => x.CashAccountId ?? Guid.Empty)
            .Select(g =>
            {
                var cikis = g.Sum(x => x.OnayliTutar ?? 0m);
                var bakiye = bakiyeler.FirstOrDefault(b => b.CashAccountId == g.Key);

                return new HesapButcesi(
                    g.Key, cikis,
                    bakiye?.GosterilenBakiye ?? 0m,
                    (bakiye?.GosterilenBakiye ?? 0m) - cikis,
                    bakiye?.Kaynak);
            })
            .ToList();

        var yukumluluk = onayli
            .Where(x => OdemePlaniKurallari.GelecekYukumlulukMu(x.Yontem))
            .Where(x => x.OnayliCekVadesi is not null)
            .GroupBy(x => new { x.OnayliCekVadesi!.Value.Year, x.OnayliCekVadesi!.Value.Month })
            .Select(g => new VadeYukumlulugu(
                g.Key.Year, g.Key.Month, g.Sum(x => x.OnayliTutar ?? 0m)))
            .OrderBy(x => x.Yil).ThenBy(x => x.Ay)
            .ToList();

        return new ButceOzeti(nakit, yukumluluk);
    }

    private async Task<OdemePlani> PlanGetirAsync(
        Guid planId, CancellationToken cancellationToken)
        => await db.OdemePlanlari
            .Include(x => x.Satirlar)
            .FirstOrDefaultAsync(x => x.Id == planId, cancellationToken)
            ?? throw new KeyNotFoundException("Ödeme planı bulunamadı.");
}
