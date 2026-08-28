namespace EnderunAI.Api.Models.Finance;

/// <summary>Plan durumu — tek yönlü ilerler.</summary>
public enum OdemePlaniDurumu
{
    Taslak = 0, Onayda = 1, Onaylandi = 2, Uygulandi = 3, Kapandi = 4
}

/// <summary>Satırın onay kararı (K1).</summary>
public enum OdemeSatirKarari
{
    Bekliyor = 0, Onaylandi = 1, Reddedildi = 2, Kismi = 3
}

/// <summary>Satırın ödeme durumu — karardan AYRI.</summary>
public enum OdemeSatirOdemeDurumu
{
    Odenmedi = 0, KismenOdendi = 1, Odendi = 2
}

public enum OdemeYontemi { HavaleEft = 0, Cek = 1, Nakit = 2 }

/// <summary>Plan kapanırken zorunlu sebep (K10).</summary>
public enum OdemeKapanisSebebi
{
    ParaYetmedi = 0, Ertelendi = 1, FaturaGelmedi = 2, IptalEdildi = 3, Diger = 90
}

/// <summary>Bakiyenin nereden geldiği (K9 · B1).</summary>
public enum BakiyeKaynagi { Hesaplandi = 0, ElleGirildi = 1 }

/// <summary>
/// HAFTALIK ÖDEME PLANI (ÖP/1a).
///
/// CARİ BAZLI, fatura bazlı değil: on bir kontrolün hiçbiri fatura
/// ayrıntısına bağlı değil. İleride fatura bazlı takip gelirse plan
/// satırı İSTEĞE BAĞLI olarak faturalara bağlanır, kurallar değişmez.
///
/// SİSTEM PLANI KENDİ ÖNERMEZ — listeyi muhasebeci kurar. Tek istisna
/// gelecek hafta vadesi dolan çekler; çekte vade verisi sağlam.
///
/// ADLANDIRMA UYARISI: `progress_payment_payment_plans` diye BAŞKA bir
/// tablo var (hakediş ödeme planı) ve FARKLI bir kavramdır. Altı ay
/// sonra biri ikisini karıştırmasın diye bu paketin tabloları
/// `odeme_plani*` adlarını taşıyor.
/// </summary>
public sealed class OdemePlani : BaseEntity
{
    public Guid CompanyId { get; set; }

    /// <summary>Haftanın PAZARTESİ günü — planın kimliği.</summary>
    public DateTime HaftaBaslangici { get; set; }

    /// <summary>Ödeme günü, varsayılan cuma.</summary>
    public DateTime OdemeGunu { get; set; }

    public OdemePlaniDurumu Durum { get; set; } = OdemePlaniDurumu.Taslak;

    public Guid? HazirlayanUserId { get; set; }
    public DateTime? OnayaSunulmaAnUtc { get; set; }

    public Guid? OnaylayanUserId { get; set; }
    public DateTime? OnaylanmaAnUtc { get; set; }

    public DateTime? KapanmaAnUtc { get; set; }

    public ICollection<OdemePlaniSatiri> Satirlar { get; set; }
        = new List<OdemePlaniSatiri>();

    /// <summary>Onay anında GÖRÜLEN bakiyeler (B1).</summary>
    public ICollection<OdemePlaniHesapBakiyesi> HesapBakiyeleri { get; set; }
        = new List<OdemePlaniHesapBakiyesi>();
}

/// <summary>
/// PLANIN GÖSTERDİĞİ BAKİYE — HESAP BAZINDA SAKLANIR (B1).
///
/// Banka bakiyesi bu sistemde SAKLANMIYOR, hareketlerden anlık
/// türetiliyor (`OpeningBalance + girişler - çıkışlar`, ölçüldü).
/// Dolayısıyla "onay anındaki bakiye" diye bir kayıt yoktu.
///
/// ONAY, BİR SAYIYA BAKILARAK VERİLEN KARARDIR. Yeniden kurulamayan
/// bir onay denetlenebilir değildir: aylar sonra "bu ödemeyi neden
/// onayladın" sorusunun cevabı, o gün ekranda görünen bakiyedir.
///
/// K9'UN İKİ HÂLİNİ TEK MEKANİZMADA BİRLEŞTİRİR: bakiye ister
/// hesaplansın ister elle girilsin, plan GÖSTERİLENİ saklar.
///
/// YENİDEN HESAPLAMA AÇIK İSTEKLE OLUR (B2): ekran her açılışta bütün
/// hareketleri taramaz — sakladığı değeri gösterir. "Bakiyeyi yenile"
/// denince yeniden hesaplanır, yeni değer yine saklanır ve kimin
/// yenilediği kayda geçer.
/// </summary>
public sealed class OdemePlaniHesapBakiyesi : BaseEntity
{
    public Guid OdemePlaniId { get; set; }
    public OdemePlani OdemePlani { get; set; } = null!;

    public Guid CashAccountId { get; set; }

    /// <summary>Ekranda GÖRÜLEN tutar — hesaplanmış ya da elle girilmiş.</summary>
    public decimal GosterilenBakiye { get; set; }

