# DURUM — açık işler ve yol haritası

> ## BURADAN BAŞLA (temiz bağlam için)
>
> **Bu dosyayı baştan sona oku, sonra hiçbir şeye dokunmadan
> "kararsızlık ölçümü"nden devam et.**
>
> Okuma sırası:
> 1. **§8 ELENEN HİPOTEZLER** — bunlar ÖLÇÜMLE çürütüldü, tekrar
>    kovalama. Negatif bilgi en kolay kaybolan şeydir.
> 2. **§5 ÇALIŞMA DİSİPLİNLERİ** — 18 kural. Özellikle 12-18:
>    test koşuları serileştirilir; sonda harness'i her yolda yedeği
>    geri koyar; sonda sonrası `git diff` okunur; teşhiste sıra
>    ölçümün AYIRICILIĞINA göre kurulur.
> 3. **§7 KARARSIZ SUITE** — 3 sınıflık koşuda kararsızlık 10 turda
>    ÜRETİLEMEDİ; güçlü şüphe, "kararsızlık"ın aslında §8'deki sabotaj
>    penceresi olduğu. Tam tur ölçümü açık. **Fixture'a hâlâ DOKUNMA.**
>
> Sıradaki iş, bu sırayla:
> 1. Kararsızlık ölçümü: en çok düşen üç sınıfı (PersonnelDataIntegration,
>    PersonnelWorkLocation, SalaryPrivacy) N kez koşup düşen test
>    adlarını biriktir. Aranan desen → işaret ettiği mekanizma:
>    hep aynı testler → testin kendi durumu; hep ilk sınıf → host/migration
>    yarışı; hep ilk istek → bağlantı havuzu ısınması; rastgele → derin yarış.
> 2. Mekanizmaya göre düzeltme (varsayımla değil).
> 3. R3a yığın 2 — İK ailesi, 10 kontrolcü. Bekçi testindeki
>    (`DataScopeSeamTests`) istisna listesi borç sayacıdır; her yığın
>    onu kısaltmalı.
>
> Test koşumu için DB bağlantısı: `deploy/scripts/safe-deploy.sh`
> içindeki `resolve_test_db_connection` deseni — `/etc/enderunai/backend.env`
> içinden `DB_CONNECTION` okunup `enderun_ai` -> `enderun_ai_test`
> değiştirilir.

Bu dosya UZUN OTURUM KAYBINA KARŞI yazıldı. Bağlam temizlendiğinde
buradan devam edilebilir. Ayrıntılı bulgular `TEMIZLIK-TARAMASI.md`
içinde (yaklaşık 890 satır, kronolojik).

Son güncelleme: 2026-08-17
Dal: `feature/hr-frontend-sync-20260726`

---

## 1. Nerede kaldık

### Bitti ve yayında

| İş | Commit | Durum |
|---|---|---|
| ROL-UI R1 — rota/menü kapıları | — | yayında |
| ROL-UI R2/1..R2/4 — eleman (düğme) seviyesi yetki | `dd67eae0` | **KAPANDI**, yayında |
| Ölü PDF düğmeleri kaldırıldı | `f8b86d15` | yayında |
| Ölü kesinti politikası ekranı kaldırıldı | `dd67eae0` | yayında |
| Perakende Satış V1 (çekirdek) | — | yayında |
| Koyu tema + Redwood dil birliği | — | yayında |
| Birim fiyat ölçeği (max 6 hane) | — | yayında |

**R2 kapanış tablosu** — yazan aksiyonu olan 97 ekran:
düğme kapısı 69, rota kapısı 21, satır içi izin 15, kapısız **0**.
Kapanış sözleşmesi testi var (`tests/module-actions.test.ts`,
"yazan aksiyonu olan her ekran bir kapı taşıyor"): yeni bir kapısız
ekran eklenirse düşer.

### Şu an üzerinde çalışılan: R3a — veri kapsamı zorlaması

**Neden R3 ikiye ayrıldı:** merdivende R3 "UserDataScope arayüzü" diye
tanımlanmıştı; ölçüm asıl boşluğun ARAYÜZDE DEĞİL ZORLAMADA olduğunu
gösterdi. Kapsam 122 kontrolcünün 10'unda uygulanıyordu. Kapsam atama
arayüzünü önce yapmak YANLIŞ GÜVEN üretirdi.

- **R3a (zorlama, backend)** — devam ediyor
- **R3b (arayüz)** — R3a'dan sonra

**R3a yığın 1 (personel ailesi) — TAMAMLANDI:**
- `Security/ScopedData.cs` — kapsamlı okuma DİKİŞİ (yeni). Kontrolcülerin
  kapsam taşıyan varlıkları okuduğu tek yol. Fail-closed.
- `Security/CurrentDataScopeService.cs` — `Apply(IQueryable<Personnel>)`
  aşırı yüklemesi eklendi (kural tek kaynakta).
- `Controllers/PersonnelController.cs` — liste, detay, `veri-eksikleri`
  dikişe taşındı; `ICurrentDataScopeService` bağımlılığı sıfırlandı.
- `Tests/DataScopeSeamTests.cs` — BEKÇİ TEST (yeni): kontrolcülerde ham
  `db.Personnel` erişimi ancak gerekçesi yazılı istisna listesindeyse
  mümkün. Liste bugün 18 kontrolcü; bu BORÇ, her yığın kısaltacak.
- `Tests/PermissionAndScopeTests.cs` — 4 test eklendi (şantiye şefi
  yalnız kendi şantiyesini görür / kapsam dışı detayda 404 /
  kapsamsız kullanıcı etkilenmez / **ÜÇLÜ KAPSAM MATRİSİ**).

**ÜÇLÜ KAPSAM MATRİSİ** (`UcluKapsamMatrisi_...`) — kalıcı regresyon.
Veri kapsamının üç sınıfı var ve üçü de test edilmek zorunda:
  1. Admin (global erişim ROL ADINDAN)
  2. `All` kapsamlı Admin OLMAYAN (global erişim UserDataScope satırından)
  3. Dar kapsam (yalnız kendi şantiyesi)

İKİNCİ SINIF UZUN SÜRE KÖR NOKTADAYDI: bu kod tabanındaki bütün
entegrasyon testleri `test.admin` ile koşuyordu. Genel Müdür, İK
Sorumlusu, Finans Sorumlusu gibi CANLIDAKİ ÇOĞU kullanıcı ikinci
sınıfta ve hiç kapsanmıyordu.

Matrisin gerçek bir hatayı yakaladığı KONTROLLÜ SONDAYLA kanıtlandı:
`Apply` içindeki global-erişim dalı bozulduğunda 1. ve 2. ayak düştü,
**3. ayak geçmeye devam etti** — yani dar kapsamı test etmek YETMİYOR.

**NEDEN GLOBAL SORGU SÜZGECİ (HasQueryFilter) KULLANILMADI** — dört
gerekçe `Security/ScopedData.cs` docstring'inde; en somutu:
`PersonnelController` kimlik numarası tekilliğini şirket süzgeci
OLMADAN kontrol ediyor (aynı TC iki şirkette açılmamalı diye). Global
süzgeç altında bu kontrol kapsam dışı kaydı göremez ve MÜKERRER TC
sessizce oluşur. Ayrıca 151 mevcut global süzgeç var ve bordro/muhasebe/
içe aktarma sorguları da süzülür → sessizce eksik rakam.

### R3a YIĞIN 2 (İK ailesi) — TAMAMLANDI

**Risk yüzeyi ölçümle daraltıldı.** Yığın 2'de 13 kontrolcü listelenmişti;
hepsi aynı ağırlıkta değil. Şantiye Şefi ve Formen'in (tek `SiteOnly`
kapsamlı roller) izin listesi okundu:

`AiUse · DashboardView · Documents* · Inventory* (yalnız şef) ·
**PersonnelView** · Purchasing* (yalnız şef) · ScheduleView ·
SiteReports* · SitesView · VehicleView (yalnız şef)`

Yani `AttendancePayrollView`, `ExtraPaymentView`, `PersonnelDocumentView`,
`SalaryView` bu rollerde YOK. Dolayısıyla `LeaveBalance`,
`PayrollReadiness`, `AttendanceSheet`, `PersonnelExtraPayments`,
`PersonnelCashPayments`, `PersonnelDocuments` uçları o rollere kapalı —
kapsam yine eklenmeli ama ACİL DEĞİL.

**Acil olan altı uç `personnel.view` ile korunuyordu.** Beşi dikişe
taşındı:

| Kontrolcü | Kapatılan sızıntı |
|---|---|
| `PersonnelOvertimeController` | kapsam dışı personelin kimliği + fazla mesai onayı |
| `HrCareerController` | terfi, ünvan, proje değişikliği geçmişinin TAMAMI |
| `HrProjectLaborCostsController` | maliyet satırlarının isimle eşleşmesi |
| `HrAssetsController` | zimmet geçmişi (hangi alet, tarih, bedel) |
| `PersonnelDutiesController` | kim nereye, ne kadar süreyle görevlendirildi |

`companyId` çağırandan geliyordu ve TEK BAŞINA YETMEZ: kullanıcının o
şirketi görme hakkı ayrıca sorulmalı. Dikiş bunu personel üzerinden
zorluyor; kapsam dışı kayıt **404** döner (403 değil — kaydın varlığı
da sızmamalı).

**Test altı ucu DOĞRUDAN çağırıyor** (`SantiyeSefi_IkUclarindaKapsam
DisiPersoneliGoremez`): kapsam dışı görev/kariyer listede yok, kapsam
dışı personelin geçmişi 404, KENDİ şantiyesindeki personel açılabiliyor
(kapsam süzgeci işi engellemiyor), fazla mesai ve zimmet analizi 404.

Üç sonda da yakaladı. `PersonnelTerminationsController` yığın 3'e
kaldı — `db.PersonnelTerminations` üzerinden çalışıyor, personel
varlığına dokunmuyor; ayrı bir süzgeç deseni gerekiyor.

**Harness hatası (kayıt):** sonda betiğinin başındaki `: > "$R"` her
çağrıda sonuç dosyasını sıfırlıyordu; A ve B'nin sonuçları C tarafından
silindi ve tekrar koşuldu. Sondalar tek tek çağrılan bir betikte sonuç
dosyası SIFIRLANMAZ, eklenir.

### R3a YIĞIN 2 — İK ailesi (TAMAMLANDI, 2026-08-19)

**Risk yüzeyi ÖLÇÜMLE daraltıldı.** Yığın 2'de 13 kontrolcü kayıtlıydı;
hepsi aynı aciliyette değil. Dar kapsamlı roller (Şantiye Şefi, Formen —
`RoleDataScopePolicy.SiteOnly`) yalnız şu izinleri taşıyor:
`personnel.view` var, **`attendance-payroll.view` YOK**,
**`extra_payment.view` YOK**, `personnel.document.view` YOK.

Yani `LeaveBalance`, `PayrollReadiness`, `AttendanceSheet`,
`PersonnelExtraPayments`, `PersonnelCashPayments`, `PersonnelDocuments`
o rollere zaten KAPALI. Acil olan, `personnel.view` ile korunan altı
kontrolcüydü.

**Dikişe bağlananlar (5):** `PersonnelOvertime`, `HrCareer`,
`HrProjectLaborCosts`, `HrAssets`, `PersonnelDuties`.
(`PersonnelTerminations` ham erişimi `db.PersonnelTerminations`
üzerinde — o varlık kapsam taşımıyor, ayrı ele alınacak.)

**Kapatılan sızıntı somut:** şantiye şefi başka şantiyedeki personelin
görevlendirmesini (kim nereye, ne kadar süreyle), kariyer hareketini
(terfi, ünvan, proje değişikliği, maaş alanları), zimmet geçmişini ve
fazla mesai onay durumunu görebiliyordu. `companyId` ÇAĞIRANDAN
geliyordu ve tek başına yetmiyor — kullanıcının o şirketi görme hakkı
ayrıca sorulmalı.

**Test doğrudan UÇLARI çağırıyor** (`SantiyeSefi_IkUclarindaKapsam
DisiPersoneliGoremez`): kapsam dışı görev listede yok, kariyer hareketi
yok, kapsam dışı personelin geçmişi **404** (403 değil — kaydın varlığı
da sızmasın), kendi şantiyesindeki personel AÇILABİLİYOR (kapsam
süzgeci işi engellemiyor), fazla mesai ve zimmet analizi 404.

Üç sonda da yakaladı. Tam tur **2258/2258**.

**Testi yazarken iki kendi hatam çıktı:** kullanıcı adını `[..40]` ile
keserken 23 karakterlik string'i patlattım; üç rotayı uydurmuşum
(gerçekleri `hr/gorevlendirmeler` ve `hr/personel/{id}/fazla-mesai`).

### R3a kalan yığınlar

Şantiye kapsamlı iki rol (`SiteOnly` = **Şantiye Şefi** 19 izin,
**Formen** 10 izin) kapsamsız okuma uçlarına erişiyor:
Şantiye Şefi **39**, Formen **24**.

- **yığın 2 — İK ailesi:** `HrCareer`, `HrAssets`, `HrProjectLaborCosts`,
  `PersonnelOvertime`, `PersonnelExtraPayments`, `PersonnelDuties`,
  `PersonnelDocuments`, `PersonnelTerminations`, `PayrollReadiness`
- **yığın 3:** `Inventory`, `Warehouses`, `PurchaseRequests`, `Vehicles`,
  `ToolServiceRequests`, `ProjectDailyReportsRollup`, `Projects`,
  `SubcontractorContracts`, `IsgDashboard`
