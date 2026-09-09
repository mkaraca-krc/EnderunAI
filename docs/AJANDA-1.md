# AJANDA/1 — kişisel + atanabilir ajanda (TASARIM, henüz inşa edilmedi)

Bu belge ÖLÇÜMÜ ve TASARIM KARARLARINI taşır. Kod yazılmadı; inşa kararı
Mehmet Bey'de.

## Mimari karar (verildi)

**İkinci bir iş emri sistemi kurulmayacak.** Atama, izin, bildirim ve
denetim `WorkTask`'ta zaten var. Ajanda yeni bir varlık değil,
`WorkTask`'ın bir KIND'ı.

## A1 — ölçüm (2026-09-09, canlı)

| ne | ölçüm |
|---|---|
| `WorkTasks` silinmemiş satır | **2** (ikisi de `IsEmri`, tek yaratıcı) |
| `Kind = Hatirlatma` satır | **0** |
| `WorkTaskKind` | `Belirsiz=0` (veritabanı kısıtıyla yasak), `IsEmri=1`, `Hatirlatma=2` |
| masraf merkezi zorunluluğu | `MasrafMerkeziKurali.cs:128` — `Hatirlatma` zaten muaf |
| `WorkTasks` okuma yolu | `WorkTasksController` içinde **11 ayrı `ApplyScope`** çağrısı |
| `ApplyScope(IQueryable<WorkTask>)` | `FinanceScopeExtensions.cs:184` — `scope.HasGlobalAccess ? query : …` |

### Alan alan taşıyabilirlik

| ajanda ihtiyacı | WorkTask karşılığı | eksik |
|---|---|---|
| tarih zorunlu | `DueDate` (nullable) | Kind=Ajanda için kısıt |
| saat isteğe bağlı | `DueDate` saat taşıyabilir | "saatli mi" bayrağı |
| ÖNEMLİ işareti | `Priority` | yeni alan gerekmez |
| durum açık/bitti | `Status` + "kendine açtıysa tek adımda kapanır" | hazır |
| atama | `AssignedToUserId` / `AssignedByUserId` | hazır |
| silme yok | `IsDeleted` | Ajanda'da silme kapatılacak |
| kişisel/gizli | **yok** | en büyük eksik — aşağıda |

### Kind önerisi

`Hatirlatma`'nın canlıda **0 satırı var**. Bu yüzden `Hatirlatma=2` →
`Ajanda=2` olarak genişletilmesi öneriliyor: sayı korunur, geçmiş satır
yeniden yorumlanmaz (çünkü geçmiş satır yok), ve birbirine çok benzeyen
iki tür bırakılmaz. Gerekçe ölçüme dayanıyor, tercihe değil.

Genişleyen invaryant açıkça yazılmalı: `Hatirlatma` "kimseye iş
yüklemez" diye belgelenmişti; Ajanda **atanabilir**.

## G1–G3 — gizlilik (işin en riskli yeri)

`ApplyScope` küresel kapsamlı kullanıcı (Admin, Genel Müdür) için
sorguyu **olduğu gibi** geçiriyor ve `WorkTasksController.GetAll`
sahiplik/atanma süzgeci **hiç** uygulamıyor.

**Kapatma yolu: EF Core genel sorgu süzgeci** (`AppDbContext`'te bu
mekanizma zaten 170 yerde kullanılıyor), okuyucuları değil kaynağı
düzelten seçenek (Kural 79):

```
WorkTask üzerinde: Kind != Ajanda
                   VEYA CreatedByUserId == ben
                   VEYA AssignedToUserId == ben
                   VEYA AssignedByUserId == ben
```

11 okuma yolunu birden kapatır ve `HasGlobalAccess`'e hiç bakmaz.
`IgnoreQueryFilters()` ile atlanabildiği için atlamayı yasaklayan bir
test kapının parçasıdır.

**Sonda:** `MuafUcKosumu`'nun bir örneği olarak yazılacak
(`yabanciRol: "Admin"`). Üçüncü kez aynı şekil yazılmayacak.
**(a) ayağı yeşil olmadan bu özellik YAYINLANMAZ.**

## A2 — Hızır'ın kontrolcüyü atlaması

`HizirActionTools` `db.WorkTasks.Add()` ile doğrudan yazıyor. Bugün
güvenli, çünkü `Kind`, `AssignedToUserId` ve `AssignedByUserId`
sabit yazılıyor. Ajanda atanabilir olunca bu yol `agenda.assign`
iznini **hiç görmez**.

**Öneri:** atama kararı `AjandaAtamaKurali` adlı saf sınıfa çıkarılsın;
hem kontrolcü hem Hızır onu çağırsın, çağrının varlığını bir test
korusun — `MasrafMerkeziKurali` + `HizirMerkezKuraliTests` deseninin
aynısı. `Kind` sabiti de o kurala taşınmalı.

## ÖN KOŞULLAR — bunlar olmadan AJANDA/1 yayınlanmaz

1. **BİLDİRİM/1 kapalı olacak.** Ajanda hatırlatmaları KİŞİSEL BİLDİRİM
   olarak gidecek. `TransitionAsync`'teki açık kapanmasaydı bir kullanıcı
   başkasının ajanda hatırlatmalarını susturabilirdi — bildirimin sahibi
   kontrol edilmiyordu. Kanonik kural: `Security/BildirimErisimKurali.cs`.
   *(2026-09-09'da kapatıldı; sondası `MuafUcAliciKapisiTests`.)*
2. **G3 sondasının (a) ayağı yeşil.** Admin başkasının kişisel ajanda
   kaydını ne listede görecek ne doğrudan çağırabilecek.
3. **Hatırlatma zamanlayıcısı NÖBET/1 altyapısını kullanacak** — ikinci
   zamanlayıcı kurulmayacak — ve aynı hatırlatma iki kez gitmeyecek
   (gönderildi kaydı).

## v1'de olmayacaklar (bilerek)

Tekrarlayan kayıt, toplantı daveti/kabul, paylaşılan takvim,
Google/Outlook eşitleme, ekli dosya. Bunlar v2.