    public BakiyeKaynagi Kaynak { get; set; }

    public DateTime OlcumAnUtc { get; set; }
    public Guid? OlcenUserId { get; set; }
}

/// <summary>
/// PLAN SATIRI — ONAY BU SEVİYEDE VERİLİR (K1).
///
/// Plan bütün olarak onaylanmaz. GM'nin kararları satır satır:
/// öde / ödeme / şu kadar öde / çekle öde (vade belirterek).
/// </summary>
public sealed class OdemePlaniSatiri : BaseEntity
{
    public Guid OdemePlaniId { get; set; }
    public OdemePlani OdemePlani { get; set; } = null!;

    public Guid CurrentAccountId { get; set; }

    public decimal OnerilenTutar { get; set; }
    public OdemeYontemi Yontem { get; set; }

    /// <summary>Yöntem çekse vade — GM belirler.</summary>
    public DateTime? CekVadesi { get; set; }

    /// <summary>
    /// ÖNCELİK ONAYIN PARÇASIDIR (K7). Para kısıtlıyken sırayı
    /// değiştirmek, kimin parasını alacağını değiştirmektir — biçim
    /// değil, ÖDEME KARARIDIR. Bu yüzden K2 anlık görüntüsüne dahil:
    /// yalnız sırası değişen satır da yeniden onaya gelir.
    /// </summary>
    public int Oncelik { get; set; }

    /// <summary>Çıkış hesabı (kasa/banka).</summary>
    public Guid? CashAccountId { get; set; }

    public string? Aciklama { get; set; }

    // ── KARAR (K1) ────────────────────────────────────────────────
    public OdemeSatirKarari Karar { get; set; } = OdemeSatirKarari.Bekliyor;
    public Guid? KararVerenUserId { get; set; }
    public DateTime? KararAnUtc { get; set; }

    /// <summary>Kısmi onayda GM'nin belirlediği tutar.</summary>
    public decimal? OnaylananTutar { get; set; }

    // ── K2 ANLIK GÖRÜNTÜSÜ ────────────────────────────────────────
    //
    // ONAYDAN SONRA DEĞİŞEN SATIR ÖDENMEZ. Uygulama anında güncel
    // değerler bunlarla karşılaştırılır; fark varsa satır yeniden
    // onaya döner.
    //
    // BU PAKETİN EN KRİTİK KURALI: onaydan sonra tutarı
    // değiştirilebilen bir sistemde onay hiçbir şey ifade etmez.
    public Guid? OnayliCurrentAccountId { get; set; }
    public decimal? OnayliTutar { get; set; }
    public OdemeYontemi? OnayliYontem { get; set; }
    public DateTime? OnayliCekVadesi { get; set; }
    public int? OnayliOncelik { get; set; }
    public Guid? OnayliCashAccountId { get; set; }

    // ── ÖDEME ─────────────────────────────────────────────────────
    public OdemeSatirOdemeDurumu OdemeDurumu { get; set; }
        = OdemeSatirOdemeDurumu.Odenmedi;

    public decimal OdenenTutar { get; set; }

    /// <summary>Çek satırı uygulanınca üretilen verilen çek (D4).</summary>
    public Guid? UretilenChequeId { get; set; }

    // ── DEVİR VE YAŞLANMA (K8) ────────────────────────────────────

    /// <summary>Bu satır hangi satırdan devretti.</summary>
    public Guid? DevrededenSatirId { get; set; }

    /// <summary>Kaç haftadır bekliyor — devirde artar.</summary>
    public int DevirHaftaSayisi { get; set; }

    // ── KAPANIŞ (K10) ─────────────────────────────────────────────
    public OdemeKapanisSebebi? KapanisSebebi { get; set; }
    public string? KapanisAciklamasi { get; set; }

    // ── Y3: kaynak belge bağı — İSTEĞE BAĞLI, bugün boş ───────────
    //
    // Alan şimdiden açılıyor ama ZORUNLU DEĞİL. On bir kontrolün
    // hiçbiri bu bağa dayanmadığı için ileride fatura bazlı takip
    // gelse de kurallar değişmez.
    public Guid? SupplierInvoiceId { get; set; }
}

/// <summary>
/// PLAN DIŞI (ACİL) ÖDEME (K5).
///
/// Acil ödeme YASAK DEĞİL, GÖRÜNMEZ olması yasak. Plana bağlı olmayan
/// her ödeme buraya düşer ve BİR SONRAKİ HAFTANIN PLANININ BAŞINDA
/// listelenir: kim, ne zaman, ne kadar, neden.
/// </summary>
public sealed class PlanDisiOdeme : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Guid CurrentAccountId { get; set; }

    public decimal Tutar { get; set; }
    public DateTime OdemeTarihi { get; set; }
    public Guid? CashAccountId { get; set; }

    /// <summary>SEBEP ZORUNLU — gerekçesiz acil ödeme denetlenemez.</summary>
    public string Sebep { get; set; } = string.Empty;

    /// <summary>Hangi haftanın planında listelendi (null ise henüz listelenmedi).</summary>
    public DateTime? ListelendigiHafta { get; set; }
}
