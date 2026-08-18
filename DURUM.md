# DURUM — açık işler ve yol haritası

> ## BURADAN BAŞLA (temiz bağlam için)
>
> **Bu dosyayı baştan sona oku, sonra hiçbir şeye dokunmadan
> "kararsızlık ölçümü"nden devam et.**
>
> Okuma sırası:
> 1. **§8 ELENEN HİPOTEZLER** — bunlar ÖLÇÜMLE çürütüldü, tekrar
>    kovalama. Negatif bilgi en kolay kaybolan şeydir.
> 2. **§5 ÇALIŞMA DİSİPLİNLERİ** — 18 kural. Özellikle 12-15:
>    test koşuları serileştirilir; sonda harness'i her yolda yedeği
>    geri koyar; sonda sonrası `git diff` okunur; teşhiste sıra
>    ölçümün AYIRICILIĞINA göre kurulur.
> 3. **§7 KARARSIZ SUITE** — mekanizma HENÜZ BİLİNMİYOR.
>    **Ölçmeden fixture'a DOKUNMA.**
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

**Cevaplanamayan alt soru:** endeks/profil ekranlarına veri girildi mi?
Veritabanı erişimi bu oturumda kapalıydı, bakılamadı. Girildiyse özelliği
öne almak mantıklı.

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

**Somut sonraki adım:** aynı 3 sınıfı N kez koşup düşen test adlarını
biriktiren bir betik; düşme deseni (hep aynı testler mi, hep ilk sınıf
mı) mekanizmayı daraltır.

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

### Ayrıca elendi: "16 düşme regresyondu"

Tam tur bir kez 16 düşme verdi, tekrarında 2244/2244 geçti; sonra iki
kez daha 2246/2246. Aynı 3 sınıf bir koşuda 13 düştü, üç koşuda 50/50
geçti. Yani o düşmeler **kararsızlıktı**, mantık hatası değil — dikişin
izi 15 isteğin hepsinde `global=True` ve `ham == kapsamli` gösterdi
(bkz. §7). Kararsızlığın MEKANİZMASI hâlâ bilinmiyor ve ölçülmeden
fixture'a dokunulmamalı.