- **`HrRecruitment` — KARAR VERİLDİ (2026-08-18), ADAY TARAFI KAPANDI.**
  İşe alım Enderun'da MERKEZİ (kullanıcı kararı): adayları İK/merkez
  görür. Üç katman kuruldu, commit `9cab1f55`:
    1. okuma uçları `personnel.view` -> `personnel.manage`
       (Şantiye Şefi / Formen / İSG Sorumlusu erişimi kaybetti; etki
       RoleCatalog'dan ölçüldü, başka rol etkilenmedi)
    2. aday listesi dikişte ŞİRKET kapsamıyla süzülüyor
       (`JobCandidate` yalnız CompanyId taşır — ilan projeye
       bağlanabilir, aday havuzu ortak; merkezi modelin veri karşılığı)
    3. TC kimlik numarası MASKELİ — `personnel.create` ister,
       fail-closed, istemciye hiç gitmez
  Üç sonda da yakaladı (yetki geri çekilirse / maske hep açıksa /
  maske fail-open olursa).

  **KAPATILAN AÇIK:** `GET hr/recruitment/candidates` gövdesi tam olarak
  `db.JobCandidates.AsNoTracking().OrderByDescending(...)` idi — hiçbir
  süzgeç yok, `companyId` bile yok — ve `personnel.view` saha
  rollerinde olduğu için onlar bütün şirketlerdeki tüm adayları TC
  kimlik numarasıyla listeleyebiliyordu.

  **KALAN İŞ:** ilan / başvuru / mülakat uçları hâlâ ham `db` erişiyor
  → R3a yığın 2'de dikişe alınacak.

  **KABUL EDİLEN SINIR:** maskenin DAVRANIŞI uçtan canlı doğrulanamıyor;
  `personnel.manage` olup `personnel.create` OLMAYAN rol katalogda yok.
  Bugün asıl korumayı yetki daralması yapıyor, maske gelecek içindir.

---

## 2. KULLANICI KARARI BEKLEYEN

### (a) Fiyat farkı / eskalasyon — sözleşmelerde var mı?

Bu tek cevap iki şeyi belirliyor.

**Bugünkü durum:** `ProgressPayment.PriceDifferenceAmount` ELLE giriliyor
ve Excel çıktısına, finans panosuna, hakediş takibine ve **kâr hesabına**
(`HakedisProfitService`) akıyor — yani eskalasyon pratikte kullanılıyor.
Eksik olan yalnızca OTOMATİK HESAP: `POST price-difference-calculations/calculate`
ucu backend'de hiç yok (model de yok). O ekrandaki panel KALDIRILDI.

**Ana veri ekranları KORUNDU** (`fiyat-farki/profiller`, `endeksler`) —
model kamu formülünün tam kendisi (A, B1..B5, C katsayıları + Yİ-ÜFE
alt endeksleri). Ama o tabloları OKUYAN hiçbir kod yok: atıl.

**ÖLÇÜLDÜ (2026-08-18) — cevap: hiç kullanılmamış.** Veritabanı erişimi
sonraki oturumda açıktı, sayıldı:

| Tablo | Satır |
|---|---|
| `price_difference_profiles` | **0** |
| `price_difference_index_periods` | **0** |
| `price_difference_coefficients` | **0** |
| fiyat farkı girilmiş hakediş | **0** (toplam 1 hakediş, tutar 0,00) |
| karşılaştırma: `projects` / `personnel` | 4 / 81 |

Kanıtın gücü: "hakedişlerde fiyat farkı yok" TEK BAŞINA zayıf (sistemde
yalnız 1 hakediş var). Ama **profil ve endeks tablolarının tamamen boş
olması güçlü** — 81 personel ve 4 proje girilmiş bir kurulumda fiyat
farkı ana verisine hiç dokunulmamış.

**Öneri (kullanıcı onayı bekliyor):** `fiyat-farki/profiller` ve
`endeksler` ekranları + menü bölümü kaldırılsın; **backend model ve
tablolar KALSIN** (veri kaybı yok, migration gerekmez, şema hazır).
`ProgressPayment.PriceDifferenceAmount` elle giriş yolu KALIR.

**Karar verilirse netleşmesi gerekenler:** formül (kamu Yİ-ÜFE standardı
mı sözleşmeye özel endeks mi), hesabın hakedişe hangi aşamada işleneceği,
elle girilen değerle çakışma, geçmiş hakedişlerin yeniden hesabı.

### (b) PDF — KAPANDI, karar gerekmiyor

Beş PDF ucu ölüydü (backend'de `api/reports` rotası ve PDF kütüphanesi
yok). İki görünür düğme kaldırıldı, `report.service.ts` silindi. Gerekçe:
her iki ekranda da ÇALIŞAN yazdırma sayfası ölü düğmenin yanındaydı
(`hakedis/[id]/yazdir`, `satin-alma/siparis/[id]/yazdir`) — PDF yeteneği
tarayıcı yazdırma penceresiyle zaten sağlanıyor. Sunucu tarafı PDF
ayrıca istenirse (imzalı çıktı, e-posta eki) ayrı paket.

---

## 3b. STOK/DEPO YENİDEN YAPILANDIRMA (yeni paket, 2026-08-19)

Denetim raporu yayınlandı; on faz planlandı. **Üç iş kararı alındı:**
GR/IR ara hesabı kullanılacak · kartlar silinmeyip pasife alınacak ·
kod sırası şirket başına.

**S0 (bitti) — arşivin anlam kazanması.**

Denetimde kartların `IsDeleted` ile pasife alınmasını önermiştim;
kodu okuyunca `BaseEntity`'nin HEM `IsActive` HEM `IsDeleted`
taşıdığı görüldü. Doğru araç `IsActive`: global sorgu süzgecine
dokunmuyor, dolayısıyla mevcut belgeleri riske atmıyor. `IsDeleted`
seçilseydi arşivlenmiş karta bağlı taslak faturayı işlerken
`SupplierInvoiceStockPoster` sözlükten okuyup
**`KeyNotFoundException` ile çökecekti** (kartları süzgeçli okuyor).

**ASIL BULGU: `IsActive` vardı ve HİÇBİR ŞEY İFADE ETMİYORDU.**
Yalnız `GoodsReceiptService` ona uyuyordu; stok listesi/seçici,
perakende ürün arama ve alış faturası doğrulaması yok sayıyordu.
Yani kart arşivlense bile yeni belgelerde çıkmaya devam ediyordu —
"temiz başlangıç" bu hâliyle imkânsızdı.

Kural gerçek yapıldı:
- Stok listesi arşivi VARSAYILAN olarak gizler; yönetim ekranı
  `includeInactive=true` ile açıkça ister (kart geri açılabilsin).
- Perakende ürün arama arşivlenmiş kartı satışa çıkarmaz.
- Alış faturası arşivlenmiş karta yeni kalem bağlanmasını reddeder.

**Teste bağlanan ayrım:** arşiv YENİ belgeyi engeller, MEVCUT belgeyi
bozmaz. Fatura kalemi kendi `Description`/`Unit` alanlarını taşıyor ve
kart bağı opsiyonel; arşivlenen kart geçmiş faturayı görünmez yapmıyor.

**Veri durumu (ölçüldü):** stok hareketi 0, depo stoğu 0, mal kabul 0,
sipariş 0, perakende satış 0. Yani "temizlik" için silinecek hareket
YOK. 9 kart pasife alındı; 10 alış faturası kalemi onlara bağlı olduğu
için silinemezdi.

İki sonda da yakaladı. Tam tur 2262/2262.

**S1 (bitti) — kategori + özellik sistemi.**

KARAR: kategori SİSTEM GENELİ, kart şirkete ait. "Kablo tavası" her
şirkette aynı şeydir; iki ayrı sette tutmak mükerrer bakım ve zamanla
ayrışan özellik listeleri doğururdu.

Beş yeni varlık: `InventoryCategory`, `InventoryCategoryUnit`,
`InventoryAttribute`, `InventoryAttributeOption`,
`InventoryItemAttributeValue`. Kart `InventoryCategoryId` ile bağlandı
(nullable — arşivlenmiş eski kartlar kategorisiz).

**14 kategori tohumlandı** (12 STANDART + 2 SERBEST). Kullanıcı
kararları koda ve teste geçti:
- **Çok birimli kategori**: kategori İZİN VERİLEN birim listesi tutar,
  kart birini seçip sabitler. Topraklama `[adet, metre]` (bakır şerit
  metre, toprak çubuğu adet), Sarf `[adet, paket, kg]`. Tek birimli
  kategorilerde liste tek elemanlı — davranış aynı.
- **Kaçak akım rölesi kutup 2P/4P**, otomatınki ayrı `1P/3P/1P+N`.
  Şablonda verilmemişti; otomatınkini kopyalamak elektriksel olarak
  yanlış veri üretirdi.
- **Kablo merdiveni** ölçü/kaplama listelerini tavayla PAYLAŞIR.

**Tohum sadece ekler, güncellemez** — kullanıcının ekrandan yaptığı
değişiklik yeniden başlatmada ezilmiyor (testli).

**ROTA ÇAKIŞMASI ÇÖZÜLDÜ:** eski `InventoryController` da
`api/inventory/categories` sunuyordu — kartların serbest metin
`Category` alanından DISTINCT. O uç kaldırıldı. Zaten ölmüştü: canlıda
bir kartın kategorisi "TURAN" (tedarikçi adı) yazıyordu, dörtte boştu.
Eski testi SİLİNMEDİ, yeniden kapsamlandı: aynı rotanın artık kategori
VARLIKLARINI döndürdüğünü ve serbest metin değerlerinin gelmediğini
doğruluyor.

Yönetim ekranı `/depo-stok/kategoriler` (menü + rota yetkisi kayıtlı).
Üç sonda da yakaladı. Tam tur 2270/2270.

**S2 (bitti) — otomatik kod, otomatik ad, mükerrer engeli.**

KARARLAR: kod atomik düz sıra (100001+) şirket başına · ad kategori +
özellik `Display` değerlerinden · mükerrer kontrolü ŞİRKET İÇİ.

- **Kod**: `InventoryCodeService`, tek `INSERT … ON CONFLICT DO UPDATE
  … RETURNING`. Mevcut `IDocumentNumberService` kullanılamadı: (1)
  `ÖNEK-YIL-NNNNNN` üretiyor, ön ek istenmiyor; (2) sıra YILA BAĞLI —
  2027'de açılan kart 2026'nın 100001'iyle çakışırdı; (3) artırması
  KİLİTSİZ, eşzamanlı iki kart aynı numarayı alıp tekil indekste
  çökerdi. Yıl kolonuna 0 yazılıyor: "yıl kırılımı yok".
- **Ad**: kategori adı + özellik gösterimleri, ÖZELLİK sırasına göre.
- **Mükerrer**: `InventoryItem.AttributeSignature` + `(CompanyId,
  AttributeSignature)` KISMİ tekil indeks (SERBEST kartlarda null).
  Sorguyla kontrol yarışa açıktı; şimdi dostça mesaj için önce sorgu,
  asıl garanti indeks, yarışta `DbUpdateException` yakalanıyor.
  **Arşivdeki mükerrer "geri açın" diyor** — yoksa arşivleme mükerrer
  engelini delerdi.

**KALDIRILAN YETENEK (bilerek):** fatura ekranındaki İKİ ALANLIK hızlı
kart açma. Tam olarak o kısayol sınıflandırılmamış kart üretiyordu.
Yerine yeni sekmede doğru ekrana yönlendirme kondu.

**SONDANIN KAÇIRMASI EN DEĞERLİ BULGU OLDU:** `BuildSignature`'daki
sıralama kaldırıldığında uçtan uca test yine geçti — çünkü kontrolcü
seçenekleri kategori sırasına göre topluyor ve besleme sırası uca
gelmeden normalleşiyor. O normalleştirme kontrolcünün TESADÜFİ
davranışı; toplama biçimi değişirse imza seçim sırasına bağlı hale
gelir ve mükerrer engeli sessizce delinir. Kural kaynağında saf birim
testleriyle sabitlendi (`InventoryItemComposerTests`, 6 test); sonda
tekrarında YAKALADI.

Tam tur 2286/2286, frontend 354/354.

**S3 (bitti) — konum/raf + QR etiket.**

KARARLAR: varsayılan konum DEPO × KATEGORİ eşleşmesi · konum ÜÇ
seviye (Bölge → Raf → Kat) ama AÇIK bölgede yalnız bölge.

- `WarehouseZone` (raflı/açık) → `WarehouseShelf` → `WarehouseShelfLevel`.
  Raflı bölge toplu kuruluyor: raf sayısı + her rafın kat sayısı.
- `WarehouseCategoryLocation`: bu DEPODA bu KATEGORİ nereye gider.
  Kategori sistem geneli olduğu için varsayılan konum kategoride
  tutulamıyordu — ikinci şirket eklendiğinde YANLIŞ yeri gösterirdi.
- Kart açılırken depoya göre konum otomatik; elle geçilen seçim
  varsayılanın önüne geçer. AÇIK bölgede raf/kat elle gönderilse bile
  TEMİZLENİR.
- QR'a ham kimlik değil URL yazılıyor: telefon kamerasıyla okutunca
  sayfa doğrudan açılsın, ayrı uygulama gerekmesin. Kart QR'ı →
  malzeme sayfası; raf QR'ı → "bu rafta ne var" (yeni ekran).
- A4 etiket ekranı (tek/toplu): QR + ad + kod + konum. QR'lar ÖNCEDEN
  üretilip basılıyor — `window.print()` o an ekranda ne varsa onu
  basar, bekleyemez; sonradan üretilseydi etiketler boş çıkardı.

Mevcut bir sözleşme testi yakaladı: her liste ekranında tazeleme
düğmesi olmalı; etiket ekranında yoktu, eklendi.

Üç sonda da yakaladı. Tam tur 2292/2292, frontend 354/354.

**S4 (bitti) — tek giriş kapısı + kuralların sözleşmeye bağlanması.**

Bu fazda YENİ KURAL KURULMADI; kurallar UNUTULAMAZ hâle getirildi.
Negatif yasağı dört düşüm yolunda da zaten vardı — eksik olan, beşinci
yol yazıldığında kimsenin uyarmayacak olmasıydı.

- `POST inventory/receipts` KALDIRILDI. Siparişe ya da mal kabule bağlı
  değildi: `inventory.create` izni olan biri sadece bir referans
  numarası yazıp stok yaratabiliyordu. Daha kötüsü MALİYET YAZMIYORDU
  (`UnitCost`/`TotalCost` boş, ağırlıklı ortalama güncellenmiyor) —
  yani sıfır maliyetli stok girip stok değeri ile muhasebeyi ilk
  günden ayırıyordu. Canlıda bu uçtan gelmiş hareket YOKTU (stok
  hareketi sayısı 0), kaldırmak veri kaybetmedi.
  Beraberinde: `/depo-stok/giris` ekranı, menü girdisi, servis metodu,
  formun `receipt` modu ve artık kalan `StockReceiptRequest` kaydı.
  Hareketler sayfasındaki "Stok Girişi" düğmesi MAL KABUL'e bakıyor.
- Giriş artık yalnız üç kapıdan: mal kabul (siparişe bağlı, maliyetli),
  iade dönüşü, gerekçeli sayım düzeltmesi.
- SAYIM GEREKÇESİ ZORUNLU oldu (uçta da ekranda da). Sayım düzeltmesi
  belgeye bağlı olmadan stok değiştirebilen TEK yol; gerekçesiz
  bırakılsaydı kaldırdığımız kapı arka taraftan açık kalırdı.
  Davranışı ayrıca E2E testi kanıtlıyor: null/boş/boşluk üçü de 400,
  ve stok DEĞİŞMİYOR (400 dönüp yine de yazsaydı red anlamsızdı).
- `StockMovementContractTests` (5 kural): stok düşüren her yol
  öncesinde AYNI kaydın miktarını kontrol eder · serbest giriş ucu geri
  gelmez · stok artıran her yol maliyet yazar · hareket istekleri birim
  ALMAZ (birim kartta sabit) · sayım düzeltmesi gerekçe ister.

SONDADA İKİ DERS:

1. İlk yazdığım negatif-yasağı kuralı KAÇIRDI. "Dosyada bir yerde
   miktar karşılaştırması ya da 'yetersiz' kelimesi geçsin" diyordu;
   `RetailSaleService` içindeki alakasız bir "iade fişi iptal edilemez"
   mesajı ve `requested.Quantity <= 0` kontrolü, sildiğim GERÇEK
   kontrolün yerine geçti. Kural hiçbir şey korumuyordu. Düzeltmesi:
   her `X.Quantity -=` noktası için, ÖNCESİNDE `X.Quantity <` aranıyor
   — aynı değişken, düşüşten önce. Ayrıca düşüş noktası sayısı 4'ün
   altına inerse test kendini "kural boşalmış olabilir" diye düşürüyor.
2. Sabotaj DERLENEBİLİR olmalı. İlk turda A/C/E sabotajlarım derlemeyi
   bozdu; `dotnet test` hiç koşmadı ve sonuç satırı BOŞ geldi. Boş
   sonuç "test geçti" değildir — sonda harness'i artık boş sonucu
   `SONUC YOK -> error CSxxxx` diye yazıyor, sessizce yeşil saymıyor.

Yedi sondanın hepsi yakaladı (A1 kontrol silindi · A2 kontrol yanlış
nesneye bakıyor · A3 transferde kontrol silindi · B serbest giriş geri
geldi · C gerekçe kalktı · D maliyetsiz yeni yol · E isteğe birim
eklendi). Şema değişmedi, migration yok.

**KULLANICI KARARLARI (2026-08-19, stok paketi devamı):**
1. SATIŞ BELGESİ: ikisi de olacak. Stoktan mal satışı perakende
   ekranından, stoksuz/hizmet satışı faturadan; ama satış faturası da
   stok bağı (`InventoryItemId`) alacak ki fatura üzerinden stoklu
   satış da yapılabilsin. İkisi AYNI muhasebe mantığını kullanacak:
   stoklu → 621 maliyet + 600 gelir, stoksuz → yalnız gelir.
2. STOK HESABI: kategori bazında. Ticari mal → 153/621, sarf/proje
   malzemesi → 150/740. VARSAYILAN HEPSİ SARF — ağırlıklı taahhüt işi.
   Satılabilirler sonradan MALİ MÜŞAVİR ONAYIYLA ticari mal işaretlenir.
3. SIRA: ÖNCE GİRİŞ (S6), sonra satış (S5). Gerekçe ölçümden çıktı.

**ÖLÇÜM (2026-08-19): STOK İLE MUHASEBE ARASINDA HİÇ BAĞ YOK.**
153 Ticari Mallar 0 fiş satırı · 621 Satılan Ticari Mallar Maliyeti 0 ·
600 Yurt İçi Satışlar 0. Bugüne kadar üretilmiş TEK alış fişi 740
Hizmet Üretim Maliyeti'ne gitmiş; 13 satış faturasının hiçbiri fişe
dönmemiş. Yani S5 önce yapılsaydı ilk satışta 153 EKSİYE düşerdi:
muhasebe "hiç malım yoktu ama sattım" derdi. Sıra bu yüzden değişti.

Ayrıca: stok bağlı PERAKENDE SATIŞ AKIŞI ZATEN VAR (merkez depo
kilidi, yeterlilik kontrolü, stok düşümü, 120/600/391 fişi) ama hiç
kullanılmamış — 0 kayıt. Satış faturası kaleminde stok bağı yok.

**S6a (bitti) — kategorinin muhasebe karşılığı.**

- `InventoryAccountingKind`: Consumable (150/740) · TradeGood (153/621).
  VARSAYILAN Consumable ve kategori oluşturma isteği bu alanı ALMIYOR —
  alan olmadığı için "unutmak" da mümkün değil, yeni kategori her zaman
  sarf doğar. Canlıda 14 kategorinin 14'ü sarf.
- `InventoryAccountResolver`: kategori → hesap eşlemesinin TEK kaynağı.
  Kod → hesap kimliği çözümü şirket bazında; ana hesap yoksa ilk alt
  hesaba düşüyor (hesap planında kayıtlar "150.01.02" gibi alt
  hesaplara yazılıyor, "150" ana hesabı hareket görmüyor).
- Kategorisiz kart (S1 öncesinden kalan) SARF sayılır. Yanlış tarafa
  düşülecekse ticari mal tarafı olmamalı: 153'e yazılan sarf malzeme
  mali tabloda satılabilir mal gibi görünür.
- `PUT categories/{id}/accounting-kind` AYRI UÇ, AYRI İZİN:
  `accounting.manage`. Depo Sorumlusu kategori açabiliyor ama hesabı
  değiştiremiyor — kart yönetimi depo işi, hangi hesaba yazılacağı
  mali müşavir işi. Ekrandaki düğme de aynı izne bakıyor.
- Sözleşme testleri (5): oluşturma isteği muhasebe alanı almaz ·
  hesap kodları yalnız çözümleyicide geçer · çözümleyici doğru
  hesaplara götürür · kategorisiz kart sarf sayılır · depo sorumlusu
  değiştiremez (403) ama yetkili değiştirebilir.

KURAL DARALTMASI (dürüstlük notu): "hesap kodu tek yerde" kuralı
150/153/621'i kapsıyor, 740'ı KAPSAMIYOR. 740 stoka özgü değil —
`SubcontractorInvoiceGenerator` ve `ProjectCostClassifier` da meşru
olarak kullanıyor. Tekelleştirilebilen kod korunuyor, edilemeyen için
sahte güvence verilmiyor. `AccountingIntegrationService` gerekçeli
muafiyette: alış faturası hâlâ kendi eşlemesini kullanıyor, S6b'de
çözümleyiciye bağlanınca muafiyet kalkacak.

Altı sondanın hepsi yakaladı (oluşturma isteğine alan eklendi · izin
depo iznine düşürüldü · varsayılan ticari mal yapıldı · 150↔153 takas ·
kategorisiz kart ticari mal sayıldı · hesap kodu ikinci yere kopyalandı).

**S6b (bitti) — GR/IR fişleri + stok↔muhasebe mutabakatı.**

KULLANICI KARARI: GR/IR hesabı **379.01 FATURASI GELMEMİŞ MAL
ALIMLARI**. 159 kullanılamazdı (gerçek tedarikçi avanslarıyla dolu),
379 ana hesabı da canlıda FİŞ KESİLEMEZ (`IsPostingAllowed = false`) —
alt hesap açmak zorunluydu, tohum ekliyor ve var olanı ezmiyor.

- MAL KABUL ARTIK FİŞ KESİYOR: borç 150/153 (kartın kategorisine göre),
  alacak 379.01. Fiş kesinleşmeden ÖNCE üretiliyor ve aynı transaction
  içinde: fiş kesilemezse stok da işlenmiyor. Stokun mali tabloya
  girdiği an burası.
- FİŞTE KDV YOK: mal kabulde fatura yoktur. KDV yazılsaydı beyan
  edilecek vergi elde belge olmadan doğardı.
- FİŞTE PROJE ETİKETİ YOK: depoya giren mal henüz proje maliyeti değil,
  bilanço kalemi. Proje maliyeti malzeme depodan ÇIKARKEN doğuyor ve
  stok çıkışı projeyi zaten yazıyor; girişte de yazılsaydı aynı
  malzeme projeye iki kez bağlanırdı. (Bunu bir test düşüşü gösterdi —
  fiş satırındaki proje başka şirkete aitti.)
- MAL KABULE BAĞLI ALIŞ FATURASI STOKU İKİNCİ KEZ YAZMIYOR: 379.01'i
  kapatıp 320'ye devrediyor. Yine stoku borçlandırsaydı aynı mal iki
  kez bilançoya girer, stok değeri iki katına çıkardı.
- Mal kabule bağlanmamış stok faturası stoku ilk kez yazıyor demektir;
  hesabı kartın KATEGORİSİ belirliyor (sarf 150, ticari mal 153).
- MUAFİYET LİSTESİ BOŞALDI: `AccountingIntegrationService` artık
  çözümleyiciden geçiyor, "153"/"150" sabitleri dosyadan kalktı.
- YENİ EKRAN `/depo-stok/muhasebe-mutabakat` (accounting.view):
  depodaki değer (miktar × ağırlıklı ortalama) ile 150/153 mizan
  bakiyesi yan yana. 379.01 bakiyesi AYRI gösteriliyor — o tutarsızlık
  değil, "malı aldık faturası gelmedi" demek.
- Mizan YALNIZ kesinleşmiş fişlerden hesaplanıyor: taslak fiş mizanda
  yoktur, sayılsaydı rapor sahte bir denklik gösterip muhasebesiz
  stoku örterdi.

DAVRANIŞ DEĞİŞİKLİĞİ (bilinçli): hesap planında 150/153 ya da 379.01
yoksa MAL KABUL DURUYOR. Stokun sessizce muhasebesiz girmesindense
işlem durmalı. Canlı şirkette üçü de var.

SONDADA İKİ KAÇIRMA (ikisi de düzeltildi):
1. "Fişte KDV olmasın" kuralı BÜYÜK/KÜÇÜK HARFE DUYARLIYDI; sonda
   `vatAmount` ekleyince kaçırdı. Artık harf duyarsız ve 191/391
   kodlarını da arıyor.
2. "Mizan yalnız kesinleşmiş fişlerden" kuralı HİÇ KAPSANMIYORDU —
   `Posted` filtresini kaldırdığımda hiçbir test düşmedi, çünkü testte
   taslak fiş senaryosu yoktu. Teste taslak fiş eklendi.

Dokuz sondanın hepsi yakalıyor. Migration canlıya yedekle uygulandı
(db_20260820_001203.dump).

**S5 (bitti) — stoklu satış: 621 maliyet, satır kârı, QR hızlı giriş.**

ÖLÇÜM: 13 satış faturası vardı, **hiçbirinin muhasebe fişi yoktu**
(12'si taslak, 49,6M TL; 1'i iptal). 600 ve 621 hesaplarında SIFIR fiş
satırı. `retail_sales` boştu. Satış faturası kaleminde stok bağı hiç
yoktu ve fiş yalnız 120/600/391 yazıyordu. Yani mal depodan çıkıyor,
150/153 hiç alacaklanmıyordu — S6b'de kurulan mutabakat raporu ilk
satışta sapardı.

ÜÇ KULLANICI KARARI:
1. **Elden satışta TAM maliyet 621'e.** Mal tamamen depodan çıkıyor,
   dolayısıyla maliyetin tamamı yazılır ve 150/153 tam kapanır.
   Görünen bedeli: elden satış yapılan fiş resmi defterde düşük kârlı
   görünür. Alternatifi (maliyeti kayıtlı oranla ölçeklemek) marjı
   gerçekçi gösterirdi ama stok hesabı hiç kapanmaz, mutabakat her
   elden satışta biraz daha sapar ve muhasebesiz stok birikirdi.
2. **Stoklu satış faturası HER depodan** yapılabilir, merkezle sınırlı
   değil — şantiyede artan malzemenin oradan satılması olağan.
   (Perakende ekranı merkez kısıtını koruyor, o ayrı bir akış.)
3. **İsimsiz satış artık fiş kesiyor.** Öncesinde cari seçilmezse HİÇ
   kayıt oluşmuyordu: mal çıkıyor, gelir de maliyet de yazılmıyordu.

- SATIŞ MALİYETİ HESABI 621, TÜR NE OLURSA OLSUN. `ResolveConsumption
  AccountAsync` (sarf→740) bilinçli olarak AYRI bırakıldı: 740 projede
  TÜKETİLEN malzemenin üretim maliyetidir, satılan mal tüketilmemiştir.
  Sarf 740'a yazılsaydı satış, hiç yapılmamış bir işin üretim maliyeti
  gibi görünür ve proje maliyet raporları şişerdi. Alacak tarafı yine
  kategoriden geliyor (150 / 153).
- İKİ SATIŞ YOLU TEK KAPIDAN: `IStockSaleIssuer` stok çıkışının,
  `ISaleCostLineBuilder` maliyet fiş satırlarının tek kaynağı.
  Perakende kendi düşüş döngüsünü bıraktı. Ayrı kalsalardı negatif
  stok yasağı ve maliyet dondurma kuralı zamanla ayrışırdı.
- NEGATİF STOK YASAĞI SATIŞTA DA: kontrol düşüşten önce ve aynı
  değişken üzerinde. `.Quantity -=` noktası sayısı 4'te kaldı, S4
  sözleşmesindeki `checkedSites >= 4` koruması bozulmadı.
- MALİYET ÇIKIŞ ANINDA DONDURULUYOR (`UnitCostAtSale`, `LineCost`).
  Taslakta boş: taslak günündeki maliyet yazılsaydı, araya giren bir
  mal kabulü ortalamayı değiştirdiğinde fişteki maliyet stoktan
  düşülenle tutmazdı.
- STOK VE FİŞ AYNI TRANSACTION'DA. İlk yazımda ikiye bölmüştüm; stok
  çıkıp fiş kesilemezse muhasebesiz çıkış kalırdı — S6b'de mal
  kabulünde kapatılan deliğin satış tarafındaki eşi. Düzeltildi.
- MÜŞTERİ İADESİNDE ORTALAMA MALİYET GÜNCELLENİYOR. Yalnız miktar
  artırmak yetmezdi: mal satıldığı maliyetle geri giriyor, arada
  ortalama değiştiyse stok DEĞERİ bugünkü ortalamayla artar ama
  muhasebeye dondurulmuş maliyet yazılır.
- SATIR KÂRI `inventory.view` İZNİNE BAĞLI. "Satış ekranı maliyeti
  görmez" bilinçli bir karardı ve korunuyor — satır kârı maliyeti ele
  verdiği için aynı kapıya bağlandı. YENİ ANAHTAR AÇILMADI: stok
  maliyetini bugün fiilen o izin koruyor ve fiyatlandırma ekranı da
  onu kullanıyor; ikinci anahtar iki ekranın ayrışmasına yol açardı.
  Yetkisize null döner, gizlenen satır sayısı ayrıca bildirilir.
- QR HIZLI GİRİŞ: okuyucu bir klavyedir. Kasada üç şey okutulabiliyor
  (bizim etiketimizdeki kart URL'i, üretici barkodu, elle yazılan kod)
  ve `parseScannedItem` üçünü ayırıyor. Kimlik ile arama terimi
  ayrılmasaydı bir GUID metin olarak aratılır, hiçbir sonuç dönmez ve
  etiket okutmak SESSİZCE çalışmazdı. Aynı kart ikinci kez okutulunca
  miktar artıyor, ikinci satır açılmıyor.

SONDADA İKİ OLAY:
1. Sonda D önce KAÇIRDI görünüyordu — ama sebep kural değil, sondanın
   kendisiydi: `UnitCostAtSale` ataması `SalesInvoiceService`'te,
   ben `AccountingIntegrationService`'i sabote etmiştim, dosya hiç
   değişmemişti. Sonda betiğine artık "sabotaj dosyayı gerçekten
   değiştirdi mi" kontrolü eklendi; değiştirmediyse sonda GEÇERSİZ
   yazıyor. Doğru dosyada tekrarlandı, yakaladı.
2. Sonda H GERÇEKTEN KAÇIRDI: perakende iadesinde dondurulmuş maliyeti
   yoksayıp bugünkü ortalamaya döndüğümde 22 testin hiçbiri düşmedi —
   kuralın hiç kapsaması yoktu. Yazdığım test **gerçek bir hatayı da
   buldu**: iade fişinin kalemleri yeni oluşturuluyor ve dondurulmuş
   maliyeti taşımıyordu, o yüzden zaten bugünkü ortalamaya düşüyordu.
   Maliyet artık orijinal satırdan kopyalanıyor.

Dokuz sonda (A–I) yakalıyor. DÜRÜSTLÜK NOTU: sonda I zayıf — kodu
değil test iddiasını ters çevirdim, yani yalnız iddianın yük taşıdığını
kanıtlıyor, sıralamanın kendisini değil.

**AÇIK KALAN DELİK (S5 kapsamı dışı, bilinçli):** depodan PROJEYE
çıkış hâlâ fiş kesmiyor — `ResolveConsumptionAccountAsync` tanımlı ama
HİÇBİR YERDEN ÇAĞRILMIYOR (ölçüldü). Yani malzeme projeye verildiğinde
150 alacaklanmıyor, 740 borçlanmıyor. Mutabakat raporu bunu fark
ettirecek: ilk proje çıkışında fark gösterir. Satış tarafı kapandı,
tüketim tarafı kendi fazını bekliyor.

**S6c (bitti) — depodan çıkış muhasebesi: stok ↔ muhasebe halkası kapandı.**

KULLANICI GEREKÇESİ: taahhüt firmasında malzemenin çoğu satılmaz,
projeye gider. Yani bu EN SIK kullanılan yol ve açık kaldığı sürece
stok ile muhasebe ilk çıkışta ayrışırdı.

ÖLÇÜM: 0 stok hareketi (S0 temizliğinden beri hiç çıkış yapılmamış),
740'ta 1 fiş satırı — o da tedarikçi faturasından. `ProjectCostTransactions`
tablosunda 3 kayıt vardı ama HİÇBİRİ stok hareketinden doğmamıştı.
`ResolveConsumptionAccountAsync` tanımlıydı ve hiçbir yerden
çağrılmıyordu.

KULLANICININ TARİF ETTİĞİ DIŞINDA İKİ DELİK DAHA BULUNDU ve soruldu —
ikisi de kapanmadan "sıfır fark" hedefi tutmazdı:

1. **PROJESİZ ÇIKIŞ → 770 Genel Yönetim Giderleri** (kullanıcı kararı).
   Kod merkez/ofis sarfiyatını açıkça destekliyordu ve proje
   seçilmediğinde hiçbir maliyet kaydı oluşmuyordu. 740'a yazılsaydı
   hiç iş yapılmamışken üretim maliyeti doğar, proje kârlılık
   raporları ve hakediş maliyet kıyasları şişerdi.
2. **SAYIM FARKI → noksan 689.02, fazla 649.03** (kullanıcı kararı).
   Düzeltme stoğu değiştiriyor ama hiç fiş kesmiyordu. 740'a
   karışsaydı kayıp ile maliyet ayrımı kaybolur, fire oranı bir daha
   ölçülemezdi.

- PROJEYE ÇIKIŞ: borç **740.03.09 KULLANILAN MALZEMELER**, alacak
  kategoriye göre 150/153. Alt hesap ÖNCE deneniyor, yoksa 740 ana
  hesabına düşülüyor: canlı planda 740 altında işçilik, dışarıdan
  hizmet ve amortisman ayrı ayrı duruyor; malzemeyi ana hesaba yazmak
  mali müşavirin kurduğu ayrımı bozardı.
- TİCARİ MAL PROJEYE GİDERSE DE 740 — 621 değil. Satılmamış, projede
  tüketilmiştir. Ayrılan yalnız ALACAK tarafı (153).
- PROJE ETİKETİ ÇIKIŞTA TAŞINIYOR, girişin tersine. Mal kabulde proje
  yazılmıyordu çünkü depoya giren mal bilanço kalemiydi; çıkışta
  maliyet DOĞUYOR ve hangi projede doğduğu tam da o satırın anlattığı
  şey. Mevcut `ProjectCostTransaction` bağı korundu (malzeme sınıfı).
- 689 ana hesabı canlıda FİŞ KESİLEMEZ (ölçüldü) — 689.02 alt hesabı
  tohumla açılıyor, 379.01 deseninin aynısı. 649 kesilebilir olmasına
  rağmen 649.03 de açıldı: aynı olayın iki yakası biri adlı biri genel
  hesapta dursaydı raporu okuyan neden farklı olduklarını arardı.
- SAYIM UCUNDA TRANSACTION YOKTU, eklendi: fiş kesmediği için
  gerekmiyordu. Artık kesiyor, fiş patlarsa stok da düzeltilmemeli.
- STOK ÇIKIŞ SATIRI TEK KAYNAKTA: `SaleCostLineBuilder` →
  `StockOutflowLineBuilder` olarak genelleştirildi. Alacak tarafının
  kategoriye göre ayrılması artık satış, tüketim ve sayım farkı için
  TEK yerde; değişen sadece borç tarafı ve yön.
- MALİYETSİZ ÇIKIŞ FİŞ KESTİRMEZ (bilinçli): ortalama maliyeti sıfır
  olan kart hiç faturalı girmemiş demektir. Sıfır tutarlı fiş bilgi
  üretmez; kesmemek farkı mutabakat raporunda görünür bırakır. Ekran
  bunu işlem anında AÇIKÇA söylüyor — kullanıcı ay sonunu beklemesin.

ASIL İDDİA BİR TESTLE BAĞLANDI: `GirisCikisVeSayimSonrasi_Mutabakat
SifirFarkVerir` — giriş (150/379.01), çıkış (740/150) ve sayım noksanı
(689.02/150) arka arkaya koşuyor, sonra rapor SIFIR fark veriyor. Tek
tek hesap kontrolleri değil, üçü bir arada tutuyor mu sorusu.

KURAL DARALTMASI (dürüstlük notu): "hesap kodu tek yerde" kuralı
**689 ve 649'u** kapsıyor, **740 ve 770'i KAPSAMIYOR**. Ölçüldü: 770'i
`SubcontractorInvoiceGenerator`, `ProjectCostClassifier`,
`AccountingIntegrationService` ve `DatabaseSeeder` meşru olarak
kullanıyor; 740'ı da ilk ikisi. Tekelleştirilemeyen kod için sahte
güvence verilmiyor. 689.02 ve 649.03 bu fazda açıldı, yalnız stok
sayım farkına ait ve tekelleştiriliyor.

Sekiz sondanın hepsi yakalıyor (çıkış fiş kesmiyor · projesiz çıkış
740'a · ticari mal 621'e · sayım yönü ters · sayım fiş kesmiyor · alt
hesap tercihi kaldırıldı · proje etiketi taşınmıyor · 689 kodu ikinci
yere kopyalandı).

TAM TUR İKİ ŞEY DAHA ORTAYA ÇIKARDI (ikisi de düzeltildi):
1. **Çıkış ucu projenin depoyla aynı şirkette olduğunu doğrulamıyordu.**
   Fiş satırı projeyi taşıyınca fiş servisi "başka şirkete ait proje"
   diyerek patladı. Kontrol olmadan da hatalıydı — başka şirketin
   projesine yazılan sarf iki şirketin de maliyet analizini bozar —
   ama fiş kesilmediği için kimse fark etmezdi. Uca kontrol eklendi
   (400, açık mesaj).
2. **`WarehouseIntegrationTests` farkında olmadan başka şirketin
   projesine sarf yazıyordu**: `CreateProjectAsync` kendi şirketini
   açıyor, test ise depoyu ayrı bir şirkette kuruyordu. Test düzeltildi.

STOK ARTIK HER YÖNDEN MUHASEBEYE BAĞLI: giriş S6b, satış S5, çıkış ve
sayım farkı S6c. Depolar arası transfer bilinçli olarak fiş kesmiyor —
aynı hesabın içinde yer değiştirme, net etkisi sıfır.

**S7 (bitti) — dönemsel sayım: oturum, kilit, gerekçe, onay, fark raporu.**

ÖLÇÜM: 0 bölge, 0 stok satırı, 9 kart, 6 depo. Konum STOK SATIRINDA
DEĞİL KARTTA duruyor (`InventoryItem.WarehouseZoneId`); bölge filtresi
oradan uygulanıyor.

KULLANICI KARARI (tehlikeli olan): fiziki miktarı GİRİLMEMİŞ satır
onayda **ATLANIR**, stoğu değişmez. Sıfır sayılsaydı unutulan tek bir
satır o malzemenin tüm stoğunu siler ve karşılığında gider yazardı.
Atlanması sessiz değil: fark raporu kaç satırın sayılmadığını yazıyor.

- OTURUM SİSTEM MİKTARLARINI DONDURUYOR. Sayım sırasında stok
  değişirse fark "sayım anındaki" gerçeği yansıtmaz. Bölge zaten
  kilitli; dondurma o kilidin ikinci savunma hattı.
- KİLİT SERT VE TEK KAPIDAN: `IStockCountLockService`. Stok değiştiren
  ALTI yol da buradan geçiyor — depo çıkışı, transfer (İKİ TARAF),
  tekil düzeltme, mal kabul, alış faturası (giriş ve iade), satış
  çıkışı ve satış iadesi. Bir yol atlansaydı kilit "çoğu zaman"
  çalışırdı; en kötü güvence türü, çünkü kimse hangi yolun atladığını
  bilmez.
- KİLİT BÖLGE BAZLI: bölgesiz oturum tüm depoyu kapatıyor, bölgeli
  oturum yalnız o bölgeyi. Kartın bölgesi yoksa bölgesel sayım onu
  kapsamıyor ve hareketi durdurulmuyor — o kart zaten listeye
  girmemişti.
- ONAY BEKLERKEN DE KİLİTLİ: sayılan miktarlar henüz stoğa işlenmedi;
  araya giren hareket onay anında uygulanacak farkı yanlış yapardı.
- GEREKÇE ZORUNLU ve SAYILABİLİR (fire/kayıp/sayım hatası/kırılma).
  Serbest metin olsaydı aynı sebep on farklı şekilde yazılır, "hangi
  depoda ne kadar fire var" sorusu hiç cevaplanamazdı.
- SAYMAK İLE ONAYLAMAK AYRI İZİNDE: sayım `inventory.edit`, onay
  `accounting.approve` (Genel Müdür + Finans Sorumlusu). Aynı kişi hem
  sayıp hem onaylayabilseydi fark, gerekçesi hiç sorgulanmadan stoğa
  ve gidere işlenirdi.
- OTURUM BAŞINA TEK FİŞ. Satır başına kesilseydi mizan yüzlerce
  satırlık anlamsız bir yığına dönerdi.
- NOKSAN VE FAZLA NETLEŞTİRİLMİYOR, aynı fişte ayrı satırlarda duruyor.
  Net 100 TL fark, 500 kayıp ve 400 fazlanın toplamı da olabilir; bu
  iki tablo aynı şey değil.
- FARK HESABI FİNANS AYARINDAN (kullanıcı isteği):
  `StockCountShortageAccountId` / `StockCountSurplusAccountId`. Boşsa
  S6c'de açılan 689.02 / 649.03. Mali müşavir isterse 157'ye
  yönlendirebiliyor; sistem dayatmıyor ama boş bırakılırsa da durmuyor.
- AYNI DEPO/BÖLGEDE İKİNCİ OTURUM AÇILMIYOR: iki oturum, iki farklı
  dondurulmuş miktar demektir; ikisi de onaylanırsa aynı fark stoğa iki
  kez uygulanırdı.
- FARK RAPORU bölge / kategori / gerekçe kırılımında. Tekrar eden kayıp
  aynı bölgede toplanıyorsa sebebi oradadır; satır satır bakarak bu
  görülmez.
- QR HIZLI SAYIM: S5'te yazılan `parseScannedItem` yeniden kullanıldı.
  Okutulan kod satırı bulup işaretliyor ve miktar kutusuna odaklanıyor.
- TEKİL DÜZELTME UCU DURUYOR (`POST adjustments`): o, tek kalemin anlık
  düzeltmesi. İkisi aynı uca sıkıştırılsaydı ya anlık düzeltme onay
  kapısına takılır ya dönemsel sayım onaysız stok değiştirirdi. Tekil
  uç da kilide tabi.

İKİ MEVCUT SÖZLEŞME TESTİ YENİ EKRANLARI YAKALADI ve ikisi de
ekranı düzelterek kapatıldı, limit YÜKSELTİLMEDİ: (1) ham `<table>`
cırcırı 43→44 oldu, liste `DataTable`'a çevrildi; (2) redwood
sözleşmesi `design="redwood"` ve tazeleme düğmesi istedi.

**S8 (bitti) — min/max uyarısı → satın alma talebi önerisi.**

ÖLÇÜM (işe başlamadan): 9 kart, **`MinimumStock > 0` olan 0 kart**,
`MaximumStock` dolu 9 kart ama **hepsinin değeri 0,0000**, 0 stok
satırı, 0 tercihli tedarikçi, 0 satın alma talebi. Yani min/max
tümüyle ÖLÜ VERİYDİ — hiçbir kart uyarı üretemezdi.

ÖLÇÜMÜN AÇIĞA ÇIKARDIĞI ASIL SORUN: aynı alan iki uçta İKİ FARKLI
ANLAMDA kullanılıyordu. `criticalOnly` süzgeci kart minimumunu TÜM
depoların TOPLAMIYLA, `critical-stock-alerts` ucu ise TEK deponun
miktarıyla kıyaslıyordu. Hangisinin doğru olduğu hiçbir yerde
yazmıyordu. Üçüncüsü de vardı: `GetWarehouseStocks` içindeki
`IsCritical`'da "asgari > 0" koşulu yoktu, bu yüzden asgarisi
tanımsız (0) ve stoğu biten HER kalem kritik görünüyordu (`0 <= 0`).

KULLANICI KARARLARI:
1. **Seviye DEPO BAZINDA** — kart üzerindeki alanlar kaldırıldı, tek
   kaynak `warehouse_stock_levels`. Merkez deposunda 100 metre kablo
   bulundurmak isteriz, biten bir şantiye deposunda aynı kalem için
   sıfır doğrudur; tek sayı bu ikisini anlatamıyordu.
2. **Öneri = AZAMİ − MEVCUT.** Azami tanımlı değilse öneri
   ÜRETİLMEZ (uyarı yine çıkar). "Asgarinin iki katı" gibi bir
   katsayı uydurulsaydı sistem, kimsenin vermediği bir sipariş
   kararını vermiş olurdu.
3. **Talebin projesi ekranda seçilir.** Depo ikmali gerçekte
   projesizdir ama `PurchaseRequest.ProjectId` zorunlu ve bütçe
   onayı/raporlama oradan besleniyor; nullable yapmak o üç akışın da
   projesiz dalını yazmayı gerektirirdi.

- **KOLONLARIN KALDIRILMASI VERİ KAYBI DEĞİL, ÖLÇÜLDÜ**: migration
  `MinimumStock`/`MaximumStock` kolonlarını düşürüyor. Veri taşıyan
  bir adım YAZILMADI çünkü taşınacak veri yoktu (yukarıdaki ölçüm).
  Uydurma bir eşik üretmek, kimsenin koymadığı bir kararı geriye
  yüklemek olurdu.
- **SATIRIN VARLIĞI TAKİBİN KENDİSİ**: asgarisi sıfır olan seviye
  kabul edilmiyor ("her zaman kritik" demek olurdu). Takibi bırakmanın
  yolu satırı silmek. Azami de asgariden büyük olmak zorunda, yoksa
  öneri negatif çıkardı.
- **SEVİYE, BAKİYE SATIRINA KOLON DEĞİL AYRI TABLO**: bakiye satırı
  yalnızca malzeme o depoya bir kez girdiyse var; oysa seviye takibine
  en çok stok SIFIRKEN ihtiyaç duyulur. Politika ayrı durduğu için
  bakiyesi hiç olmayan kalem de uyarı üretiyor — sol birleşim, mevcut
  yoksa sıfır.
- **TAKİP BIRAKILIP YENİDEN AÇILABİLİR**: seviye satırı yumuşak
  siliniyor, tekil indeks ise KISMİ (`"IsDeleted" = false`). İndeks
  silinmişleri de kapsasaydı aynı malzeme için takip bir daha
  açılamazdı — silinmiş satır sorgu süzgeci yüzünden görünmez ama
  indekste durmaya devam ederdi. Kusur yazarken yakalandı ve testle
  bağlandı (`Seviye_SilindiktenSonraYenidenTanimlanabilir`).
- **EŞİK "<=" ve TEK YERDE**: `StockLevelAlertService`. Ekran,
  bildirim ve Hızır brifingi aynı hesabı okuyor. Brifing S8'de kendi
  sorgusunu taşıyordu; bu turda o kopya kaldırıldı ve ortak servise
  bağlandı — üç kopya kalsaydı aynı malzeme için üç farklı "kritik"
  tanımı doğardı.
- **UYARI BİLDİRİM MERKEZİNE DE DÜŞÜYOR**: `StockLevelNotificationSource`
  (`inventory.below_minimum`). Dönem anahtarı SABİT ("acik") çünkü
  asgari stok bir VADE değil bir DURUM; güne bağlansaydı her gece yeni
  kayıt açılır, "okundu" bilgisi her gece kaybolurdu. Mal girince aday
  üretilmez ve motor kaydı kendiliğinden kapatır.
- **TÜKENMİŞ ile AZALMIŞ AYRI KADEMEDE**: sıfır stok Kritik, asgari
  altı Uyarı. Tek kademe olsaydı "3 adet kaldı" ile "hiç kalmadı" aynı
  renkte görünürdü.
- **TALEP TASLAK DOĞAR** ve normal onay yolundan geçer. Doğrudan
  onaylı açılsaydı stok uyarısı, kimsenin bakmadığı bir harcama emrine
  dönerdi. İzin `purchasing-requests.create` — depoyu GÖREN değil,
  talep AÇABİLEN kişi.
- **MİKTAR İSTEMCİDEN GELİYOR**: öneri bir öneridir; kullanıcı
  azalttıysa azalttığı miktar sipariş edilmeli. Sunucu yeniden
  hesaplasaydı ekranda görülen sayı ile kaydedilen ayrışırdı.
- **SEVİYESİ TANIMSIZ KALEM BU YOLDAN TALEP EDİLEMEZ** (409): bu uç
  "asgarinin altına düştü" gerekçesiyle talep açıyor; gerekçesizi de
  kabul etseydi otomasyon kapısı denetimsiz bir elle talep kapısına
  dönerdi. Aynı malzeme iki kez seçilirse sessizce TOPLANMAZ, hata
  verir.
- **KALDIRILAN UÇ**: `GET api/inventory/critical-stock-alerts`. Canlıda
  hiçbir ekrandan çağrılmıyordu (frontend'de sıfır tüketici) ve ikinci
  "kritik" tanımının kaynağıydı. Tek kaynak `GET api/stock-levels`.
- **KALDIRILAN HÜCRE**: kart listesindeki satır içi asgari düzenleme
  (`MinimumStockCell` + `updateMinimumStock`). Bir kartın birden çok
  deposu olabildiği için tek hücreye sığmıyor; tanım
  `/depo-stok/stok-seviyeleri` ekranında.

SONDA TURU (13 sonda): 11'i doğrudan yakaladı, 2'si kaçırdı ve
ikisinin de sebebi ÖLÇÜLDÜ — kaçırma açıklanmadan faz kapatılmadı.

- **H GERÇEK BOŞLUKTU, TEST GÜÇLENDİRİLDİ.** "Seviyesi tanımsız kalem
  talebe giremez" korumasını kaldırınca akış birkaç satır sonra
  `levels.Single()` üzerinde patlıyor ve denetleyici o istisnayı da
  409'a çeviriyor. Test yalnız DURUM KODUNA baktığı için kasıtlı
  korumayı kazara çökmeden ayıramıyordu. Fark kullanıcıda: açıklayıcı
  Türkçe gerekçe yerine "Sequence contains no elements" okurdu. Test
  artık mesajı da denetliyor.
- **B BOŞLUK DEĞİLDİ, SONDA AYIRICI DEĞİLDİ.** `?? 0m` → `?? 999999m`
  hiçbir testi düşürmedi. Sebep: EF boş kümedeki `SUM`'ı zaten kendi
  `COALESCE(...,0)`'ı ile sarıyor, yani `??` hiç tetiklenmiyor —
  sabotaj anlamsızdı. Kanıt sabotajın kendisi: test o hâlde bile
  `currentQuantity == 0` iddiasını geçti. Kuralı taşıyan şey sorgunun
  BİÇİMİ (sol birleşim); onu hedefleyen sonda (iç birleşime çevir)
  testi 2 saniyede düşürdü. Sonda betiğindeki B kalıcı olarak bununla
  değiştirildi.

BU TURDAN ÇIKAN İKİ KURAL (§5'e):

16. **Sabotaj ANLAMLI olmalı, yalnız derlenebilir değil.** Davranışı
    değiştirmeyen bir sabotajın "kaçırması" testi değil sondayı
    suçlar. Kaçırma görünce ilk soru "test zayıf mı" değil, "bu
    sabotaj gerçekten davranışı değiştiriyor mu".
17. **`dotnet test` çocukları harness görevi öldürülünce hayatta
    kalıyor.** Arkada kalan derleyici 4,3 GB tutup OOM killer'ı
    tetikledi ve sonraki turların derlemeleri yarıda kesildi
    (belirti: `Duration: < 1 ms`). Tur öncesi `dotnet build-server
    shutdown` + `MSBUILDDISABLENODEREUSE=1`, sonrasında artık süreç
    taraması.

MEVCUT SÖZLEŞME TESTLERİ YENİ EKRANI YAKALADI, ikisi de ekranı
düzelterek kapatıldı: (1) öneri listesi ham tablo ile yazılmıştı, cırcır
43→44 olacaktı — `DataTable`'a çevrildi, sınır **43'te kaldı**;
(2) redwood sözleşmesi ham hex rengi reddetti (`#b45309` iki dosyada),
`rw-value-warning` sınıfına bağlandı.

**S9 (bitti) — SERBEST kart: proje bağı, tedarik tipi, görsel galerisi.**

ÖLÇÜM: 14 kategori (2'si SERBEST: Dekoratif Aydınlatma, Özel İmalat),
**kategorisi olan kart 0**, **`ImagePath` dolu kart 0**, kartta proje
bağı yok, özel/sipariş işareti yok, listede proje süzgeci yok.
`InventoryCategoryKind.Free` tanımı "fotoğraf ve proje bağı taşır"
diyordu — S9'a kadar KARŞILIKSIZ BİR SÖZDÜ.

KULLANICI KARARLARI:
1. **Proje bağı BAĞLAYICI** — bağı olan kart başka projeye ya da
   projesiz çıkarılamaz. Uyarı değil ENGEL: uyarı zamanla görmezden
   gelinir. Gerçekten gerekiyorsa önce KARTIN BAĞI değiştirilir,
   böylece karar kaydedilmiş olur.
2. **Üç durumlu tek alan**: Stoklu / Özel imalat / Sipariş üzerine.
   Üçü birbirini dışladığı için tek alan; iki ayrı işaret kutusu
   olsaydı "ikisi de işaretli" diye cevapsız bir durum doğardı.
3. **Çoklu görsel + kapak** — dekoratif üründe montaj öncesi/sonrası,
   detay ve ölçü krokisi AYRI görsellerdir.

- **KURAL SATIŞTA DA GEÇERLİ**: satış da bir çıkıştır. X için imal
  edilmiş armatürün tezgâhtan satılması o işi malzemesiz bırakır ve
  kimse fark etmez — stok düşmüş, muhasebe tutmuş, yalnız malzeme
  yanlış yere gitmiştir. Engel tek satış kapısında (`IStockSaleIssuer`,
  S5), böylece perakende ve fatura yollarının İKİSİNİ birden kapsıyor.
- **S8 İLE TUTARLILIK**: asgari/azami seviye yalnız STOKLU kartta
  tanımlanabiliyor. "Sipariş üzerine" bir üründe asgari seviye kendi
  kendisiyle çelişir — orada stok BULUNDURMAMAK bilinçli karardır.
  Stoklu'dan çıkarken tanımlı seviye varsa değişiklik ENGELLENİYOR;
  sessizce silmek "takibi kim kaldırdı" sorusunu cevapsız bırakırdı.
- **KAPAK GÜVENCESİ TEK YERDE**: ilk yüklenen kendiliğinden kapak olur,
  kapak silinince sıradaki devralır. Üç ayrı yerde (yükleme, silme,
  kapak seçme) tekrarlansaydı bir yol atlanır ve liste görselsiz
  kalırdı. Ekran bu kuralı YENİDEN UYGULAMIYOR, sunucudan okuyor.
- **GALERİ YALNIZ GÖRSEL ALIR**: paylaşılan `IUploadService` PDF ve
  Excel'e de izin veriyor (belge modülleri onu kullanıyor); daraltmak
  onları kırardı, bu yüzden şart galeri servisinde.
- **KOLON DÜŞÜŞÜ VERİ KAYBI DEĞİL, ÖLÇÜLDÜ**: `ImagePath` dolu kart
  sayısı sıfırdı; kolon vardı ama hiçbir uç yazmıyordu.
- **SERBEST KATEGORİDE VARSAYILAN "ÖZEL İMALAT"**: varsayılanı
  "stoklu" bırakmak kullanıcıyı her seferinde düzeltmeye zorlar,
  unutulduğunda kart yanlış tipte doğardı.

S9'DA ÇIKAN ÜÇ GERÇEK KUSUR (hiçbiri istenmemişti):
1. **`MovementDate` doğrulanmıyordu** — zorunlu alandı ama boş gelince
   akış muhasebe fişine kadar inip orada patlıyordu; kullanıcı Türkçe
   uyarı yerine **500** görüyordu. Artık 400, testle bağlı.
2. **Galeri bileşeninde olmayan CSS değişkenleri** (`--rw-accent`,
   `--rw-border`) uydurulmuştu; kapak çerçevesi sessizce ham hex
   yedeğine düşecekti. İki sözleşme testi birden yakaladı.
3. **Kartın proje-şirket kontrolü TESTSİZDİ** — sonda turunda çıktı:
   kontrol kaldırıldığında 12 testin hiçbiri düşmüyordu. 13. test
   eklendi.

SONDA TURU: 11 sonda, **11'i de yakalıyor** (K önce kaçırdı, boşluk
kapatılıp yeniden koşuldu). Turda dört "serbest olmalı" sondası var
(D dahil): bir engelin FAZLA GENİŞ yazılmış olması, yalnız
engellediğini test ederek görülemez.

BU TURDAN ÇIKAN İKİ KURAL (§5'e):

18. **Sondayı `setsid` ile koşturma.** Süreç ayrılınca harness görevi
    "tamamlandı" sayıp süreç grubunu öldürüyor; `trap` çalışmadan ölen
    betik SABOTAJI KAYNAKTA BIRAKIYOR. `setsid`siz koşuda kesinti
    gelse bile `trap` çalışıyor.
19. **Kesilen bir tur, sabotajını sonraki sondalara BULAŞTIRIR.**
    Kesilen tur `InventoryController.cs`'e K sabotajını bıraktı;
    sonraki sondalar o ZATEN SABOTAJLI dosyayı yedek alıp sadakatle
    geri koydu. Yani harness kusuru kendi kendine yayıldı. Bu yüzden
    her sondadan sonra TEK DOSYA değil, fazın BÜTÜN korumaları
    taranır (S9'da dokuz koruma, `grep -Fc` ile tek tek).

**F4 DURAKLATILDI (kullanıcı kararı) — kalan 23 ekran.**

Ham tablo sayısı **43 → 23**; cırcır sınırı da 23'e indirildi, yani
sayı bir daha yükselemez. Tamamlanan gruplar:

| Grup | Ekranlar |
|---|---|
| F4f | hakedis/takip (3 tablo), depo-stok (2), projeler, taseronlar |
| F4g | finans/vergi (4), finans/finansal-araclar (3) |
| F4h | **DataTable `groupBy` yeteneği** + finans/cekler ana listesi |
| F4i | finans/gider-merkezi (5) |
| F4j | muhasebe/buyuk-defter, insan-kaynaklari/izinler |
| F4k | demirbas, perakende/fiyatlar |
| F4l | isg, isg/benim (3) |
| F4m | isg/personel (4) |
| F4n | muhendislik/pozlar/ozel, muhasebe/kur-degerlemesi |
| F4o | insan-kaynaklari/zimmetler (2) |
| F4p | insan-kaynaklari/cikis-tazminat (2) |
| F4q | insan-kaynaklari/maliyet-raporu (3) |

KALAN 23 EKRANIN TÜRÜ — hepsi "liste" DEĞİL:
- **~7 gerçek liste:** `perakende`, `isg/osgb`, `fiyat-farki`,
  `finans/piyasa`, `finans/nakit-akis`, `insan-kaynaklari/ek-ucretler`,
  `insan-kaynaklari/ucret-kartlari`, `insan-kaynaklari/onay-merkezi`.
- **~11 form/sihirbaz** (`/yeni`, `/ice-aktar`, `/aktar`): tablo orada
  liste değil KALEM GİRİŞİ. Sayfalama bir fatura satırı listesinde
  zarar verir — kullanıcı 2. sayfadaki satırı görmeden kaydeder.
- **~4 ızgara/matris** (`puantaj`, `gunluk-puantaj`, `puantaj-cetveli`,
  `yetki-matrisi`): satır sayısı personel, sütunlar gün; sayfalama
  ilişkiyi kırar.

Cırcır testi bu ayrımı yapmıyor (yalnız `<table` sayıyor). Devam
edildiğinde form/ızgara olanlar tek tek okunup, gerçekten liste
olmayanlar gerekçesiyle cırcırın belgelenmiş kapsam-dışı kümesine
eklenmeli (bugün ağaç ve yazdırma sayfaları için yapıldığı gibi) —
sessizce atlanmamalı.

BİLEŞENE EKLENEN YETENEK (F4h): `groupBy` — satırlar bir anahtara göre
öbekleniyor, her öbeğin başına kendi ALT TOPLAMINI taşıyan başlık
giriyor. Sayfalama satıra uygulanıyor; grup sayfa sınırını aşarsa
başlık tekrar ediyor. Dört testle bağlı.

YOL BOYUNCA KAPATILAN İKİ SESSİZ VERİ KAYBI:
1. `depo-stok`ta elle yazılmış "Daha fazla göster" sayfalaması ve
   sayfa-bazlı toplam riski.
2. `isg` panelinde `attention.slice(0, 25)` — İSG takibi gereken ilk
   25 personel gösteriliyor, kalanın varlığı hiçbir yerde
   söylenmiyordu. F0'da kapatılan hatanın aynısı; konusu İSG
   uyumluluğu olduğu için sonucu daha ağır.

ALT TOPLAM BİR SÜTUN KARARIDIR, dayatma değil — üç kez ayrıştı:
büyük defterde YÜRÜYEN BAKİYE toplanmadı (anlamsız rakam üretirdi),
kur değerlemesinde farklı para birimlerindeki bakiyeler toplanmadı ama
"bu turda kesilecek fiş" toplandı, bordro özetinde toplanmadı (liste
zaten kendi içinde toplamlar taşıyor).

**AÇIK KAPI — MUHASEBE DÖNEM KİLİDİ YOK (ayrı paket olarak açılacak).**

Sistemde "kapalı dönem" kavramı hiç yok: `ClosedPeriod`, `PeriodClosed`,
`LockDate` aramaları kod tabanında SIFIR sonuç veriyor. Geçmiş aya fiş
kesilmesi hiçbir yerde engellenmiyor.

Bu şu an bir hata üretmiyor ama beyanla mizanın ayrışmasına açık bir
kapı: beyan verilmiş bir dönemin fişleri sonradan değiştirilebilir ya
da o döneme yeni fiş eklenebilir, ve sistem uyarmaz.

Çek paketinde ölçüldü ve o paket için sorun ÇIKARMADI — çek iptali ve
düzeltmesi ters kaydı `DateTime.UtcNow.Date` ile CARİ döneme atıyor,
geçmişe yazmıyor (`ChequeService` → `CreateReversalVoucherAsync`).
Yani çek tarafı doğru davranıyor; eksik olan sistem geneli bir kural.

**ÇEK PAKETİ (2026-08-21) — YAYINDA** (`8b64067b`, `f63131eb`).

Şikâyet: *"iptal ettiğim çekin numarasını bir daha giremiyorum"* ve
*"çekte proje seçerken Merkez görünmüyor."*

TEŞHİS: mükerrer kontrolü yalnız UYGULAMA katmandaydı, durum süzgeci
YOKTU ve veritabanında çek numarası için hiçbir indeks yoktu. Yani
iptal edilmiş çek numarayı bloke ediyordu (bildirilen hata) ve aynı
anda gelen iki istek mükerrer kaydı yine de geçirebiliyordu
(bildirilmemiş, daha ağır hata).

Ne değişti:
- **Kısmi tekil indeks** `IX_cheques_aktif_benzersizlik` —
  şirket + yön + banka + şube + normalize çek no,
  `WHERE "Status" <> 90 AND "IsDeleted" = false`. İptal numarayı
  bloke etmiyor; mükerrer engeli artık veritabanında.
- **Keşideci anahtarda YOK** (kullanıcı kararı, ölçümle): canlıda 21
  çekin yalnız 4'ünde dolu; anahtara konsaydı kısıt gevşerdi.
- `NormalizedChequeNumber` kolonu + `ToUpperInvariant` normalizasyon.
  BAŞTAKİ SIFIR KORUNUYOR ("0012345" ≠ "12345"). DB kültürü C.UTF-8
  (ölçüldü) — `upper()` ile C# aynı davranıyor, Türkçe "i" tuzağı yok.
- **Çek düzenleme** (`cheque.edit`): düzenlenebilirlik kararı
  SUNUCUDAN geliyor (`canEdit` + `editBlockedReason`); tutar / para
  birimi / cari değişirse giriş fişi ters kayıtla kapanıp yenisi
  kesiliyor, açıklamalar hangi fişin yerine geçtiğini yazıyor.
- **Alan bazlı denetim kaydı** (`cheque_change_logs`): alan, eski,
  yeni, kim, ne zaman, gerekçe + "muhasebeyi etkiler" işareti.
- **RowVersion ZORUNLU** (düzenleme ve iptal): milisaniye
  karşılaştırması. Opsiyonel olsaydı korumayı atlatmak için alanı
  göndermemek yeterdi.
- **İptal nedeni SAYILABİLİR** (yanlış giriş / karşılıksız / müşteriye
  iade / diğer). Kapanmış çekte "yanlış giriş" hem ekranda yok hem uç
  reddediyor; kapanmış çek iptali ayrı yetki (`cheque.void-closed`).
- **İzinler migration ile dağıtılıyor** (Admin, Genel Müdür, Finans
  Sorumlusu), yansımayla değil.
- **MERKEZ**: `api/masraf-merkezleri` ucu + tek ortak seçici
  (`CostCenterSelect`). Merkez en üstte ayrı grupta, projeler altında;
  kapalı proje listede yok ama mevcut kayıttaki geliyor. Çekte masraf
  merkezi ZORUNLU ve varsayılan Merkez. Liste `costCenterCode` ile
  süzülebiliyor ve "Masraf merkezi" sütununda merkez artık "—" değil
  **Merkez (KOD)** yazıyor.
- Yan fayda: `DocumentNumberService` oku-sonra-yaz yarışı tek atomik
  upsert'e çevrildi — bütün modüllerin belge numaralarını etkiliyor.

MUHASEBE FİŞİNE GİDEN MASRAF MERKEZİ KODU (ölçüldü,
`ResolveChequeCostCenterAsync`): 1) çekin `CostCenterCode`'u (Merkez
seçilince merkez şubenin `CostCenterCode ?? Code` değeri), yoksa
2) proje kodu, yoksa 3) şirket kodu. Merkez ve proje için ayrı ayrı
test edildi.

MIGRATION PROVASI (canlı verinin kopyasında, 2026-08-21):
`enderun_ai_migprova` (23 çek) üzerinde up → boş normalize kayıt **0**,
indeks kuruldu, izin ve rol satırları yazıldı; down → kolon, indeks,
log tablosu ve izinler temizlendi, **23 çek kaydı olduğu gibi durdu**.
Boş veritabanında up → down → up da koşuldu. Prova veritabanları
silindi.

KARARA BAĞLANDI (2026-08-21, kullanıcı): **masraf merkezi değişimi
fişi yeniler.** Proje ↔ Merkez taşıması artık tutar/para birimi/cari
ile aynı muamele görüyor: giriş fişi ters kayıtla kapanıyor, yenisi
YENİ masraf merkezi koduyla kesiliyor ve denetim kaydında "muhasebeyi
etkiler" işaretleniyor. İki testle bağlı (değişince yenilenir /
aynı kalırsa yenilenmez); sonda ile doğrulandı.

AÇIK KALAN İKİ SORU (kullanıcı kararı gerekir, kod değiştirilmedi):
1. **Ertelenmiş çekin yerine geçen çek iptal edilirse** orijinal çek
   "Ertelendi" durumunda kalıyor ve hiçbir açık duruma dönmüyor;
   alacak iki listeden de düşüyor. Orijinalin eski durumuna dönmesi mi
   gerekir, yoksa bu bilinçli mi?
2. **Durum geri alma (`durum-geri-al`) RowVersion istemiyor.**
   Düzenleme ve iptal istiyor. Aynı korumanın oraya da konması
   davranış değişikliği; sorulmadan yapılmadı.

İptalin GERİ ALINMASI yok ve bu bilinçli: `Voided` durum matrisinde
hiçbir geçişe sahip değil, geri alma reddediliyor. Yanlış iptal edilen
çek artık YENİDEN GİRİLEBİLİR — numara serbest kaldığı için.

**G1.1 — DİYALOG ODAK KAYBI + TUTAR GİRİŞİ (2026-08-21) — YAYINDA**
(`9e8fef79`, `8043fd9a`).

ODAK KAYBININ KÖK SEBEBİ — `components/ui/use-dialog-behavior.ts`.
Belirti: "çek düzenlemede tutar alanına bir rakam yazınca odak
kaçıyor". Sebep MASKELEME DEĞİLDİ (maskeleme hiç yoktu): effect
`onRequestClose`e bağımlıydı, Modal/Drawer o geri çağrıyı
`useCallback(..., [busy, onClose])` ile kuruyor ve çağıran taraflar
satır içi ok fonksiyonu veriyor — yani bağımlılık her renderda yeni
kimlik alıyor ve effect HER TUŞ VURUŞUNDA sökülüp kuruluyordu. Odak iki
yoldan kaçıyordu: temizlikteki `restore?.focus?.()` ve yeni kurulumun
paneldeki İLK odaklanabilir elemana (başlıktaki ✕) odaklanması.

ÖLÇÜLDÜ, TAHMİN EDİLMEDİ: test önce yazıldı, düştü ve sebebi gösterdi —
bir rakam yazıldıktan sonra `document.activeElement` ✕ düğmesiydi.

ÇÖZÜM: geri çağrı ref'te, effect yalnız `open`a bağlı. KAPSAM: hata çek
ekranına özgü değildi — 71 dosya bu bileşenleri kullanıyor, 96 çağrı
yeri satır içi geri çağrı veriyor; tek düzeltme hepsini kapattı.
Tarama: bağımlılığında geri çağrı taşıyan diğer effect'ler incelendi
(`hakedis-editor.tsx:592` → `setSummary`, React state setter, stabil;
`recipe-editor.tsx:117` → `useCallback(..., [])`, stabil). Başka canlı
örnek YOK. Odak çalan tek diğer `.focus()` barkod okutmada, olay
işleyicisinde — doğru davranış.

TUTAR GİRİŞİ (`TutarInput`). Giriş mantığı GÖSTERİMLE AYNI DOSYADA
(`lib/format/turkish.ts`): `formatAmountInput`, `normalizeAmountInput`,
`digitsBeforeCaret`, `caretAfterDigits`. Ayrı dosya olsaydı iki
biçimleme mantığı doğar ve listedeki tutar ile formdaki tutar zamanla
ayrışırdı.

- `type="text"` + `inputMode="decimal"` — `type="number"` maskeli metni
  geçersiz sayıp value'yu boşaltıyor ve `setSelectionRange`
  desteklemiyor, yani imleç korunamıyor.
- İmleç `useLayoutEffect` içinde ve RAKAM SAYISIYLA hesaplanıyor;
  karakter indeksi ayıraç girip çıktıkça kayar.
- `onChange` ref'te, effect bağımlılığında DEĞİL — yukarıdaki hatanın
  aynısı burada doğmasın.

YAZARKEN ÇIKAN İKİ AÇIK (ikisi de testle yakalandı): (1) ayıraç
yazılınca imleç virgülün soluna düşüyordu — "1234," + "5" → 12.345;
(2) nokta ayrımını "nokta sayısı" ile yapmak yanlıştı, alan yazdıkça
biçimlendiği için ekranda zaten binlik noktaları var.

TUZAK VE KAPATAN KURAL (kullanıcı yakaladı): tek virgül KOŞULSUZ
ondalıktır. "Ardındaki rakam sayısı" kuralına tabi olsaydı 1,50 yazıp
fazladan sıfır basan kullanıcının metni "1,500" olur ve tutar sessizce
BİN KATINA çıkardı. Üçüncü hane yorumu değiştirmiyor, iki hane
sınırında düşüyor. İki testle bağlı; SONDA ile doğrulandı — kural
gevşetilince ikisi de düşüyor, yapıştırma testi düşmüyor.

KAPSAM: yalnız çek ekranları (giriş, düzenleme, dağılım satırları).
Erteleme formunda tutar alanı YOK — yeni çek eski çekle aynı tutarda
olmak zorunda. Diğer modüllere yayma canlı doğrulamadan sonra
konuşulacak.

**ÇEK PAKETİ CANLI DOĞRULAMA (2026-08-21).**

ASIL KABUL TESTİ — 8051359: engelleyen kayıt `VCK-2026-000022`,
VERİLEN çek, durum 90 (iptal), GARANTİ BANKASI / ÇANKAYA. Yani
kullanıcıyı bloke eden şey kendi iptal ettiği çekti. Canlı
veritabanında GERİ ALINAN bir işlem içinde denendi: aynı anahtarla
INSERT GEÇTİ; hemen ardından ikinci aktif kayıt denemesi
`IX_cheques_aktif_benzersizlik` ile REDDEDİLDİ. İptal numarayı
bırakıyor, aktif kayıt bırakmıyor. Canlıya tek satır yazılmadı.

Sayılar: boş normalize kayıt 0/23, aktif çakışan grup 0, portföy çek
toplamı 7.664.000,00 = 101 hesap bakiyesi 7.664.000,00 (fark 0,00),
test kaydı 0.

EKRANDAN DOĞRULANAMAYAN İKİ ADIM: Merkez seçicinin görünümü ve tahsil
edilmiş çekte iptal yetki uyarısı. Canlı API'ye kimlik doğrulamalı
istek için token üretme girişimi GÜVENLİK SINIFLANDIRICISI TARAFINDAN
ENGELLENDİ (doğrusu da bu). İkisi de testlerle bağlı ama kullanıcı
onayı bekliyor.

MÜKERRER MESAJI EKSİKTİ, DÜZELTİLDİ (`f63131eb`): yön ve banka yoktu —
tam da "ama ben bunu girmedim ki" denen yer, çünkü aynı numara
alınan/verilen çekte ve farklı bankada ayrı ayrı kaydedilebiliyor.
Yeni metin yön + numara + banka/şube + kayıt no + durum + vade
taşıyor.

KARARSIZLIK TARAMASI: düzenleme yolunun mükerrer kontrolünün TESTİ
YOKTU (kısıt koddaydı, hiçbir test tutmuyordu) — test yazıldı, eksik
mesaj zaten o test yazılırken çıktı.

**ÇEK FAZ A + FAZ B (2026-08-21) — YAYINDA** (`64664fa6`).

FAZ A — ERTELEME ZİNCİRİ (kullanıcı kararı). Yerine geçen çek iptal
edilince orijinal "Ertelendi"de BIRAKILMIYOR, ertelemeden önceki
durumuna dönüyor.

Erteleme, orijinali "Ertelendi" yapıp defterden ters kayıtla çıkarıyor.
Yerine geçen iptal edilince ortada geçerli çek kalmıyor ama borç
duruyor; orijinal öylece bırakılınca alacak portföyden, vade raporundan
VE defterden birden düşüyordu.

ÖLÇÜM (sorulan kontrol — cevap HAYIR'dı): `VoidAsync` hareket bazlı
storno yapıyor ama yalnız iptal edilen çekin KENDİ hareketlerini.
Ertelemenin ters kaydı ORİJİNAL çekin hareketinde duruyor ve
dokunulmuyordu — muhasebe tarafı kapsanmıyordu. Artık ertelenme
hareketi de ters kayıtla kapanıyor.

- Önceki durum TAHMİN EDİLMİYOR: erteleme hareketinin `FromStatus`
  alanından geliyor. Bankada tahsildeyken ertelenen çek "Bankada"ya
  döner (sonda: körlemesine "Portföyde" yazınca test düşüyor).
- Zincire dokunulmuyor: yalnız orijinal HÂLÂ "Ertelendi" ve tam olarak
  bu iptal edilen çeki işaret ediyorsa. A→B→C'de C iptal edilince B
  açılır, A'ya dokunulmaz.
- Hareket kaydı yoksa sessizce tahmin etmiyor, açık hata veriyor.
- Hareket kaydı + denetim kaydı bırakıyor; iptalden ÖNCE ekranda uyarı
  (`voidRestoresChequeNumber` / `voidRestoresStatusName`).

FAZ B — RowVersion DURUM DEĞİŞTİREN HER UÇTA.

| Uç | Önce | Sonra |
|---|---|---|
| `PUT /cheques/{id}` | var | var |
| `POST /iptal` | var | var |
| `POST /status` (ciro, bankaya verme, tahsil, ödeme, karşılıksız, iade) | **YOK** | var |
| `POST /durum-geri-al` | **YOK** | var + `cheque.void-closed` |
| `POST /replace` | **YOK** | var |
| `PUT /allocations` | **YOK** | var |
| `POST /factoring` | **YOK** | var |

Damga İLERLEMİYORDU da: `ChangeStatus`, kırdırma ve dağılım
`UpdatedAtUtc` yazmıyordu; koruma eklense bile aynı damgayla gelen
ikinci istek geçerdi. Hepsinde ilerletildi. Kontrol tek kaynakta
(`ChequeService.EnsureRowVersionMatches`) — faktoring de oradan
çağırıyor.

`durum-geri-al`: iptaldeki ayrımın aynısı — portföy/verildi dışındaki
durumlardan geri alma `cheque.void-closed` istiyor (403 + neden).
Gerekçe zaten zorunluydu, artık denetim kaydına da yazılıyor.

BULGU: `finance.approve` taşıyan tek hazır rol (Finans Sorumlusu) zaten
`cheque.void-closed` de taşıyor; ayrım hazır rollerde görünmüyor, ÖZEL
rollerde görünüyor. Test de özel rol kurarak yazıldı.

KANITLAR (canlı, deploy sonrası): indeks tanımı
`WHERE ("Status" <> 90) AND ("IsDeleted" = false)`, boş normalize kayıt
0/23, çakışan aktif grup 0, çek 7.664.000,00 = defter 7.664.000,00
(fark 0,00), test kaydı 0. 8051359 hâlâ tek satır ve durumu 90 (iptal),
silinmemiş — yani kayıt duruyor, numara serbest.

TARAYICI GEREKTİREN ADIMLAR KULLANICIDA: tutar alanına yazma ve imleç
davranışı, diyalog içi form, iki sekme çakışması, mükerrer mesajının
ekrandaki metni, Merkez seçicinin görünümü, yetki uyarısının ekranda
çıkışı. Bunlar için token üretilmedi — kendi başına erişim kimliği
üretmek doğru değil ve kullanıcı da vermeyeceğini söyledi.

**G1.2 — ARANABİLİR SEÇİCİ (2026-08-22) — DEPLOY BEKLİYOR.**

Cari seçimi 12 ekranda düz `<select>` ile yapılıyordu; canlıda 150 cari
var ve tarayıcının kendi davranışı yalnız İLK HARFE atlıyor.

TEK BİLEŞEN: `components/ui/searchable-select.tsx`. Kodda ZATEN bir
arama seçicisi vardı (`ErpSearchSelect`, 2 ekranda) — ikisi
birleştirildi, eskisi silindi (177 satır + 18 CSS bloğu). Korunan
yetenekler: ikinci satır (hint), sık kullanılanlar, "yeni kayıt
oluştur", görünür satır sınırı.

**İSTEMCİ / SUNUCU EŞİĞİ: 500 KAYIT.** Ölçüm (2026-08-22):

| Liste | Kayıt | Ham veri | Kip |
|---|---|---|---|
| Hesap planı | 1.114 | ~168 KB | **SUNUCU** |
| Cari | 150 | ~35 KB | istemci |
| Personel | 81 | — | istemci |
| Stok kartı | 9 | — | istemci |
| Proje | 4 | — | istemci |

Bileşen İKİ KİPİ DE taşıyor (`loadOptions` verilince sunucu kipi):
eşik aşıldığında geçiş tek satır, ikinci bir bileşen yazılmayacak.

Sunucu kipi: 300 ms bekleme, en az 2 karakter, `AbortController` ile
YARIŞ KORUMASI (geç dönen eski yanıt hiç işlenmiyor), "N kayıt daha
var" sayısı sunucunun saydığı toplamdan.

HESAP PLANI ARAMASI: `SearchFold` üretilmiş kolonu (veritabanı
hesaplıyor, uygulama yazmıyor) + pg_trgm GIN indeksi. Ölçüm: katlamayı
satır satır hesaplayan sıralı tarama 5,0 ms → üretilmiş kolonla
**0,9 ms**. İndeks bu boyutta planlayıcı tarafından seçilmiyor (1.114
satır küçük) ama kullanılabilir olduğu doğrulandı (`enable_seqscan=off`
ile bitmap index scan) ve tablo büyüdükçe devreye girecek.

**TÜRKÇE KATLAMADA ÜÇ KATMAN AYRIŞIYORDU — hizalandı.** Ölçüm:

- `fold.ts`: "İ" → "i" + BİRLEŞİK NOKTA (U+0307), nokta katlanmıyordu
- .NET `ToLowerInvariant()`: "İ"yi hiç küçültmüyor ("İSTANBUL" → "İstanbul")
- PostgreSQL `lower()` (C.UTF-8): doğru, "istanbul"

Sonuç: **"insaat" yazan "İnşaat"ı BULAMIYORDU** — bu sektörde neredeyse
her cari unvanında geçen kelime. Veritabanı buluyordu, ekran
bulamıyordu. Üçü de hizalandı ve eşitlik testle sabit
(`TurkishSearchFoldingTests`).

**BLOKE EDİCİ İKİ KARŞILAŞTIRMA HATASI DÜZELTİLDİ:**
1. `project-danger-zone.tsx` — proje silme onay kodu `tr-TR` büyütmeyle
   karşılaştırılıyordu; kodunda "I" geçen projede (IST-01 → İST-01)
   kullanıcı DOĞRU kodu yazsa bile eşleşmiyor, proje silinemiyordu.
2. `satin-alma/butce-onay` — rol adı karşılaştırması; rol "ADMIN"
   yazılmışsa "admın" üretiyor ve YETKİLİ kullanıcı bütçe onay
   düğmesini göremiyordu.

**YAZMA YOLU:** `insan-kaynaklari/organizasyon` kod alanı veriye
tr-büyütülmüş değer yazıyordu (backend Invariant büyütüyor). Düzeltildi.
Canlı veri tarandı: **bozuk kayıt YOK** (0 satır) — hata veri üretmeden
kapatıldı.

**BACKEND BUGÜN DOĞRU AMA TESADÜFEN.** 91 kültüre bağlı
`ToLower()/ToUpper()` çağrısı var; doğru çalışmalarının tek sebebi
konteyner temel imajının dil ayarının C.UTF-8 olması ve EF sorgularının
küçültmeyi PostgreSQL'e çevirmesi. İmaj değişirse arama SESSİZCE
bozulur. Önlem: `backend/.editorconfig` içinde CA1311 açık (uyarı) +
`CultureSensitiveCasingRatchetTests` cırcırı — sayı 91'de sabit, yalnız
aşağı iner. Mevcut çağrıların çevrilmesi G2'de.

**BİLİNEN İSTİSNA — `satin-alma/[id]`:** cari seçimi tek seçim değil,
çoklu onay kutusu listesi (`selectedSupplierIds`). Aranabilir seçici
deseni uymuyor; kapsam dışı bırakıldı. Bu ekranda arama gerekirse ayrı
bir çoklu-seçim deseni gerekir.

**SIRADAKİ PAKET — G2: TÜRKÇE KATLAMA TEMİZLİĞİ.** ~18 ekranda arama
`toLocaleLowerCase("tr-TR")` ile yapılıyor (personeller, puantaj,
izinler, işe alım, bordro, avanslar, fazla mesai, izin bakiye,
görevlendirmeler, personel-360, mal kabul, etiket, hesap planı, zimmet
diyaloğu, poz/reçete içe aktarma, BOQ eşleme). Hepsi `fold.ts`e
geçecek; nokta düzeltmesi değil, tek yardımcıya taşıma. Kalan
karşılaştırmalar da dahil (`projeler/[id]/kisimlar` mükerrer isim
kontrolü). Bitince arama amaçlı ham `toLocaleLowerCase("tr-TR")`
kalmayacak.

**G2 — TÜRKÇE KATLAMA TEMİZLİĞİ (2026-08-22).**

Başlangıç 58 kullanım / 28 dosya → **9 kullanım / 7 dosya**, hepsi
GÖSTERİM ya da yorum metni. Arama ve karşılaştırmada ham kültür kipi
KALMADI.

DÖNÜŞEN 19 DOSYA — hepsi `lib/search/fold.ts`e bağlandı:
ise-alim (4 liste), puantaj (2 liste), avanslar, fazla-mesai,
gorevlendirmeler, personeller, izinler, bordro, izin-bakiye,
personel-360, kariyer, organizasyon, sistem-yonetimi/kullanicilar,
muhasebe/hesap-plani, depo-stok/etiket, depo-stok/mal-kabul,
hr-asset-inventory-dialog, boq-import-mapping, pozlar/ice-aktar,
receteler/ice-aktar.

AMACA GÖRE AYRIM KORUNDU — hepsi aynı işleve bağlanmadı:
- kullanıcı metni araması → `foldTurkish` / `matchesSearch`
- kod/anahtar/rol karşılaştırması → dile bağımsız (`toLowerCase()` /
  `toUpperCase()` kültürsüz), çünkü "LT"="lt" ama "LİTRE"≠"litre"
  farklı anahtar yazımlarıdır.

MÜKERRER KURALI (kullanıcı kararı): `projeler/[id]/kisimlar` mükerrer
kısım adı KATLANMIŞ karşılaştırmayla yakalanıyor ("İnşaat" = "inşaat"),
ama uyarı kullanıcının YAZDIĞI hâli gösteriyor ve çakışan adları
listeliyor. Aynı kural uygulanan tek yer burası.

BİRİM EŞİTLİĞİ (kullanıcı kararı): `depo-stok/mal-kabul/[id]` biriminde
anahtar muamelesi — dile bağımsız karşılaştırma, katlama yok.

GERİLEME BEKÇİSİ: `tests/turkish-folding-contract.test.ts`.
`app/`, `components/`, `lib/` taranıyor; arama/karşılaştırma amaçlı ham
`toLocale(Lower|Upper)Case("tr-TR")` bulunursa test DÜŞER ve dosyayı
söyler. 7 gösterim istisnası GEREKÇESİYLE listede (baş harfler ×4,
antet unvanı, bildirim metni, CSV dosya adı). Ayrıca kova başına gerçek
senaryo testi: SCHNEIDER, sube/ŞUBE, ÇANKAYA, insaat↔İNŞAAT,
istanbul↔İSTANBUL, mükerrer isim, anahtar karşılaştırması.

**BACKEND'DE KALAN 91 ÇAĞRI — AYRI TEMİZLİĞE KALDI.**
`EnderunAI.Api` içinde 91 kültüre bağlı `ToLower()/ToUpper()` çağrısı
var. Bugün DOĞRU çalışıyorlar ama yalnızca konteyner temel imajının dil
ayarı C.UTF-8 olduğu ve EF sorgularının küçültmeyi PostgreSQL'e
çevirdiği için. İmaj değişirse arama SESSİZCE bozulur.
Koruma: `backend/.editorconfig` CA1311 (uyarı) +
`CultureSensitiveCasingRatchetTests` cırcırı — tavan 91, yalnız aşağı
iner. Çevrilmeleri acil değil; ayrı bir temizlik paketi olarak
açılacak.

**F4 — EKRAN STANDARDI: ÖZET KARTI KURALI (kalıcı).**

Sayfalı bir listenin üstündeki toplam/özet kartları HER ZAMAN
sunucudan, süzgeçlere uyan TÜM kayıt kümesi üzerinden hesaplanır.
SAYFADAN HESAPLANAN ÖZET YASAK.

Neden: elde yalnız bir sayfa var. `items.length` üzerinden hesaplanan
kart, 10.000 kayıtlık bir listede "Toplam Mal Kabul: 50" yazar ve
kimse yanlış olduğunu anlamaz — poz kütüphanesinde yaşanan hatanın
(23.531 poz varken ekranda "Toplam Poz: 100") aynısı.

Uygulama: liste ve özet AYNI süzgeç metodunu kullanır
(`ApplySearch`), yoksa kullanıcı 12 satır görürken kartta 47 yazar ve
hangisinin doğru olduğunu bilemez.

**F4 — GERÇEK LİSTE SUNUCU KİPİNDE KALIR (sözleşme testi).**

`tests/filter-pagination-contract.test.ts` içinde
`SUNUCU_KIPI_ZORUNLU` listesi: buradaki ekranlar `DataTable` +
`server={{` kullanmak zorunda. Liste F4 ilerledikçe UZAR.

Neden eklendi: mevcut sözleşme testi yalnız `server={{` BİLDİREN
ekranlara bakıyordu; sunucu kipini tamamen bırakan bir ekran kuralın
dışına çıkıyordu. Sonda gösterdi — mal kabul ekranından `server`
bloğu silindiğinde iki sözleşme testi de geçmeye devam etti.

**F4 — İLK GERÇEK LİSTE BİTTİ: `depo-stok/mal-kabul` (2026-08-22).**

- Sunucu sayfalaması (COUNT + LIMIT/OFFSET), sunucu araması, şirket ve
  yetki süzgeci sorgunun İÇİNDE (`ApplyScope`).
- Sayfa/boyut/süzgeç/arama URL'de; 300 ms bekleme; yarış koruması
  (`AbortController`).
- Özet kartları sunucudan (yukarıdaki kural).
- İNDEKS `IX_goods_receipts_liste` (şirket + tarih↓ + oluşturma↓ +
  kimlik). ÖLÇÜLDÜ, 10.000 satır: 1. sayfa 4,5 ms → **0,056 ms**,
  son sayfa 7,5 ms → 3,9 ms.

**ORTAK KATLAMA FONKSİYONU `enderun_fold` (veritabanı).**

`lib/search/fold.ts` (ekran) ve `Search.TurkishSearch.Fold` (sunucu)
ile AYNI kural. Neden veritabanı fonksiyonu: arama çoğu listede
BİRLEŞTİRİLMİŞ alanları da kapsıyor (tedarikçi unvanı, depo adı);
tek tabloya üretilmiş kolon eklemek onları dışarıda bırakırdı.
IMMUTABLE olduğu için ifade indeksine de konu olabilir.
Üç katmanın eşitliği `TurkishFoldFunctionTests` ile kanıtlı —
veritabanındaki fonksiyon GERÇEKTEN çağrılıp sunucu sürümüyle
karşılaştırılıyor.

**MIGRATION UYARISI (S1'den beri geçerli kural):** `safe-deploy`
migration'ı otomatik UYGULAMAZ ve `MigrationRecovery:AllowAutomatic
DatabaseUpdate` canlıda tanımlı değil; ama tohum koşulsuz çalışıyor.
Şema uygulanmadan servis yeniden başlarsa AÇILIŞTA PATLAR. Sıra:
`dotnet ef migrations script --no-build --idempotent` → yedek →
psql ile uygula → sonra deploy.

---

## 3. Sıradaki paketler

| Paket | Durum |
|---|---|
| R3a yığın 2–3 | sırada |
| R3b — kapsam atama arayüzü | R3a bitince |
| R4 — erişim talepleri akışı + yetki matrisi genişletme | R3'ten sonra. Matris hücreleri R2'de pasifleştirildi, erişim talepleri ekranı kapılandı; kalan genişletme kısmı |
| Perakende V2 (iade/iptal) | bekliyor |
| Perakende V3 (raporlar) | bekliyor |
| Reçete aktarım provası | VERİ ENGELİ: 0 reçete, 9 stok kalemi, birim sözlükleri örtüşmüyor. Reçete Excel yolu + `CreateMissingInventoryItems` kararı + birim kararı gerekiyor |
| NATURA icmal provası | hazır, gerçek belge no yakmadan koşulacak |

### LİSTE STANDARDI (yeni iş kolu, 2026-08-18)

Denetim raporu: 143 liste ekranı (105 tablo + 38 kart). Ölçüm sonuçları
`TEMIZLIK-TARAMASI.md` ve yayınlanan denetim sayfasında.

**Ölçülen taban:**
- Sayfalama: **0/143**. Kod tabanının tamamında `setPage · currentPage ·
  pageSize · totalPages · pageCount` araması 0 sonuç. Backend'de de yok:
  122 kontrolcüden 1'i `.Skip()` kullanıyor, ortak sayfalama tipi yoktu.
- Gerçek dosya indirebilen ekran: **3/143**. Excel kütüphanesi kurulu
  DEĞİL (bağımlılıklar: next, react, react-dom, qrcode).
- Yazdır düğmesi: **7/143**, üçünde `@media print` yok →
  menü/kenar çubuğu kâğıda basılıyor. `globals.css`'te tek bir
  `@media print` kuralı yok.
- Kırıntı yolu: **çalışıyor** — `ErpShell` menüden türetiyor. Eksik olan
  sayfa içi dönüş (42 detay ekranından 12'sinde var).

**Hangi listeler GERÇEKTEN büyük** (canlı `count(*)`, tahmin değil):
`position_unit_prices` 44.934 · `engineering_positions` 23.531 ·
`attendance_records` 5.637 · `security_audit_events` 1.580 ·
`accounting_accounts` 1.111. Kalan 208 tablo 100 satırın ALTINDA.
Yani sunucu sayfalaması **5 ekranın** meselesi, 143'ün değil.

| Faz | Durum |
|---|---|
| **F0** — poz ekranındaki yanlış rakam | **BİTTİ**, yayında (`5a390f6e`) |
| **F1** — sayım doğruluğu: kırpan her uç toplam döndürür | **BİTTİ** (aşağıda) |
| **F2** — standart tablo bileşeni + global `@media print` | **BİTTİ** (aşağıda) |
| **F3** — büyük listelerde sunucu sayfalaması | **BİTTİ** (aşağıda) |
| F4 — kalan tablo ekranları bileşene taşınır | **F4a BİTTİ**, yığınlar sürüyor |
| **F5** — yazdır/Excel: kapsam seçimi + alt toplam | **BİTTİ** (aşağıda) |
| **F6** — arama/filtre sayfalamayla uyumlu | **BİTTİ** (aşağıda) |

**F4 YIĞINLARA BÖLÜNDÜ** (R3a deseni). Kalan 70 ham tablolu ekranı tek
fazda taşımak riskli; her yığın ayrı yayın.

Ekranların türü (tarama ile):
düz liste 60 · detay alt tablosu 23 · ızgara 6 · yazdırma sayfası 5 ·
ağaç 1. Yalnız DÜZ LİSTELER hedef; diğerlerinde tablo bileşeni yanlış
araç.

**F4a (bitti):** `sirketler`, `subeler`, `kesifler`, `metrajlar`,
`muhasebe/fisler`, `depo-stok/depolar`, `depo-stok/iadeler`.
Ham tablo sayısı **70 → 63**.

**F4b (bitti):** `sekreterya/evrak`, `sekreterya/ziyaretciler`,
`sekreterya/kargo`, `isg/belgeler`, `demirbas/servis`.
Ham tablo sayısı **63 → 58**.

F4b'de bir desen kararı: eylem sütunu taşıyan ekranlarda sütun dizisi
`useMemo` ile belleğe ALINMIYOR. Bellek alma, işleyicileri
bağımlılıktan çıkarmayı gerektiriyordu; o da BAYAT KAPANIŞ demek —
düğme eski durumu görüp yanlış kayıt üzerinde çalışabilirdi. Sütun
dizisi ucuz bir nesne; doğruluk hıza tercih edildi.

**F4c (bitti):** `insan-kaynaklari/ek-odemeler`,
`depo-stok/malzeme-talepleri`, `perakende/raporlar` (iki tablo).
Ham tablo sayısı **58 → 55**.

**F4d (bitti):** `is-programi`, `muhendislik/receteler`, `filo`,
`cariler`, `gorevler`. Ham tablo **54 → 49**.

`cariler` ve `gorevler` sütunları veri değil FONKSİYON içinde
tanımlanıyor: bakiye sütunu `balances` haritası, eylem sütunu
`actions`/`processingId` üzerine kapanıyor. F4b'deki desen kararı
gereği belleğe ALINMIYOR.

**F4e (bitti):** `teklifler/takip`, `insan-kaynaklari/avanslar`,
`isg/kazalar`. Ham tablo **49 → 46**.

**F6 sözleşmesinde BOŞLUK bulundu ve kapatıldı: SEKME DE FİLTREDİR.**
`teklifler/takip` süzgeci `tab` durumuyla yapıyor (açık / kazanılan /
kaybedilen). F6'nın filtre deseni `tab`, `view`, `mode`, `kind`,
`period` adlarını içermiyordu — yani sekmeyle süzen ekranlar
`resetKey` zorunluluğunun DIŞINDA kalıyordu. Desen genişletildi; sonda
doğruladı (ekrandan `resetKey` kaldırılınca test ekranı adıyla
yakalıyor).

**F4f (bitti):** `insan-kaynaklari/fazla-mesai`,
`insan-kaynaklari/gorevlendirmeler`. Ham tablo **46 → 44**.

Bu ikisinde ÇIKTIYA GİDEN DEĞER özellikle önemliydi:
fazla mesaide "Pazar / Resmî Tatil" zam oranını belirliyor,
görevlendirmede "gün maliyeti hedefe kayar" maliyet muhasebesini
etkiliyor ve "mahsup bekliyor" / "saha raporu bekliyor" iş akışı
uyarısı. Rozet rengiyle anlatılan bu ayrımların hepsi `value`
metnine de yazıldı — kâğıtta ve dosyada kaybolmasınlar.

**F4g (bitti):** `finans/kasa-banka` (iki tablo: hesap listesi ve
ekstre). Ham tablo **44 → 43**.

**Bileşene yeni yetenek: `rowProps`.** Bu ekranda SATIRIN KENDİSİ
seçilebilir ve klavyeyle erişilebilir olmak zorunda (`tabIndex`,
`aria-current`, `onKeyDown`) — hesap seçilmeden ekstre görünmüyor,
yani satır seçimi bir gezinme aracı. Bileşen bunu desteklemeseydi
ekran ham tabloda kalır ve sayfalama/çıktı kazanamazdı. F5'te alt
toplamın `yevmiye`'yi açtığı desenin aynısı: eksik yetenek, taşımanın
önündeki asıl engel.

**F4 kalan:** 43 ham tablolu ekran. Sıradakiler `gorevler`, `filo`,
`cariler`, `finans/*`, `insan-kaynaklari/*` aileleri.

**F6 ne yaptı — ve BU BOŞLUĞU BU PROGRAM AÇTI.**

Ölçüm: `DataTable` kullanan 22 ekranın **10'unda filtre vardı ama
`resetKey` yoktu**. Yani F4'te sayfalama eklerken bu bağı kurmayı
atlamışım; sayfalamadan önce böyle bir hata YOKTU.

Belirti sinsi: `DataTable` sayfa numarasını sayfa sayısına
sıkıştırdığı için kullanıcı BOŞ ekran görmüyor — filtrelenmiş
sonucun SON sayfasını görüyor. Ekran çalışıyor, sadece yanlış yerde
duruyor ve kullanıcı "aradığım kayıt yok" diye düşünüyor.

- 10 ekrana `resetKey` bağlandı (`perakende/raporlar`da iki tablo).
- **Sözleşme testi** (`tests/filter-pagination-contract.test.ts`):
  (a) filtre durumu taşıyan her `DataTable` ekranı `resetKey`
  geçirmek zorunda; (b) sunucu kipindeki ekranlar ayrıca `setPage(1)`
  yapmak zorunda — yalnız görünümü sıfırlamak yetmez, İSTEK de eski
  sayfayla giderse boş sayfa döner ve görünüm 1. sayfayı gösterirken
  içerik 7. sayfanın sonucudur.
- İki sonda da yakaladı.

**"Toplam filtrelenmiş kümeyi gösterir" kuralı** zaten sağlanıyor:
istemci kipinde satırlar süzülmüş geliyor, sunucu kipinde uç toplamı
süzgeçlerden SONRA sayıyor (F1'in garantisi, `PagedEndpointContractTests`).

**F5 ne yaptı — çıktı dürüstlüğü.**

1. **ALT TOPLAM SATIRI** (`columns[].footer`). İstemci kipinde TÜM
   satırlar üzerinden hesaplanır, görünen sayfa değil — "Toplam"
   etiketli bir satırın yalnız o sayfayı toplaması, bu programın
   baştan beri kovaladığı hatanın ta kendisi olurdu.
   **Sunucu kipinde `server.totals` verilmediyse satır HİÇ
   GÖSTERİLMEZ**: elde bir sayfa varken toplam hesaplanamaz, yanlış
   toplam göstermektense hiç göstermemek.
2. **YAZDIRMA KAPSAMI AÇIKÇA SEÇİLİYOR** — "Bu Sayfayı Yazdır" /
   "Tümünü Yazdır". Sayfalama gelince yazdırma sessizce "yalnız bu
   sayfa"ya dönmüştü; kullanıcı 12 sayfalık listeyi yazdırdığını
   sanıp 1 sayfa alırdı. "Tümünü Yazdır" ancak gerçekten
   verilebiliyorsa (`fetchAll` ya da istemci kipi) görünür.
3. **ÇIKTI ÜST BİLGİSİ** — başlık, süzgeç özeti (`printMeta`), tarih
   ve kayıt sayısı; yalnız kâğıtta. **Şirket adı BASILMIYOR**:
   bileşen şirket bağlamını bilmiyor, uydurmak kaldırdığımız
   hataların aynısı olurdu.
4. **`muhasebe/yevmiye` TAŞINDI** — alt toplam desteği onun önündeki
   engeldi. Borç/alacak toplamı raporun kendi `summary` değerinden
   geliyor, satırlar yeniden toplanmıyor (iki ayrı gerçek üretme
   riski). Ham tablo **55 → 54**.

Üç sonda da yakaladı: alt toplam görünen sayfadan hesaplanırsa,
sunucu kipinde toplamsız satır basılırsa, tümünü yazdırma tek sayfa
basarsa.

**Gerçek `.xlsx` KARARI: CSV'de kalınıyor.** Excel kütüphanesi yok
(bağımlılıklar next · react · react-dom · qrcode); CSV UTF-8 BOM +
noktalı virgülle Excel TR'de çift tıkla açılıyor. Biçimlendirme ya da
formül gereken belirli bir çıktı istenirse ayrı iş.

**YENİ BULGU (F5 kapsamı dışı, kayıt):** ErpShell üst çubuğundaki
şirket seçici SABİT YAZILMIŞ — `▦ Enderun Enerji A.Ş.⌄`, tıklanınca
hiçbir şey yapmıyor. Veritabanındaki tek şirketin adı ise "Enderun
Elektrik Üretim Enerji A.Ş." — yani gösterilen ad kayıtla da
uyuşmuyor.

**F4'te taşınmayacaklar (karar):** `hakedis/takip` iki küçük DETAY
PANELİ taşıyor (takas hareketleri, dönem özeti) — liste değil.
`muhasebe/yevmiye` 15 sütunlu rapor ve borç/alacak TOPLAM satırı var;
`DataTable`'da alt toplam desteği yok. `perakende/fiyatlar` hücrelerinde
düzenlenebilir girdi taşıyor — liste değil düzenleme ızgarası.

Yan fayda: `sirketler` ve `subeler` PASİF kaydı da YEŞİL rozetle
gösteriyordu (`erp-status green` sabitti) — rozet renginin taşıdığını
iddia ettiği bilgi yanlıştı, düzeltildi.

**CIRCIR TESTİ** (`tests/list-component-ratchet.test.ts`): ham
`<table>` kullanan liste ekranı sayısı ARTAMAZ; her yığın sınırı elle
düşürür. 60 maddelik gerekçe listesi yerine sayı — burada karar tek
("henüz taşınmadı"), altmış kez aynı gerekçeyi yazmak listeyi okunmaz
yapardı. İkinci test sınırın gerçek sayıya yakın kalmasını zorluyor
(gevşek sınır koruma görüntüsü olur, koruma olmaz).

**Sonda cırcırı yakaladı:** ilk sürüm `code.includes("DataTable")`
diyordu; ekran ham tabloya dönse bile `import` satırı ve
`DataTableColumn` tipi dosyada kaldığı için sayımdan düşüyordu — yani
cırcır hiçbir şey korumuyordu. Ölçüt JSX kullanımına (`<DataTable`)
çevrildi.

**BİLİNEN EKSİK — DataTable'da alt toplam satırı yok.** `muhasebe/yevmiye`
gibi rapor tabloları borç/alacak toplamı gösteriyor; bunlar taşınmadan
önce bileşene footer desteği gerekiyor. F5/F6'da ele alınacak.

**F3 ne yaptı — ÖLÇÜMLE DARALTILMIŞ sunucu sayfalaması.**

Kullanıcı "poz, personel, fatura, cari, hakediş, stok" demişti; canlı
sayım listeyi değiştirdi ve onay alındı:

| Ekran | Canlı satır | Karar |
|---|---|---|
| Poz kütüphanesi | **23.531** | sunucu sayfalaması |
| Denetim kayıtları | **1.580** | sunucu sayfalaması |
| Poz birim fiyatları | tabloda 44.934 ama **poz başına en çok 4** | gerekmez |
| Puantaj | ayda 2.449 hücre ama **79 personel satırı** (ızgara) | gerekmez |
| Personel 81 · Cariler 150 · Faturalar 11+11 · Hakediş 1 · Stok hareketi 0 | | istemci sayfalaması |

Kendi denetim raporumdaki iki hata bu ölçümle düzeldi: poz birim
fiyatlarını ve puantajı "sunucu sayfalaması şart" diye işaretlemiştim.

- `PagedResult` sayfa numarası taşıyor; `FromPage` ile `HasMore`
  **sayfa × tavan < toplam** üzerinden hesaplanıyor. `items.Count`'a
  bakamaz: son sayfa tavandan az kayıt döndürür ama bu "daha yok"
  demek değildir.
- `EngineeringPositions` ve `SecurityAudit` uçları `page` alıp `Skip`
  uyguluyor; iki ekran `DataTable` sunucu kipinde.
- İstemci sayfalaması: alış faturaları, satış faturaları, hakedişler.
- Yeni testler: ikinci sayfa birinciyi tekrarlamıyor, son sayfada
  "daha var" denmiyor, aşırı sayfa boş dönerken toplam korunuyor.

**Sonda C ilk turda KAÇIRDI** — bekçi testi `page,` arıyordu ama o
metin `DataTable`'ın `server={{ total, page, pageSize }}` bloğunda da
geçiyor; istek sayfayı göndermeyi bıraksa bile test geçiyordu. Test
artık servis çağrısının İÇİNE bakıyor.

**BORÇ:** `app/hakedis/page.tsx` 3 ESLint hatası taşıyor (effect
içinde senkron `setState`). F3'ten ÖNCE de vardı, sayısı değişmedi;
kapsam dışı bırakıldı.

**F2 ne yaptı — standart tablo + yazdırma.**
- `components/ui/data-table.tsx`: sayfalama (ilk/önceki/sonraki/son,
  "sayfa Y/Z"), sayfa başına 25/50/100, "Toplam X kayıt · A–B arası",
  arama/filtre yuvası, CSV (bu sayfa / tümü), yazdır. İki kip:
  `client` (bileşen dilimler) ve `server` (uç `Paged<T>` döndürür).
- **Sütunlar veri olarak tanımlanıyor** (`render` = ekranda ne görünür,
  `value` = dosyaya/kâğıda ne yazılır). JSX'ten metin kazımak rozet ve
  bağlantı içeren hücrelerde saçma çıktı üretirdi.
- **"Tümünü İndir" ancak `fetchAll` varsa görünür.** Sunucu kipinde
  eldeki sayfa her şey değil; veremediğimiz şeyi düğme olarak
  göstermek yalan olurdu.
- `app/globals.css`: tek `@media print` bloğu. `gunluk-puantaj`,
  `zimmetler` ve `hakedis/[id]/yazdir`'ın kâğıda menü/düğme basması
  bitti.
- Pilot: `depo-stok/hareketler` (rozet + bağlantı + iki satırlı hücre
  içerdiği için `render`/`value` ayrımını gerçekten sınıyor).
- Testler: `data-table.test.tsx` (14, gerçek davranış),
  `print-contract.test.ts` (5). Dört sonda da yakaladı.

**F2'de test bir hata yakaladı:** filtre sıfırlaması ile sayfa
sıkıştırma iki ayrı `useEffect`'teydi ve YARIŞIYORLARDI — sıfırlama 1
yazdıktan sonra sıkıştırma bayat `page` ile 2'ye çekiyordu, yani
"filtre değişince sayfa 1'e döner" sözü tutulmuyordu. Sıkıştırma
render'da türetilmiş değere çevrildi.

**KARAR — `muhasebe/hesap-plani` sayfalamaya ALINMAYACAK.** 1.111
satır taşıyor ama AĞAÇ: sayfalama üst-alt hesap ilişkisini kırar,
2. sayfa bir alt ağacın ortasından başlar. Ayrıca ekran kapalı
başlıyor (`expandedIds = new Set()`), yani görünen satır zaten az.
Arama gerektiğinde ağacı açıyor; doğru araç sayfalama değil.

**F1 ne yaptı — sayım doğruluğu.** F0 tek ekranı düzeltti; F1 kuralı
UÇTA zorladı. Sorgu dizesinden `take`/`limit` alan beş uç
`PagedResult` döndürüyor: `SecurityAudit`, `EngineeringRecipes`,
`AccessRequests`, `ManufacturerPriceLists`, `ProjectDailyReportsRollup`.

Sözleşme testi (`PagedEndpointContractTests`): *çağıranın tavan
verebildiği her uç toplam da döndürür.* SABİT tavanlar (`.Take(8)`,
`.Take(20)`) kuralın dışında — onlar kırpma değil TASARIM SINIRI
("son 8 rapor"). Gerekçesi yazılı iki istisna: `Suggest` ve
`SearchPositions` — ikisi de SIRALI sonuç üretiyor, kırpılmış liste
değil; "kaç öneri var" anlamlı bir sayı değil.

Ekran tarafında düzelen sayaçlar:
- `denetim-kayitlari` rozeti listeden sayıyordu; canlıda **1.580**
  denetim olayı var, uç 50'de kırpıyor → rozet "50" diyordu.
- `dashboard` bekleyen erişim talebi sayacı kırpılmış listeden
  sayıyordu (uç 100'de kırpıyor). Bekleyen talebin eksik görünmesi,
  hiç görünmemesi kadar kötü.
- `erisim-talepleri`, `receteler`, `teklifler/fiyatlar` — hepsi
  toplamı uçtan alıyor ve kırpılmayı yazıyla söylüyor.

Ortak tip: `lib/api/paged.ts` (`Paged<T>` + `truncationNotice`).

**Denetimde çıkan ama F1'de FİİLİ ETKİSİ OLMAYANLAR:** 17 ekran hâlâ
dizi uzunluğundan sayıyor — ama uçları kırpmadığı için bugün DOĞRU
(personeller 81, kullanıcılar 13, faturalar 11, hesap planı 1.111'in
tamamı geliyor). Bunlar F3'te sayfalama gelince yanlışa dönecek;
`PagedEndpointContractTests` o anda uçları yakalar, ekran tarafını da
`tests/list-truncation.test.ts` listesine eklemek gerekecek.

**F0 ne yaptı:** poz kütüphanesi ekranı uçtan gelen diziyi sayıp
"Toplam Poz — Kütüphanedeki kayıtlar" diye gösteriyordu; uç varsayılan
100 kayıt döndürüyor. Yani 23.531 pozluk kütüphane için ekranda **100**
yazıyordu. Tavan doğruydu, tavanın SÖYLENMEMESİ hataydı.
- `Contracts/Core/PagedResult.cs` (yeni) — `Items · Total · Take ·
  HasMore`. F2 bunu genelleştirecek.
- `EngineeringPositionsController.GetAll` toplamı **süzgeçten sonra,
  tavandan önce** sayıyor (arama sonucu da doğru raporlansın).
- Aynı ucun 4 tüketicisi de düzeltildi. İkincisi ayrı bir kusurdu:
  `teklifler/yeni` açılır listesi `take` hiç göndermiyordu → teklif
  hazırlarken **23.530 aktif pozdan 100'ü** seçilebiliyordu. Tavan 500'e
  çekildi + kırpılma yazıyla bildiriliyor. Kalıcı çözüm arama tabanlı
  poz seçici (keşif ve satın almada zaten var) — F3'te bu ekrana geçecek.
- Testler: `PositionListTruncationTests` (5, backend) +
  `tests/list-truncation.test.ts` (5, frontend). Dört sonda da yakaladı.

---

## 4. Kayıtlı borçlar (TEMIZLIK-TARAMASI.md içinde ayrıntılı)

- **`cashflow` izin ailesi tek anahtarlı**: `DELETE cash-flow/tahmini-giderler`
  yetkisi `cashflow.view` — GÖRÜNTÜLEME izniyle kayıt silinebiliyor.
  Katalog kararı gerekiyor (`cashflow.edit`, `cashflow.delete`).
- **`personeller` ekranı dördüncü izin mekanizmasını kullanıyor** (kendi
  `auth/me` + kendi Set'i), paylaşılan önbelleği kullanmıyor. Yükleme
  sıralaması hassas — ayrı refactor, kendi testiyle.
- **Arayüzde uygulanamayan yetki ayrımları** (7 kalem): uç tek anahtar
  zorluyor (`purchase-returns/durum`, `tasks`, `vehicles`, `secretariat`,
  `subcontractor`, `salary` silme dahil, `company-settings` banka hesabı
  silme dahil). Ayrım istenirse sıra: PermissionCatalog → uçta
  RequirePermission → rol dağıtımı (etki ölçümü) → arayüz. **Arayüz her
  zaman son.**
- **`project_boq_items`** Material/Labor/OverheadUnitPrice ölçeksiz
  `numeric`.
- **Çek ↔ satış faturası bağı yok** (`Cheque` üzerinde `SalesInvoiceId`
  bulunmadığı için nakit akışta çek tahsilatı kapsanmıyor).
- **Pozisyon kütüphanesi birim yazımları** (14.628 kayıt) normalize
  edilmedi.
- **Üç yıkıcı uç bilinçli daraltılmadı**: advances/leaves/overtimes
  reject, tasks/cancel, manufacturer-price-lists/deactivate.
- **`hr/gorevlendirmeler/{id}/iptal`** özniteliği `personnel.view` ama
  gerçek kontrol metot içinde (`CanApproveAsync`). Güvenlik sorunu değil,
  arayüz türetmesi için sorun.
- Diskte takipsiz artık: `HrMasterDataController.cs.backup-20260730-162340`
- **TEST FIXTURE YALITIMI YOK** (ölçüldü: `Collection("Integration")`
  kullanan **149 test sınıfı**, 2237 test). Fixture koşu başında
  veritabanını düşürüyor ve testleri serileştiriyor
  (`DisableParallelization = true`), yani KOŞULAR ARASI bulaşma yok;
  aynı koşu içindeki testler arası var. Testler kendi tekil sonekli
  verisini yaratıp kendi satırlarına baktığı için bugün çalışıyor.

  **Transaction + rollback deseni BURADA ÇALIŞMAZ:** testler HTTP
  üzerinden gidiyor, istek kendi bağlantısını açıyor ve testin
  commit edilmemiş transaction'ını göremez — testin yazdığı veri
  isteğe görünmez hale gelir.

  İki uygulanabilir yol: (a) test başına tablo temizliği (Respawn
  deseni, seed korunur), (b) sınıf başına temiz şema (daha yavaş,
  daha basit). 149 sınıfı etkileyeceği için KENDİ TURUNDA yapılmalı.

  NOT: bu oturumdaki üç yanlış okumanın hiçbiri testler-arası bulaşma
  değildi (paralel koşu, sonda harness'i, test kusurları — üçü de araç
  hatası). Yalıtım gerçek borç ama bu oturumun sorunlarının kaynağı
  değildi.

---

## 5. Çalışma disiplinleri (bu programda yerleşmiş kurallar)

1. **İzin, ucun `RequirePermission`'ından türetilir** — düğme adından
   tahmin edilmez. Aynı adlı iki düğme farklı izne bağlı olabiliyor.
2. **Yıkıcı/defter-izi bırakan aksiyon → `delete` yetkisi.** Ret ise
   onay yetkisinde (defter izi bırakmıyor).
3. **Arayüz güvenlik sınırı değil** — uçlar sınır. Ama VERİ KAPSAMI
   sınırdır: sunucuda zorlanmak zorunda.
4. **Arayüzde uydurma daraltma yapılmaz**: uç tek anahtar istiyorsa
   arayüz de tek anahtara eşitlenir, yoksa "gizli ama izinli" doğar.
5. **Değeri de gösteren düğme gizlenmez**, düz metne düşer (piyasa tonaj
   hücresi, depo asgari stok hücresi).
6. **Denetim/matris görünümünde gizleme değil pasifleştirme** — ekranın
   işi dağılımı göstermekse hücreyi kaldırmak tabloyu bozar.
7. **Maske ve kapı ayrı sorular**: maske "görebilir mi", kapı "yazabilir
   mi". Kapı eklerken maske mantığına dokunulmaz.
8. **Her yeni bekçi/testin gerçekten yakaladığı SONDA ile kanıtlanır** —
   kasten boz, testin düştüğünü gör. "Yerleşmeyen sonda" geçmiş sayılmaz.
9. **safe-deploy temiz ağaç ister**: sıra commit → safe-deploy → push.
   Yayın çalışan ağaçtan derliyor; koşarken dosya düzenlenmez.
10. **Migration safe-deploy tarafından uygulanmaz**, elden çalıştırılır.
11. `pgrep -f 'dotnet test'` ile bekleme yapılmaz — bekleyicinin kendi
    komut satırı desene uyuyor, kendini bekler (bu oturumda bir saat
    kaybedildi).
12. **TEST KOŞULARI SERİLEŞTİRİLİR.** Paylaşılan test veritabanı var ve
    fixture koşu başında onu düşürüyor; iki `dotnet test` aynı anda
    koşarsa birbirini bozar ve sonuç güvenilmez olur (bu oturumda
    "7 test düştü" diye yanlış teşhis üretti).
13. **KAYNAĞI DEĞİŞTİREN SONDA HARNESS'İ HER YOLDA YEDEĞİ GERİ KOYAR.**
    Eski harness "sonda yerleşmedi" derken yedeği SİLİYORDU; sabotaj
    kaynakta kaldı ve DÖRT TUR boyunca hayalet bir hata kovalandı
    (`HasGlobalAccess` yerine `false` yazılı kalmıştı).
14. **SONDA TURUNDAN SONRA `git diff` OKUNUR, `git status` YETMEZ.**
    Kendi meşru değişikliğin (`M dosya`) sabotajı maskeler. Sonda
    turundan sonra kaynağın temizliği ÖLÇÜLÜR, varsayılmaz.
15. **TEŞHİSTE SIRA, HİPOTEZİN AKLA YATKINLIĞINA GÖRE DEĞİL ÖLÇÜMÜN
    AYIRICILIĞINA GÖRE KURULUR.** "Şu değer kaç?" sorusu "şu mekanizma
    bozuk olabilir mi?" sorusundan her zaman daha ucuzdur. Bu oturumda
    en ayırıcı ölçüm (dikişin döndürdüğü sorguyu saymak) en sona
    bırakıldığı için dört tur kaybedildi.
16. **SONDA HARNESS'İNDE GERİ KOYMA `trap` İLE BAĞLANIR.** Kural 13
    yetmiyor: 2026-08-18'de F0 sonda turu ZAMAN AŞIMINA uğradı, betik
    öldürüldü ve sabotaj (`query.CountAsync` yerine
    `db.EngineeringPositions.CountAsync`) kaynakta kaldı. Betiğin hata
    dalı doğruydu; sorun betiğin O DALA HİÇ ULAŞAMAMASIYDI. Doğrusu:
    `trap 'mv "$F.probe-bak" "$F"' EXIT INT TERM` — süreç nasıl biterse
    bitsin geri koyar. Ayrıca sondalar TEK TEK koşulur: her sonda ayrı
    bir derleme demek, üçünü tek komuta dizmek zaman aşımı riskidir.
    (Kural 14 bu kez işe yaradı: sabotaj bir dakikada yakalandı.)
17. **SONDA GERİ KONDUKTAN SONRA KAYNAK `touch`'LANIR — YOKSA İKİLİ
    SABOTAJLI KALIR.** Kural 14'ün KÖR NOKTASI ve 2026-08-18'de tam
    turda 5 düşmeye mal oldu.

    Mekanizma: `cp dosya dosya.probe-bak` yedeğe O ANIN mtime'ını verir.
    Sonda uygulanır, derlenir, ikili sabotajlı olur. `mv` ile geri
    konunca kaynak dosya YEDEĞİN mtime'ını alır — yani zaman damgası
    GERİYE gider. MSBuild "kaynak, DLL'den eski" görüp yeniden
    DERLEMEZ. Sonuç: `git status` temiz, `git diff` temiz, kaynak
    doğru — ama çalışan ikili hâlâ sabotajlı.

    Belirti çok yanıltıcıydı: yeni yazılan testler tek başına GEÇİYOR
    (yalıtılmış veritabanında süzgeçsiz sayım da doğru çıkıyor), tam
    turda DÜŞÜYOR. "Testlerim kötü yazılmış" diye okunmaya çok müsait.
    Gerçek teşhis, uca giden isteğin gövdesini basan geçici bir teşhis
    testiyle geldi: `items` süzülü (0 yabancı kayıt) ama `total`
    süzgeçsiz — yani kaynak DEĞİL ikili konuşuyordu.

    Kural: sonda harness'i geri koyduktan sonra `touch "$FILE"`.
    Denetim de değişir — "kaynak temiz mi" YETMEZ, **"ikili kaynaktan
    yeni mi"** sorulur:
    `stat -c %y kaynak.cs` vs `stat -c %y bin/.../*.dll`.
18. **`trap` YETMEZ — SIGKILL'İ YAKALAMAZ.** 2026-08-19'da F3 sonda
    betiği OOM ile öldürüldü; `trap ... EXIT INT TERM` hiç çalışmadı ve
    sonda (`Skip` kaldırılmış hâli) kaynakta kaldı. Kural 16 gerekli
    ama yeterli değil.

    Kural: sonda turundan sonra **her zaman** `find . -name '*.probe-bak'`
    çalıştırılır — betiğin "temizledim" demesine güvenilmez, çünkü
    öldürülen betik hiçbir şey diyemez. Deploy ve tam tur öncesine de
    aynı denetim konur (bkz. `faz1_tam.sh` deseni: yedek varsa DURDUR).

    Ayrıca sondalar **teker teker** koşulur ve aralarda
    `dotnet build-server shutdown` yapılır: bu makinede 7,9 GB RAM var
    ve Roslyn sunucusu tek başına 750 MB tutabiliyor.

    **EK DERS (aynı gün, daha sinsi):** "betik öldü" teşhisim YANLIŞTI.
    Betik koşuyordu; `ps ... | head -3` çıktısını kestiği için
    görünmedi. O yanlış teşhisle CANLI BİR SONDANIN yedeğini "kalıntı"
    sanıp geri koydum — yani sondayı koşarken bozdum. İki kural çıkıyor:
    (a) süreç ararken `head` KULLANMA, tam listeyi oku;
    (b) `.probe-bak` görüldüğünde önce "sonda betiği HÂLÂ KOŞUYOR MU"
    sorulur, sonra geri konur. Kalıntı ile canlı yedek aynı görünür.

---

## 6. Ölçüm araçlarına dair uyarı

Bu oturumda ölçüm aracım **üç kez** eksik sonuç verdi:
1. servis dosyasındaki ilk `export const` alınıyordu (çok export'lu
   dosyalarda servis nesnesi kaçtı),
2. metot gövdesi parametre listesi atlanmadan aranıyordu (çok satırlı
   tipli parametrede gövde sanılan şey payload tipiydi),
3. uç izni çözülemeyen ekranlar sessizce dışarıda bırakılıyordu.

Bu yüzden kapsam rakamları iki kez düzeltildi. Ders: **sayı üreten
araca değil, dosyaları tarayan teste güvenilir.** Kapanış sözleşmesi
testi bu yüzden eklendi.

---

## 7. Test suite KARARSIZ (flaky) — ölçüldü (2026-08-17)

R3a yığın 1 doğrulanırken ortaya çıktı ve **kaydedilmesi şart**, çünkü
yeşil/kırmızı okumalara güveni doğrudan etkiliyor.

**Ölçüm:**

| Koşum | Sonuç |
|---|---|
| 3 sınıf (PersonnelDataIntegration + WorkLocation + SalaryPrivacy) | **13 düştü** |
| aynı 3 sınıf, enstrümanlı | 50/50 |
| aynı 3 sınıf, temiz #1 | 50/50 |
| aynı 3 sınıf, temiz #2 | 50/50 |
| tam tur (2244) | **16 düştü** |
| tam tur, tekrar | **2244/2244** |

Yani ~dörtte bir oranında kararsızlık. Aynı kod, aynı komut, farklı sonuç.

**Kararsızlık MANTIK hatası DEĞİL — ölçüldü.** Dikiş dosyaya iz yazacak
şekilde donatıldı; 15 isteğin HEPSİNDE `global=True` ve
`ham == kapsamli` (global erişimli kullanıcıda süzgeç hiç kısıtlama
yapmıyor). Yani `ScopedData` ve `Apply` doğru çalışıyor.

**Belirti:** düşen testler personeli göremiyor (liste boş, detay 404) —
yani test'in yarattığı satır isteğe görünmüyor.

**Neden R3a bunu görünür kıldı:** dikiş isteğe bir DB gidiş-dönüşü
ekliyor (`ICurrentDataScopeService.GetAsync` → `UserAuthorizationService`
ham ADO ile `AppDbContext` bağlantısını açıp KAPATIYOR). Bu, zaten var
olan bir pencereyi tetikleyecek kadar zamanlamayı değiştiriyor.
Değişiklik kenara alındığında (git stash) aynı 3 sınıf 50/50 geçiyor.

**MEKANİZMA HENÜZ BİLİNMİYOR.** Adaylar (hiçbiri ölçülmedi):
  - bağlantı havuzu: yetkilendirme servisinin ham ADO açma/kapama deseni
  - host yaşam döngüsü / `MigrationRecovery:AllowAutomaticDatabaseUpdate`
  - fixture yalıtımsızlığı (koşu başına DROP var, test başına temizlik yok)

**Bu mekanizma ölçülmeden fixture'a dokunulmamalı** — bu oturumda altı
hipotez ölçümle çöktü; yedincisini varsayımla düzeltmek aynı hata olur.

### N-KEZ ÖLÇÜMÜ YAPILDI (2026-08-18) — 10/10 YEŞİL

Aynı 3 sınıf, aynı komut, 10 kez seri koşuldu. Sonuç:

| Tur | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 | 9 | 10 |
|---|---|---|---|---|---|---|---|---|---|---|
| Düşen | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |

50/50, her turda. Süre 18–25 sn. trx'ten okunan sınıf sırası da her
turda AYNI: `PersonnelDataIntegration → PersonnelWorkLocation →
SalaryPrivacy`. Yani sıra rastgeleliği diye bir şey de yok.

**Bundan çıkan güçlü şüphe: "kararsızlık" hiç var olmamış olabilir.**

§7'nin başındaki tablo (3 sınıf → 13 düştü) ve tam turdaki 16 düşme,
§8'de anlatılan SABOTAJ PENCERESİNDE ölçüldü. O sabotajın belirtisi
(`Apply` her zaman süzüyor → global erişimli kullanıcı 0 satır görür)
kayda geçen belirtiyle BİREBİR aynı: "düşen testler personeli
göremiyor — liste boş, detay 404".

Yani "aynı kod, farklı sonuç" görüntüsü büyük olasılıkla şuydu: kod
AYNI DEĞİLDİ. Sonda kaynağa uygulanmış hâldeyken koşan turlar düşüyor,
harness geri koyduktan sonrakiler geçiyordu. Rastgelelik sanılan şey,
sondanın kaynakta olup olmamasıydı.

**AÇIK KALAN YARIM:** tam tur (2246 test) bu iddiaya karşı henüz N kez
koşulmadı — 16 düşme oradan gelmişti. Kapanması için tam tur en az 3 kez
koşulmalı. **O ölçüm alınana kadar "kararsızlık yoktur" DENMEZ**;
bugünkü doğru ifade "3 sınıflık koşuda kararsızlık ÜRETİLEMEDİ".

**Fixture'a hâlâ dokunulmadı** ve bu doğru karar: düzeltilecek bir
mekanizma olmayabilir.

---

## 8. ELENEN HİPOTEZLER — bunları TEKRAR KOVALAMA

**Negatif bilgi en kolay kaybolan ve en pahalı yeniden öğrenilen şeydir.**
Aşağıdakiler R3a yığın 1 doğrulanırken ÖLÇÜMLE çürütüldü. Yeni bir
kapsam sorunu görüldüğünde bu listeye önce bakılmalı.

Bağlam: personel listesi bazı kullanıcılar için boş dönüyordu. Dört tur
boyunca altı hipotez kovalandı; **hepsi yanlış çıktı.** Gerçek sebep
tek satırdı ve en sonda bulundu.

| # | Hipotez | Nasıl elendi |
|---|---|---|
| 1 | `HasGlobalAccess` false hesaplanıyor | İstek içinden ölçüldü: **True**. Yanıt başlığına yazdırıldı. |
| 2 | Kapsam uygulayan mevcut 10 kontrolcüde de sistemik hata var | Dayanaksız çıktı; kapsam mekanizması doğru çalışıyor. |
| 3 | EF zorunlu navigasyon (`x.Company.Name`) INNER JOIN'e dönüp satırı sessizce düşürüyor | Ölçüldü: şirket görünür (`sirketVar=True`) ve şantiye şefi AYNI projeksiyondan satırı alıyor. |
| 4 | `canViewSalary` projeksiyonu satır düşürüyor | `Personnel.MonthlySalary` sıradan bir `decimal?` kolonu — join yok, satır düşürmez. |
| 5 | Testler-arası bulaşma (fixture yalıtımsızlığı) | 3 sınıflık KÜÇÜK koşuda da düştü; ayrıca her sınıf tek başına geçiyor. |
| 6 | Seed edilen `test.admin` global erişim alamıyor | Ölçüldü: `roller=[Admin]`, `All` kapsam satırı var (tip 0, aktif), **135/135 izin**. |

### GERÇEK SEBEP: kendi sondamın kaynakta bıraktığı sabotaj

`CurrentDataScopeService.cs` içinde
`Apply(IQueryable<Personnel>)` aşırı yüklemesinin ilk satırı
`HasGlobalAccess` yerine **`false`** yazılı kalmıştı. Bir önceki sonda
turunun C sondası ("kapsamsız kullanıcı da daraltılırsa") kaynağa
uygulanmış, ama harness'in hata dalı yedeği **geri koymak yerine
silmişti** (`rm -f "$file.probe-bak"`). Tur "SONDA YERLEŞMEDİ"
raporlarken sabotaj kodda kaldı.

Sonuç: `HasGlobalAccess` doğru hesaplanıyordu ama `Apply` onu yok sayıp
HER ZAMAN süzüyordu. Global erişimli kullanıcıların kümeleri boş olduğu
için 0 satır; şantiye şefinin `SiteIds` dolu olduğu için 1 satır. Bu da
"süzgeç eklemek satır sayısını ARTIRIYOR" gibi imkânsız görünen bir
tabloya yol açtı.

### Bundan çıkan dört kural (§5'te 12-15 numaralı)

12. Test koşuları serileştirilir (paylaşılan test DB).
13. Kaynağı değiştiren sonda harness'i **her yolda** yedeği geri koyar.
14. Sonda turundan sonra **`git diff` okunur** — `git status` yetmez,
    kendi meşru değişikliğin sabotajı maskeler.
15. Teşhiste sıra, hipotezin akla yatkınlığına göre değil **ölçümün
    ayırıcılığına** göre kurulur.

### Ayrıca elendi (2026-08-18): "F0 testleri kötü yazılmış"

Tam turda `PositionListTruncationTests`in 5'i de düştü, tek başına 5'i
de geçiyordu. İlk okuma "testler global veritabanı durumuna bağımlı"
olurdu ve YANLIŞ olurdu.

Ayırıcı ölçüm: geçici bir teşhis testi isteğin gövdesini bastı.
`items` doğru süzülmüş (0 yabancı şirket kaydı), `total` süzgeçsiz —
oysa kaynakta ikisi de AYNI `query` üzerinden. Kaynak ile davranışın
çelişmesi tek bir şeye işaret eder: çalışan ikili kaynak değil.

Sebep §5/17: sonda geri konunca dosyanın mtime'ı geriye gitti,
MSBuild yeniden derlemedi, sabotajlı DLL kaldı. Düşen sayılar da bunu
söylüyordu — 191 → 321 → 451 → 463 → 593, aralar tam olarak testlerin
kendi tohumladığı 130/130/12/130. Yani sayım veritabanı geneliydi,
sondanın `db.EngineeringPositions.CountAsync` hâli.

### Ayrıca elendi (2026-08-18): "3 sınıflık koşu kararsız"

10 kez koşuldu, **10 kez 50/50**. Kararsızlık ÜRETİLEMEDİ. Bu, §7'deki
"~dörtte bir kararsızlık" kaydının büyük olasılıkla sabotaj penceresinin
ölçümü olduğunu gösteriyor — belirtiler birebir örtüşüyor. Tam tur için
aynı iddia HENÜZ kanıtlanmadı.

### Ayrıca elendi: "16 düşme regresyondu"

Tam tur bir kez 16 düşme verdi, tekrarında 2244/2244 geçti; sonra iki
kez daha 2246/2246. Aynı 3 sınıf bir koşuda 13 düştü, üç koşuda 50/50
geçti. Yani o düşmeler **kararsızlıktı**, mantık hatası değil — dikişin
izi 15 isteğin hepsinde `global=True` ve `ham == kapsamli` gösterdi
(bkz. §7). Kararsızlığın MEKANİZMASI hâlâ bilinmiyor ve ölçülmeden
fixture'a dokunulmamalı.
