namespace EnderunAI.Api.Models.Expenses;

/// <summary>
/// Gider merkezinin türü — "nereye harcadık" ekseni.
///
/// AYRI TABLO AÇILMADI: merkezler zaten var olan kayıtlardır (şube,
/// proje, şantiye). İkinci bir "gider merkezi tanımları" listesi
/// tutulsaydı iki liste kaçınılmaz olarak ayrışırdı — yeni açılan bir
/// şantiye gider merkezinde görünmez, kapanan bir proje orada
/// yaşamaya devam ederdi. Merkez, kaynağından TÜRETİLİR.
///
/// Departman kırılımı bilinçli olarak yok (v2): bugün personelin
/// departmanı yönetilmiyor, olmayan bir eksende rapor üretmek boş
/// sütun demek.
/// </summary>
public enum ExpenseCenterType
{
    /// <summary>
    /// Merkez/ofis. ŞUBE BAŞINA: <c>Branch</c> kaydına bağlanır.
    /// Tek bir "merkez" sabiti olsaydı ikinci şube açıldığında
    /// geçmiş kayıtların hangi ofise ait olduğu ayrıştırılamazdı.
    /// </summary>
    Branch = 0,

    /// <summary>Proje geneli — şantiyeye inmeyen proje gideri.</summary>
    Project = 1,

    /// <summary>Şantiye. Projesi üzerinden de toplanabilir.</summary>
    ProjectSite = 2
}
