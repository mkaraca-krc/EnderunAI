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

**S6b (sıradaki) — GR/IR fişleri + stok↔muhasebe tutarlılık raporu.**
AÇIK SORU: GR/IR (faturası gelmemiş mal) hangi hesapta duracak?
159 Verilen Sipariş Avansları GERÇEK tedarikçi avanslarıyla dolu,
yeniden kullanılamaz. 379 Diğer Borç ve Gider Karşılıkları boş ve
uygun görünüyor ama bu mali müşavir kararı — kullanıcıya soruldu.

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
