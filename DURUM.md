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

### G3/1b — para/maaş uçlarında şirket kapsamı (2026-08-23, KAPANDI)

Commit `71018e6d` (kapsam) + `dd2f6463` (kimlik doğrulama açığı) +
`aa7a2568` (kök çözüm). Üçü de yayında, tam suite 2463/2463.

**Kapatılan uçlar:** finance/dashboard (5 rakam), financial-dashboard
(ciro, proje/merkez/finansman gideri, kâr/zarar), cari-summary,
projects-summary, progress-payments liste + previous-context,
hakedis-export/{id}/excel, projects/{id}/cost-transactions |
cost-breakdown | cost-reconciliation, perakende liste + kaynaklar +
fiyatlar + urunler, perakende/raporlar/gun-sonu | personel | acik-vade.

**Kim ne kaybetti: kimse.** 13 kullanıcının 12'si `All` kapsamlı
(`HasGlobalAccess`), tek kapsamlı kullanıcı `ccihan` (Site kapsamı,
Formen) ve Formen'de FinanceView/HakedisView/SalesView yok.

**Kapsam borcu (ölçüm aracı düzeltildikten sonra): 480 -> 453.**
Kalan dağılım: para/muhasebe 152, İK/İSG 101, diğer 91,
proje/operasyon 72, satın alma/stok 42.

#### G3/1b'de öğrenilen üç şey

1. **Ön yüz taraması yanlış ölçüttü.** "Arayüzden erişilebiliyor mu"
   diye bakınca tek dışa aktarım ucu göründü. Doğru ölçüt "kimlik
   doğrulamış bir kullanıcı çağırabiliyor mu": sunucu tarafı taramada
   ön yüzde düğmesi olmayan ÜÇ para raporu çıktı (gun-sonu, personel,
   acik-vade) ve üçü de kapsamsızdı.

2. **Bekçinin kendisi kördü.** `CoverageBaselineTests` DbSet regex'i
   `DbSet<(\w+)>` idi; `\w` nokta içermez, yani
   `DbSet<Models.Expenses.ExpenseEntry>` biçimindeki 22 tablo haritaya
   hiç girmiyordu — ExpenseEntries, BankLoans, CreditCards,
   PartnerAccounts dahil. Borç 418 görünüyordu, gerçekte 458'di.
   ARAÇ DÜZELTİLDİ. Ders: bekçi de ölçülmelidir; "sayı düşüyor" tek
   başına güven vermez, aynı araçla iki uçtan ölçmek gerekir.

3. **Zaman damgası tuzağına yeniden düşüldü (§5 kural 17).** Sonda
   geri konduktan sonra `touch` yapılmadığı için MSBuild sabotajlı
   assembly'i güncel saydı ve bir koşu YANLIŞ sonuç verdi. `trap`'e
   `touch` eklendi.

### KİMLİK DOĞRULAMA AÇIĞI VE KÖK ÇÖZÜM (2026-08-23)

**Açık:** `RetailSalesController` sınıfında `[Authorize]` yoktu.
`RequirePermission` düz bir attribute, filtre değil; zorlamayı
`PermissionAuthorizationMiddleware` yapıyor ve o middleware kimlik
doğrulanmamış isteği kontrol etmeden `next`'e geçiriyor. Yani izin
kontrolü YALNIZCA giriş yapmış kullanıcılar için çalışıyordu.

Sonuç: perakende modülünün tamamı — satış listesi, ürün fiyatları,
gün sonu kasa raporu, satış oluşturma/onaylama POST'ları — anonim
çağrılabiliyordu. Üstelik uçlarda `[RequirePermission(...)]` yazdığı
için KORUNUYOR GİBİ görünüyordu.

**Süre:** 2026-08-15 (`215453cd`, PERAKENDE V1) — 2026-08-23. Sekiz gün.
Dosya ilk commit'inde `[Authorize]`'sız doğdu, 7 commit boyunca
eklenmedi.

**Kullanıldı mı: HAYIR.** nginx kayıtlarında (10 Ağustos'tan bugüne,
17.955 `/api/` isteği) perakende geçen istek 0. Uygulama kayıtlarında
yalnız 7 kayıt, hepsi kendi doğrulama isteklerim. `retail_sales`
tablosu 0 satır. Ayrıca `ufw` aktif (INPUT DROP; yalnız 23422, 80,
443 açık) — backend 5155'te dinliyor ama dışarıdan erişilemiyor,
dış dünyadan tek yol nginx.

**Kök çözüm:** `FallbackPolicy = RequireAuthenticatedUser`. İşareti
olmayan her uç artık kimlik doğrulama ister; `[Authorize]` unutmak
açık değil HATA üretiyor. `/api/health`'e açıkça `.AllowAnonymous()`
eklendi — eklenmeseydi safe-deploy'un sağlık kontrolü 401 alır ve
KORUMANIN KENDİSİ her deploy'u kilitlerdi.

**Bilerek anonim kalanlar (tamamı):** auth/login, auth/access-requests,
portal/{token}/*, company-settings/logo, health.

**İki savunma hattı:** `FallbackAuthPolicyTests` çalışan uygulamaya
sorar (anonim istek reddediliyor mu), `AuthorizeGuardTests` kaynağı
tarar (her controller `[Authorize]` taşıyor mu). Sonda ile ayrıldılar:
yalnız `[Authorize]` kaldırıldığında fallback tek başına koruyor
(8/8 yeşil); ikisi birden kaldırıldığında 4 test kırmızı.

#### AÇIK MADDE — PORTAL, SİSTEMİN TEK KİMLİK DOĞRULAMASIZ VERİ KAPISI

`portal/{token}/*` (4 uç) denetlendi. Beş başlıktan **üçü tam**:

- **Token rastgeleliği: YETERLİ.** `RandomNumberGenerator.GetBytes(32)`
  = 256 bit, URL-safe base64 (43 karakter, canlıda ölçüldü). `Token`
  üzerinde `IsUnique()` indeksi var.
- **Hız sınırı: VAR.** `[EnableRateLimiting("portal")]`, 1 dakika /
  60 istek, bölüm anahtarı `{token}:{ip}`, kuyruk yok, 429.
- **Veri kapsamı: KAPALI.** Dört ucun dördü de ilk iş
  `ResolveActiveLink` çağırıyor; tüm sorgular `link.ProjectId`'ye
  bağlı. `photoId` ve `siteId` ile başka projeye geçiş denendi:
  `photoId` sorgusunda
  `x.DailyReport.ProjectSite.ProjectId == link.ProjectId` şartı var,
  `siteId` yalnız zaten süzülmüş kümeyi daraltıyor. Ayrıca sadece
  `Approved` raporlar ve `IsVisibleToEmployer` fotoğraflar dönüyor.

**EKSİK İKİ ŞEY:**

1. **SÜRE YOK.** İptal var ve sıkı (`IsActive && RevokedAtUtc == null`,
   ikisi birden), ama `EmployerPortalLink` modelinde `ExpiresAt` /
   `ValidUntil` alanı hiç yok. E-postayla paylaşılan bağlantı elle
   iptal edilene kadar SÜRESİZ geçerli.
2. **BAŞARISIZ TOKEN DENEMESİ KAYDA GEÇMİYOR.** `PortalController`
   `security_audit_events`'e hiç yazmıyor; geçersiz token sessizce
   404 dönüyor. Canlı tabloda portal kaynaklı tek kayıt yok. Hız
   sınırı deneme yanılmayı YAVAŞLATIYOR ama DENENDİĞİNİ GÖSTERMİYOR.

Canlı durum: 6 portal bağlantısı, **1'i aktif**.

`company-settings/logo` ayrıca denetlendi: gövdeye hiçbir şirket alanı
girmiyor (unvan, vergi no, adres, banka bilgisi YOK) — `LogoPath`
okunup yalnız dosya dönüyor. Canlıda logo yüklenmemiş olduğu için
şu an 404 + standart `problem+json` (tek yan bilgi `traceId`).

**KAPATILDI (2026-08-23, Mehmet Karacabey kararı: M1'den önce).**

- **Süre:** `ExpiresAtUtc`, varsayılan 6 ay. Süresi geçen bağlantı
  **404** döner — 401 DEĞİL: 401 "böyle bir bağlantı vardı ama artık
  geçerli değil" bilgisini ele verirdi.
- **Uzatma:** `POST .../extend`, YENİ TOKEN ÜRETMEZ. Üretseydi
  işverene gönderilmiş bağlantı ölür, "uzatma" adı altında sessizce
  bir iptal olurdu. Süresi geçmiş bağlantıda tarih BUGÜNDEN ileri
  alınır; eski tarihe eklenseydi kullanıcı "uzattım" der, portal 404
  dönmeye devam ederdi.
- **Denetim:** oluşturma / uzatma / iptal → `security_audit_events`
  (kim, ne zaman, neden). Token bu kayıtlara GİRMEZ, yalnız bağlantı
  kimliği.
- **Başarısız denemeler:** `PortalTokenRejected` — zaman, IP,
  User-Agent, sebep (`bilinmeyen_token` / `suresi_gecmis` /
  `iptal_edilmis`) ve token'ın YALNIZ İLK 8 KARAKTERİ. Sebep ayrımı
  yalnız kayda girer; dışarıya dönen yanıt her durumda 404.
- **Tarama eşiği:** aynı IP'den 10 dakikada 10 başarısız deneme →
  `PortalTokenScanSuspected`, pencere başına BİR KEZ (her istekte
  yazılsaydı kayıt aynı olayın kopyalarıyla dolar, asıl bilgi
  görünmez olurdu).
- **Karar tek noktada:** `Services/Portal/PortalLinkResolver.cs`.
  Dört ucun dördü de oradan geçiyor. Dört yerde ayrı yazılsaydı biri
  unutulduğunda o uç sessizce korumasız kalırdı — RetailSalesController
  dersinin doğrudan uygulaması.
- **Migration tuzağı:** EF'in ürettiği `ExpiresAtUtc` varsayılanı
  `0001-01-01` idi ve uygulandığı anda MEVCUT BÜTÜN BAĞLANTILARI —
  aktif olan dahil — öldürürdü. `now() + interval '6 months'` olarak
  değiştirildi: eski kayıtlar oluşturma tarihinden değil MIGRATION
  tarihinden süre alıyor.
- **Ekran:** durum şeridi (aktif / sarı "yaklaşıyor" / gri "süresi
  geçti" / gri "iptal"), son geçerlilik + kalan gün, son erişim,
  açılma sayısı, uzatma sayısı, "6 Ay Uzat". Durum SUNUCUDAN geliyor —
  tarayıcının saatine bırakılsaydı saati geri alınmış bir makinede
  süresi geçmiş bağlantı geçerli görünürdü. Renk tek başına bilgi
  taşımıyor, yazılı karşılığı da şeritte.

### NGINX TOKEN MASKELEMESİ (2026-08-23, uygulandı)

Token `/portal/{token}` biçiminde URL YOLUNDA taşınıyor ve nginx
varsayılan olarak istek satırının tamamını kaydediyor: 256 bitlik
anahtar düz metin olarak erişim kaydına ve oradan log yedeklerine
düşerdi. Uygulamada maskeleyip kaydın sızdırmasına göz yummak
anlamsız olurdu.

`deploy/nginx/portal-token-maskeleme.conf` — **repoda**, çünkü sunucu
yeniden kurulduğunda ya da yapılandırma elle değiştiğinde koruma
sessizce kaybolmasın. Repoda değilse yoktur.

DİKKAT: `log_format maskeli` conf.d içinde tanımlı olduğu için
`access_log` satırı `include /etc/nginx/conf.d/*.conf;` SATIRINDAN
SONRA olmak zorunda — nginx bir log biçimini tanımlanmadan önce
kullanamıyor ve "unknown log format" ile başlamayı reddediyor. İlk
denemede tam olarak bu oldu, `nginx -t` yakaladı.

Canlı doğrulama (kod okuyarak değil, GÖREREK):
`GET /portal/*** HTTP/1.1 200` — token kayıtta 0 kez geçiyor.
Arşiv dahil tüm eski kayıtlar tarandı: hiçbir gerçek token düz metin
geçmiyor, temizlenmesi gereken kayıt YOK. (Eski `/portal/...`
satırları bot taramaları: `.env`, `config.env` vb.)

#### PORTAL TOKENI ARTIK SAKLANMIYOR — ÖZET MODELİ (2026-08-23, KAPANDI)

**Önce bir sızıntı bulundu.** Canlı doğrulamada (kod okuyarak değil,
veritabanında gerçek tokenları arayarak) DÖRT denetim kaydında düz
metin token çıktı. Kaynak `AuditSaveChangesInterceptor` idi:
`EmployerPortalLink link => (link.Id, link.EmployerEmail ?? link.Token)`.
E-postası olmayan bağlantılarda 256 bitlik anahtarın TAMAMI
`security_audit_events`'e yazılıyordu; canlıdaki bağlantıların
hiçbirinde e-posta yoktu. 2026-08-02'den beri böyleydi.

ÜSTELİK BİR ÖNCEKİ PAKETTE EKLEDİĞİM erişim sayacı `SaveChanges` ile
çalıştığı için HER PORTAL AÇILIŞI token içeren bir "Updated" kaydı
üretiyordu — yani mevcut sızıntıyı tekrarlanır hale getirmiştim.

**Dört yerde maskeleme, sonra kökten çözüm.** Token sırayla nginx
erişim kaydında, denetim kesicisinde, `PortalTokenRejected` olayında
ve hata günlüğünde (`GlobalExceptionHandler` `Path=` yazıyordu)
maskelendi. Dördü ayrı kod yolu; biri düzeltilince diğeri
düzelmiyordu.

Ama maskeleme sırrı korumaz, yalnız görünmesini engeller. **Asıl
çözüm: token artık HİÇ SAKLANMIYOR.**

  - Tabloda `TokenHash` (SHA-256, benzersiz + filtreli indeks) ve
    `TokenPrefix` (ilk 8 karakter, sır değil) duruyor.
  - Token yalnız ÜRETİLDİĞİ AN bellekte var; oluşturma yanıtıyla bir
    kez gidiyor ve bir daha hiçbir okuma döndürmüyor.
  - "Linki kopyala" yalnız o an çalışır. Adres kaybedilirse geri
    getirilemez; yeni bağlantı üretilir. Ekran bunu gizlemiyor,
    açıkça yazıyor.
  - Arama özetle yapılıyor: denetim kaydına ya da bir log satırına
    token sızsa bile tabloyla eşleşmez.

**TUZ (SALT) YOK — PAROLADAN FARKI:** parola özetlerinde tuz ve yavaş
algoritma şart, çünkü parola insan seçimidir (kısa, tekrar eden,
sözlükten tahmin edilebilir). Portal tokenı 256 bit kriptografik
rastgelelik: sözlüğü yok, gökkuşağı tablosu kurulamaz. Yavaş algoritma
burada yalnızca her portal isteğini yavaşlatırdı.

**KAYBEDİLEN AYRIM GERİ GELDİ.** Karartma döneminde iptal edilen
bağlantının tokenı tablodan siliniyordu ve "muhatabın elindeki eski
bağlantıyı denemesi" ile "yabancının anahtar araması" birbirine
karışıyordu. Özet iptalden sonra da durduğu için `iptal_edilmis` ile
`bilinmeyen_token` yeniden ayrılabiliyor — kaydın asıl değeri bu
ayrımda.

**`Karart()` GEREKSİZLEŞTİ AMA DURUYOR.** Yeni bağlantılarda `Token`
alanı boş doğuyor, karartacak bir şey yok. Metot 2026-08-23 öncesi
doğmuş 7 satır için duruyor: onların karartılmış değerleri tabloda ve
benzer bir veri düzeltmesi gerekirse kural orada yazılı. Eski
satırların `TokenHash`'i null ("eski kayıt, özet yok"), hiçbir istekle
eşleşmiyorlar — zaten hepsi iptal edilmiş.

**BİR TASARIM EKSİĞİNİ TESTLER YAKALADI:** `Token` üzerindeki eski
benzersiz indeks duruyordu ve alan artık boş kaldığı için ikinci
bağlantı "duplicate key" hatası veriyordu. İndeks düşürüldü;
benzersizlik anlamlı olduğu yere, `TokenHash` üzerine taşındı.

**KENDİ HATAM — KAYDA GEÇİYOR:** bu paketi yazarken canlıda o sırada
GEÇERLİ olan tokenı test verisi olarak `SensitivePathMaskingTests`
içine yazdım; commit ve push edildi. Yani paketin bütün konusu olan
hatayı testin kendisi tekrarlıyordu. Kendim buldum ve düzelttim
(uydurma değerle değiştirdim), git geçmişine dokunmadım — geçmişi
yeniden yazmak repoyu bozar.

Tekrarını engellemek için `SecretInSourceGuardTests` kondu: kaynak
kodda 43 karakterlik base64 DİZGİ SABİTİ arıyor. İlk sürümü uzun metot
adlarını ve EF migration adlarını yakalıyordu; yalnız tırnak içine
bakacak şekilde daraltıldı, gömülü dosya verisi (PNG/PDF imzası) ve
`TEST-` öneki (harf duyarsız) elendi. Uydurma test verisi bundan sonra
`TEST-` ile başlamak zorunda.

#### AÇIK MADDE — TOKEN URL'DEN ÇEREZE TAŞINMALI

Maskeleme YALNIZCA sunucu kaydını korur. Token URL yolunda taşındığı
sürece:
  - tarayıcı geçmişine,
  - `Referer` başlığına,
  - paylaşılan ekran görüntülerine
de düşer.

KALICI ÇÖZÜM: bağlantı ilk açıldığında token kısa ömürlü bir OTURUM
ÇEREZİNE dönüştürülsün, sonraki isteklerde URL'de taşınmasın. Mevcut
bağlantılar çalışmaya devam eder (ilk açılış yine URL ile olur).
Mehmet Karacabey kararı: ŞİMDİ YAPILMAYACAK, açık madde olarak
taşınıyor.

### CIRCIR ÇİZGİSİ SATIR NUMARASINDAN KURTARILDI (2026-08-23)

**Sorun:** çizgi `dosya:satır:DbSet` biçimindeydi. Alakasız bir kod
eklemesi satırları kaydırınca aynı okumalar "yeni" ve "kapanmış"
görünüyor, test düşüyordu. Düzeltmesi hep aynıydı: çizgiyi yeniden
üret. Bu 2026-08-23'te İKİ KEZ üst üste yaşandı; ikisinde de elle
doğrulandı (aynı araçla iki uçtan ölçüm, 453 = 453, gerçek artış yok).

**Tehlike üçüncüsündeydi:** kimse doğrulamaz. "Tazeleyeyim geçsin"
alışkanlığı bekçiyi YEŞİL GÖRÜNEREK işlevsiz bırakır. Bir bekçinin
güvenilirliği, koruduğu şeyden daha önemlidir.

**Yeni biçim:** `dosya : DbSet : adet`. Satır numarası hiç geçmiyor;
karşılaştırma yalnız bu üçlü kümesi üzerinde. Toplam borç, dosya
satırı sayısından DEĞİL adetlerin toplamından hesaplanıyor — aksi
halde altı okumayı bir satıra indirgeyen bir düzenleme "borç düştü"
gibi görünürdü. Hata mesajı hangi dosya/DbSet olduğunu ve adedi
söylüyor.

**Üç sonda ile kanıtlandı:**
  1. Kapsamsız yeni okuma eklendi → DÜŞTÜ.
  2. Alakasız 50 satır eklendi (satırların kaydığı `diff` ile
     doğrulandı) → DÜŞMEDİ. **Asıl kanıt bu.**
  3. Mevcut çiftin adedi artırıldı (DocumentAttachments 4 → 5) →
     DÜŞTÜ.

312 satır, toplam 453 okuma — eski biçimle birebir aynı sayı.

### M1/1 — İŞ AKIŞI ÇEKİRDEĞİ, VERİ MODELİ (2026-08-23)

**WorkTasks GENİŞLETİLDİ, yeniden kurulmadı.** Canlıda 1 kayıtlık bir
tabloyu atıp aynı şemayı ikinci kez yazmak israf olurdu; şema zaten
`CompanyId`, `ProjectId`, `TaskNumber`, `Priority`, `AssignedTo/By`,
`DueDate`, `SourceModule`+`SourceEntityId` taşıyordu.

**KALDIRILAN İKİ DURUM — anlamı belirsiz durum bırakılmadı:**
  - `Draft` kodda hiç kullanılmıyordu. Taslak görev gelen kutusunu
    bulandırırdı: "bana atandı" denen şey henüz gönderilmemiş olurdu.
  - `Waiting` tek bir listede geçiyordu. "Bekliyor" KİMİN İŞİ olduğunu
    belirsizleştirir; bekleme zaten `Completed` (top gönderende) ve
    `Returned` (top yapanda) ile temsil ediliyor.
  Sayılar kaydırılmadı: kaldırılan değerlerin yerine yeni durum
  konmadı, yoksa veritabanındaki eski bir sayı sessizce başka bir
  duruma dönüşürdü.

**EKLENENLER:** `Approved`/`Returned`, `ReturnCount` (iade sayısı
görevde görünür), devretme izi (`DelegatedFromUserId`,
`DelegatedAtUtc`, `DelegationCount`), masraf merkezi
(`CenterType` + `BranchId` + `ProjectSiteId` — gider kaydıyla AYNI
desen, ikinci bir "masraf merkezi" kavramı uydurulmadı).

**ÜÇ YENİ TABLO, KAPSAM İLK GÜNDEN İÇERİDE:** `task_comments`,
`attachments`, `notification_recipients`. Cırcır çizgisine **tek satır
borç eklenmedi** (test yeşil, ölçüldü).

**MEVCUT TEK KAYIT:** `GRV-2026-00001` "Test görev", atanmamış,
durumu Completed. Yeni akışta Completed "gönderenin onayı bekleniyor"
demek; ne atanan ne gönderen olduğu için sonsuza kadar onay
kuyruğunda asılı kalırdı. Migration'da GEREKÇESİYLE iptal edildi
(silinmedi — silme yok kuralı görevler için de ilk günden geçerli).

#### BELGE NUMARASI YARIŞI — ÜÇ YER, İKİSİ YENİ BULGU

`DocumentNumberService`'in KENDİSİ sağlam: numara tek SQL ifadesinde
üretiliyor (`INSERT ... ON CONFLICT DO UPDATE ... RETURNING`), artırım
veritabanında atomik, benzersiz kısıt canlıda doğrulandı. Ama adanmış
testi yoktu — tek güvencesi çek modülünün kendi testiydi.
`DocumentNumberConcurrencyTests` eklendi.

GRV numarası Hızır'da `CountAsync() + 1` ile üretiliyordu; taşındı.
**Sonda taşımanın testsiz olduğunu gösterdi** (sabotaj hiçbir testi
kırmadı) ve eklenen sözleşme bekçisi
(`BelgeNumarasiSozlesmeTests`) İKİ YER DAHA buldu:
  - `WorkTasksController` — görev oluşturma ucu, aynı hata
  - `HrRecruitmentController` — iş ilanı numarası (ILN)
Üçü de merkezî üretece taşındı.

#### AÇIK MADDE — BELGE NUMARASINDA BOŞLUK (MHS DAHİL)

ÖLÇÜLDÜ, iddia değil. Üreteç çağıranın transaction'ına KATILIYOR;
boşluksuzluk çağırana bağlı:

  **A grubu (transaction açan):** çek (VCK/ACK), satış/alış faturası
  (SAT/SFT), mal kabul, sayım, perakende. Geri alma numarayı da geri
  alıyor — testle kanıtlandı (`TransactionGeriAlinirsa_...`).

  **B grubu (transaction AÇMAYAN):** **MUHASEBE FİŞİ (MHS)**,
  teklif/RFQ, malzeme talebi, sekreterya, e-fatura içe aktarım.
  Numara ham SQL ile kendi implicit transaction'ında commit oluyor;
  sonraki `SaveChanges` patlarsa numara YANAR ve sırada boşluk kalır.
  Testle ölçüldü (`TransactionYoksa_BasarisizKayittanSonraNumaraYanar`).

**KARAR (Mehmet Karacabey, 2026-08-23): YALNIZ MHS ÖNEMLİ.**

Asıl endişe fatura numaralarıydı; onlar A grubunda çıktı, yani zaten
korunuyor. Kalan B grubunda tek kritik kalem MUHASEBE FİŞİ: fiş
numarasında boşluk denetimde "12345 nerede" sorusunu doğurur ve
cevabı "sistem yakmış" olur.

  - **MHS: DÜZELTİLDİ (2026-08-23).** Fiş oluşturma akışı numara
    üretimiyle aynı transaction'a alındı. `DocumentNumberService`
    çağıranın transaction'ına zaten katılıyordu, yani servise
    dokunulmadı — yalnız `AccountingVoucherService.CreateAsync`
    sarmalandı.

    SONDA İLE KANITLANDI: transaction kaldırıldığında sayaç 0 yerine
    1 oluyor (numara yanıyor) ve test kırmızı veriyor.

    **İLK DENEMEM 163 TESTİ KIRDI — İÇ İÇE TRANSACTION.**
    Koşulsuz `BeginTransactionAsync` koymuştum. Ama
    `AccountingVoucherService.CreateAsync` BEŞ YERDEN çağrılıyor —
    stok sarfı, sayım, mal kabul, perakende satış, KDV tahakkuku — ve
    o çağıranların hepsi kendi transaction'ını açıyor. İç içe
    transaction denemesi bütün o akışları 500'e düşürdü.

    HEDEFLİ TESTLERİM BUNU GÖRMEDİ (23 test yeşildi), çünkü hepsi
    doğrudan fiş ucunu çağırıyordu. **Tam suite gösterdi.** Ders:
    bir servisi sarmalarken "beni kim çağırıyor" sorusu, "ben ne
    yapıyorum" sorusundan önce gelir.

    Düzeltme, üretecin kendi desenini izliyor: mevcut transaction
    varsa ona KATIL, yoksa aç; katılındıysa commit etme (dış
    transaction'ı yarıda kapatmak olurdu). İki katman aynı kuralı
    paylaşmalı, yoksa biri diğerinin varsayımını bozar.

    **Bu testi yazarken ÜÇ KEZ zayıf test ürettim, sonda üçünü de
    yakaladı** — hepsi "yeşil görünen ölü test" sınıfından:
      1. Fişi dengesiz satırlarla reddettiriyordum; doğrulama numara
         üretiminden ÖNCE çalışıyor, yani numara hiç üretilmiyordu.
      2. Hesap bulunamazsa `return` eden sessiz kaçış kapısı vardı;
         test veritabanında hiç hesap yok (ölçüldü: 0 satır), yani
         test HER KOŞUDA erken dönüyordu.
      3. Satırlarda `currencyCode` eksikti; istek model
         doğrulamasında (400) reddediliyor, controller'a bile
         ulaşmıyordu.
      Üçünde de sabotaj testi kırmadı. Ders: "test yeşil" ile "test
      bir şey ölçüyor" ayrı şeyler; sonda bu farkı gösteren tek araç.
  - **Teklif (TKL), malzeme talebi (PR), sekreterya, e-fatura içe
    aktarım:** bunlar İÇ TAKİP NUMARASI; boşluk kimseyi
    ilgilendirmiyor. Açık madde olarak kalıyor, DOKUNULMAYACAK.

**İKİ İDDİA KARIŞTIRILMAYACAK:** MHS düzeltmesinden sonra test o tip
için "boşluksuz" diyebilir; diğer tipler için iddia "hepsi farklı"
olarak kalır. Tek bir testin iki farklı güvence vermesi, birinin
zamanla diğerinin arkasına saklanması demektir.

### M1/5 — YAPILACAKLAR EKRANI (2026-08-24)

`/yapilacaklar`: üstte "Onayımı bekleyenler" (görev onayları + eski
onay merkezinin dört kuyruğu, TEK LİSTEDE), altta "Bana atananlar" ve
"Gönderdiklerim".

**ACİLİYET SIRASI — TARİH DEĞİL:**
  1. Termini geçmiş (kırmızı, en üstte)
  2. Bugün biten (işaretli)
  3. Kalanlar: **bekleme süresi uzun olan üstte** — en kolay unutulan
     iş, uzun süredir bekleyendir; yeni gelenler zaten göz önünde.

Aciliyet bekleme süresini EZER: yeni ama gecikmiş bir iş, eski ama
zamanı gelmemiş bir işten önce gelir. Sekiz test bu kuralı
sabitliyor.

**KAYNAK BAZINDA HATA YALITIMI:** biri patlarsa o bölüm "yüklenemedi,
tekrar dene" der, diğerleri görünmeye devam eder, sayaç "3+" olur ve
altında uyarı çıkar. Sessizce eksik sayı göstermek, olmayan sayıdan
kötüdür. İzni olmayan kaynak HİÇ ÇAĞRILMAZ — boş dönmesi beklenmez.

**MOBİL ÖNCELİKLİ KART DÜZENİ:** tablo değil kart. Renk tek başına
bilgi taşımıyor; "Termini geçti" / "Bugün" yazısı da kartta.

#### KASITLI KISIT — TOPLU ONAY/RET KALDIRILDI

Onay merkezindeki toplu onay/ret kaldırıldı. Onay artık kaydın kendi
ekranından veriliyor.

**Gerekçe:** listeden tek tıkla onaylamak, kaydı GÖRMEDEN onaylamayı
kolaylaştırıyordu; hakediş ve satın alma onayları bakılmadan
verilecek kararlar değil. Geri istenirse Yapılacaklar'a onay düğmesi
eklenebilir — ama BİLİNÇLİ OLARAK eklenmedi.

**Bedeli hafifletildi:** satır kaydın doğru yerine götürüyor —
`#onay` çapasıyla doğrudan onay bölümünün göründüğü noktaya. Bir tık
daha var ama o tık, bakmadan onaylamayı engelleyen tık.

`/onay-merkezi` adresi korundu ve yönlendirmeye çevrildi; yer imi
kırılmıyor. Sözleşme testi (`module-actions`) yeni ekrana taşındı:
"onay ekranı tek anahtara bağlanamaz" kuralı aynen sürüyor.

**MENÜ:** Yapılacaklar YÖNETİM grubunun en üstünde. VARSAYILAN SAYFA
DEĞİŞTİRİLMEDİ (hâlâ `/dashboard`) — ekran bir hafta kullanılsın,
gerçekten işe yaradığı görülsün. Kimsenin görmediği bir ekranı
herkesin açılış sayfası yapmayalım. Sonra ayrı madde: kullanıcı
tercihi + rol bazlı varsayılan (GM/Admin dashboard, diğerleri
yapılacaklar); `UserUiPreference` deseni zaten kurulu.

**İKİ SÖZLEŞME TESTİ BULGUSU — ikisi de gerçekti:**
  1. Kendi `Intl.NumberFormat`'ımı kurmuşum. Bu, G1.1'de tam olarak
     önlemeye çalıştığımız şeydi: iki ekranın aynı tutarı farklı
     göstermesi. `redwood-contract` yakaladı; ortak `currencyMoney`e
     çevrildi.
  2. `useModuleActions` yerine `has()` kullanmışım; proje deseni
     ilkiydi. `module-actions` yakaladı.

Sözleşme testlerinin ikisini de yakalaması, o kuralların gerçekten
koruduğunu gösteriyor.

#### E-POSTA ALTYAPISI — ÖLÇÜLDÜ, ÖNCEKİ RAPORUM YANLIŞTI

**İKİ e-posta servisi var** ve `EMAIL_PROVIDER` ile seçiliyor:

  - `SmtpEmailService` — `SMTP_HOST/USER/PASS/PORT/FROM` okuyor
  - `EmailService` — Brevo API, `BREVO_API_KEY/SMTP_FROM/SMTP_FROM_NAME`

**VARSAYILAN `smtp`** (Program.cs: `EMAIL_PROVIDER ?? "smtp"`). Canlıda
değişken TANIMSIZ, yani **aktif olan SMTP servisi** ve SMTP
değişkenleri **canlı olarak okunuyor**.

İlk raporumda `EmailService.cs`'in (Brevo) `IsConfigured`'ına bakıp
"false, özellik ölü doğacak" demiştim — YANLIŞTI, DI'da kayıtlı olan o
değil. Ölü sandığım `SMTP_HOST/USER/PASS` değişkenlerini silmek
üzereydim; silseydim e-posta tamamen bozulurdu. "Silmeden önce
gerçekten okunmadığını bir kez daha tara" talimatı bunu yakaladı.

**Gerçek durum: `IsConfigured` TRUE.** Özellik ölü doğmuyor.

#### GÜNLÜK E-POSTA ÖZETİ — ÜÇ KADEMELİ BAYRAK (2026-08-24)

`DAILY_SUMMARY_MODE` (tek değişken, üç durum; ortam değişkeninden
okunur, kaynak koda gömülü DEĞİL):

| Mod | Davranış |
|---|---|
| `kapali` | Özet üretilmez, e-posta gitmez. **VARSAYILAN** — değişken tanımsız veya tanınmayan bir değerse de bu geçerli. |
| `dryrun` | Özet ÜRETİLİR, **SMTP HİÇ ÇAĞRILMAZ**. Yalnızca toplam istatistik günlüğe yazılır. **BUGÜNKÜ CANLI MOD.** |
| `acik` | Herkese gerçekten gönderir. |

**`test` KADEMESİ KALDIRILDI.** Dört durumluyken bir kademe daha
vardı: "yalnız `DAILY_SUMMARY_TEST_RECIPIENTS` listesine gönder".
Kaldırıldı çünkü `dryrun` ile arasındaki tek fark GERÇEK GÖNDERİM
yapmasıydı — yani "güvenli" görünen ama SMTP yolunu tam açan bir
kademeydi ve yanlış yazılmış bir adres listesi onu `acik`'tan
ayırt edilemez hale getirirdi. Eski `on` değeri geriye uyum için
hâlâ `acik` olarak okunuyor.

**MOD YALNIZ E-POSTAYI KESER, TARAMAYI DEĞİL.** `kapali` modda bile
`TaskDueNotificationScanner` (termin uyarıları) ve
`ScopeDeferralWatchdog` (G3 erteleme nöbetçisi) koşar. Bayrak
gönderim kapısıdır, servisin tamamının şalteri değil.

> **BU KURAL BİR SÜRE YALNIZCA YORUMDA DURDU VE YORUM YANLIŞTI.**
> `DailySummaryMode.Kapali` üzerindeki XML yorumu "Tarama HİÇ
> KOŞMAZ" diyordu; kod ise taramayı moddan bağımsız koşturuyordu.
> `git log` ile bakıldı: ikisi de **aynı commit'te** (`9212d291`,
> M1/4) doğmuş — yani bu bir kayma değil, doğuştan tutarsızlık, ve
> hiçbir test onu tutmuyordu. Bayrak önce "ana şalter" olarak
> tasarlanmış, sonra "gönderim kapısı"na daraltılmış, yorum
> güncellenmemiş.
>
> **Kod doğru, yorum yanlıştı** (2026-08-24 kararı): davranışa
> dokunulmadı, yorum düzeltildi. Kapatma seçeneği REDDEDİLDİ —
> `kapali` yazan biri e-postayı susturduğunu sanırken G3 erteleme
> **güvenlik uyarısını** da susturmuş olurdu; `RetailSalesController`
> kazasıyla aynı sınıftan gizli bir bağlantı. Maliyet de gerekçe
> değil: kapalı modda tur **3 sorgu / 10 ms / günde 1 kez**
> (04:00 UTC turu journald'dan ölçüldü).
>
> **ARTIK İDDİA YORUMDA DEĞİL TESTTE:**
> `DailySummaryModeGatingTests.Kapali_TaramaVeBekciyiYineDeKosturur`
> — çağrı sayacıyla (kural 23), dört değer için (`kapali`, `off`,
> tanınmayan bir değer, tanımsız). Ters yönü de
> `KapaliDegilse_GonderimYoluAranir` tutuyor; o olmasaydı bayrak
> tamamen işlevsizleşse bile ilk test yeşil kalırdı.
>
> Sonda: tarama çağrısı erken `return`'ün ALTINA taşındı → dört
> `kapali` durumu da kırmızıya döndü (`Failed: 4`). Geri alındı.
>
> Test bunun için `ITaskDueNotificationScanner`,
> `IScopeDeferralWatchdog` ve `IDailySummaryRunner` arayüzlerine
> dayanıyor (`Services/Notifications/IDailyScanSteps.cs`). Somut
> sınıflar `sealed` KALDI — sırf test için mühür açmak yerine dar
> arayüz eklendi; DI'da arayüz **aynı scoped örneğe** bağlanıyor.

#### KURU KOŞU PAKETİNDE KAPATILAN ÜÇ EK AÇIK (2026-08-24)

**1) "FIRLATIR" KANITI GEÇERSİZ İLAN EDİLDİ.** Sayaç testinin ilk
sürümünde ikinci bir kanıt vardı: test kapsayıcısına
`DailySummaryService` hiç kaydedilmiyordu, gönderim yoluna girilirse
`GetRequiredService` fırlasın diye. Bu kanıt yalnızca testin üretim
sarmalayıcısını ATLAMASI sayesinde çalışıyordu —
`DailySummaryBackgroundService.ExecuteAsync:38` turu
`catch (Exception)` ile sarıyor. Biri testi `ExecuteAsync` üzerinden
koşturmaya çevirse kanıt sessizce buharlaşırdı. **Kural 23'ün kendi
testimize uygulanması:** özet yolu da `IDailySummaryRunner` arayüzüne
alındı ve tek kanıt ÇAĞRI SAYACI oldu (`Kapali` modda
`Ozet.CagriSayisi == 0`).

**2) ÜRETİM KAPSAYICISI AYRICA SINANIYOR.** Sahtelerle koşan test,
üretimde kaydın var olduğunu KANITLAMAZ: `IDailySummaryRunner` kaydı
unutulsa sahte testler yeşil kalır, üretimde tur her gece
`GetRequiredService` ile fırlar ve `ExecuteAsync:38` bunu yutar —
arıza kimseye görünmeden günlerce sürer.
`DailyNotificationWiringTests` günlük turun dokunduğu her şeyi
üretim kapsayıcısından gerçekten çözüyor.

**3) ARAYÜZ = AYNI ÖRNEK, TESTE BAĞLANDI.**
`AddScoped<IArayuz, Somut>()` derlenir ve testlerin çoğu yeşil kalır,
ama tek scope içinde İKİ AYRI örnek doğurur — tarayıcı ve nöbetçi
`AppDbContext` üzerinden yazdığı için bu, aynı turda ikinci bir
değişiklik izleyicisi demek. `Arayuzler_SomutSiniflarlaAyniOrnegeBagli`
`Assert.Same` ile tutuyor; `AyriScopelar_AyriOrnekAlir` de testin
"her şey singleton olmuş" kazasıyla yeşil kalmadığını gösteriyor.
Sonda: forwarding ayrı `AddScoped`'a çevrildi → kırmızı.

#### MOD AYRIŞTIRMA TABLOSU — `ModCozumle` (saf fonksiyon)

Büyük/küçük harf ve baştaki/sondaki boşluk önemsiz:

| Ham değer | Mod | Uyarı kaydı |
|---|---|---|
| `kapali`, `off` | Kapali | — |
| `dryrun` | DryRun | — |
| `acik`, `on` | Acik | — |
| tanımsız (null) | Kapali | — (beklenen durum) |
| `""`, `"   "` | Kapali | **UYARI** |
| başka her şey (`offf`, `dryrunn`, `açık`, `true`, `1`, `enabled`…) | Kapali | **UYARI** |

**`Acik`'A YALNIZ AÇIKÇA DÜŞÜLÜR.** Hiçbir yazım hatası, boş değer
veya tanımsız değişken gerçek insanlara e-posta göndermeye
başlatamaz. Varsayılanın yanlış tarafı geri alınamaz bir hata olurdu.
Bunu iki test tutuyor: `ModCozumle_EslemeTablosu` (18 örnek) ve
`ModCozumle_AcikDisindaHicbirDegerAcikUretmez` — ikincisi örnek
değil KURAL sayıyor. Sonda: varsayılan `Acik` yapıldı → 8 test
kırmızı.

**TANINMAYAN DEĞER SESSİZ KALMIYOR.** `Kapali`'ya düşmek doğru ama
sessizce düşmek değil: `DAILY_SUMMARY_MODE=dryrunn` yazan kişi
özetin koştuğunu sanır ve haftalarca boş kayda bakar. Artık
`LogWarning` ham değeri ve düşülen modu yazıyor.

**KURU KOŞU KAYDI — YALNIZCA TOPLAM, KİŞİSEL VERİ YOK.**
Günlüğe yazılan alanların TAMAMI:

```
tarih, aliciSayisi,
satirEnAz / satirOrtalama / satirEnCok,
acikGorev, terminGecen, onayBekleyen, okunmamisBildirim,
uretimSuresiMs
```

**YAZILMAYAN:** görev başlığı, kişi adı, kullanıcı adı, e-posta
adresi, açıklama metni. Sunucu günlüğü journald'da tutuluyor ve
`journalctl` okuyabilen herkese açık — orası kişi listesi tutulacak
yer değil. Bunu koruyan test: `DryRun_KaydaKisiselVeriYazmaz`,
yasaklı alan listesiyle.

**ÖZET ÜRETİMİ KAPSAM SÜZGECİNDEN GEÇER.** Sayılar
`db.WorkTasks.ApplyScope(kapsam)` üzerinden hesaplanıyor; kapsam her
alıcı için `IUserAuthorizationService` ile ayrı ayrı kuruluyor
(`ICurrentDataScopeService` oturuma bağlı olduğu için arka plan
servisinde kullanılamaz). Yetkisi çözülemeyen kullanıcı BOŞ kapsam
alır — hiçbir şey görmez. Sessizce "hepsini gör"e düşmek, özeti
şirketler arası sızıntı borusuna çevirirdi.

**SAAT: 04:00 UTC = 07:00 Türkiye.** Sunucu `Etc/UTC` (ölçüldü),
Türkiye sabit UTC+3, yaz saati YOK. Kodda "07:00" yazıp sunucunun UTC
olduğunu unutmak, özetin sabah 10'da gitmesi demekti. Test sabiti
doğrudan sınıyor, yerel makine ayarına güvenmiyor.

**BOŞ ÖZET GÖNDERİLMİYOR:** yapacak işi olmayana "0 açık göreviniz
var" e-postası, zilin kapatılmasıyla aynı sonucu doğurur.

**KULLANICI KAPATABİLİR:** `UserUiPreference.DailySummaryEmailEnabled`
(varsayılan true). Zil ve uygulama içi bildirim ETKİLENMİYOR, yalnız
e-posta. Bu seçenek olmasaydı istemeyen kişi e-postayı filtreye atar,
sonra gerçekten önemli bir e-posta da aynı filtreye düşerdi.

**KİŞİ BAZINDA HATA SINIRI:** bir kişinin gönderimi patlarsa tur
diğerlerine devam eder ve hata `DailySummaryEmailFailed` olarak kayda
düşer. Tek kişinin bozuk adresi yüzünden kimsenin özet almaması,
sessizce yutmaktan farksız bir arıza olurdu. **Bu yutma mekanizması
bir test açığı doğurdu — bkz. §5 kural 23.**

**BEŞ İŞ GÜNÜ SONUNDA BAKILACAK KAYIT (tek satır):**
```bash
sudo journalctl -u enderunai-backend --since '5 days ago' --no-pager | grep 'GÜNLÜK ÖZET (kuru koşu)'
```

**MOD DEĞİŞTİRME (Mehmet Karacabey yapacak):**
```bash
sudo nano /etc/enderunai/backend.env      # DAILY_SUMMARY_MODE=dryrun
sudo systemctl restart enderunai-backend  # ŞART: EnvironmentFile systemd'den okunuyor
```

**BREVO'YA GEÇİŞ:** yalnız `BREVO_API_KEY` yazmak YETMEZ —
`EMAIL_PROVIDER=brevo` da gerekir. Karar: Brevo'da kalınacak ama geçiş
henüz yapılmadı; bugün SMTP aktif.

#### ⚠️ SMTP DEĞİŞKENLERİNİ SİLMEYİN — CANLI OLARAK OKUNUYOR

`SMTP_HOST`, `SMTP_PORT`, `SMTP_USER`, `SMTP_PASS`: bunlar ÖLÜ AYAR
DEĞİL. `EMAIL_PROVIDER` tanımsız olduğu için varsayılan `smtp` ve
aktif servis `SmtpEmailService` — bu dört değişkeni doğrudan okuyor.
Silinirse `IsConfigured` false olur ve SİSTEMDE HİÇ E-POSTA GİTMEZ.

2026-08-23'te bunları "ölü ayar" sanıp silmek üzereydim. Yanlış
teşhisin sebebi: `IsConfigured`'ı `EmailService.cs` (Brevo) içinde
okumuştum, ama DI'da kayıtlı olan `SmtpEmailService`. İki servis aynı
arayüzü uyguluyor ve seçim Program.cs'te bir ortam değişkenine bağlı;
dosyaya bakmak yetmiyor, KAYDIN HANGİSİ OLDUĞUNA bakmak gerekiyor.

Silme talimatı verilmişti; "silmeden önce gerçekten hiçbir yerde
okunmadığını bir kez daha tara" uyarısı yakaladı. Ders: bir ayarın
ölü olduğunu, onu okuyan dosyayı bulamadığın için değil, KAYITLI
SERVİSİN okumadığını görerek söyleyebilirsin.

#### AÇIK MADDE — HEIC DÖNÜŞTÜRME: ÖLÇÜM BEKLİYOR

**Bugünkü durum (M1/3):** HEIC yüklenebiliyor (iPhone varsayılanı,
izinli uzantı listesinde) ama Chrome ve Firefox GÖSTEREMİYOR. Ek dosya
yanıtında `isBrowserViewable=false` dönüyor ve ekran "bu dosya
tarayıcıda görüntülenemiyor, indirin" diyor — bozuk resim simgesi
göstermiyor. Bu kadarı M1/3'te yapıldı.

**DÖNÜŞTÜRME ERTELENDİ, ÇÜNKÜ SORUN OLMAYABİLİR** (Mehmet Karacabey
kararı): iOS, fotoğraf kütüphanesinden web formuna dosya seçildiğinde
çoğu durumda HEIC'i kendisi JPEG'e çeviriyor. Yani sunucuya hiç HEIC
gelmiyor olabilir.

ÖLÇÜM YOLU HAZIR: `attachments.ContentType` her yüklemede gerçek tipi
kaydediyor. Bir süre sonra tek sorgu yeter:
```sql
SELECT "ContentType", count(*) FROM attachments GROUP BY 1;
```
Sıfır HEIC çıkarsa dönüştürme hiç gerekmez ve sunucuya kütüphane
kurmamış oluruz.

**GEREKİRSE MAGICK.NET — SİSTEM PAKETİ (heif-convert) DEĞİL.**
Gerekçe: NuGet paketi proje dosyasında SÜRÜMLENİR, sunucu yeniden
kurulduğunda kendiliğinden gelir. Sistem paketi repoda değildir;
nginx token maskelemesinde tam olarak bu tuzağa düşmemek için
yapılandırmayı `deploy/nginx/` altına aldık. Aynı disiplin burada da
geçerli: sunucuya elle kurulan hiçbir şeye bağımlı olmayalım.
40 MB'lık yerel kütüphane bedeli, "sunucu yeniden kurulunca
fotoğraflar açılmıyor" sürprizinden ucuz.

Bugünkü ölçüm: sunucuda `convert`/`magick`/`heif-convert`/`vips`
YOK, .NET tarafında görüntü kütüphanesi YOK, canlıda yüklenmiş HEIC
YOK (0 dosya).

#### AÇIK MADDE — MOBİL: 94 TABLO EKRANI

183 ekranın 94'ü tablo kullanıyor. `erp-table-wrap` üzerinde
`overflow-x: auto` var, yani tablo yatay kayıyor ve taşmıyor — ama
globals.css'teki 29 medya sorgusunun incelenen kırılımları yalnız
GÖSTERGE PANELİ kartlarını düzenliyor; iş ekranları uyarlanmamış.
Telefonda 8 sütunlu bir tabloyu yatay kaydırarak kullanmak "çalışıyor"
sayılmaz. M1 ekranları (gelen kutusu, görev detayı, yorum akışı,
fotoğraf) mobil öncelikli KART düzeniyle yazılıyor; mevcut 94 ekran
BU PAKETİN İŞİ DEĞİL.

#### AÇIK MADDE — HAKEDİŞ DOSYALARI DB KAYDI OLMADAN DİSKTE

`hakedis/files/{storedName}` diskteki gevşek dosyaları veriyor;
hiçbir tabloda kaydı yok, dolayısıyla kapsam bağlanamıyor. Portal
denetiminde bulundu. `Attachment` tablosu (M1/1) bu sorunun yolunu
açıyor: varlık tipi + kayıt kimliği + yükleyen. Diğer modüllerin
(`ProjectDocument`, `PersonnelDocument`, `DutySurveyPhoto`) buraya
taşınması AYRI İŞ.

#### AÇIK MADDE — BİLDİRİMDE İKİLİ MODEL GEÇİCİ

`Notification` satırı ŞİRKETE ait ve tek `ReadAtUtc` taşıyor; bu
tasarım tarama kaynakları için doğru (bir çek vadesi herkesi
ilgilendirir). M1 olayları KİŞİSEL: `TargetUserId` + ayrı
`NotificationRecipient` okuma durumu.

**KURAL:** bundan sonra eklenecek HER yeni bildirim kişisel modelde
doğar. Şirket satırı modeli yalnız mevcut DÖRT tarama kaynağı için,
GEÇİCİ olarak duruyor. Olay tabanlı bildirim 24 saatlik taramaya
bağlanmayacak; anında yazılacak.

#### M2a KARARI (Mehmet Karacabey, 2026-08-23) — M2a bloke olmasın

**"İş olayı" sayılacak varlıklar** (audit_logs'a yazılacak): çek,
satış ve alış faturası, muhasebe fişi, stok hareketi, mal kabul,
hakediş, satın alma talebi ve siparişi, teklif, proje, cari,
perakende satış, puantaj, ek ücret, görev. (Senet yok, atlandı.)

**security_audit_events'te KALACAKLAR** (yetki/kullanıcı tarafı):
`RolePermission`, `UserPermissionOverride`, `AppUser`,
`RoleWorkHourWindow`, `UserDataScope`, `AccessRequest`.

**`Personnel` İKİSİNE DE GİRER** — ama iş kaydına yalnız
"güncellendi" olarak, ALAN DEĞERİ YAZILMADAN.

Bugünkü durum ölçüldü: `audit_logs` **0 satır**,
`security_audit_events` 1644 satır. Kesici 17 varlık tipi izliyor ve
hepsini security tarafına yazıyor; dağılım ağırlıklı yetki/kullanıcı
(RolePermission 559, UserPermissionOverride 328, AppUser 234).
**İş tarafı neredeyse hiç kayıtlı değil** — M2a'nın işi kesiciye tip
eklemekten çok, hangi varlığın "iş olayı" sayıldığına karar vermek.

**M2a KAPSAMINA AÇIKÇA GİREN BORÇ — ÇEK İZİ AYRI TABLODA
(ÇEK/1'de ölçüldü, 2026-08-26):**

`audit_logs` **çek hareketlerini İÇERMİYOR**; çek izi
`cheque_movements` tablosunda tutuluyor. Ölçüm: çek 805088'in iki
kaydı için `audit_logs`ta **0 satır**, buna karşılık
`cheque_movements`ta dört satır (alındı → iptal, düzenlendi →
ödendi), her biri `CreatedByUserId`, `MovementDate`, `FromStatus`,
`ToStatus` ve fiş bağıyla birlikte.

**M2a BUNU BİRLEŞTİRMELİ.** Aksi hâlde M2b'nin günlük faaliyet
raporu çek hareketlerinin **hiçbirini görmez**: "bugün ne oldu"
sorusunun cevabında çek düzenleme, ciro, tahsil, ödeme, karşılıksız
ve iptal **eksik kalır** — ve eksikliği kimse fark etmez, çünkü
rapor boş değil, yalnızca çek satırları yok.

Birleştirme biçimi M2a'nın kararı; iki seçenek var ve ikisi de
`cheque_movements`ı SİLMEZ (fiş bağı ve durum geçiş matrisi oraya
dayanıyor):
- kesiciye çek tipini ekleyip `audit_logs`a da yazmak (çift yazım,
  ama tek okuma noktası), ya da
- faaliyet raporunun okuma katmanında iki kaynağı birleştirmek
  (tek yazım, birleştirme mantığı raporda).

### M1/6 — ORTAK YORUM BİLEŞENİ + GÖREV DETAY EKRANI (2026-08-24)

**ÖNCE BİR CANLI HATA KAPANDI: `/gorevler/{id}` ROTASI YOKTU.**
M1/5'te Yapılacaklar satırları `todo.service.ts:501`'de
`/gorevler/{id}`'ye bağlanmıştı ama o dinamik rota hiç
oluşturulmamıştı — kullanıcı bir göreve tıklayınca boş sayfa
görüyordu. Canlı derleme bildirgesinden doğrulandı: `/gorevler`
statik rota olarak vardı, `/gorevler/[id]` yoktu. Diğer beş hedef
(`hakedis/[id]`, `satin-alma/[id]`, `satin-alma/siparis/[id]`,
`projeler/[id]/santiyeler/[siteId]`, `projeler`) sağlamdı.

M1/5'te iki uydurma rotayı yakalayıp düzeltmiştim; bu üçüncüsünü
kaçırdım çünkü `/gorevler` ekranının varlığını görüp `{id}`
biçiminin de olduğunu VARSAYDIM — dinamik rotanın kendisini
doğrulamadım. **Bir bağlantı hedefinin varlığı, önekinin varlığıyla
kanıtlanmaz.**

**İKİNCİ ÖLÇÜM: YORUM/EK İÇİN ÖN YÜZDE HİÇBİR ŞEY YOKTU.**
`CollaborationController` yedi ucu (M1/2, M1/3) sunucuda hazırdı ama
`frontend/` içinde `collaboration` kelimesi hiç geçmiyordu. Yani
M1/6 "mevcut bileşeni ortaklaştırma" değil SIFIRDAN YAZMA işiydi ve
yorumun asılacağı bir görev detay ekranı da yoktu. Karar (onaylandı):
detay ekranı + yorum + ek dosya TEK PAKET — ayrı çıksalardı ya ekran
boş ya bileşen ölü doğardı.

#### ADLAR DTO'YA GİRDİ — TEK SORGUDA

Yorum, ek dosya ve görev DTO'ları yalnız `CreatedByUserId` /
`UploadedByUserId` / `AssignedToUserId` döndürüyordu. Ekranda GUID
gösteren bir yorum dizisi, kimin ne dediği okunamadığı için yorum
değildir; ekran adı çözmek için satır başına istek atmak zorunda
kalırdı.

Adlar artık çağıran tarafta TEK `IN (...)` sorgusunda toplanıp DTO'ya
PARAMETRE olarak geçiyor. Çözülemeyen ad sessizce boş geçmiyor,
`(bilinmeyen kullanıcı)` yazıyor — yazarsız görünen bir kayıt arızayı
gizler.

**N+1 YAPISAL OLARAK ENGELLİ.** DTO üreticileri (`YorumDto`, `EkDto`,
`ToDto`, `AdBul`) `static`. Bir `static` metodun `db` alanına erişimi
YOKTUR, yani satır başına sorgu ATAMAZ. Bunu
`DtoUreticileri_StaticKalmali` bekçisi yansımayla kilitliyor: biri
"sadece adı buradan çekiveririm" diye üreticiyi örnek metoduna
çevirirse test düşer.

Sorgu SAYAN test yazılmadı ve bu bilinçli: sayaç, paylaşılan test
fabrikasına araya girici eklemeyi gerektiriyor ve o fabrika 2500'den
fazla testin altında. Yapısal kilit aynı şeyi garanti ediyor —
erişim yoksa sorgu da yok.

**GEÇİŞ UÇLARI DA ADLARLA DÖNÜYOR.** `start`/`complete`/`approve`/
`return` önce adsız DTO dönüyordu; ekran işlem sonrası adları
kaybederdi. Sekiz dönüşün hepsi bağlandı.

#### ORTAK BİLEŞEN — MODÜL BİLMEZ

`services/collaboration.service.ts` ve `components/collaboration/`
(yorum dizisi + ek dosya bloğu) yalnızca `(entityType, entityId)` ile
çalışıyor. Modüle özel bir dal açılırsa ortaklık biter ve her ekran
kendi kopyasını taşımaya başlar — bu dosyaların varlık sebebi o dalın
hiç açılmamasıdır. Varlık tipleri `EntityContextResolver` ile aynı
olmak zorunda; `CommentEntityTypeGuardTests` bunu 444 dosyalık kaynak
taramasıyla koruyor (sonda ile doğrulandı).

**MOBİLDE KAMERA DOĞRUDAN AÇILIYOR** (`capture="environment"`).
Sahadaki kişi için "dosya seç" akışı, çekilmiş fotoğrafı galeriden
bulmak demek. Ayrı düğme, çünkü galeriden seçmek de gerekiyor. Düğme
yalnız dar ekranda görünür: masaüstünde `capture`'ın karşılığı yok ve
kullanıcı iki aynı düğme görürdü.

**GİZLENEN YORUMUN YERİ DURUR.** Silinmiş gibi görünmez; kim
gizlediği yazar. Cevap verilmiş bir cümle konuşmadan çıkarılırsa
kalan cevaplar anlamsızlaşır.

**DÜZENLEME PENCERESİ EKRANDA DA KAPANIYOR.** `Date.now()` doğrudan
render içinde okunsaydı, 15 dakika dolduğunda düğme ekranda kalmaya
devam ederdi (React yeniden çizmez) ve kullanıcı tıklayıp uçtan hata
yerdi. Şimdiki zaman durumda tutuluyor, dakikada bir tazeleniyor.

#### İKİ UYDURMA SINIF — ÖLÇÜMLE YAKALANDI

`erp-card` / `erp-card-head` diye sınıflar UYDURMUŞTUM; yoklar.
`erp-panel` / `erp-panel-header` var, onlara çevrildi. `erp-detail-grid`
de `dt`/`dd` değil `span`/`strong` bekliyor — `dl` yazmak anlamsal
olarak daha doğru ama GÖRSEL OLARAK BİÇİMSİZ çıkardı. Kural: yeni bir
sınıf yazmadan önce `globals.css`'te ARANIR.

#### AÇIK BORÇ: `react-hooks/set-state-in-effect`

Yeni dosyalar bu kuralı 3 yerde ihlal ediyor. SUSTURULMADI. Ölçüldü:
kuralın projede **107 dosyada 151 ihlali** var ve `npx eslint .`
bugün 166 hatayla düşüyor. Lint deploy kapısı DEĞİL — safe-deploy
`npm run test` + `npm run build` koşuyor. Doğru çözüm bir veri çekme
katmanı; M1/6'nın kapsamı değil, **ayrı paket olarak konuşulmalı**.

### M1/7-0 — YORUM KAPISI: TİP BAŞINA YETKİ (2026-08-24)

**KAPATILAN AÇIK:** `CollaborationController` yalnızca `[Authorize]`
taşıyordu ve kapısı (`ErisimKontroluAsync`) yalnızca VERİ KAPSAMI
bakıyordu — dosyada hiç `RequirePermission` yoktu. Sonuç: hakediş,
çek ya da teklif görme izni OLMAYAN bir kullanıcı, şirket kapsamı
yettiği sürece o kayıtların yorumunu okuyabiliyor ve ek dosyasını
indirebiliyordu; ekranı hiç açmadan, doğrudan uca giderek.

Açık canlıda ölçüldü: aktif 4 kullanıcının HEPSİ global veri
kapsamlı ama izinleri farklı (Admin / Araç Sorumlusu / Teknik Ofis /
çoklu rol). "Hepsi Admin olduğu için güvendeyiz" varsayımı YANLIŞTI.
Sızan veri olmadı çünkü o gün `task_comments` ve `attachments`
**0 satırdı** — kapı açıktı ama oda boştu. M1/7 tam da odayı
dolduran paket.

#### YETKİ TABLOSU — YEDİ TİP

| Tip | Ekranın bugünkü kapısı | Yorum kapısı | Rol sayısı |
|---|---|---|---|
| `WorkTask` | `tasks.view` | `tasks.view` | — |
| `Project` | `projects.view` | `projects.view` | 12/15 |
| `ProgressPayment` | `hakedis.view` | `hakedis.view` | 5/15 |
| `PurchaseRequest` | `purchasing.view` | **`purchasing-requests.view`** | 5/15 |
| `GoodsReceipt` | `inventory.view` | **`purchasing-receipts.view`** | 5/15 |
| `Offer` | `projects.view` | **`offer_tracking.view`** | 5/15 |
| `Cheque` | `finance.view` | `finance.view` | 5/15 |

**ÜÇ YERDE EKRAN KAPISINDAN BİLEREK AYRILDI** (kalın olanlar):

- **Teklif:** ekran `projects.view` ile açılıyor ve bu izin 15 rolün
  12'sinde var. Teklif yorumunu buna bağlamak, fiyat pazarlığı
  tartışmasını neredeyse herkese açmak olurdu.
- **Mal kabul:** ekran genel `/depo` kuralına düşüp `inventory.view`
  ile açılıyor. Mal kabul tartışması bir depo listesi değil,
  tedarikçi ve eksik teslim konusu.
- **Satın alma talebi:** `purchasing.view` MODÜL kapısı,
  `purchasing-requests.view` KAYIT kapısı. Yorum kayda ait.

**İLKE:** ekranı açabilmek, TEK BİR KAYDIN tartışmasını okuyabilmek
demek DEĞİLDİR. Ekran kapısı gevşekse onu kopyalamak hatayı çoğaltır.

#### AÇIK BORÇ — ÇEK YORUMLARI

Çek yorumları `finance.view` ile korunuyor; Teknik Ofis ve Teknik
Koordinatör de okuyabiliyor. **Tetikleyici:** `cheque.view` anahtarı
açıldığında çek yorumları Finans + Admin + GM'e daraltılacak.

Sebep: `cheque.view` diye bir izin YOK (yalnız `cheque.edit` ve
`cheque.void-closed` var) ve M1/7 içinde yeni anahtar açılmayacak
(karar). Anahtar uydurmak yerine mevcut en yakın sınır seçildi.

#### AÇIK BORÇ — `tasks.view` YALNIZ 2 ROLDE

`tasks.view` **15 rolün 2'sinde** var: Admin ve Genel Müdür.
Aktif 4 kullanıcının 3'ünde YOK (vtepe/Araç Sorumlusu,
smemis/Teknik Ofis, uakkaya/çoklu rol).

**BU BİLİNÇLİ BİR KARAR DEĞİL, YAN ETKİ.** `RoleCatalog`, tüm izin
anahtarlarını yansımayla toplayıp Admin ve Genel Müdür'e veriyor
(`K`). Yani koda eklenen HER yeni anahtar yalnız o iki role düşüyor;
başka bir rol onu kendi listesinde tek tek saymadıkça almıyor. M1
paketlerinde `tasks.view` eklendi ama rol dağıtımı yapılmadı.

**SONUÇ:** M1 iş akışı çekirdeği bugün 13 role görünmüyor —
`/gorevler` rota kapısı da `tasks.view`, `WorkTasksController`'ın
4 ucu da. Görev sistemi "herkes iş alsın" diye kurulduğuna göre bu
bir dağıtım eksiği.

**YORUM KAPISI BUNU KÖTÜLEŞTİRMİYOR:** yorum da `tasks.view`
istiyor, yani ekranı açabilen herkes yorumu da okuyabiliyor. Kapı
yeni bir kısıt getirmiyor, mevcut kısıtla aynı hizada.

**Tetikleyici:** `tasks.view` rollere dağıtıldığında yorum
görünürlüğü de kendiliğinden genişler; ayrıca bir iş gerekmiyor.
Dağıtım AYRI BİR KARAR ve bu pakete girmedi.

**Bugün canlıda `task_comments` 0 satır** — okunabilecek yorum yok.

#### AÇIK BORÇ — `Project` YORUMLARI GENİŞ (`projects.view`, 12/15)

Teklif için 12/15'i geniş bulup reddettim ama `Project` için aynı
anahtarı kabul ettim. Fark bilinçli:

- **Teklifte daha dar, AMACA UYGUN bir anahtar VARDI**
  (`offer_tracking.view`, 5/15). Kullanmamak tembellik olurdu.
- **Projede yok.** Mevcut anahtarlar: `projects.view` (12),
  `projects.manage` (4), `projects.create/edit/delete`.
  `projects.manage` bir YAZMA anahtarı; onu okuma kapısı yapmak,
  `cheque.view`'i uydurmakla aynı kategoride bir hata olurdu —
  anahtarın anlamını çağrı yerinde yeniden tanımlamak.

Proje yorumu sözleşme, gecikme ve taşeron tartışması taşıyabilir;
Sekreterya, İK Sorumlusu, İSG Sorumlusu ve Araç Sorumlusu bunu
okuyabilecek. **Bugün pratik etki YOK:** hiçbir ekran `Project`
yorumunu takmıyor ve M1/7'nin beş hedefinde de yok.

**Tetikleyici:** `Project` yorumu bir ekrana takılmadan ÖNCE karar
verilecek — ya `project_discussion.view` gibi amaca uygun yeni bir
anahtar açılacak, ya da geniş kalacağı açıkça onaylanacak.
Takıldığında yorum kutusuna görünürlük notu geçilecek:
"Bu yorumu, projeyi görebilen herkes görür."

#### GÖRÜNÜRLÜK KARARI GERİYE DÖNÜK UYGULANIR

Yetki OKUMA ANINDA değerlendirilir, yoruma yazılmaz. Görünürlük
ileride daraltılırsa mevcut yorumlar da kapanır.

Gerekçe: görünürlüğü yoruma damgalarsak kural iki yerde yaşar ve
ayrışır — `route-permissions` kopyası tam olarak böyle ayrışmıştı.
Daha önemlisi, birinin çek erişimini kaybetmesinin sebebi genellikle
artık çek bilgisi görmemesi gerektiğidir; geçmiş tartışmayı görmeye
devam etmesi, daraltmanın engellemek istediği şeyi sızdırır.
**Bundan çıkan kural: görünürlük kararı yorum satırına asla
önbelleklenmez/denormalize edilmez.**

#### KAPALI TARAFA DÜŞER — VE BU BİR TEST AÇIĞI DOĞURDU

Tabloda karşılığı olmayan tip REDDEDİLİR. Varsayılan "izin ver"
olsaydı, yeni tip ekleyip tabloyu unutan kişi o tipi herkese açardı.

**SONDA BİR AÇIK GÖSTERDİ:** kapalı-taraf varsayılanı önce yalnız
UÇTAN sınanıyordu ve "varsayılanı serbest yap" sabotajı testleri
KIRMADI. Sebep: bilinmeyen tipi `EntityContextResolver` de
reddediyor ve uç yine 404 dönüyordu — iki bariyer aynı sonucu
verince hangisinin çalıştığı ölçülemiyordu. Asıl tehlike ise
`SupportedTypes`'a tip eklenip tablonun unutulmasıydı; o durumda
çözümleyici tipi TANIR ve serbest varsayılan kapıyı ardına kadar
açardı.

Düzeltme: karar saf bir fonksiyona çıkarıldı
(`CollaborationPermissions.ErisebilirMi(tip, izinVarMi)`) ve test
`izinVarMi: _ => true` ile — yani TÜM İZİNLERE SAHİP bir kullanıcı
taklit edilerek — koşuyor. Reddin sebebi yetersiz izin olamaz; tek
sebep tipin tabloda olmamasıdır. Sabotaj tekrarlandı, artık 4 test
kırmızıya dönüyor.

**GENEL KURAL (§5 kural 23'ün akrabası):** iki bağımsız bariyer aynı
gözlemlenebilir sonucu üretiyorsa, o sonucu ölçen test hangi
bariyerin çalıştığını KANITLAMAZ. Sınanacak bariyer, diğerini
devre dışı bırakan bir yolla ayrı ayrı ölçülmelidir.

#### EKRAN BOZULMASIN — `canRead` ZORUNLU PROP

Yorum kapısı üç tipte ekran kapısından DAR. Ekranı açabilen ama
yorum izni olmayan kullanıcı 403 ya da boş bir hata kutusu
GÖRMEMELİ: olmayan bir bölümün hata vermesi, kullanıcıya bozulmuş
bir ekran gösterir ve "sistem çalışmıyor" izlenimi bırakır.

`CommentThread` ve `AttachmentPanel` artık `canRead` alıyor;
`false` ise **hiç render edilmiyor ve hiçbir istek atmıyor**.
Prop ZORUNLU — varsayılanı `true` olsaydı yeni bir ekran takarken
kararı atlamak mümkün olurdu ve atlandığı kimseye görünmezdi.
TypeScript şimdi her takma yerinde açık karar istiyor.

`tests/collaboration-mount-contract.test.ts` bir adım ötesini
tutuyor: kararın SABİT `true` ile geçilmediğini. Sabit `true`,
zorunlu prop'u sağlar ama kararı vermez — kapıyı açık bırakmanın en
kolay yolu tam olarak budur. Sonda ile doğrulandı.

#### YETKİSİZE 403 DEĞİL 404

403 "bu kayıt VAR ama sana kapalı" der ve kayıt kimliği deneyerek
varlık taraması yapmayı mümkün kılar.

### 7a — ÜÇ BEKÇİ: ROTA, LINT, 404 KAYDI (2026-08-24)

#### A) ROTA BEKÇİSİ

`tests/route-guard.test.ts` + `tests/bekci/rota-envanteri.ts`.

**NEDEN:** M1/5'te Yapılacaklar satırları `/gorevler/{id}`'ye
bağlandı ama o dinamik rota hiç oluşturulmamıştı; kullanıcı boş
sayfa gördü ve bu SEKİZ GÜN canlıda durdu. Kural: **bir bağlantı
hedefinin varlığı, önekinin varlığıyla kanıtlanmaz.**

**ROTALAR KAYNAKTAN TÜRETİLİYOR, `.next/routes-manifest.json`'DAN
DEĞİL.** Manifest bir yapı artığı: bayat olabilir, CI'da hiç
bulunmayabilir, ve "bekçi yeşil çünkü dosya yok" durumu sessizce
oluşur. `app/**/page.tsx` tek gerçek kaynak.

**ÜÇ SINIF:**
| Sınıf | Örnek | Doğrulama |
|---|---|---|
| Değişmez | `href="/hakedis"` | Rotaya çözülmeli |
| Şablon | `` href={`/hakedis/${id}`} `` | Segment biçimi dinamik rotayla eşleşmeli |
| Hesaplanmış | `href={item.href}` | Doğrulanamaz — SAYILIYOR, cırcırlı |

**ÖLÇÜM (ilk koşu):** 406 dosya, 185 rota (45 dinamik), 549 hedef
(412 değişmez + 137 şablon), 29 hesaplanmış hedef (25 dosya).

**KAPSAM YALNIZ SAYFA ROTALARI.** `app/api/**` altındaki route
handler'lar bu turda DIŞARIDA — ayrı bekçi işi, açık madde.

**ÇÖZÜLMEYEN 4 HEDEF ÇİZGİDE, KARAR BEKLİYOR** (temizlik mi çizgi
mi): üçü `status:"Planlandı"` veri satırı (ekran `active ? <Link> :
<button disabled>` ile kapılıyor, tıklanamıyorlar), biri iki
değişkenli şablon (`/projeler/${project.id}/${module.href}` — 14
açılımın hepsi tek tek doğrulandı ve çözülüyor, bekçi iki değişkenli
şablonu açamıyor).

#### B) LINT CIRCIRI — ÇİZGİ 154

`tests/lint-ratchet.test.ts` + `tests/bekci/lint-cizgi.txt`.

**"DÜZGÜN ÇÖZÜLEBİLİR Mİ" SORUSUNUN CEVABI: HAYIR — ÖLÇÜLDÜ.**
Senkron `setState` çağrıları efekt yolundan çıkarıldı (gösterge
zaten `useState(true)` ile açık) ve ihlal **1'den 1'e kaldı**.
Kural, efektin çağırdığı fonksiyonun İÇİNE bakıyor; senkronluk fark
etmiyor. Efektten veri çekip durum yazmanın kuralla uyumlu bir
biçimi YOK. Düzgün çözüm bir veri çekme katmanı (SWR/React Query) ya
da ilk veriyi sunucu bileşeninden props ile geçirmek — ikisi de
mimari değişiklik, **110 dosya bu desende**.

**AÇIK BORÇ:** M1/6'nın 3 ihlali gerekçeli susturma aldı ve
**veri çekme katmanı paketine bağlı** borç olarak duruyor.

**SUSTURMA İHLAL SAYILIR — KAÇIŞ YOLU YOK.** Çizgi = raporlanan
ihlal + `eslint-disable` yorumu. Sayılmasaydı çizgi bir gün "0"
görünür ve hiçbir şey ölçmezdi. Aritmetik değişmedi: önce
151 + 3 = 154, şimdi 148 + 6 = 154.

**ESLINT TESTTEN ÇAĞRILIYOR.** safe-deploy `npm run test` koşuyor
ama `npm run lint` KOŞMUYOR; cırcır lint adımında dursaydı otomatik
kapı olmazdı. eslint hata bulunca çıkış kodu 1 döndürüp fırlatıyor —
çıktı `stdout`'tan alınıyor, ama çıktı da yoksa test FIRLAR:
ölçemeyen cırcır yeşil kalmamalı.

#### C) 404 KAYDI — JOURNALD, TABLO YOK

`app/not-found.tsx` (önce YOKTU — 404'lerin hiçbir yere düşmemesinin
sebebi buydu) + `app/kayit/404/route.ts`.

**YOL NEDEN `/api/` ALTINDA DEĞİL:** nginx `location /api/` bloğunu
BACKEND'e (5155) veriyor; yalnız `/api/auth/` ve `/api/backend/`
Next.js'e gidiyor. Uç `/api/not-found` olsaydı backend'e düşer ve
404 kaydı sessizce hiç yazılmazdı.

**BOT SÜZGECİ İKİ KATMANLI:**
1. **Oturum şartı** — `enderun_token` çerezi yoksa kayıt YAZILMAZ.
   Tarayıcı botları ve zafiyet tarayıcıları oturum açmaz. Bu aynı
   zamanda kararın kendisi: yalnız oturum açmış kullanıcıların
   uygulama içi 404'leri.
2. **User-agent** — oturumlu istekte bile bilinen tarama imzaları
   elenir (`bot, crawl, spider, curl, sqlmap, nmap, headlesschrome`…).
   User-agent kayda YAZILMAZ, yalnız süzgeçte kullanılır.

**KAYIT ALANLARI:** zaman (journald ekler), kullanıcı KİMLİĞİ, yol,
geldiği yol. **Ad ve e-posta YOK** — `journalctl` okuyabilen herkes
okur, orası kişi listesi tutulacak yer değil.

**HAFTALIK ÖZET SORGUSU (tek satır):**
```bash
sudo journalctl -u enderunai-frontend --since '7 days ago' --no-pager | grep '404-KAYDI' | grep -oP 'yol=\S+' | sort | uniq -c | sort -rn
```

#### SONDA 5'İN GÖSTERDİĞİ ŞEY — KAYDA DEĞER

Tarayıcıyı yanlış dizine baktırdım (`app` → `uygulama`). Sonuç:
**"her değişmez hedef çözülüyor" testi YEŞİL kaldı.** Boş küme her
iddiayı doğrular. Yalnız tarama-sağlığı testi (dosya/rota/hedef
sayılarının alt sınırı) ve çift yönlü çizgi testleri kırmızıya döndü.

Kural 25'in ("iki bariyer aynı sonucu üretirse test hangisinin
çalıştığını kanıtlamaz") kaynak taramasındaki karşılığı: **bir kaynak
tarayıcısı, taradığını ayrıca kanıtlamalıdır.** Sıfır bulgu "her şey
yolunda" ile "hiçbir şey taranmadı" arasında ayrım yapmaz.

### DAL DÜZENİ — `main` GÖVDE OLDU (2026-08-24)

**ÖNCEKİ DURUM BİR RİSKTİ.** Canlı, `feature/hr-frontend-sync-20260726`
dalından besleniyordu; `main` 2026-07-18'den beri ayrıydı ve
**90 commit'lik paralel bir iş** taşıyordu (hepsi 2026-07-26, tek
yazar). `origin/HEAD` **tanımsızdı**: yeni bir klon varsayılan olarak
canlıda OLMAYAN koda düşüyordu.

**YAPILAN (Seçenek 1):**
1. `archive/procurement-20260726` dalı açıldı ve uzağa itildi —
   `e3f68062`, eski `main` ile birebir aynı. **Önce arşiv, sonra
   taşıma:** bu sıra sayesinde force push hiçbir şey kaybedemezdi.
2. `origin/main` canlı commit'e (`47d1c40d`) getirildi.
   `--force-with-lease=main:e3f68062` kullanıldı: uzaktaki `main`
   beklenen SHA'da değilse push REDDEDİLİR, yani araya giren bir
   iş sessizce ezilmez.
3. `origin/HEAD` → `main` tanımlandı.
4. Çalışma ağacı `main`'e alındı (iki dal aynı commit'te olduğu için
   içerik değişmedi).

**ARŞİVDE NE VAR:** 90 commit, 67 dosya. 40 `feat(procurement)`,
9 `fix(hizir)`, 7 `feat(budget)`, 6 `feat(rfq)`, 5 `ci`,
4 `feat(inventory)`, 4 `feat(documents)`.

Canlıda **karşılığı olmayan** 8 denetleyici ve 8 migration:
`HizirActionsController`, `ProcurementDocumentsController`,
`ProcurementNotificationsController`, `ProcurementTechnicalController`,
`ProjectBudgetsController`, `PurchaseOrderPdfController`,
`RfqInvitationsController`, `SupplierPerformanceController`.
Tabloların hiçbiri canlıda yok (`rfq_invitations`, `project_budgets`,
`supplier_performances`, `procurement_documents`,
`procurement_notifications`, `procurement_technical_evaluations`).

Karşılığı **olan** 5 denetleyici gövdede farklı adla duruyor
(`RfqsController`→`RfqController`,
`ProcurementApprovalsController`→`ProcurementApprovalController`,
`HizirController`, `ProcurementDashboardController`,
`PurchaseOrders/PurchaseRequests/GoodsReceipts`) — aynı iş iki kez,
paralel yazılmış.

**GERİ ALMAK GEREKİRSE:** istenen modül arşiv dalından tek tek
taşınır. Düz birleştirme ÖNERİLMEZ: çakışma yüzeyi 11 dosya
(en ağırı `AppDbContext.cs`) ama asıl risk 8 migration'ın gövdenin
5 haftalık zincirine sokulması ve `__EFMigrationsHistory` ile canlı
şemanın uyuşmaması.

**BUGÜN İKİ TARAFIN DA SATIN ALMASI KULLANILMIYOR:** `rfqs`,
`rfq_suppliers`, `purchase_orders` hepsi **0 satır**. Yani çift
yazımın bugün pratik bir kaybı yok.

#### DAL DÜZENİ — KURAL (2026-08-25)

1. **Yayın dalı `main`'dir. Canlı her zaman `main`'in ucudur.**
2. **İş kısa ömürlü dallarda yapılır**; birleşince dal `archive/`
   önekine alınır.
3. **Bir dal 2 haftadır dokunulmamışsa `archive/` önekine alınır.**
4. **Dal adı yaptığı işi anlatır.**
5. **Dal SİLİNMEZ** — adlandırmayla emekliye ayrılır. Silmek bilgi
   kaybıdır; `archive/` öneki aynı bilgiyi koruyup "bu aktif değil"
   der.

**2026-08-25'te uygulandı:** 19 dal `archive/` önekine alındı, her
biri için önce arşiv kopyası oluşturulup **SHA eşitliği 19/19
doğrulandıktan sonra** eski ad kaldırıldı. Uzakta artık tek aktif
dal var: `main`. Arşivde 20 dal duruyor.

#### 28 TEMMUZ ÖNCESİ DALLAR BİRLEŞTİRİLMEYECEK — KURAL

`archive/feature/accounting-finance-sprint-1-20260728` (113 commit)
ve `archive/feature/project-hierarchy-20260728` (91 commit) —
**ikisi aynı soydan**, hiyerarşi dalı muhasebe dalının atası
(ölçüldü: `merge-base --is-ancestor` doğruladı, commit başlıkları
birebir aynı küme).

**Bu dallar bugün yürürlükte olan kuralların HİÇBİRİNİ taşımıyor:**
kapsam süzgeci, keyset sayfalama, RowVersion + UpdatedAtUtc, kırık
servis çağrısı çizgisi, rota bekçisi, uç bekçisi — hepsi 28
Temmuz'dan sonra kondu. Birleştirme, 450'de tutulan kapsam borcunu
geri getirir ve dört bekçinin çizgisini birden yükseltir.

**Oradan KOD değil NİYET kurtarılır:** istenen özellik bugünkü
kurallarla YENİDEN YAZILIR. Envanter aşağıda.

#### YAYIN DALI ARTIK SABİT

`safe-deploy.sh` önce `git rev-parse --abbrev-ref HEAD` ile HANGİ
DALDAYSA onu yayınlıyordu — dal sabitlemesi YOKTU. Yanlış bir
`git checkout` ya da yarım kalmış bir deneme dalı, hiçbir engel
olmadan canlıya çıkardı.

Artık `DEPLOY_BRANCH` (varsayılan `main`) ile sabit ve
`require_expected_branch` uyuşmazlıkta durduruyor. Bilinçli bir
istisna hâlâ mümkün: `DEPLOY_BRANCH=<dal> safe-deploy.sh` — ama
geçmek AÇIK bir hareket olmak zorunda.

## DEPODAN ZİMMET (2026-08-25)

Malzeme depo stoğundan düşer, **şirket varlığından çıkmaz**.

#### "ZİMMET KONUMU" AÇILMADI — ÖLÇÜM GEREKÇEYİ ÇÜRÜTTÜ

Karar "zimmet konumuna taşınsın, üç seviyeli konum yapısı buna
uygun" idi. **Uygun değil:** bölge/raf/kat MİKTAR TUTMUYOR. Miktar
yalnız `warehouse_stocks` üzerinde (depo, kalem) çiftinde duruyor;
üç seviye stok kartının YERLEŞİM bilgisi. Oraya miktar taşınamaz.

Ayrı bir "Zimmet" deposu açmak da düşünüldü ve bırakıldı — o tam
olarak "yeni mekanizma kurma" olurdu.

**Açık zimmet kaydının kendisi konumdur:**

| | |
|---|---|
| depo mevcudu | `warehouse_stocks.Quantity` — düştü |
| zimmette | açık zimmetlerin çıkış hareketleri — arttı |
| şirket varlığı | ikisinin toplamı — **DEĞİŞMEDİ** |

Miktar için yeni alan AÇILMADI: miktar zaten çıkış hareketinde ve
kayıt ona `IssueStockMovementId` ile bağlı. İkinci kopya zamanla
sapabilecek ikinci bir doğruluk kaynağı olurdu. **Migration yok.**

#### GİDER KURALI `InventoryAccountingKind`E EKLENMEDİ — GENİŞ OLAN OYDU

İlk tasarım o enum'a `Durable` eklemekti. Ölçüm çürüttü: enum'u
**İKİLİ varsayan 15 çağrı yeri** var (`kind == TradeGood ? a : b`).
Üçüncü değer eklendiğinde dayanıklı kalemler o 15 yerin HEPSİNDE
sessizce sarf tarafına düşerdi — mal kabul, stok sayımı ve
stok-muhasebe mutabakatı dahil, yani zimmetle ilgisi olmayan
akışların muhasebesi kayardı.

Zimmet sorusu ayrı bir eksen: "bu kalem bir kişiye verilince TÜKENİR
Mİ". Karşılığı stok kartında zaten var (`InventoryItem.Type`) ve o
alana bugün hiçbir muhasebe kararı bağlı değil — tek kullanımı
reçete aktarımında varsayılan atamak. **Blast yarıçapı sıfır.**

Karar tek yerde: `ZimmetGiderKurali.GiderYazilir`. Ekipman dışındaki
her tür tükenir sayılıyor; **tanınmayan tür gider YAZMAZ** (iki
yanlıştan geri alınabilir olanı).

#### EŞZAMANLILIK: SATIR KİLİDİ — KAPANDI (2026-08-25)

`warehouse_stocks` üzerinde eşzamanlılık jetonu **YOK** (yalnız
(depo, kalem) benzersiz indeksi var) ve çıkış oku-değiştir-yaz
yapıyor. İki işlem aynı anda "1 adet var" okuyup ikisi de düşerse
stok **-1** olur.

Zimmet paketinde bu akışa özel `SELECT ... FOR UPDATE` ile
kapatılmıştı; Kural 27 gereği açıkta kalan yazılmıştı. Eşzamanlılık
paketi bu açığı kapattı — **kilit tek yere taşındı**:
`IStokSatirKilidi` (`StokSatirKilidiService.cs`).

**ÖLÇÜM — "StockSaleIssuer tek kapı mı?" HAYIR.** Depo stoğunu
değiştiren canlı nokta **sekiz** (`backups/**` csproj'da derlemeden
çıkarılmış, sayılmadı):

| Nokta | Ne yapıyor | Tehlike |
|---|---|---|
| `StockSaleIssuer:129` | düşürür | negatif stok |
| `SupplierInvoiceStockPoster:236` | düşürür (fatura iadesi/iptali) | negatif stok |
| `InventoryController.Issue` | düşürür (depodan çıkış) | negatif stok |
| `StockCountService:280` | **mutlak yazar** | kayıp güncelleme |
| `InventoryController.Adjustment` | **mutlak yazar** (tekil düzeltme) | kayıp güncelleme |
| `StockSaleIssuer:230` | artırır (satış iadesi) | kayıp giriş |
| `SupplierInvoiceStockPoster:119` | artırır | kayıp giriş |
| `GoodsReceiptService:663` | artırır (mal kabul) | kayıp giriş |
| `RetailSaleService:722` | artırır (fiş iadesi) | kayıp giriş |

**SON İKİSİNİ İLK ÖLÇÜMDE KAÇIRDIM.** Kapıları `grep` ile ararken
`request.` içeren satırları elemiştim (gürültü sanmıştım); iki
denetleyici ucu tam o yüzden görünmedi. Kaçırılanları **kendi
yazdığım nöbetçi kural** yakaladı — nöbetçinin ölçümden bağımsız
olması bu yüzden değerli: ölçüm insan hatasına açık, tarama değil.

Bu iki uçta kilit **karardan sonra** değil, karardan önce iş görüyor:
`Issue`'da yeterlilik kontrolü işlem açılmadan önce yapılıyordu,
kilitten sonra **tekrarlanıyor**; `Adjustment`'ta fark (`delta`)
kilitten sonra **yeniden hesaplanıyor**. Kilidi koyup kararı bayat
veriyle bırakmak, kilidi hiç koymamakla aynı sonucu verirdi.

Perakende satış ve stoklu satış faturası çıkışı `IssueAsync`'ten
geçiyor, ayrı nokta değil. Artıran yollar stoğu eksiye düşüremez ama
kilitsiz iki eşzamanlı giriş birbirinin artışını siler: mal depoya
girmemiş sayılır. Bu yüzden kilit **altı noktanın hepsine** kondu;
yalnız düşüren ikisi kapatılsaydı nöbetçi kural istisna taşımak
zorunda kalırdı ve istisnalar zamanla büyür.

**ÜÇ TUZAK — ÜÇÜ DE KODDA YAZILI:**

1. **İşlem yoksa kilit yok.** `FOR UPDATE` işlem dışında yalnız o
   ifade boyunca tutar. Sessiz geçilseydi "kilidi çağırdım" diye
   korunduğunu sanan ama korunmayan akış üretilirdi. Ölçüldü: bugün
   altı yolun hepsi çağıran tarafta `BeginTransactionAsync` açıyor.
   Açmayan bir yol yazılırsa **hata fırlatılır**.
2. **EF kimlik haritası.** Satır bu bağlamda daha önce okunmuşsa
   kilitten sonraki sorgu veritabanına gitmez, bayat miktar döner.
   Kilit alındıktan sonra izlenen kayıt **tazeleniyor**.
   `SupplierInvoiceStockPoster` stokları döngüden ÖNCE topluca
   okuduğu için bu tuzak orada gerçek.
3. **Kendi işlemini bozma.** Tazeleme koşulsuz yapılsaydı aynı kalemi
   iki satırda içeren belge kendini bozardı: ikinci satır birincinin
   bekleyen düşüşünü geri alır, 5 stoktan 2+2 çıkınca 3 yerine 1
   kalırdı. Anahtar (işlem, depo, kalem) ve yalnız `Unchanged` kayıt
   tazeleniyor. İşlem kimliği anahtarın parçası çünkü kilit işlem
   bitince serbest kalır.

**NÖBETÇİ:** `StockMovementContractTests` iki kural ekledi —
stok miktarını değiştiren her nokta öncesinde kilit alır (**metot
gövdesi bazında**, dosya bazında değil: aynı dosyadaki komşu metodun
kilidi kuralı yeşil tutuyordu) ve `FOR UPDATE` yalnız tek dosyada
geçer.

#### İKİ SONDA YEŞİL GEÇTİ — VE BU BİR BULGUDUR

Tazeleme kararını iki ayrı bariyer koruyordu: (1) aynı işlemde aynı
satırın ikinci kez işlenmemesi, (2) yalnız `Unchanged` kaydın
tazelenmesi. **İkisi de tek tek sondalanamadı:**

| Sonda | Kaldırılan | Sonuç |
|---|---|---|
| C | tekrar-engelleyici küme | **YEŞİL** — `Unchanged` koşulu sonucu aynı tuttu |
| F | `Unchanged` koşulu | **YEŞİL** — küme ikinci çağrıyı zaten erken döndürdü |

Birini kaldırınca diğeri sonucu aynı tutuyor; yani yeşil hiçbir şey
söylemiyor. **Kural 25'in tam tarifi.** Karar
`StokSatirKilidiKarari.TazelenmeliMi(ilkKilit, izlenenDurum)` saf
fonksiyonuna çıkarıldı ve dört hâl doğrudan sınandı
(`StokSatirKilidiKarariTests`, 8 test). Artık her koşulun sabotajı
kaçmıyor.

İki koşul birbirinin yedeği DEĞİL: küme "aynı belgede aynı kalem iki
satır" hâlini kapatıyor, `Unchanged` koşulu ise "kayıt kilitten önce
değiştirilmiş" hâlini. Örtüşüyorlar ama kapsamları farklı.

#### SONDA DÜZENEĞİNDE İKİ HATA — KURAL 32'YE EK

1. **`git diff` bu ağaçta ölçüm aracı değildi.** Paket commit
   edilmemişti; dosyalar HEAD'e göre zaten farklıydı. "Sabotaj
   uygulandı mı" ve "geri alındı mı" ölçümlerinin İKİSİ de her zaman
   yanlış cevap veriyordu — sabotaj hiç uygulanmasa bile "uygulandı"
   diyordu. Ölçüm **yedeğin kendisiyle** (`cmp`) yapılacak.
2. **Sonda D sabotajı hiç uygulanamadı** (çapa metni tutmadı).
   Düzenek bunu "GEÇERSİZ" diye bildirdi ve geri aldı; yeşil sayıp
   geçmedi. Kural 32 tam da bunun için var.

#### İADE VE İPTAL TEK YARDIMCIDA

İkisi de malzemeyi depoya geri koyuyor ve çıkışta gider yazıldıysa
ters kaydı atıyor. Ayrı yazılsalardı biri ters kaydı atarken diğeri
unutabilirdi; fark stok-muhasebe mutabakatında çıkar ve hangi
akıştan geldiği belli olmazdı.

**İPTALDE GEREKÇE ZORUNLU.** İptal bu akıştaki en çok suistimal
edilebilecek eylem: malzeme kişide kalırken kayıt kapatılmış
görünebilir. Zimmet, iade ve iptal `SecurityAuditEvents`e yazılıyor,
`ActorUserId` ile.

İade maliyeti çıkıştakiyle AYNI dönüyor — bugünün ortalamasıyla geri
almak, aradaki fiyat değişimini iadenin üzerine yıkardı.

#### YOL ADLARI ÖN YÜZDEN ALINDI

`hr-asset.service.ts` bu uçları zaten çağırıyordu ve uç yazılmadığı
için **kırık servis çağrısı çizgisinde** duruyorlardı. Sunucuya ayrı
bir yol koymak aynı iş için iki sözleşme yaşatırdı:

- `POST api/hr/assets/from-inventory`
- `POST api/hr/assets/{id}/return-to-warehouse`
- `POST api/hr/assets/{id}/cancel-assignment` (yeni, ön yüzde henüz çağrılmıyor)

`Miktar` ön yüzde yoktu (tek kalem varsayılıyordu); zorunlu yapmak
mevcut çağrıyı kırardı — isteğe bağlı, varsayılanı 1.

**Yeni izin anahtarı açılmadı:** uçlar `inventory.edit` kullanıyor,
kitle değişmiyor.

#### HESAP PLANI AKTARIMI — EKLER YA DA ATLAR, ASLA DEĞİŞTİRMEZ

`POST api/accounting-accounts/import`, ayrı `chart.import` anahtarı.

- Mevcut hesap kodu gelirse GÜNCELLENMEZ — atlanır, "zaten var" listelenir
- Üst hesap yoksa OLUŞTURULMAZ — hata verilir, satır atlanır
- Hiyerarşi kodun kendisinden türetiliyor (`150.01.02` → `150.01`);
  ayrı üst-hesap alanı kodla çelişebilecek ikinci kaynak olurdu
- Satırlar koda göre sıralı işleniyor: aynı dosyadaki üst hesap önce

**İZİN YAYILMASI TUZAĞI:** `RoleCatalog`'daki `K` dizisi anahtarları
**yansımayla** topluyor — yeni anahtar, `K` kullanan her role
otomatik gider. Ölçüldü: `K`/`KWithSensitive` yalnız Admin ve Genel
Müdür'de. Finans Sorumlusu'na AÇIKÇA eklendi; **Ön Muhasebe
dışarıda** — fiş girer, hesap planını toplu değiştiremez.

#### KIRIK SERVİS ÇAĞRILARI: 3 → 0

Bu paket çizgideki üç satırı da kapattı. Sınıf yeniden **sıfırda**.

## JETON/1 — TÜMLEYEN KODLAMA VE REDDETME MUHAFIZI (2026-08-29)

### ARIZA: GİRİŞ DÖNGÜSÜ, SEBEBİ BİR İZİN ÇIKARMAK

ÖP/1a'da `payment.plan.approve` Admin'den çıkarıldı — yetkilendirme
açısından doğruydu (İ2: ödeme onayı teknik bir rolün işi değil).
Öngörülmeyen sonucu:

| Halka | Ne oldu |
|---|---|
| `RoleCatalog` | Admin 141 → **140** izin |
| `HasEveryPermission` | artık **false** |
| `TokenService` | 140 izni **tek tek** jetona yazdı |
| Jeton | **4394 bayt** (ölçüldü; tarayıcı sınırı 4096) |
| Tarayıcı | çerezi **SESSİZCE** attı |
| `middleware` | `enderun_token` göremedi → `/login` |
| Kullanıcı | giriş 200 dönüyor, oturum hiç açılmıyor |

**Yayın günü hiçbir belirti yoktu.** Mehmet'in elindeki jeton 12 saat
geçerliydi; arıza jeton dolduğunda ortaya çıktı (Kural 56).

`mehmet` kullanıcısının rolü **Admin**'di (Genel Müdür değil). Acil
düzeltme olarak Genel Müdür rolü **eklendi** (Admin kaldırılmadı —
arıza anında rol takası gereksiz risk): birleşik izin 141/141 = tam
katalog = bayrak = küçük jeton.

### BEKÇİ VARDI VE ATEŞLEMEDİ — VEKİL SINIYORDU

`TokenCookieSizeTests` dört testle tam bu arızayı koruyordu. Üçü
`AllPermissionKeys()` geçiyordu — **kataloğun tamamı**, ki o küme her
zaman bayrağı tetikler ve jetonu küçültür. Dördüncüsü `Take(44)` ile
elle yazılmış bir sayı kullanıyordu.

Yani testler Admin'in gerçek anahtar kümesini değil, **onun yerine
geçen bir vekili** sınıyordu. Vekil, "Admin = kataloğun tamamı" doğru
olduğu sürece geçerliydi; ÖP/1a o eşitliği bozdu, vekil geçersizleşti,
testler yeşil kalmaya devam etti. **Kural 58** bu olaydan doğdu.

Yeni testler `RoleCatalog.Roles` üzerinden koşuyor.

### ÜÇÜNCÜ KODLAMA: TÜMLEYEN

Sorun izin listesi değil, **bayrağın yarattığı uçurum** (Kural 57).
Üçüncü bir hâl eklendi:

| Kodlama | Ne zaman |
|---|---|
| `all_permissions` | kataloğun tamamı |
| `all_permissions` + `not_permissions` | tamamı eksi listelenenler |
| `permissions` | yalnız listelenenler |

**Seçim boyuta göre ve DETERMİNİSTİK**: `|izinler| <= |tümleyen|` ise
liste, değilse tümleyen. Eşitlik kuralın içinde — `<` olsaydı katalog
bir izin büyüyünce sınıra yakın bir rol kodlama değiştirir, aynı
kullanıcı bir girişte çalışıp ötekinde çalışmazdı.

Admin'in jetonu artık tek anahtar taşıyor.

### TEK YORUMLAYICI (Ş1)

Üç alan adı yalnız iki dosyada geçer:
`Security/JetonIzinKodlamasi.cs` ve `lib/auth/jeton-izinleri.ts`.
İki çalışma ortamı olduğu için iki dosya; **her tarafta tek yer**.

`tests/jeton-kodlamasi-tek-yer.test.ts` **her iki tarafı** tarıyor.
Yalnız ön yüz taransaydı arka uçtaki ikinci bir okuma görülmezdi.

En tehlikeli hata bu muhafızın önlediği şey: `all_permissions`
bayrağını **tek başına** okuyan bir tüketici, yanındaki
`not_permissions` listesini görmez ve kullanıcıya **olmayan bir
yetkiyi verir**. Eski okuma biçimi tam böyleydi ve ikili dünyada
doğruydu; tümleyen eklendiği an sessizce yanlışa dönerdi.

Muhafız **yorumları eliyor** — bir yorum karar veremez. `permissions`
kelimesi ayrı ele alınıyor: her yerde geçen bir iş terimi (tablo adı,
`user.permissions`), yalnız *jeton alanı olarak okunması* yasak.

`middleware` artık `routeErisimi(yol, izinVar)` çağırıyor;
`canAccessRoute` imzası **bozulmadı** (20+ test ona bağlı) ve kabuk
ile menü `/auth/me`'den gelen açılmış listeyle çalışmaya devam ediyor.

### REDDETME MUHAFIZI (Ş3)

`TokenService` jetonu üretip **ölçüyor**; paylı eşiği (3500) aşarsa
`InvalidOperationException` fırlatıyor. Gerekçe: bugünkü teşhis
saatler aldı çünkü **hiçbir katman "bu çerez atıldı" demedi.**

Açık hata, sessiz arızadan her zaman iyidir: kullanıcı yine giremez
ama **neden** giremediği bellidir.

### SINIR VE JETON/2'NİN TETİĞİ (Ş4)

**Tümleyen kodlaması bugünkü uçurumu kaldırır, ÖLÇEKLENMEYİ ÇÖZMEZ.**
Kataloğun yaklaşık yarısına sahip bir rol hâlâ şişer: 141 anahtarda
~70 anahtar ≈2400 bayt (güvenli), 300 anahtarda ~150 anahtar ≈4000
bayt (değil).

> **JETON/2** (izin listesini jetondan tümüyle çıkarma + middleware'in
> yeniden kurulması) **ŞU OLAYLA açılır: herhangi bir rolün jetonu
> 3500 bayt uyarı çizgisini aşarsa.** O ana kadar açılmaz; o an
> geldiğinde tartışılmaz, başlar.

Bir sonraki paketin ne zaman gerektiğini bugün yazmazsak, gelecek
sefer bunu bir arıza sırasında keşfederiz — bugün olduğu gibi.

### SONDA MUHAFIZIN KENDİSİNDE DELİK BULDU

Sonda D (kodlamayı middleware'de ikinci kez oku) ilk denemede
**GEÇMEDİ** — muhafız 3/3 yeşil kaldı.

Sebep ölçüldü: muhafız alan adını **yalnız tırnaklı** arıyordu
(`"all_permissions"`). Sabotaj özellik erişimi yazmıştı
(`.all_permissions`) — yani muhafız, koruduğunu söylediğim şeyi **en
doğal yazım biçiminde görmüyordu**.

Muhafız üç biçimi de görecek şekilde düzeltildi (`"alan"`, `.alan`,
`['alan']`) ve sonda ikinci denemede kırmızı verdi, ihlal satırını
adıyla gösterdi. Sağlam kodda yanlış alarm yok.

**Sonda olmasaydı bu delik commit'e girer ve "Ş1 korunuyor" yazardı.**
Kural 42'nin bir başka yüzü: ölçülmemiş bir koruma konmuş sayılmaz —
ve bir muhafızın varlığı, çalıştığının kanıtı değildir.

### KIRMIZI ÖNCE GÖZLENDİ

Test düzeltmeden **önce** yazıldı ve gerçek kusura karşı kırmızıya
döndüğü gözlendi: 21 testin 2'si kırmızı, `Admin` **4394 bayt**,
diğer 14 rol yeşil. Sonda ile taklit edilen kusur, gerçeğinin yerini
tutmaz (Kural 59).

## İŞEMRİ/1 — "YAZACAK YER YOK" (2026-08-30)

Genel Müdür `/yapilacaklar`ı açtı: *"burda görev veya emir yazılacak bir
yer yok."* Haklıydı — ama **oluşturma formu 26 Temmuz'dan beri
`/gorevler`de duruyordu** (`app/gorevler/page.tsx:591`).

### FAZ 0 PAKETİ GEÇERSİZ KILDI

Paket "form yaz" diye açıldı. Ölçüm formun zaten var olduğunu gösterdi
ve paket **adlandırma + bulunabilirlik** paketine döndü. Kod yazılmadan
önce ölçüldüğü için yanlış iş hiç yapılmadı (KAPI 1).

### KÖK SEBEP: EŞ ANLAMLI İKİ AD

| | `/yapilacaklar` | `/gorevler` |
|---|---|---|
| Eski ad | "Yapılacaklar" | "Görevler" |
| Menüdeki yeri | GİRİŞ grubunda, üstte | YÖNETİM grubunda, aşağıda |
| Ne yapar | kişisel + rol kuyruğu | iş emri kütüğü, **form burada** |

"Yapılacaklar" ile "Görevler" Türkçede eş anlamlı. Kullanıcının
hangisinin gelen kutusu, hangisinin kütük olduğunu anlamasını sağlayan
hiçbir işaret yoktu; **üstte olana bastı.** Üstelik doğru ekran
23 Ağustos'tan beri zarf uyumsuzluğundan açılmıyordu (ZARF/1).

Aynı ekran menüde **iki adla** da duruyordu: "Onay Merkezi"
(`/onay-merkezi`) birincisi ikincisine yönlendiriyor.

### YAPILANLAR

- `/gorevler` → **"İş Emirleri"**, `/yapilacaklar` → **"Bekleyen İşler"**.
  Rotalar DEĞİŞMEDİ (yer imleri).
- İş Emirleri, Bekleyen İşler'in **hemen altına** taşındı.
- "Onay Merkezi" menü girişi kaldırıldı; **rota ve yönlendirme duruyor**.
- `/yapilacaklar` boş ekranı artık İş Emirleri'ne **bağlantı** veriyor.
  **Form değil** — ikinci form ikinci doğrulama, ikinci izin kapısı ve
  ikinci hata yüzeyi demek olurdu. Tek kapı `/gorevler`.
- Şema DEĞİŞMEDİ: migration yok, yeni alan yok.

### "Bekleyen İşlerim" DEĞİL, "Bekleyen İşler"

Ekranın yedi bölümünden **üçü** kişisel (`assignedToUserId`,
`assignedByUserId`), **dördü** rol kuyruğu: `purchase-orders?status=1`,
`purchase-requests?status=1`, `site-reports/pending-approval` ve
ilerleme. Aynı izne sahip iki kişi aynı satırları görür. "İşlerim"
ekranın yarısı için yalan olurdu.

### MUHAFIZ İLK KOŞUSUNDA İKİ TANE DAHA BULDU

`tests/menu-es-anlamli-ad.test.ts` yazıldığında `/raporlar` (Rapor
Merkezi + Raporlar) ve `/depo-stok/mal-kabul` (Mal Kabul + Mal Kabul)
tekrarlarını bildirdi. **Silmedim**: ikisi de aynı ekranı İKİ
DEPARTMANIN menüsünde gösteriyor ve kasıtlı olabilir — mal kabul hem
satın almanın hem deponun işi. Satın alma ve depo kullanıcılarının
gezinme yolunu bu paketin kapsamı dışında tek taraflı değiştirmedim.
Muhafızda **açık gerekçeli istisna listesi** olarak duruyorlar; karar
Mehmet'te.

### ÖLÇÜM: İŞ EMRİNİ ON ÜÇ ROL GÖREMİYOR

`tasks.view` ve `tasks.manage` yalnızca **Admin** ve **Genel Müdür**
rollerinde. Şantiye Şefi, Formen, Teknik Ofis, İK Sorumlusu dahil on üç
rolde ikisi de yok. Yani bu paket ekranı öne çıkarıyor ama iki rol
dışında kimse menüde göremeyecek. **İzin genişletmek bu paketin işi
değil** (yetki genişletmesi ayrı karar); ölçüm kayda geçti.

---

### SONDALAR — ALTI SABOTAJ, BİRİ BEKLENMEDİK

| # | Sabotaj | Beklenen | Gerçekleşen |
|---|---|---|---|
| A | `/yapilacaklar`a `<form>` ekle | kırmızı | kırmızı ✓ |
| B | oluşturma düğmesini izin kapısının dışına al | kırmızı | kırmızı ✓ |
| C | menüye eş anlamlı "İşler" girişi | kırmızı | kırmızı ✓ |
| D | aynı ekranı ikinci kez menüye koy | kırmızı | kırmızı ✓ |
| E | grup anahtarını çakıştır | kırmızı | kırmızı ✓ |
| F | POST'tan `[RequirePermission]` niteliğini sil | kırmızı | **YEŞİL** |

### F NEDEN YEŞİL KALDI — İKİ KİLİT DEĞİL, BİR YEDEK ZİNCİR

**Önce "iki bağımsız kilit" dedim. YANLIŞTI.** Kodu satır satır
okuyunca çıkan şey bir zincir: `PermissionAuthorizationMiddleware`
önce niteliğe bakar, **nitelik varsa yol sezgisine HİÇ BAKMAZ** ve
oracıkta karar verip döner. Yol sezgisi yalnızca nitelik YOKKEN
çalışır:

    if (ContainsAny(path, "task", "gorev", "görev"))
        return isRead ? TasksView : TasksManage;

Yani `/api/tasks` POST'u nitelik tamamen silinse bile `tasks.manage`
istiyor. Sabotajım iki kilitten yalnızca birini açmıştı.

**F2 — ikisi birden kapatıldı** (nitelik silindi + ara katmanın yol
eşleşmesi bozuldu): `S2b_TasksManageOlmayanKullaniciIsEmriAcamaz`
**kırmızıya döndü.** Bekçi gerçek; sonda geçerli hâle gelince kanıtladı.

**Bunun iki yüzü var.** İyi yüzü: niteliği unutulan bir uç yine de
korunur. Dikkat gerektiren yüzü: yolunda tesadüfen `task` geçen bir uç,
kimse istemeden görev izinlerine bağlanır. Bu bir arıza değil, kayda
geçmiş bir davranış.

### S1 — TÜRKÇE TUZAĞI

`S1` ilk koşuda kırmızı verdi ama **uç doğru davranmıştı**:
`{"message":"Görev başlığı zorunludur."}`. İddiam `Contains("başlık")`
arıyordu; Türkçede son ünsüz yumuşar (**başlık → başlığı**) ve o dizi
cümlede geçmez. Arıza kodda değil, aramadaydı — kök arayan bir iddia
Türkçede sessizce yanlış yere düşer. İddia tam cümleye çevrildi.

### ÖLÇÜM ARACININ KENDİSİ ÖLÇÜME KARIŞTI

Test sayısı çizgisini yukarı taşırken cırcırın kendi sayacını kullandım:
dosyaya geçici bir `it("GÜNCEL SAYIM")` ekleyip sayıyı okuttum, sonra
geçici testi sildim. Okuduğum sayı **390**'dı ama gerçek sayı **389** —
**ölçüm aracı kendini saymıştı.**

Cırcır yeni çizgiyi hemen reddetti ("390 → 389, 1 düştü"). Sessizce
1 fazla bir çizgi kalsaydı, bir sonraki paket sebepsiz kırmızı alırdı
ve kimse sebebini anlamazdı.

Ölçüm aracı ölçtüğü şeyin içinde yaşıyorsa, ölçümü değiştirir
(Kural 58'in bir alt hâli).

## ACIL/2 — AYNI AÇIĞIN PUT'TAKİ KARDEŞİ (2026-08-31)

ACIL/1 `POST /api/tasks`'ın atama kapısını kapattı. **Aynı açık PUT'ta
duruyordu:** `item.AssignedToUserId = request.AssignedToUserId` yazılıyor,
doğrulanmıyordu. Kayıt yetkili biriyle açılır, sonra PUT ile görevi
göremeyen birine devredilirdi — POST'un reddettiği şey bir güncelleme
üzerinden geçerdi.

Bulunuşu KURAL-KATMAN/1 Faz 0'da, yazma yollarının doğrulama durumu
tablolanırken oldu.

### DERS — KURAL DEĞİL, HENÜZ

**Bir kapı eksiği bulunduğunda, aynı kaynağın BÜTÜN yazma fiilleri aynı
turda sınanır** (POST/PUT/PATCH/DELETE ve eylem uçları).

ACIL/1'de yalnız POST'a bakıldı. *"Yapısal düzeltme zaten kapsayacak"* ve
*"delegate zaten doğruluyor"* — ikisi de **okumaydı, ölçüm değil.**
Bu turda delegate ÖLÇÜLDÜ ve okuma doğrulandı; PUT ölçüldü ve **açık
çıktı.** Tekrar ederse kurallaşır.

Mehmet'in gerekçesi kayda değer: *"Yapısal düzeltme zaten kapsayacak"
cümlesi, canlı bir deliğin beklemesinin en sık gerekçesidir.*

### KOŞULAR

| Aşama | Sonuç |
|---|---|
| Düzeltmeden önce | **1 kırmızı / 5 yeşil** — `Expected: BadRequest` |
| Düzeltmeden sonra | **22/22 yeşil** |
| Sabotaj (`if (false && ...)`) | **1 kırmızı / 13 yeşil** |

Sabotajda POST testleri ve delegate **yeşil kaldı** — kesimin yalnız
PUT'u vurduğunun kanıtı.

### İDDİA GÜÇLENDİ, SEBEBİ DEĞİŞTİ

Mehmet'in itirazı: *"Doğrulama güncellenmiş merkez alanlarıyla yapılıyor"
— bu bir OKUMA, ölçüm değil.* Haklıydı; sonda listesinde tek başına
atama vardı, birleşik durum yoktu.

`PUT_AtamaYeniMerkezeGoreDogrulanir` yazıldı ve **üç sabotaj varyantının
ikisi yeşil kaldı**:

| Sabotaj | Sonuç |
|---|---|
| yalnız "eski alanları oku" | **yeşil** — işlemsiz |
| yalnız "doğrulamayı yukarı taşı" | **yeşil** |
| ikisi birden | **kırmızı** |

Deploy durduruldu ve sebep kod okunarak ölçüldü: doğrulama çalıştığında
`item.ProjectId` **zaten** `request.ProjectId`'ye yazılmıştı (satır 36),
iki kaynak aynı değerdi.

**SONUÇ İDDİAYI HEM DÜZELTTİ HEM GÜÇLENDİRDİ.** Doğrulama yeni merkeze
bakıyor **çünkü `request.*` okunuyor** — `item` mutasyona uğradığı için
değil. Mehmet'in *"sıra bir sözleşmedir"* endişesi **kısmen yersizdi**:
sıra bozulsa bile iddia ayakta kalır; bozulması için **iki bağımsız
hata** gerekiyor.

Bulgu koda yazıldı: bir sonraki düzenleyen bu bloğu merkez yazımının
üstüne alırsa neden hâlâ doğru çalışacağını ve neyin onu bozacağını
okuyabilsin.

### SİLİNEN-SAVUNMA KONTROLÜNÜN SINIRI CANLIDA ÖLÇÜLDÜ

Sabotaj bilerek `if (false && ...)` biçiminde yapıldı: hiçbir satır
silinmedi. Kontrol koşuldu ve **görmedi** — *"savunma şekilli satır
silinmemiş."*

Dosyasında yazılı sınır artık bir iddia değil, **ölçüm**:
*bu kontrol silmeye karşı korur, etkisizleştirmeye karşı değil.*

### BUGÜNKÜ MUHAFIZLARIN ORTAK SINIRI

| Muhafız | Ölçtüğü | Göremediği |
|---|---|---|
| Yetim muhafız | çağrı VAR MI | çağrı ÇALIŞIYOR MU |
| Silinen savunma | satır SİLİNDİ Mİ | satır ETKİSİZ Mİ |
| Çizimde belirsiz değer | metin kalıbı | dolaylı çağrı |
| Arka uç rotaları | dizge sabiti | birleştirilmiş yol |

Dördü de metin tabanlı ve dördü de aynı yerde duruyor: **kodun şeklini
ölçüyorlar, davranışını değil.** Davranışı ölçen tek şey test — ve bugün
iki kez, testsiz bir savunmanın sessizce yok olabildiği görüldü.

---

## ACIL/1 — SESSİZ SİLİNEN ATAMA DOĞRULAMASI (2026-08-31)

MERKEZ/1'in kendi commit'i (`2d90c946`) bir güvenlik kontrolünü sildi ve
canlıya çıkardı. Ayrıntı ve kural: **Kural 72**.

### NASIL BULUNDU — TESADÜFE YAKIN

KURAL-KATMAN/1 Faz 0'da WorkTask yazma yollarını sayarken `new WorkTask`
sayısının **3'ten 2'ye** düştüğü görüldü. Bir sayının beklenmedik
düşüşü; başka hiçbir işaret yoktu.

**Bunu sayacak ikinci bir tesadüf yoktu.** Mehmet A0'ı üç kez sordu ve
haklıydı: A1 tek bir bulguyu düzeltir, A0 aynı kesimin başka ne
götürdüğünü söyler.

### A0 — TAM TARAMA SONUCU

`2d90c946` o dosyada **50 satır** sildi:

| Satır | Sınıf |
|---|---|
| 1–20 | **(a)** merkez kuralı → ortak metoda taşındı |
| **21–46** | **(b) KAZARA** — atama doğrulaması |
| 47–50 | **(a)** `CenterType` türetme · erken çıkış genişletme · sözlük değişkeni · DTO alanları |

**Bugünkü sekiz commit'in tamamı tarandı.** Aralıkla kesme YALNIZ bir
yerde kullanıldı; diğer yedi commit'te `assert`'li tam-metin çapası
vardı. Tek kazara silme bu.

### A2 — TEST ÖNCE, SIRA TERS KURULMADI

| Aşama | Sonuç |
|---|---|
| Düzeltmeden ÖNCE | **1 kırmızı / 2 yeşil** — `Expected: BadRequest, Actual: OK` |
| Düzeltmeden SONRA | **19/19 yeşil** |

Kırmızının mesajı regresyonun kendi kanıtı oldu.

### A3 — SABOTAJ, EN SİNSİ BİÇİMİYLE

Blok **silinmedi**, `if (false && ...)` yapıldı: çağrı kodda durdu,
hiç çalışmadı. **1 kırmızı / 12 yeşil.**

`YetimMuhafizTests` **yeşil kaldı** — hem sabotajın doğru yeri kestiğini
hem o muhafızın bu sınıfı göremediğini kanıtlıyor.

### A4 — SAYIM TABANI REDDEDİLDİ, DİFF UYARISI YAZILDI

İlk öneri savunma satırlarının sayısı için taban çizgisiydi. **Mehmet
reddetti ve gerekçesi ölçümden daha keskindi:** sayım tabanı yanlış
katmanda ölçüyor — bugün olan şey bir sayının düşmesi değil, bir
DEĞİŞİKLİĞİN amaçladığından fazlasını götürmesiydi. Doğru yer diff.

Ayrıca toplamı sabit tutan bir silme (başka yerde +2, burada −2) sayım
tabanından sessizce geçerdi; **en tehlikeli silme tam olarak odur.**

`deploy/scripts/silinen-savunma-kontrolu.sh` — 50 commit'e geriye dönük
koşuldu: **2 alarm (%4), 0 yanlış alarm**, `2d90c946` yakalanıyor.
safe-deploy'a bağlandı, **kapı değil uyarı** (çıkış her zaman 0).

### BU PAKETİN KANITI TEST

**Kural 71 burada uygulanmıyor** çünkü atama kapısını tarayıcıdan
sınamak ikinci bir kullanıcıya atama denemesi gerektirir ve canlıda
`tasks.view`'e sahip ikinci kullanıcı yok (ölçüldü: yalnız Admin ve
Genel Müdür rollerinde). **Kural esnetilmedi; kapsamı dışında.**

---

## MERKEZ/1 — MASRAF MERKEZİ GÖRÜNÜRLÜĞÜ (2026-08-31)

Genel Müdür: *"İş emrinde merkez çıkmıyor."* Ölçüm üç ayrı eksik buldu.

### 1. FORMDA ŞUBE VE ŞANTİYE SEÇİCİSİ HİÇ YOKTU

Yalnız "Proje" vardı; gövde `branchId` ve `projectSiteId` alanlarını
hiç göndermiyordu. **Kendi tarifim yanlıştı**: arka uçtaki kuralı okuyup
formu ona göre anlatmıştım, oysa kural formda hiç işlemiyordu.

### 2. MERKEZ HİÇBİR YERDE GÖRÜNMÜYORDU

Arka uç `CenterType`, `BranchId`, `ProjectSiteId` üçünü de DTO'da
gönderiyordu; **ön yüz tipi onları tanımıyordu bile.** Veri geliyordu,
ekran okumuyordu.

### 3. `CenterType` DOĞRULANMIYORDU

İstekten olduğu gibi alınıyor, hangi alanın dolu olduğuyla
karşılaştırılmıyordu: `Project` yazıp `BranchId` doldurmak mümkündü.

### KURAL TEK YERE TAŞINDI — VE PUT'UN KAPISIZ DUVARI KAPANDI

`Services/Common/MasrafMerkeziKurali.cs` — saf, veritabanısız, üç iddia:
merkez zorunlu · tür seçimle çelişemez · şantiye kendi projesiyle gelir.

**PUT'a merkez alanları eklendi.** Mehmet "doğrulama POST ve PUT'u da
kapsasın" demişti; ölçüm talimatın dayandığı varsayımı düzeltti: PUT
merkez alanlarına **hiç dokunmuyordu**, yani ikinci kapı değil
**kapısız duvardı** — merkez oluşturmada konuyor ve **bir daha
düzeltilemiyordu.** Alanlar eklendi, aynı metoda bağlandı.

`CenterType` artık **saklanmıyor, türetiliyor**; istekten gelen değer
yalnız çelişki kontrolünde okunuyor.

Merkez adları `AdlariGetirAsync`'e katıldı: liste ve detay **tek
kaynaktan** besleniyor. Detay ekranı hiçbir liste çekmiyor ve çekmemeli.

### AÇIK KALAN KAPI — BİR TESTLE SABİTLENDİ

`SourceModule` dolu istekler kuralın dışında kalmaya devam ediyor. Ön
yüz artık her zaman merkez gönderdiği için kaçış **fiilen kullanılmıyor**
ama **kapı kapanmadı.**

`KaydaBagliGorev_MerkezsizGecer_ACIK_KAPI` testi bunu sabitliyor ve
yorumunda yazıyor: *"BU TEST BİR KUSURU SABİTLİYOR, BİR DAVRANIŞI
DEĞİL… KURAL-KATMAN/1 geldiğinde DEĞİŞTİRİLECEK — silinmeyecek, tersine
çevrilecek."* Böylece "kapandı" sanılması imkânsız: kapıyı kapatan paket
bu testi kırmızıya düşürmek zorunda kalacak.

### KURAL-KATMAN/1'İN KABUL KRİTERİ — İKİNCİ SATIR (2026-09-03)

`sourceModule` kaçışına dayanan **ikinci** bir test daha var ve o bir
kusuru sabitlemiyor, kaçışı **iskele olarak kullanıyor**:

> **`IsEmriTuruKapisiTests.S3d_MerkezsizGorevde_De_PersonelAdiCozulur`
> GÜNCELLENECEK.**

S3d, merkezi olmayan bir görevde de personel adının çözüldüğünü
ölçüyor; merkezsiz görevi yaratabilmek için `sourceModule` kaçışından
geçiyor. Kaçış kapandığı gün o istek 400 dönecek ve test **kapsam
kaybından değil, iskele kaybından** kırmızıya düşecek.

NEDEN BUGÜN YAZILIYOR: o gün kırmızıyı gören kişi önce "adı çözme
mantığını mı bozdum" diye arayacak — yanlış yerde. Kaydedilmezse bu
yarım saat kayıptır. Kaydedilirse tek satırlık iş: görev merkezli
kurulur, iddia aynı kalır.

AYRIM: `KaydaBagliGorev_MerkezsizGecer_ACIK_KAPI` **tersine
çevrilecek** (kusuru sabitliyor); S3d **korunacak, kurulumu
değişecek** (davranışı sabitliyor). İkisini karıştırmak, çalışan bir
iddiayı silmek olurdu.

### ÜÇ CIRCIR YAKALADI, ÜÇÜ DE GEVŞETİLMEDİ

| Cırcır | Ne dedi | Ne yapıldı |
|---|---|---|
| `set-state-in-effect` | 154 → **155** | efekt kaldırıldı, şantiye listesi olay işleyicisine taşındı; çizgi 154 |
| Sessiz yükleniyor | `gorevler/page.tsx: 0 → 1` | çıplak `return;` → if/else |
| Redwood sözleşmesi | alıcı deseni şaştı | yazım düzeltildi |

**LINT CIRCIRI TAVAN, TEST SAYISI CIRCIRI TABAN.** Çizgiyi yükseltmeye
hazırlanıyordum — *"yukarı serbest"* diye. Dosya `toplam ÇİZGİYİ AŞAMAZ`
diyor. İkisini karıştırmak, bir borcu sessizce büyütmek olurdu.

### DÖRDÜNCÜ CIRCIR: KAPSAM TABANI — VE ÇIKARIMIN ÖLÇÜM YERİNE GEÇMESİ

Merkez adlarını çözerken `db.Projects` ve `db.Branches` **süzgeçsiz**
okunuyordu. `CoverageBaselineTests` yakaladı.

İçimden geçen gerekçe: *"kimlikler zaten kapsamlı bir görev listesinden
geliyor, dolayısıyla güvenli."* **Bu bir ÇIKARIM; süzgeç bir ÖLÇÜM.**
Kapsamı dar bir kullanıcı, göreceği bir görevin bağlı olduğu ama kendi
kapsamı dışındaki bir projenin **kodunu ve adını** görebilirdi.

İstisna listesine yazılmadı: `ApplyScope` her iki tip için de mevcuttu,
yani düzeltilebilir bir şeyi "düzeltilmeyecek" diye kaydetmek yanlış
olurdu.

**CIRCIRIN GÖRMEDİĞİ DE KAPATILDI.** `ProjectSite` `CompanyId`
taşımadığı için cırcırın kapsamı dışında — **bildirmedi**. Ama sızıntı
sınıfı aynı ve `ProjectSite` için `Apply` aşırı yüklemesi yok; süzgeç
**projesi üzerinden geçişli** kuruldu. *Cırcırın kapsamı bir ölçüm
sınırıdır, güvence değil* (X4'ün tekrarı).

### AYNI İŞİN İKİ ADI — BU KEZ KOD İÇİNDE

İlk düzeltmem `kapsam.Apply(db.Projects...)` yazdı ve cırcır **yine
kırmızı** verdi: dedektör kapsam süzgecini `ApplyScope` **dizgesini**
arayarak tanıyor. Süzgeç vardı, cırcır göremedi.

Dizgeye bakmak cırcırın zayıflığı — ama çözüm cırcırı gevşetmek değildi:
`Branch` için bir `ApplyScope` uzantısı eklendi ve mevcut
`CurrentDataScopeSnapshot.Apply`'a **delege ediyor**. Yeni kural yok,
tek ad var.

Bu, günün ikinci "aynı iş iki adla yaşıyor" bulgusu. Birincisi menüde eş
anlamlı ekran adlarıydı (Kural 69); bu, kod içinde eş anlamlı metot
adları. İkisi de bir okuyucuyu yanlış cevaba götürüyordu — biri insanı,
diğeri cırcırı.

### SONDA — 7 KIRMIZI, 4 YEŞİL, İLAN EDİLDİĞİ GİBİ

Kural tamamen devre dışı bırakıldı. Beklenen kırmızılar ve yeşiller
KOŞUDAN ÖNCE yazıldı; sonuç birebir uydu. Yeşil kalan dördü `null`
bekleyenlerdi — sabotajın şekli onları ayırt edemez hâle getiriyor ve
bu, pozitif kontrolün neden gerektiğini gösteriyor.

---

## X4 — MUHAFIZIN İKİ KÖRLÜĞÜ (2026-08-31)

`/dashboard` canlıda hâlâ React #418 üretiyordu ve o ekran 26'lık
listede **yoktu**. Kaynak: **"Günaydın"**.

`lib/greeting.ts:18` → `timeGreeting(now: Date = new Date())`.
Varsayılan **çağrı anında** değerleniyor; pano onu çizim gövdesinde
çağırıyordu (`app/dashboard/page.tsx:129`). Derleme sabah yapıldığı için
HTML'e "Günaydın" dondu; öğleden sonra açan istemci "İyi günler" çizdi.

### MUHAFIZ NEDEN İKİ KEZ KAÇIRDI

**Birinci körlük — KAPSAM.** `tests/cizimde-belirsiz-deger.test.ts`
yalnız `app/` ve `components/` altını, yalnız `.tsx` uzantısını
tarıyordu. `lib/greeting.ts` iki sebepten birden görünmezdi.

Dışlamayı **ben seçmiştim**: `lib/use-istemci-zamani.ts`'i oraya
koyarken *"belirsizlik tek gözden geçirilmiş yerde yaşasın"* dedim.
Gerekçe kulağa iyi geliyordu; ölçüm çürüttü. **Dışlanan yer gözden
geçirilmiş değildi, sadece görünmezdi.**

**İkinci körlük — DİREKTİF SEZGİSİ.** Kapsamı genişlettim, dosya
**yine atlandı**: `"use client"` satırı yoktu ve sezgi onu "sunucu
bileşeni, hidrasyon yok" saydı. Ama `lib/` modülleri direktif taşımaz —
istemci bileşenleri onları içe aktarır ve kod çizimde koşar.

Direktif yokluğu `app/` ve `components/` altında bir şey söyler;
`lib/` ve `services/` altında **hiçbir şey söylemez**.

### YENİ KURAL DEĞİL, YENİ ÖLÇÜT: MUAFİYET ORANI

Mehmet'in yakaladığı satır: *"belirsiz çağrı: 141 · ihlal: 0 ·
MUAF: 141"*. **Muafiyet oranı %100 olan bir muhafızın yeşili hiçbir şey
söylemez.**

Paylaşılan modüllerde (`lib/`, `services/`) sezgi artık **hiç
uygulanmıyor**: her nokta ihlaldir, muafiyet yalnız **gerekçeli listeyle**
verilir (bugün 6 satır, her biri ölçülmüş). Yanlış alarma katlanıyoruz —
*yanlış alarm veren muhafız, hiç konuşmayan muhafızdan iyidir.*

### İKİ SONDA — DEĞİŞMİŞ SEZGİYLE

| Sonda | Sonuç |
|---|---|
| `lib/greeting.ts` düzeltmeden önce | **yakalandı** (`:18` ve `:63`) |
| Çizim gövdesine kasıtlı `new Date()` | **yakalandı** (`gorevler/page.tsx:565`), geri alınca yeşil |

İlk sabotaj denemem **uygulanmadı** (hedef dizge eşleşmedi) ve yeşil
verdi; o yeşil kanıt sayılmadı, dizge ölçülüp tekrarlandı (Kural 36).

### KALAN SINIR

`app/` + `components/` altındaki 141 muafiyet hâlâ **sezgiye** dayanıyor
ve o sezginin `useState` başlatıcılarını kaçırdığı ölçülmüş durumda.
Daraltmak ölçülmemiş büyüklükte bir kuyruk açar; ayrı iş olarak bekliyor.

---

## METİN-BAĞ/1 — PANODA İKİ KIRIK BAĞLANTI (2026-08-31)

Genel Müdür tarayıcı konsolunda iki 404 gördü. nginx günlüğü kaynağı
verdi: `_rsc=` önyüklemesi, yönlendiren `/dashboard`.

**Bağlantıları ARKA UÇ üretiyor.** `HizirBriefingSources.cs` pano
brifing kalemlerine ön yüz rotalarını **sabit kodluyor** ve ikisinin
karşılığı yoktu:

| Rota | Gerçek |
|---|---|
| `/muhasebe/tedarikci-faturalari` | `/muhasebe/faturalar` — menü etiketi "Tedarikçi Faturaları" olduğu için yol da öyle sanılmış |
| `/santiye/gunluk-raporlar` | **hiç yok** — `app/santiye/` dizini bile yok. Günlük raporlar `/projeler/{id}/santiyeler/{siteId}` altında; düz adres yok, `/projeler`e bağlandı |

GM bu uyarılara tıkladığında **boş sayfa** görüyordu.

### NEDEN MEVCUT BEKÇİ GÖRMEDİ

`tests/route-guard.test.ts` tam bu sınıf için yazılmıştı — *"bir bağlantı
hedefinin varlığı, önekinin varlığıyla kanıtlanmaz"* — ama **yalnız ön
yüzü tarıyor.** Arka uçtan gelen bağlantılar kapsamının dışındaydı.
Bekçi yeşil, bağlantı kırık.

`tests/arka-uc-rotalari.test.ts` bu boşluğu kapatıyor: 30 dosya, 19 rota,
0 kırık. Düzeltmeden önce koşuldu ve ikisini de adıyla bildirdi.

### MUHAFIZ KENDİ AÇIKLAMAMA TAKILDI — ÜÇÜNCÜ KEZ

Düzeltmeyi yapıp koştuğumda muhafız **yine kırmızı** verdi: eski rotayı
açıklayan yorumum o dizgeyi taşıyordu ve tarama onu bağlantı sandı.
Yorum bağlantı üretmez; muhafız artık yorum satırlarını atlıyor.

Aynı hatayı KABUK paketinde de yapmıştım (açıklama yorumum redwood
sözleşmesini tetiklemişti). **Metin tarayan bir muhafız, kendisini
anlatan metni de tarar.**

Ayrıca 42. metin kapatıldı: form alt başlığı *"Manuel görev oluşturun ve
projeye bağlayın."* → *"Elle iş emri açın ve projeye bağlayın."*
Üç satıra bölünmüş, tırnaksız JSX gövdesindeydi; kaynak taramam altı kez
üst üste bu biçimi kaçırdı (Kural 70).

---

## İŞEMRİ/1-A — HİDRASYON UYUŞMAZLIĞI (2026-08-30)

`components/ui/data-table.tsx:565` çıktı üst bilgisine **çizim
sırasında** `new Date().toLocaleString("tr-TR")` yazıyordu. Sunucu
geçişi derleme anında, istemci geçişi kullanıcının açtığı anda koşuyor:
her yüklemede React #418.

**11 gün açık kaldı** (`7c5b25bc`, 19 Ağustos → 30 Ağustos).
**144 statik önçizilen rotanın 26'sı** derleme saatini HTML'ine
dondurmuştu; `/finans/odeme-planlari` de bu 26'nın içindeydi.

### ÇERÇEVE DÜZELTMESİ — GECİKMİŞ DOĞRULAMA KULLANICININ İHMALİ DEĞİL

Altı haftadır "GM ödeme planında tek yazma yapmadı" diye not tutuluyordu.
O ekran donmuş damga taşıyan 26 ekrandan biri: **tık yapılmamış olabilir
çünkü YAPILAMIYORDU.**

**Bekleyen bir doğrulamanın gecikmesini kullanıcının ihmaline yazmak,
ekranın çalıştığını varsaymaktır — ve bugün o varsayım üç kez çöktü**
(204/502, zarf uyumsuzluğu, hidrasyon).

### DÜZELTME — VE İKİ CIRCIRIN İKİ KEZ HAKLI ÇIKMASI

İlk düzeltmem `useEffect` + `setState` desenindeydi. **Lint cırcırı
154'ten 159'a çıktı ve haklıydı**: o desen veri çekme için tartışmalı,
bu iş için ise doğrudan yanlış araç. `lib/use-istemci-zamani.ts`
yazıldı — `useSyncExternalStore` ile sunucu ve istemci anlık
görüntüleri ayrı veriliyor, geçişi React yönetiyor, efekt yok. Çizgi
**154'e döndü**; yükseltilmedi.

İkincisi: `setCiktiDamgasi(new Date().toLocaleString("tr-TR"))` yazımı
redwood sözleşmesini kırdı — kuralın alıcı deseni açılış parantezini
yutup alıcıyı `setCiktiDamgasiD` sanıyordu. **Kural gevşetilmedi**,
yazım düzeltildi.

Toplam 7 gerçek bulgu kapatıldı: `data-table` (her yükleme),
`demirbas` ×2 (garanti bitişi), `mal-kabul/yeni` (derlemeden sonraki her
gün), `ice-aktar` ve `kar-analizi` ×2 (yılbaşı).

### MUAFİYET SAYACI MUHAFIZI KENDİ KÖRLÜĞÜNÜ İTİRAF ETTİRDİ

`tests/cizimde-belirsiz-deger.test.ts` ilk hâlinde yalnız bulgu sayısını
basıyordu. Muafiyet sayısı eklenince çıkan tablo: **146 muaf / 0 ihlal.**

Sebep ölçüldü: sezgi bir satırdan yukarı doğru `return (` arıyor, ama
`useState` başlatıcıları `return`'ün üstünde durur; arama işaret
bulamayınca **açık tarafa** düşüyor. Yani muhafız JSX içindekileri
yakalıyor, durum başlatıcılarını kaçırıyor.

**Sayı görünmeseydi bu körlük fark edilmezdi.** Kapsam genişletmesi ayrı
iş olarak bekliyor; sınır muhafızın kendi dosyasında yazılı.

### YAN ÖLÇÜM — YUMUŞAK SİLME DAMGASI (sistemik değil)

21 tabloda 35 yumuşak silinmiş satır, **13'ü damgasız**. Hepsinin
`CreatedAtUtc` tarihi **2026-08-01**; 11 Ağustos'taki silmeler damgalı.
`AuditSaveChangesInterceptor:85,97` her silmede damgayı yazıyor.
Tarihsel kalıntı, bozuk yol değil. Düzeltilmedi.

---

### Kural 72 — KOD BLOĞU METİN ARALIĞIYLA SİLİNMEZ

**Kod bloğu metin aralığıyla silinmez. Aralıkla kesmek zorunda kalırsan,
kesilen aralığı silmeden önce bas ve oku; aralığın içinde amaçladığından
fazlası varsa kesim yanlıştır. Yerine: kesilecek bloğun tam metnini çapa
olarak kullan ve eşleştiğini doğrula — bugün sekiz commit'in yedisinde
yaptığın buydu, hata sekizincisinde, çapasız kesimde oldu.**

**DOĞURAN OLAY (`2d90c946`, MERKEZ/1).** Merkez kuralı ortak metoda
taşınırken POST gövdesi `s.index(bas)` … `s.index(son)` aralığıyla
kesildi. Aralıkta duran **atama doğrulaması** da gitti:

    if (request.AssignedToUserId is Guid atanan)
    {
        var taslak = new WorkTask { ... };
        if (!await GorevAtanabilirMiAsync(taslak, atanan, ...))
            return BadRequest("Seçilen kullanıcı bu görevin kaydını
                               göremiyor, dolayısıyla göreve atanamaz.");
    }

**26 satır sessizce silindi ve canlıya çıktı.** `POST /api/tasks` bir
gün boyunca `AssignedToUserId`'yi doğrulamadı: `tasks.manage` taşıyan
biri, iş emrini kendi veri kapsamı dışındaki bir kullanıcıya
atayabilirdi. Pratik risk düşüktü (`tasks.manage` yalnız Admin ve Genel
Müdür'de, ikisi de küresel kapsamlı) ama kapı açıktı.

**HİÇBİR ŞEY YAKALAMADI. 2965 test, dört cırcır, kapsam tabanı — hiçbiri.**
Sebep: silinen kod **testsizdi**. `YetimMuhafizTests` de görmedi çünkü
`GorevAtanabilirMiAsync` başka iki çağrı yerinde yaşamaya devam
ediyordu — yetim değildi, yalnız **en önemli çağıranını kaybetmişti.**

Bulunması tesadüfe yakındı: KURAL-KATMAN/1 Faz 0'da yazma yollarını
sayarken `new WorkTask` sayısının 3'ten 2'ye düştüğü görüldü.

**KURAL 55'İN KARDEŞİ.** 55 dosya düzeyinde (`cat >` ile var olan test
dosyasını ezmek), 72 blok düzeyinde. İkisi de aynı aile: **yazma işlemi
amaçladığından fazlasını götürdü.**

**MEKANİK KARŞILIĞI:** `deploy/scripts/silinen-savunma-kontrolu.sh` —
commit'in SİLDİĞİ satırlara bakar, savunma şekilli olanları olduğu gibi
ekrana basar ve beyan ister. Taban çizgisi tutmaz, sayı saymaz.
Geriye dönük ölçüldü: son 50 commit'te **2 alarm (%4), 0 yanlış alarm**,
`2d90c946` yakalanıyor.

### Kural 71 — YAYIN, TARAYICIDA BİR ETKİLEŞİM GÖRÜLMEDEN TAMAM DEĞİLDİR

**Bir ekran, tarayıcıda üzerinde en az bir gerçek etkileşim gözlenmeden
yayınlanmış sayılmaz. Test takımları hidrasyon çalıştırmaz; yeşil takım
ekranın açıldığını değil, kodun derlendiğini söyler.**

**DOĞURAN OLAY (İŞEMRİ/1, 2026-08-30).** Ön yüz 524/524, arka uç
2946/2946, muhafızlar yeşil, duman kontrolü 204 — ve ekran tarayıcıda
**tamamen etkileşimsizdi**. Genel Müdür "+ Yeni İş Emri"ye dört kez
bastı, form hiç açılmadı; "Yenile" hiç ağ isteği üretmedi.

**Kök sebep, 11 gün eskiydi ve bu paketle ilgisizdi.**
`components/ui/data-table.tsx:565` çıktı üst bilgisine çizim sırasında
`new Date().toLocaleString("tr-TR")` yazıyordu. Sunucu geçişi **derleme
anında** koşuyor, istemci geçişi kullanıcının açtığı anda: her yüklemede
hidrasyon uyuşmazlığı (React #418).

**ÖLÇÜLEN MALİYET: 144 statik önçizilen rotanın 26'sı** derleme saatini
HTML'ine dondurmuştu — `/gorevler`, `/finans/odeme-planlari`, `/hakedis`,
`/cariler`, `/muhasebe/fisler`, `/metrajlar`, `/sirketler` dahil.
Damgayı ekleyen paketin adı **"F5: çıktı dürüstlüğü"**ydü (`7c5b25bc`,
19 Ağustos): çıktının dürüstlüğü için konan damga 26 ekranı bozdu.

**İKİNCİ ÖĞRETİCİ KISIM — MUHAFIZ NEDEN KAÇIRDI.** Aynı pakette B4
("Onay Merkezi menü girişi kaldırılır") **yarım uygulandı**: menü
tanımından kalktı, ama `components/erp/erp-shell.tsx:446`'da
`MENU_GROUPS`'tan bağımsız, **sabit kodlanmış** ikinci bir
`<Link href="/onay-merkezi">` duruyordu. Muhafız `MENU_GROUPS`'u okuyor,
kabuğun gövdesine yazılmış bağlantıyı değil — **yeşil verdi, ekran
ihlalliydi.** İhlali kod taraması değil, tarayıcı gördü.

Kural 65 ve 70'in yanına, ölçüm-aracı ailesine girer: ölçüm aracı neyi
göremediğini söylemez.

**Mekanik karşılıkları:**
- `tests/hidrasyon.test.tsx` — `renderToString` + `hydrateRoot`, saat
  ilerletilerek. Pozitif kontrolü kasıtlı uyuşmazlığı yakalıyor.
- `tests/cizimde-belirsiz-deger.test.ts` — çizim gövdesinde
  `new Date()/Date.now()/Math.random()/crypto.randomUUID()`.

**Ama ikisi de kuralın yerine geçmez.** İkisi de bu arızadan SONRA
yazıldı ve ikisi de metin tabanlı. Kural insana ait: ekranı aç, bir şeye
bas.

#### Kural 71 — NOT (a): ÖLÜMCÜLLÜK EKRANA GÖRE DEĞİŞİR

**Hidrasyon uyuşmazlığının ekranı öldürüp öldürmediği, uyuşmazlığın
ağacın neresinde olduğuna bağlıdır. Bir ekranda ölümcül olması genel
yasa değildir.**

`/gorevler`de uyuşmazlık etkileşimi tamamen kesiyordu: dört tıkta form
açılmadı, "Yenile" ağ isteği üretmedi. `/dashboard`da aynı hata sınıfı
(#418) üretiliyor ama ekran **etkileşimli** — bağlantı tıklaması
çalışıyor.

İŞEMRİ/1-A raporunda "(i) doğrulandı" derken bunu bir ekran için
söylemiştim; genel yasa diye okunmamalı.

#### Kural 71 — NOT (b): KONSOL BİRİKİMLİDİR

**Tarayıcı konsolu birikimlidir: duran bir hata satırı, süren bir
arızanın kanıtı değildir. Konsol ipucudur; ölçüm, sunucu günlüğü ya da
temiz yüklemedir.**

**BU NOT MEHMET'İN HATASINDAN DOĞDU** ve öyle kalsın — kuralların kimin
hatasından doğduğu, onları hatırlanır kılan şey.

METİN-BAĞ/1 yayınlandıktan sonra konsolda iki 404 duruyordu ve arıza
sürüyor sanıldı. nginx erişim günlüğü tersini söyledi: GM'nin IP'sinden
bugün **767 istek**, üç 404'ün **üçü de deploy'dan ÖNCE** (07:17:54,
07:17:54, 08:11:25 — yayın 08:56:32'de bitti). Deploy sonrası pano
yüklemeleri 200/304 ve taze `_rsc` belirteçleriyle.

Konsoldaki satırlar sekmede duruyordu, yeni istek üretmiyorlardı.


### Kural 70 — KAYNAKTA GREP ÖLÇÜM DEĞİL, İPUCUDUR

**Kullanıcıya görünen bir metnin "yok" olduğunu kaynak taramasıyla
söyleme. Ölçüm, render edilmiş çıktıda yapılır.**

Bugün beş kez "aramam dardı" dedim. Bu bir dikkat sorunu değil,
tasarımın sonucu: kullanıcıya görünen metin JSX içine dağılmış — kimi
`"tırnaklı"`, kimi `{degisken}` içinde, kimi düpedüz tırnaksız gövde
metni. Tek bir kalıp hepsini yakalayamaz.

Son örnek: `grep -c '"Başlat"'` **0** verdi ve "etiket yok" dedim.
Etiket 486. satırda, JSX gövdesinde, tırnaksız duruyordu. Cırcır onu
buluyordu; benim aramam bulmuyordu.

**Uygulama:** bir metnin varlığı/yokluğu iddiası, testin gördüğü render
çıktısına dayanmalı. Kaynak taraması yalnızca "nereye bakayım" sorusunu
cevaplar.

**Kalıcı çözüm bu değil.** Metinlerin tek dosyada toplandığı bir metin
katmanı bu arıza sınıfını tümden bitirir — not olarak duruyor, paket
değil.

### Kural 69 — İKİ EKRAN EŞ ANLAMLI ADA SAHİP OLAMAZ

**İki ekran eş anlamlı ada sahip olamaz ve bir ekran menüde birden fazla
girişle görünemez. Menü etiketi ekranın NE OLDUĞUNU değil, kullanıcının
oraya NİYE GİTTİĞİNİ söyler.**

Arızanın şekli: kullanıcı doğru işi yapmak için yanlış ekrana gider,
orada aradığını bulamaz ve **özelliğin var olmadığı sonucuna varır.**
Kod kusursuz çalışırken özellik yok sayılır.

Mekanik karşılığı `tests/menu-es-anlamli-ad.test.ts`: eş anlamlı çiftler,
tekrarlı yol, tekrarlı grup anahtarı/etiketi. **Dürüst sınırı**: eş
anlamlılık bir kelime listesinden okunur; listede olmayan yeni bir çift
testten geçer. Liste arıza tekrar ettikçe büyür.

## ZARF/1 — /gorevler ZARF UYUMSUZLUĞU (2026-08-30)

`/gorevler` **23 Ağustos'tan beri hiç açılmıyordu.** Uç
`{ items, hasMore, nextCursor }` zarfına çevrilmiş, ekran düz dizi
beklemeye devam etmişti: `TypeError: M.slice is not a function`.

**"WorkTasks 1 kayıt" tablosunun sebebi buydu** — bulunabilirlik ya
da kullanıcı ilgisizliği değil, ekranın açılmaması.

**NEDEN GÖRÜLMEDİ:** istemci hata bildirim kanalı da ayrı bir
arızayla (204/502) çöküktü. Ekran çöküyor, bildirmeye çalışıyor,
bildirim de düşüyordu (Kural 66).


> **DÜZELTME (İŞEMRİ/1 Faz 0'da ölçüldü).** Yukarıda önce "F4 turundan
> beri" yazıyordu. YANLIŞTI ve ölçülmeden yazılmıştı. Gerçek:
>
> - Ön yüz **26 Temmuz**'dan beri (`a71c11c8`) düz dizi okuyor.
> - Arka uç zarfa **23 Ağustos**'ta geçti (`d57a4c50`, M1/2).
> - F4 commit'leri 21–22 Ağustos'tandır; kırılmanın sebebi F4 değil.
>
> Süre **7 gün**, "haftalarca" değil. Kanıt: `GRV-2026-00001` 1 Ağustos'ta
> `/gorevler` formundan açılmış — o gün iki taraf da düz dizi
> konuşuyordu ve ekran çalışıyordu.
>
> Bu, ölçülmeden tekrarlanan bir tarihin kayda geçmesiydi (Kural 58).

### MUHAFIZ ÖNCE YAZILDI, KIRIK HÂLDE KOŞTU

`tests/zarf-tuketimi.test.ts` **düzeltmeden ÖNCE** koşuldu ve
`/gorevler`i yakaladı:

    services/work-task.service.ts: apiClient<WorkTask[]>("tasks")
    — WorkTasksController.cs zarf dönüyor

Düzeltmeden sonra yeşil. Bu, muhafızın yerleşik olumlu denetimi
(Kural 59: ilk gözlem gerçek kusura karşı).

### TARAMA SONUCU: YALNIZ /gorevler

Zarf dönen **5 kök uç** var (WorkTasks, AccountingAccounts,
GoodsReceipts, EngineeringPositions/Recipes, Collaboration).
**Başka hiçbirinde** düz dizi tüketimi yok.

Elle grep 5 yanlış aday üretmişti; muhafız onları eledi çünkü zarf
**kök uçta**, dizi bekleyen çağrılar **alt uçlara** gidiyor
(`/arama`, `/{id}/inventory-options`). Controller seviyesinde
eşleştirmek beşini de yanlış bildirirdi (Kural 47).

### MUHAFIZIN BİLİNEN SINIRI

`apiClient<X[]>` kalıbını arıyor. Zarfı `any` ile ya da tipsiz
tüketen bir yer varsa **görmez**. Kapsam genişletmesi bilinçli
olarak yapılmadı: `any` taraması gürültülüdür ve yanlış alarm üreten
muhafız okunmamayı öğretir.

## 204/502 — ALTI HAFTALIK SESSİZ ARIZA (2026-08-30)

Ön yüz proxy'si (`app/api/backend/[...path]/route.ts`) her yanıtı
`arrayBuffer()` ile okuyup gövde olarak geçiriyordu. Web standardına
göre `new Response(gövde, { status: 204 })` **FIRLATIR** — boş tampon
bile geçersiz. Fırlatan yapıcı `catch`e düşüyor ve proxy **502**
döndürüyordu.

**18 Temmuz'dan 30 Ağustos'a — altı hafta.** (`83f567d9`, monorepo
birleştirmesi.)

### ON PAKET, YAZMA UÇLARI ÖLÜYKEN "TAMAMLANDI" SAYILDI

| Controller | 204 uç | Paket |
|---|---|---|
| `OdemePlanlari` | **6** | ÖP/1b (28 Ağu) |
| `HrRecruitment` | 4 | M1/1 (23 Ağu) |
| `HrMasterData` | 3 | Personel ek ödeme (6 Ağu) |
| `CompanySettings` | 1 | Banka hesabı ucu (25 Ağu) |
| `ProgressPayments` | 1 | G3/1b (22 Ağu) |
| `Tax` | 1 | ROL-UI R2/4c (17 Ağu) |
| `ProjectMeasurements` | 1 | Yetki daraltma (16 Ağu) |
| `ProjectBoq` | 1 | NATURA B2 (12 Ağu) |
| `ProjectSiteDailyReports` | 1 | P5-P6 (5 Ağu) |
| `ProjectSites` | 1 | Yetki Faz 2 (2 Ağu) |
| `IstemciHatalari` | 1 | KABUK (29 Ağu) |

**2865 test yeşildi.** Testler servisi doğrudan çağırıyor, proxy'den
geçmiyor (Kural 68).

### NEDEN KİMSE BİLMİYORDU

İlk kurban `istemci-hatalari` ucuydu — **hata bildirim kanalının
kendisi.** Ekranlar çöküyor, bildirmeye çalışıyor, bildirim de 502
alıyordu (Kural 66).

Kaybedilen bildirim **sıfır**: kanal 14 saatliktı ve o sürede tek
istek geldi (ölçüldü, arşiv günlükler dahil).

### DÜZELTME

- Proxy: 204/205/304'te gövde yerine `null` — **tek yerde, 21 uç**
- `GET /api/health/govdesiz` — anonim, gövdesiz, yan etkisiz
- safe-deploy: proxy üzerinden **üç sonuçlu** duman kontrolü

**KALICI OLAN ÜÇÜNCÜSÜ:** tek satırlık bir kontrol, altı haftalık
arızayı ilk yayında yakalardı.

## BEKLEYEN PAKET — GÖÇ/ETKİ: SESSİZCE VERİ BOZAN GÖÇ

GÖÇ/PROVA'nın **kanıtlanmış sınırı**: prova, göçün canlının bir
kopyasında **patlamadığını** ölçer. Verinin **doğru kaldığını**
ölçmez.

Sonda A bunu somut gösterdi. `DropColumn(name:"Title",
table:"WorkTasks")` kopyada sorunsuz uygulandı — `Applying migration`
basıldı, çıkış kodu 0, `PROVA GEÇTİ`. Kolon ve içindeki bütün görev
başlıkları yok oldu ve **prova bundan hiç söz etmedi.** Yıkıcı beyan
kapısı bu göçü ancak beyan EDİLMEDİĞİ için durdurdu; beyan doğru
yazılınca prova yeşil verdi. Yani bugünkü savunma **beyana**
dayanıyor, ölçüme değil.

Beyanın durduramadığı asıl tehlike yıkıcı olmayan göçtür:
`AlterColumn` ile tip daraltmak, `UpdateData` ile yanlış dönüşüm
yazmak, varsayılan değerle var olan satırları ezmek. Bunların hiçbiri
`yikici_kalemler()` desenine girmez, hiçbiri hata vermez, hepsi veriyi
sessizce bozar.

**İş:** provaya bir **etki ölçümü** aşaması eklemek — göçten önce ve
sonra kopyada seçili tabloların satır sayısı ve anahtar sütunlarının
boş-olmayan sayımı alınıp farkın raporlanması; fark beyan edilmemişse
prova düşer. "Patlamadı" ile "doğru" arasındaki boşluk bu.

## BEKLEYEN PAKET — TEST DÜZENEĞİ: PROJE KAPSAMLI KULLANICI

`TestUserFactory.CreateClientWithRolesAsync` yalnız **şirket** kapsamı
kurabiliyor (`scopedCompanyId`). **Şirket kapsamı bazı ayrımları
GÖREMEZ:** şirket kapsamlı bir kullanıcı o şirketin bütün projelerini
görür, yani "A projesini görüyor ama B'yi görmüyor" senaryosu
kurulamaz.

ACIL/2'de `PUT_AtamaYeniMerkezeGoreDogrulanir` tam bu ayrıma
dayanıyordu ve **kapsamı elle kurmak zorunda kaldı** (`UserDataScopes`
satırlarını silip `DataScopeType.Project` eklemek). Yazılmasaydı test
yeşil verir ve **hiçbir şey ölçmezdi.**

Yarın aynı ayrımı sınamak isteyen bir sonda aynı tuzağa düşecek —
ve o sonda muhtemelen tuzağı fark etmeyecek, çünkü test yeşil olacak.

**İş:** `TestUserFactory`'ye proje (ve gerekirse şube/şantiye) kapsamı
eklemek. Küçük ama ACIL/2'nin işi değildi.

---

## BAĞ/1 — LİSTEDEN DETAYA GİDİŞ (2026-09-01)

Genel Müdür iş emri numarasına tıkladı, hiçbir şey olmadı. Numara
`<strong>` idi; `/gorevler/[id]` ekranı **vardı** ama listeden oraya
**gidilemiyordu**.

**Yan etkisi:** MERKEZ/1'in *"detayda masraf merkezi görünüyor"* iddiası
**doğrulanamıyordu** — doğrulanacak ekrana ulaşılamıyordu. Açık duran bir
iddia, bir bağlantı eksikliği yüzünden açık kaldı.

### ÖLÇÜM: BU SINIF TARANABİLİR DEĞİL

46 dinamik detay rotası tarandı; **hiçbir yerden bağlantı almayan 2**:
`/portal/[token]` (beklenen — dış paydaşa e-postayla giden jeton
bağlantısı) ve `/depo-stok/raf/[warehouseId]/[shelfId]`.

**`/gorevler/[id]` bu listede ÇIKMADI** ve tarama **teknik olarak
haklıydı**: rotaya bağlantı **var** — `TaskDueNotificationScanner.cs:88`,
termin bildirimi `/gorevler/{id}` üretiyor.

Yani sınıf *"hedef ulaşılamaz"* değil, ***"kullanıcının bulunduğu yerden
ulaşılamaz"***. **Bu taranabilir bir şey değil:** hangi ekrandan hangi
ekrana gidilmesi gerektiği bir TASARIM KARARI, kod özelliği değil.
Muhafız yazılsaydı yanlış şeyi ölçerdi — bu yüzden yazılmadı.

Bir sonraki sefer aynı soruyu soran kişi taramaya güvenmesin.

### DERS: BAĞLANTILAR ARKA UÇTA DA YAŞIYOR

Ön yüzü tarayan bir ölçüm `/gorevler/[id]` için *"bağlantı yok"* der ve
yanılır. METİN-BAĞ/1'de aynı ders tersinden çıkmıştı: arka uç ön yüz
rotalarına bağlantı üretiyor ve `route-guard` onları göremiyordu.

---

## ÖLÇÜM DÜZELTMESİ — `MANUAL` KAÇIŞI ARTIK KULLANILIYOR (2026-09-01)

KURAL-KATMAN/1 Faz 0'da ölçmüştüm: *"`SourceModule` hiçbir kayıtta dolu
değil — `MANUAL` kaçışı canlıda hiç kullanılmamış."*

**O ölçüm eskidi.** 1 Eylül 15:00'te Genel Müdür ilk gerçek iş emrini
açtı: `GRV-2026-000001`, **`SourceModule = MANUAL`**, merkezi dolu
(şantiye, `CenterType = 2`).

**ASIL DERS BU:** kaçış yolu üretimde kullanılmıyordu; **ekran onu
kullanmaya başladığı anda kullanılır oldu.** Bir kaçışın bugün
kullanılmıyor olması, kapatılmasını erteleme gerekçesi değildir.

Burada zararsızdı — merkez doluydu. Ama kural onu **atladı**: merkezi
boş bırakan bir istek de aynı yoldan geçerdi.

**KURAL-KATMAN'ın gerekçesi artık ölçülmüş bir gerçek**, öngörü değil.

**Göç planı bu tek kaydı hesaba katacak:** `MANUAL` kaynaklı, merkezi
dolu, atanmamış; tür alanı geldiğinde **İşEmri** olacak.

---

## BEKLEYEN PAKET — KAPI/1: NİTELİK YOKSA REDDET

**Sırası: GM'nin İŞEMRİ/1 doğrulamasından sonra. M3/2b'den ÖNCE ya da
onunla BİRLİKTE — sonra değil.**

### HÜKÜM (Mehmet, 2026-08-30)

Yol türetimi bir güvenlik ağı **DEĞİL**. İki sebeple:

1. **Bugün sıfır uç yakalıyor** — 34 niteliksiz ucun hiçbiri yol
   kuralına uymuyor, yani var olduğu altı ay boyunca hiçbir şey yapmadı.
2. Yakalasaydı bile **daha KABA bir izne düşürerek** yakalardı. Nitelik
   silinen uç 403 vermiyor, sessizce daha gevşek çalışıyor. **Sessiz
   gevşeme, gürültülü kapanmadan kötüdür — kimse fark etmez.**

**Doğru tasarım tersidir: nitelik yoksa REDDET.** Ve tercihen çalışma
anında değil, **açılışta**: muaf listede olmayan ve niteliği olmayan bir
uç varsa uygulama hiç başlamasın. Böylece 183'lük kuyruk riski tümden
biter — çünkü **nitelik silen kişi deploy'da öğrenir, saldırgan değil.**

### ÖLÇÜM (İŞEMRİ/1 sonda F, 2026-08-30)

Sonda F "POST'tan `[RequirePermission]` niteliğini sil" idi ve **yeşil
kaldı**. Sebebi testin körlüğü değildi; `PermissionAuthorizationMiddleware`
niteliği görünce yol sezgisine **hiç bakmıyor** — nitelik varsa orada
karar verip dönüyor, yol sezgisi yalnızca nitelik YOKKEN çalışıyor.
F2'de iki katman birden kapatıldı ve `S2b` kırmızıya döndü.

| Ölçüm | Sayı |
|---|---|
| Toplam uç | 790 |
| Nitelikli | 756 |
| **Niteliksiz** | **34** |
| Niteliksiz ucun yol kuralına uyanı | **0** — ağ bugün hiçbir şey yakalamıyor |
| Nitelik silinse ağ yakalar | 553 |
| **Nitelik silinse HİÇBİR KORUMA KALMAZ** | **183** |
| Nitelik ≠ yoldan türeyen (nitelik daha ince) | 400 / 736 |

O 183'ün içinde **`/api/cheques` ailesinin tamamı (13 uç)**,
`/api/company-settings`, `/api/kurumlar-vergisi-oranlari`,
`/api/e-invoice/import/commit`, `/api/access-requests/{id}/approve` var.

### M3/2b BAĞLANTISI — SIRALAMANIN SEBEBİ

34 niteliksiz ucun **8'i `/api/mesajlar/*`**. Mesajlaşma ekranı (M3/2b)
sıradaki paketlerden biri ve bugün o uçların izin kapısı **yok** —
yalnız `[Authorize]` var, yani **oturum açan herkes**. Daha önceki sonda
D *"arama üyelik süzgecini atlayınca yabancı başkasının mesajını buldu"*
demişti. İkisi aynı yüzeye bakıyor.

Bu yüzden sıra: **KAPI/1, M3/2b'den önce ya da onunla birlikte gelir.**

Kalan niteliksiz uçlar: `/api/bildirimler/*` (5), `/api/collaboration/*`
(7), `/api/portal/*` (4), `/api/auth/*` (4), `/api/user-preferences` (2),
`/api/yonetim/kpi`, `/api/istemci-hatalari`, `/api/isg/benim`,
`/api/company-settings/logo`.

---

## MUHAFIZ DESENİ — KAPSAM İLE ÖLÇÜT AYRI İKİ ŞEYDİR (2026-09-02)

`silinen-savunma-kontrolu.sh`, `9d4ffd0b` commit'inde `goc-provasi.sh`'ten
silinen 8 satırlık muhafız bloğunu görmedi. İlk teşhis kapsamdı: kontrol
yalnız `*.cs`, `*.ts`, `*.tsx` dosyalarına bakıyordu.

**Muhafızı `*.sh`'e açmak YETMEDİ; muhafızın KENDİSİ kabuk savunmasını
tanımıyordu.** Deseni tamamen C#/TS şekilliydi (`return BadRequest`,
`throw new`, `[RequirePermission`) ve yorum eleyicisi `#` bilmiyordu.
Kapsam genişletildikten sonra aynı commit yeniden denetlendi ve yine
"savunma şekilli satır silinmemiş" dedi.

**Kapsamı genişletmek ile görmeyi öğretmek ayrı işlerdir — ve bunu
pozitif kontrol ortaya çıkardı, kapsam değişikliği değil.** Kapsam
değiştirildikten sonra "artık kapsıyor" denip geçilseydi, kontrol
genişletilmiş gibi yapacak ve aynı körlüğü sürdürecekti. Bir kontrolü
değiştirdikten sonra, onu KAÇIRDIĞI BİLİNEN VAKA ile yeniden koşmak
zorunludur; yoksa değişikliğin işe yarayıp yaramadığı ölçülmemiş olur.

Desene kabuk savunması eklendi (`hata`/`fail` çağrısı, sıfırdan farklı
`exit`, `|| exit` kısayolları) ve iki yönde sınandı: `9d4ffd0b` artık
6 satırı buluyor, `f9b61709` (yalnız ön yüz bağlantısı) temiz kalıyor.

---

## GÖÇ/PROVA — SEKİZ SONDA VE ÖLÇÜMÜN KENDİSİ (2026-09-02)

Sekiz sonda koşuldu; **hepsinin beklentisi koşudan önce ilan edildi**,
hepsi tuttu: A1 beyan yok · A2 doğru beyan · D2 `[Migration]` yok ·
D3 bayat kopya (sayılar eşit) · E sahte araçla fark boş · C1 olmayan
veritabanı · C2 yanlış parola · temizlik koşusu.

**En somut katkı: tazelik kanıtının kendisi bozuktu.** Yalnız
`count(*)` karşılaştırıyordu; 205 = 205 iki FARKLI küme için de
doğrudur. Bayat bir kopya kuruldu (sonda göçü eklendi, başka bir göç
silindi, sayı yine 205) ve **commit'li eski betik onu tazelik
kanıtından geçirdi**. Sonra göç patladı ve düzenek masum bir göç için
"bu göç canlıda da patlardı" dedi — bayat kopya, yanlış kırmızıya
dönüştü. **Sayıyla doğrulama, kümeyle doğrulamanın yerini tutmaz.**

**İki sessiz kusur sondaların değil, ÖLÇMENİN ürünü oldu:**
1. `dotnet ef migrations list` veritabanına BAĞLANAMADIĞI hâlde çıkış
   0 döndü ve her şeyi "uygulanmamış" gösterdi. Kapatma: EF'in saydığı
   uygulanmış toplam, kopyanın `__EFMigrationsHistory` satır sayısıyla
   tutmak zorunda — iki bağımsız okuyucu.
2. JSON ayrıştırıcısı sessizce düşüyordu: `dotnet ef`, JSON'dan önce
   `info: …Command[20101]` basıyor ve ilk `[` yanlış yeri gösteriyor.

**Geri çekilen iddia — kayda böyle geçsin.** "Eski kod bayat kopyada
YEŞİL verirdi" dedim; kurduğum ispat yeşil vermedi, kırmızı verdi
(silinmiş göçü yeniden uygulamaya çalışıp 42P07 aldı). Gösterilen şey
"eski kod bayat kopyayı tazelik kanıtından geçirdi ve sonra masum göçü
suçladı"dır. Fark ölçümünün durum ölçümünden üstünlüğü **kod
okumasına** dayanıyor, gösterilmiş bir yeşile değil. İleride biri "bu
düzenek şunu yakalıyor" derken **neyin gösterildiğini, neyin
okunduğunu** ayırt edebilsin.

---

## GÖÇ/PROVA — KURULDU (2026-09-01)

`deploy/scripts/goc-provasi.sh`. Canlının **taze kopyasını** alır, göçü
oraya uygular, patlarsa yayın durur ve **canlıya dokunulmaz**.

### İKİ KONTROL

| Kontrol | Sonuç |
|---|---|
| **Pozitif** — geçerli göç | `SAHİPLİK ✓ · TAZELİK 205=205 ✓ · GEÇTİ · UYGULAMA KANITI: 1 göç geçmişte` · çıkış 0 |
| **Negatif** — bozuk göç | `PROVA DÜŞTÜ · GEREKÇE: Applying migration · SQLSTATE · göç adı` · `42P07 relation "WorkTasks" already exists` · çıkış 1 |

**Aynı göç dosyası**, tek fark bir SQL satırı. Düzenek göçün İÇERİĞİNE
bakıyor. Sonda göçü silindi; canlıda hiçbir iz kalmadı (ölçüldü).

### DÜZENEK ALTI KEZ YANILDI, HER YANILGI BİR KANIT SATIRI DOĞURDU

| # | Yanılgı | Doğurduğu |
|---|---|---|
| 1 | `dotnet-ef` PATH'te sanıldı | araç kontrolü + KARAR VEREMEDİ |
| 2 | tek bağlam sanıldı, `JWT_SECRET` unutuldu | iki bağlam ayrı ayrı |
| 3 | bağlantı `ConnectionStrings__` sanıldı | `DB_CONNECTION` (fabrika okunarak) |
| 4 | koşan betik zulaya alındı | `/tmp` kopyasından koşma |
| 5 | `[Migration]` niteliksiz sabotaj → **yanlış YEŞİL** | **UYGULAMA KANITI** |
| 6 | kopya `postgres` adına açıldı → `42501` | **SAHİPLİK KANITI** |

**BEŞİNCİSİ EN TEHLİKELİSİYDİ.** Düzenek *"bekleyen göç uygulandı"* dedi
ve **hiçbir şey uygulanmamıştı**: betik dosya adına bakıyordu, EF
`[Migration]` niteliğine. Yanlış kırmızı kapıyı öldürür; **yanlış yeşil
kapıyı gereksiz kılar ve kimse fark etmez.**

Ve **pozitif kontrol ilk hâlinde BOŞTU** (`Applying migration: 0`). Bir
göçün uygulanabildiğini hiç kanıtlamamıştı; bunu ancak negatif kontrol
ortaya çıkardı.

### SINIFLANDIRMA ASİMETRİK — MEHMET'İN KARARI

İlk sürüm bir YASAK LİSTESİ tutuyordu. Mehmet reddetti: *"o liste
gözlemle büyüyor ve her büyüme bir yanlış kırmızıyla satın alınıyor."*
Üç koşuda üç kez oldu.

Yeni kural: **"göç patlardı" hükmü POZİTİF OLARAK KANITLANMADIKÇA
verilmez.** Üç kanıttan biri aranır — uygulama aşamasına girildi mi,
PostgreSQL hatası (SQLSTATE) var mı, göç adı çıktıda geçiyor mu. Hiçbiri
yoksa **KARAR VEREMEDİ**.

*"Yanlış kırmızı kapının kendisini öldürür; yanlış 'karar veremedi'
yalnızca bir insanın bakmasını ister. Maliyet farkı asimetrik olduğu için
sınıflandırma da öyle."*

Karar sınavını verdi: yasak listesi olsaydı `42501` (izin) ile `42P07`
(gerçek göç hatası) **aynı kovaya** düşerdi.

### ASIL YERİ ELLE ÇAĞRI

`gocleri_dogrula` bekleyen göç bulunca yayını **zaten durduruyor** —
yani göçler deploy'dan ÖNCE elle uygulanıyor ve prova safe-deploy içinde
neredeyse her zaman boş geçer.

**Provanın işe yaradığı an, göçü elle uygulamadan öncedir.**
safe-deploy'daki çağrı bir ağ: akış değişirse yakalar, maliyeti sıfıra
yakın.

---

## ÖLÜ ÖN YÜZ KOPYALARI TAŞINDI (2026-08-30)

`frontend/` altındaki **yedi kullanılmayan ön yüz kopyası** (2.1 GB,
30-31 Temmuz tarihli, git'te izlenmiyor) `/opt/enderun-olu-kopyalar-
20260830/` dizinine taşındı. Orada bir `README.md` var: tam eski
yollar, taşınma gerekçesi, taşımadan önce yapılan referans kanıtı.

**NEDEN:** depo kökünden yazılan taramaları şişiriyorlardı. Yarım
zincir ölçümü canlı kopyada 64, depo genelinde **218**; FORM/1'in
payload ölçümü 6'ya karşı **48**.

**BUGÜNE KADAR HİÇBİR SAYI YANLIŞ OLMADI** — mevcut cırcırların
tamamı `join(__dirname, "..")` ile canlı kopyaya kapsamlıydı; sekizi
tek tek ölçüldü. Tehlike gelecek taramalardaydı ve sessiz olurdu.

**TAŞIMADAN ÖNCE KANITLANDI:** nginx dosya yolu göstermiyor (hepsi
`proxy_pass`), `next-server` cwd'si `frontend/enderun-ai`, systemd
`WorkingDirectory` aynı, symlink/cron/timer referansı yok. Tek
referans `test-safe-deploy-fastpath.sh` içinde ve **dize olarak** —
saf sınıflandırma fonksiyonuna veriliyor, dosya sistemine
dokunmuyor.

**TAŞIMA SONRASI:** `/api/health` 200, ön yüz 200, `next-server` aynı
dizinden, depo kökünden tarama 218 → **71** (64 üretim + 7 test).

**SİLME 30.09.2026'DAN SONRA** — karar ayrıca verilecek. Git'te
karşılıkları yok; silinirse geri getirilemezler.

## BEKLEYEN PAKET — FORM/1: DÜZENLENEBİLİR ALAN TİPİNİ DTO'DAN TÜRET

**Sorun (Kural 62):** bir alan istek sözleşmesinden çıkarıldığında,
ekrandaki kontrolü sessizce yalan söyleyen bir kutuya dönüşüyor.
Derleyici görmüyor çünkü form durumu ile istek gövdesi ayrı tipler.

**Öneri:** form durumunu ikiye ayır —

| Bölüm | İçerik | Tipi |
|---|---|---|
| GÖSTERİLEN | kod, durum, oluşturma tarihi | serbest |
| DÜZENLENEBİLİR | isteğe giden alanlar | **istek DTO'sundan TÜRETİLİR** |

Böylece DTO'dan bir alan çıktığında o alana bağlı düzenlenebilir
kontrol **DERLENMEZ**. HP/1'deki iki sessiz yalan derleme hatası
olurdu.

**KAPSAM ÖLÇÜLDÜ (2026-08-30):**

| Ölçüm | Sayı |
|---|---|
| DTO tipli payload kuran ekran | **6** |
| Form durumunu ayrı tip olarak tutan ekran | **37** |

Dar uygulama 6 ekran, geniş uygulama 37. HP/1'e sığmıyor.

**BU SINIF TEK SEFERLİK DEĞİL:** her sözleşme daraltmasında yeniden
doğar. Bugün hesap planında oldu; yarın başka bir DTO daraltıldığında
aynı yerden çıkar.

## GÜN KAPANIŞI — 2026-08-28/30

### CANLIYA ÇIKAN ALTI COMMIT, BEŞ YAYIN

| Commit | Paket | Yayın |
|---|---|---|
| `718b220f` | ÖP/1b — ödeme planı ekranları, iki ayrı izin kapısı | 28 Ağu 13:43 |
| `47b984d9` | KABUK — hata sınırı, istemci hata kaydı, yarım zincir cırcırı | 29 Ağu 21:32 |
| `d075eebf` | Test sayısı cırcırı — Kural 55'in mekanik karşılığı | 29 Ağu 22:11 |
| `10ed01b9` | Tarih bağımlı ekstre testi düzeltmesi | 30 Ağu 01:25 |
| `99cae2d8` | JETON/1 — tümleyen kodlama, tek yorumlayıcı, reddetme muhafızı | 30 Ağu 01:25 |
| `2ff2c447` | EKRAN/1 — liste hücresinde ikinci satır ayracı | 30 Ağu 02:06 |

Son iki commit tek yayında çıktı. Beş yayının beşinde de göç kapısı
yeşil verdi, hiçbirinde geri alma çalışmadı.

**SON DURUM:** backend 2915 test, ön yüz 494 test, tsc 0 hata.

### CANLIYI KIRAN İKİ OLAY

#### 1. GİRİŞ DÖNGÜSÜ (30 Ağustos)

**Sebep bizdik.** ÖP/1a'da `payment.plan.approve` Admin'den çıkarıldı
— karar DOĞRUYDU (İ2) ve hâlâ doğru. Görülmeyen, o kararın jeton
boyutuna ne yapacağıydı: "hepsine sahip" kısayolu düştü, 140 izin tek
tek jetona yazıldı, jeton 4394 bayta çıktı, tarayıcı 4096'yı aşan
çerezi SESSİZCE attı.

**Yayın günü hiçbir belirti yoktu.** Arıza, eldeki jetonun 12 saati
dolduğunda ortaya çıktı — GM sisteme giremedi.

Acil çözüm veri düzeyinde oldu (`mehmet`'e Genel Müdür rolü EKLENDİ,
Admin kaldırılmadı): birleşik izin 141/141 = bayrak = küçük jeton.
Kalıcı çözüm JETON/1.

**DÖRT ŞEY BUNU YAKALAYAMADI ve dördü de aynı sınıftan:**

| Nerede | Ne oldu |
|---|---|
| `TokenCookieSizeTests` | gerçek rolleri değil `AllPermissionKeys()` VEKİLİNİ sınıyordu (Kural 58) |
| Tam takım 2865/2865 | yeşildi ama KAPSAMI EKSİKTİ — üstüne yazılan bekçiler koşmuyordu (Kural 55) |
| `Tumleyen_Yoksayilirsa…` | doğru ölçüyordu, SONUCU DAR OKUNDU (Kural 61) |
| Tek-yer muhafızı | `"alan"` arıyordu, `.alan` yazımını görmüyordu — SONDA yakaladı |

Hepsi tek bir cümlenin farklı yüzleri: **ölçtüğünü sandığın şey ile
gerçekte ölçtüğün şeyin ayrışması.**

#### 2. DERLEME BELLEK TÜKENMESİ (28 Ağustos, önceki tur)

Makine sertleştirmesi paketinden önce üç kez bellek tükenmesi yaşandı
ve yetim süreçler kaldı. Çözüm: systemd scope ile tek-örnek kapısı,
cgroup ile süreç ağacının tamamının öldürülmesi, `MemoryMax=6500M` ve
`DOTNET_GCHeapHardLimitPercent`.

**Sınırın kendisi de bir ders verdi:** 3G tahminle konmuştu ve her
yayını kıracaktı; gerçek ihtiyaç ÖLÇÜLDÜ (5,72 GB) ve sınır ona göre
konuldu (Kural 42).

### DOĞRULANMAMIŞ KALEMLER — KODDAN ÇIKMAZ

Bunlar Mehmet'te; hiçbiri testle kapatılamaz.

| Kalem | Neyi kanıtlar |
|---|---|
| **`uakkaya` ile temiz giriş** | JETON/1'in TÜMLEYEN yolu. `mehmet` artık bayrak yolunda, kendi girişi bu paketi KANITLAMAZ |
| **805088'in ekstresi** | muhasebe düzeltme fişi (§5a) |
| **Muhasebecinin bugünkü ödeme listesi yöntemi** | ÖP/1b'nin gerçek işe oturup oturmadığı |
| ÇEK/2 | kapanmış çekte düzeltme; verilen çekte kasa hesapları listede yok |
| Acil yama | `Banka · Garanti Bankası — BANKA-004` |
| KURULUM/1 | departman sil → aynı kodla aç → geçmeli; iki aktif departman aynı kodu alamamalı |
| ÖP/1b | Ön Muhasebe'ye karar düğmesi görünmemeli; bakiye yetmezken uyarı + onay düğmesi yerinde; onaydaki planda "Satır Ekle" yok, "Düzelt" var |
| KABUK | bir ekran çökerse yan menü ayakta kalmalı |
| EKRAN/1 | çek listesinde banka/keşideci ve çek no/belge no İKİ SATIRDA |

**`uakkaya` doğrulaması üç maddeyi birlikte gerektiriyor:** giriş
yapılabiliyor mu (boyut), ödeme ONAY ekranına erişemiyor mu (İ2 —
tümleyen doğru okunuyor), hazırlama ekranına erişebiliyor mu (fazla
kapanmadı). Üçü olmadan paket doğrulanmış sayılmaz.

### AÇIK KALEMLER

- **`mehmet`'te Admin rolü** kalacak mı — karar Mehmet'te, dokunulmadı
- **`FinancialInstrumentTests`'te 10 `Today.AddDays` daha** var; ay
  sınırına duyarlılıkları ÖLÇÜLMEDİ. Bu sınıf kırılganlık yalnız
  belirli takvim günlerinde görünür, yani yayın kapısı çoğu gün geçirir
- **JETON/2'nin tetiği yazılı**: herhangi bir rolün jetonu 3500 baytı
  aşarsa açılır — o an geldiğinde tartışılmaz, başlar
- **Yarım zincir çizgisi**: 28 dosyada 64 yer donduruldu, düzeltme
  bekliyor
- **Sıradaki paketler**: HP/1 (hesap planı + banka/şube referans
  listesi — Faz 0 ölçümü yapılmıştı, yeniden okunacak), SEMA-KAYNAK/1
  → SQUASH/1, INDEKS/1

### BU TURDA EKLENEN KURALLAR

55 (yeni dosya adı boş mu), 56 (yetkilendirme anında görünmez),
57 (izin çıkarmak eklemekten tehlikeli), 58 (elle kurulmuş girdi
kapsamı dondurur), 59 (ilk gözlem gerçek kusura karşı), 60 (anlamayan
okuyucu kapalı tarafa düşer), 61 (sonda raporu yeşilleri de taşır).

## EKRAN/1 — LİSTE HÜCRESİNDE İKİNCİ SATIR (2026-08-30)

### AYRAÇSIZ BİRLEŞME — 20 EKRANDA, TEK SEBEPTEN

Canlıda görülen (Mehmet, tarayıcı):

    "HALKBANKFIRAT LIFE"                 (banka adı + keşideci)
    "HALKBANKFIRAT LIFE YATIRIM İNŞAAT…"
    "C1 1796766ACK-2026-000005"          (çek no + belge no)

İki ayrı bilgi tek kelimeymiş gibi okunuyordu.

**KÖK NEDEN:** sütunlar ikinci bilgiyi `<small>` ile yazıyor.
`.erp-table td small { display: block }` VARDI; DataTable'ın
kullandığı `.erp-data-table-grid` için **YOKTU**. `<small>` satır
içi kalıyordu.

İşaretleme doğruydu, eksik olan stildi — bu yüzden hiçbir render
testi yakalayamazdı.

**KAPSAM: 20 ekran** (DataTable + `render` içinde `<small>`).
En yoğunları: satış faturaları 5, faturalar 5, İSG belgeler 5,
yevmiye 4, kur değerlemesi 4.

### DÜZELTME TEK KAYNAKTAN

`app/globals.css` içine tek kural. 20 ekrana ayrı ayrı yazmak, 20
kopyanın zamanla ayrışması demekti.

**YALNIZ `<small>` ALINDI.** `.erp-table` ayrıca `span` ve `strong`u
da bloklıyor, ama `.erp-data-table-grid tbody td span` özgüllüğü
(0,3,3) `.erp-status` (0,1,0) kuralını EZER ve DataTable içindeki her
durum rozeti tam satır kaplardı — düzeltilenden büyük bir hata.

### SONDA

`display: block` kaldırıldı → `.erp-data-table-grid` testi kırmızı,
`.erp-table` testi **YEŞİL KALDI**. İkisi birden kırmızı olsaydı test
sınıfları ayırt etmiyor demekti (Kural 61).

### CSS SÖZLEŞMESİ, RENDER TESTİ DEĞİL

Kural harici bir stil dosyasında ve jsdom onu uygulamıyor; render
testi kuralın varlığını ölçemez, yalnız işaretlemeyi ölçer.
İşaretleme zaten doğruydu.

### CHECKBOX ADI — ÖLÇÜLDÜ, KUSUR YOK

"İptalleri göster" / "Kapanmışları göster" kutularının erişilebilir
adının `"on"` göründüğü bildirildi. Ölçüldü: `<label>` sarmalı
checkbox'ın erişilebilir adı **doğru üretiliyor** (erişilebilir ad
hesabıyla doğrulandı, `aria-label` niteliği null — beklenen bu).

`"on"`, checkbox'ın `value` niteliği yokken varsayılan DEĞERİ; aracın
adı değil değeri göstermesi. Süzgeç bulgusunun geri çekilmesiyle aynı
sınıf.

**KOD DEĞİŞTİRİLMEDİ.** Ölçülmemiş bir soruna karşı "her ihtimale
karşı" `aria-label` eklemek, bu programın disiplininin tersi olurdu —
ve var olmayan bir kusura muhafız koymak, muhafızın neyi koruduğunu
belirsizleştirir.

## TARİH BAĞIMLI TEST — AYIN 30'UNDA KIRILIYORDU (2026-08-30)

`FinancialInstrumentTests.Statement_ProducesOneCashOutflowForThePeriod`
JETON/1'in tam takım koşusunda kırmızı verdi. **JETON/1 ile ilgisi
yoktu** — yalıtılmış koşuda da kırmızıydı, yani kirlenme de değildi.

**KARARSIZ DEĞİL, TARİH BAĞIMLI.** Aradaki fark önemli: kararsız test
rastgele kırılır, bu ayın 30'unda **kesin** kırılır.

| Gün | Harcama tarihleri | Ekstre sayısı |
|---|---|---|
| 29 Ağustos | 31 Ağu, 1 Eyl | ikisi de Eylül ekstresinde → **1** ✓ |
| 30 Ağustos | 1 Eyl, 2 Eyl | 1 Eyl → Eylül, 2 Eyl → **Ekim** → **2** ✗ |

**ÜRETİM KODU DOĞRUYDU.** `CreditCardService.CutDateFor` "kesim
gününde yapılan harcama o ekstreye girer" diyor ve bunu doğru
uyguluyor. Yanlış olan testin varsayımıydı: *"Today+2 ile Today+3 aynı
dönemdedir"* — takvime bağlı bir varsayım ve **ayda bir gün yanlış**.

Tarihler gelecek ayın 5-6'sına sabitlendi. Kesim günü 1 olduğunda
dönem ayın 2'sinden sonraki ayın 1'ine sürüyor; 5 ve 6 hangi gün
koşulursa koşulsun dönemin ortasında kalıyor.

**AYNI DOSYADA 10 `Today.AddDays` DAHA VAR** (kredi taksitleri, gider
girişleri). Ay sınırına duyarlı olup olmadıkları **ÖLÇÜLMEDİ** — ayrı
kalem olarak bekliyor. Bu tür bir kırılganlık yalnız belirli
takvim günlerinde görünür, yani yayın kapısı onu çoğu gün geçirir.

## SÜZGEÇ BULGUSU GERİ ÇEKİLDİ (2026-08-29)

`/finans/cekler` süzgeç kontrollerinin etkileşimli öğe olmadığı
bulgusu **YANLIŞTI.** Erişilebilirlik ağacı varsayılan derinlikle
okundu, kontroller o derinliğin altında kaldı, **eksik sonuç yokluk
sayıldı** — Kural 48'in ihlali, olumlu denetim yapılmadı.

Kontroller yerel `<select>`, `<input type=checkbox>` ve textbox
olarak mevcut; süzgeç çubuğu koşulsuz render ediliyor ve önünde erken
çıkış yok (kaynakta ölçüldü).

20 ekranlık tarama **yazılmadı**.

## ÇEK/1 CANLIDA DOĞRULANDI (2026-08-29, gözlem)

| Görünüm | Ölçüm |
|---|---|
| Varsayılan (Verilen çekler) | 16 çek · "Açık çekler toplamı: 21.392.358,18 ₺" |
| | Ağustos 2026 grubu **yok**, 805088 listede **değil** ✓ |
| Süzgeç = Ödendi | 3 çek · "Toplam (Ödendi): 3.327.488,00 ₺" |
| | 805088 **görünüyor** ✓ |

Etiket parantezli biçimde çalışıyor (Kural 37).

## BOŞ ALAN GÖRÜLDÜĞÜNDE ÖNCE YAZMA YOLU ARANIR (Mehmet onayı, 2026-09-03)

> **BİR ALANIN MODELDE OLMASI, ONU DOLDURAN BİR YOLUN OLDUĞU ANLAMINA
> GELMEZ. BOŞ ALAN GÖRÜLDÜĞÜNDE ÖNCE YAZMA YOLU ARANIR.**

DOĞURAN OLAY (İŞEMRİ/2 Faz 2 KAPI 1, ölçüldü): `Personnel.DepartmentId`
canlıda **79 aktif personelin 0'ında** dolu. İlk okuma — benim de,
Mehmet'in de ilk okuması — *"veri girilmemiş"* oldu. Ölçüm başka bir
şey gösterdi:

**Kod tabanında `Personnel.DepartmentId`'ye yazan hiçbir yol yok.**
`DepartmentId = …` biçimindeki tüm eşleşmeler `HrPosition.DepartmentId`
(pozisyon→departman). Personelin departmanını yazan bir uç, bir servis,
bir ekran hiç yazılmamış.

Yani 0/79 bir **veri girme ihmali değil**, bir **eksik yazma yolu**.
Alanı doldurmak için "birinin oturup girmesi" yetmez; önce alanı
yazabilen bir mekanizma gerekir.

NEDEN ÖNEMLİ: iki teşhis birbirine hiç benzemeyen iki iş üretir.
"Veri girilmemiş" teşhisi bir HATIRLATMA üretir (birine söyle,
doldursun) ve o hatırlatma sonsuza kadar karşılıksız kalır. "Yazma yolu
yok" teşhisi bir PAKET üretir. Yanlış teşhisle geçen her gün, hiç
gelmeyecek bir veriyi beklemekle geçer.

BUNUN AYNISI DAHA ÖNCE YAŞANDI, TERSİNDEN: `MANUAL` kaçışı için
*"bugün kullanılmıyor"* denmişti; ekran onu kullanmaya başladığı anda
kullanılır oldu. Orada yokluğu kalıcı sandık, burada varlığı. İkisinin
ortak dersi: **alanın durumu hakkında çıkarım yapma, yazma yolunu
ÖLÇ.**

MEKANİK KARŞILIĞI YOK — bu bir teşhis alışkanlığı, bir bekçi değil.
Uygulanışı şu: boş bir alan raporlanmadan önce
`grep -rn "AlanAdi\s*=" --include=*.cs` koşulur ve sonucu rapora
yazılır.

---

## TEST SAYISI CIRCIRI — KURAL 55'İN MEKANİK KARŞILIĞI (2026-08-29)

**Kural 55'in mekanik karşılığı test sayısı cırcırıdır. Kural akılda
değil, çizgide durur.**

ÖP/1b'de bir test dosyasının üstüne yazıldı ve ÖP/1a'nın altı test
niteliği silindi; tam takım 2865/2865 YEŞİL verdi. O gün yakalatan
şey `git status` çıktısındaki `M` harfiydi — **şans, düzenek değil**.

### BİLDİRİM SAYILIYOR, ÇÖZÜLMÜŞ TEST DEĞİL

Çözülmüş sayı yalnız takım koşarken bilinir; bir test dosyasından
okunamaz. Sayaç bildirimleri sayıyor ve bu, aranan şeyi tam
karşılıyor: bir durum silinirse sayı düşer.

| Eksen | Arka uç | Ön yüz |
|---|---|---|
| statik | `[Fact]` + `[InlineData]` = **2756** | `it(` / `test(` = **367** |
| dinamik | `[MemberData]`/`[ClassData]` = **22** | `it.each(` = **11** |

**İKİ EKSEN ŞART**: `[MemberData]` taşıyan bir teori tek satırdır ama
çalışma anında 148 durum üretiyor (2756 statik ≠ 2874 gerçek). Tek
sayıda toplansaydı o teorinin silinmesi "1 düşüş" gibi görünür,
gürültüde kaybolurdu.

### NEDEN ÖN YÜZ TAKIMINDA

Tek sayaç, iki taraf. İki ayrı sayaç yazılsaydı zamanla ayrışırlardı:
biri `[Theory]` sayımını düzeltir, diğeri eski hâlinde kalırdı.
`endpoint-guard.test.ts` zaten backend kaynağını okuyor; desen yeni
değil.

Bedeli: arka uçtan test silen kişi kırmızıyı BAŞKA BİR TAKIMDAN alır.
Hata mesajı bu yüzden dört soruyu birden cevaplıyor — hangi taraf,
kaçtan kaça hangi eksende, cırcır neden burada, düşüş meşruysa ne
yapılacağı. Yoksa yarım saat yanlış yerde aranır.

### İKİ SONDA — BİRİ KUSURU, DİĞERİ SINIRI GÖSTERDİ

| Sonda | Beklenen | Sonuç |
|---|---|---|
| Arka uçtan test dosyası silindi | KIRMIZI | **KIRMIZI** — "arka uç · statik 2756 → 2751" |
| Geri alındı | YEŞİL | 3/3 yeşil |
| Çizgi elle 2756 → 2600 düşürüldü | **YEŞİL kalmalı** | **3/3 yeşil** |
| Sayaç backend yolunu bulamıyor | KIRMIZI | **KIRMIZI** — pozitif kontrol + sayı testi |

Üçüncü sonda kanıtsız bir iddiayı kapatmak için koşuldu: "sayaç
bozulursa pozitif kontrol yakalar" denmişti ama ÖLÇÜLMEMİŞTİ. Yol
bozulduğunda `dosyalar()` boş dizi döndürüyor ve iki test birden
kırmızı veriyor — sayaç sessizce boşa düşemiyor (Kural 48).

### YAKALAYAMADIĞI ŞEY — ÖLÇÜLDÜ, GİZLENMİYOR

**Çizginin sessizce DÜŞÜRÜLMESİNİ yakalayamaz.** Sonda koşuldu:
çizgi 2756'dan 2600'e çekildi, pozitif kontrol (>2000) sorunsuz
geçti, cırcır sustu.

Bu bir eksik değil, yapısal bir sınır: **düşürülmüş bir çizgi ile
meşru birleştirme sonrası çizgi mekanik olarak AYIRT EDİLEMEZ.**

Pozitif kontrolün eşiğini gerçek sayıya yaklaştırmak da çözüm değil —
o zaman her meşru birleştirme eşiği de güncellemeyi gerektirir ve
eşik İKİNCİ BİR ÇİZGİYE dönüşür; aynı delik ikinci kez açılmış olur.

Koruma bu yüzden usule ait:

> **ÇİZGİ YUKARI SERBESTÇE GÜNCELLENİR. AŞAĞI HAREKET AYRI BİR
> COMMIT'TE, GEREKÇESİ COMMIT MESAJINDA YAZILI OLARAK YAPILIR.**

Korumanın değeri sayının kendisi değil, **düşüşün bir KONUŞMAYA
dönüşmesidir**. Kapsam cırcırının "yalnız küçülebilir" kuralının
tersi: bu yalnız büyüyebilir.

### BİRLEŞTİRME MEŞRUDUR — CIRCIR İYİLEŞTİRMEYİ ENGELLEMEZ

KURULUM/1'de 23 ayrı test yerine tek parametreli test yazılması
istendi ve doğrusu buydu. Bu cırcır altında o iyi değişiklik sayıyı
23'ten 1'e düşürür ve kırmızı verir.

Bu yüzden hem belgeye hem hata mesajına açıkça yazıldı: **çizgiyi
düşürmek ucuz, normal ve kayıtlı bir işlemdir — savunulması gereken
bir şey değildir.** Bu cırcır testin SİLİNMESİNİ fark ettirmek için
vardır; testin İYİLEŞTİRİLMESİNİ engellemek için değil.

Kural 42'nin doğrudan sonucu: insanı doğru işi yapmaktan caydıran bir
kural, ya susturulur ya da kötü kodu teşvik eder.

## KABUK DAYANIKLILIĞI (2026-08-28)

### BAŞLANGIÇ DURUMU — ÖLÇÜLDÜ

Uygulamada **hiçbir hata sınırı yoktu**: ne `app/error.tsx`, ne
`componentDidCatch`, ne de bir istemci hata kaydı ucu. React render
sırasında hata alan ağacı KÖKÜNDEN söküyor; yakalayan olmayınca
geriye boş bir `<div>` kalıyor. Yani bugüne kadar **herhangi bir
bileşendeki herhangi bir hata = beyaz ekran**, ve kimsenin haberi
olmuyordu.

### İKİ KATMANLI SINIR — NEDEN İKİ

İlk tasarımım tek katmandı ve YANLIŞTI: sınır yalnız `{children}`ı
sarsaydı kabuğun KENDİ kodundaki bir hata yakalanmazdı. Kabuk her
ekranı sardığı için orada bir çöküş, açık kalan tek bir sayfa bile
bırakmıyor.

| Katman | Neyi sarar | Çöküşte ne olur |
|---|---|---|
| Dış | `ErpShellIc`in tamamı | Tam sayfa hata ekranı |
| İç | yalnız `{children}` | Yan menü/arama/kimlik AYAKTA, içerik yerine hata ekranı |

Tek katman olsaydı bir raporun hatası bütün gezinmeyi de götürürdü.
İç sınır önce yakalar; dıştaki yalnız kabuğun kendi çöküşünde
devreye girer.

**Sınıf bileşeni zorunlu**: `getDerivedStateFromError` ve
`componentDidCatch` kancalarla yazılamıyor. React'in kendi sınırı,
tercih değil.

**NE YAKALAMAZ** (sonradan "neden çalışmadı" denmesin): olay
işleyicileri, `setTimeout`, sunucu tarafı render, ve sınırın KENDİ
render'ı. Olay işleyicileri zaten `try/catch` ile ekranda hata
gösteriyor.

### KAYIT — KİŞİSEL VERİ KURALINA UYGUN

`POST /api/istemci-hatalari`, `[Authorize]`, ayrı izin anahtarı YOK
(her giriş yapmış kullanıcı kendi ekranında hata alabilir ve
bildirebilmelidir; anonime açık bir günlük yazma ucu ise günlüğü
şişirmenin en kolay yolu olurdu).

Kabul edilen alanlar **beyaz liste** — serbest nesne alınsaydı
istemci günlüğe tutar, IBAN, cari unvanı yazdırabilirdi:

| Alan | Sınır |
|---|---|
| `nerede` | 40 karakter ("kabuk" / "içerik") |
| `hataAdi` | 80 karakter |
| `mesaj` | 200 karakter — **iki tarafta da** kısaltılıyor |
| `yol` | 300 karakter, sunucuda `SensitivePathMasker` ile maskeleniyor |

- **Kullanıcı kimliği oturumdan, istekten DEĞİL.** İstemcinin
  gönderdiğine güvenilseydi biri başkasının adına kayıt ürettirebilirdi.
- **Bileşen yığını gönderilmiyor** — yalnız tarayıcı konsoluna.
- **Mesaj iki tarafta da kısaltılıyor**: istemci kısaltması
  atlatılabilir, sunucu ona güvenmiyor.
- **Yol maskeleme tek kaynaktan**: `GlobalExceptionHandler` ile aynı
  `SensitivePathMasker`. İkinci bir maskeleme yazılsaydı biri
  güncellenip diğeri kalırdı.
- Bir test bunu doğrudan ölçüyor: taklit oturumdaki kullanıcı adının
  bildirimin hiçbir alanında geçmediğini iddia ediyor.

**VERİTABANI TABLOSU YOK — BİLEREK.** Tablo göç demektir ve bu bir
sertleştirme yaması. Ayrıca istemciden TETİKLENEN bir kaydın tabloya
yazılması, bozuk ya da kötü niyetli bir istemcinin veritabanını
şişirmesine yol açardı.

### BİLDİRİM SESSİZ BAŞARISIZ OLUR

Kullanıcı zaten hata ekranına bakıyor. Bildirim de patlarsa (ağ yok,
oturum düşmüş) ikinci bir hata fırlatmak sınırın kendisini döngüye
sokar: `componentDidCatch` içinden atılan hata YAKALANMAZ, ağacı
tekrar söker. `void` ile ayrılıyor — beklenseydi hata ekranı bir ağ
turu kadar geç görünürdü.

### SÜPÜRME — SAYILDI, DONDURULDU, DÜZELTİLMEDİ

`a?.b.c` / `a?.b[0]` deseni: dış nesne korunmuş, **iç alan
korumasız**. Yazan "a henüz yüklenmemiş olabilir" diye düşünmüş,
"a geldi ama b gelmemiş olabilir" diye düşünmemiş.

**TypeScript bunu yakalamaz ve yakalamaması doğru**: tip `b`yi
zorunlu ilan ediyorsa derleyicinin şüphelenmesi için sebep yok. Kusur
TİPİN KENDİSİNDE — sunucu sözleşmesi ile tip tanımı ayrıştığında tip
yalan söyler.

**28 dosyada 64 yer.** `tests/bekci/yarim-zincir-cizgi.txt` içinde
donduruldu; `tests/yarim-zincir-ratchet.test.ts` dört testle koruyor:
tarama boşa düşmüyor (pozitif kontrol), sayı çizgiyi aşmıyor,
düzeltilen dosya çizgiden silinir, ve kabuk ayrıca ayrı sınanıyor
(orada bir geri adım diğerlerinden ağır).

Düzeltme bu turda YOK — hepsi aynı ölçüde riskli değil, bir kısmı
`Promise.all` çıktısı gibi yapı gereği güvenli. Tek turda hepsini
değiştirmek, gerçek riskli olanları gürültünün içinde kaybederdi.

ÖP/1b'nin kendi yeni sayfası da bu desenden bir tane taşıyor
(`data?.detay.butce`) ve çizgiye yazıldı — kendine muafiyet yok.

**HATA SINIRI BU CIRCIRIN YERİNE GEÇMEZ.** Sınır çöküşü EKRANA
çeviriyor, çöküşü ortadan kaldırmıyor. İki ayrı katman.

### İKİ SONDA

| Sonda | Kırmızıya dönen |
|---|---|
| Dış katman kaldırıldı | `kabuğun kendi çöküşü hata ekranına düşer, ekran BOŞ KALMAZ` + `çöküş kayda bildirilir` |
| `roles?.[0]` geri alındı | süpürme cırcırının İKİSİ birden (toplam + kabuk özel testi) |

**Sonda A'nın asıl kazancı**: yalıtılmış hata sınırı testleri YEŞİL
kaldı. Yani "sınır çalışıyor" ile "sınır kabuğa doğru bağlanmış" iki
ayrı şey; bağlantı koparsa sınır hâlâ çalışıyor görünür. Bu yüzden
`tests/kabuk-dayanikliligi.test.tsx` GERÇEK `ErpShell`i render ediyor
ve çöküşü `{children}`ın DIŞINDAN (`NotificationBell`) veriyor —
yalnız dış katmanın yakalayabileceği yerden.

### GEÇMİŞTEKİ İDDİAMIN DÜZELTMESİ

ÖP/1b sırasında "`roles` alanı olmayan bir kullanıcı gelirse tüm
uygulama beyaz ekrana düşer" dedim. **Gördüğüm kanıttan daha güçlü
bir iddiaydı**: çökmeyi üreten, test taklidimin bilinmeyen yollara
`[]` dönmesiydi — kabuk oturumu `auth/me` ile çektiği için
`currentUser` bir DİZİ oldu. Canlı sözleşmenin böyle davrandığına
dair ölçümüm yoktu ve hâlâ yok.

Kırılganlık yine de gerçek, ama gerekçesi "canlıda patlıyor" değil
**"tip yalan söyleyebilir ve kabuk tek bir alan yüzünden komple
çökebiliyor"**. Doğru çözüm de bu yüzden tek bir `?.` değil, hata
sınırıdır.

### ÜÇÜNCÜ SONDA — SÖZLEŞMENİN KENDİSİ YAKALADI

Kabuk iki parçaya bölününce (`ErpShell` sarmalayıcı + gövde) ön yüz
takımı `redwood-contract`'ı düşürdü: sözleşme kabuk açılışlarını
`<ErpShell` ÖNEKİNE bakarak sayıyor ve gövdenin ilk adı `ErpShellIc`
o önekle eşleşiyordu. Kabuk, kendi içinde bayraksız bir ekran
açıyormuş gibi göründü.

**Sözleşme gevşetilmedi — ad düzeltildi** (`KabukGovdesi`). Zaten
doğrusu da buydu: bu bileşen bir kabuk değil, kabuğun gövdesi.

Arkasından ikinci bir tuzak: düzeltmeyi anlatan YORUMUN İÇİNE
`<ErpShell` yazmıştım ve sayaç yorum ile kodu ayırmıyor. Bu oturumda
aynı sınıftan üçüncü hata (önce `FOR UPDATE` yorumda, sonra hata
mesajı metninde). Yorum, öneki yazmayacak şekilde düzeltildi.

### SAYILAR

| Ölçüm | Sonuç |
|---|---|
| Backend takımı | 2874/2874 |
| Ön yüz takımı | **485/485** (474 → +11) |
| TypeScript | 0 hata |
| Yarım zincir | 28 dosya / 64 yer, donduruldu |

## ÖP/1b — ÖDEME PLANI EKRANLARI (2026-08-28)

### TEK EKRAN, ÜÇ KİP

E2 (hazırlama), E3 (onay) ve E4 (uygulama) **ayrı yollara
bölünmedi**. Plan tek bir nesne ve haftanın içinde durumdan duruma
geçiyor; ayrı adresler olsaydı kullanıcının "bu hafta hangi adreste"
diye bilmesi gerekirdi. Daha somut engel: D1 gereği onaydaki bir
planda satır düzeltilebiliyor — ayrı ekranlarda bu, hazırlama
ekranına geri dönmeyi gerektirirdi.

Kip **durumdan ve izinden birlikte** çıkar:

| Kip | Koşul |
|---|---|
| Ekleme/silme (D2) | `payment.plan.prepare` **ve** Durum = Taslak |
| Düzenleme (D1) | `payment.plan.prepare` **ve** Durum ≠ Kapandı |
| Karar (E3) | `payment.plan.approve` **ve** Durum = Onayda |
| Ödeme kaydı (E4) | `payment.plan.prepare` **ve** Durum ∈ {Onaylandı, Uygulandı} |

### İKİ KAPI AYRI AYRI SINANDI

Bunlar **bilerek iki ayrı testte**:

- **Ekran görünürlüğü** — `tests/odeme-plani-ekran.test.tsx`, yol izni
  üzerinden. `/finans/odeme-planlari` iki anahtardan biriyle açılır
  (`payment.plan.prepare` VEYA `payment.plan.approve`). Tek anahtara
  bağlansaydı planı onaylayacak kişi kendi onay ekranını açamazdı.
  Kural genel `/finans` kuralından **ÖNCE** duruyor; sonra kalsaydı
  ekran `finance.view` olan herkese açılırdı.
- **Uçtaki 403** — `OdemePlaniUcIzinTests.cs`. Ön Muhasebe ve Finans
  Sorumlusu karar ucundan 403 alır; GM almaz.
- **Katalog düzeyi** — `OdemePlaniIzinTests.cs` (ÖP/1a'dan). "Hangi
  rol hangi anahtarı taşıyor" sorusunu `RoleCatalog` üzerinden
  sorar. Bu üçüncü dosya bilerek ayrı: katalog doğru olup uçtaki
  attribute unutulsaydı oradaki testler yeşil kalırdı (bkz.
  Kural 55 — bu dosya ÖP/1b sırasında bir kez üstüne yazılıp geri
  alındı).

Tek testte birleştirilseydi, arayüz kapısı bir gün kaldırıldığında
sunucu kapısının hâlâ durup durmadığı görülmezdi — yeşil yanlış
yerden gelirdi.

**Olumlu kontrol var:** "Ön Muhasebe planı OKUYABİLİR" ve "GM karar
ucunda 403 ALMAZ" testleri, 403'lerin "izin yok"tan geldiğini, "rol
hiç yaratılmamış"tan gelmediğini kanıtlıyor.

### DÖRT SONDA — HEPSİ KAPANDI

| Sonda | Kırmızıya dönen | Kapsam |
|---|---|---|
| D2 kapısı kaldırıldı (backend) | `D2_OnaydakiPlana_SatirEklenemez`, `D2_OnaydakiPlandan_SatirSilinemez` | 64 testten 2'si |
| Onay ekranı Ön Muhasebe'ye açıldı | `yalnız hazırlama izniyle karar düğmeleri görünmez` | 11 testten 1'i |
| K9 uyarısı kaldırıldı | `fark eksiyse uyarı görünür ve onay düğmesi kalır` | 11 testten 1'i |
| K6'nın iki sayısı toplandı | `iki sayının TOPLAMI hiçbir yerde görünmez` | 11 testten 1'i |
| Karar ucu Prepare iznine düşürüldü | `OnMuhasebe_KararUcuna_403_Alir`, `FinansSorumlusu_KararUcuna_403_Alir` | `OdemePlaniUcIzinTests` 4 testten 2'si |

**D2 sondasının asıl kazancı D1 testlerinin YEŞİL KALMASIYDI.** Tek
kapıya bağlı olsalardı sabotaj beşini birden kırardı — bu da
düzenlemenin de kapandığı, yani K2'nin "değişiklik onayı düşürür"
yarısının ölü koda döndüğü anlamına gelirdi.

### K6 EKRANDA — TOPLAM YOK

İki tablo ayrı: "Bu Cuma Çıkacak Nakit" (hesap bazında) ve "Bu Hafta
Yaratılan Gelecek Yükümlülük" (vade ayına göre). **Hiçbir yerde
"haftanın toplamı" satırı yok** ve test bunu doğrudan ölçüyor: nakit
40.000, çek 25.000 seçilmiş; ekranda `65.000,00` aranıyor ve
bulunmaması bekleniyor. Değerler eşit seçilseydi toplayan bir hata
"iki sayıdan biri" gibi görünüp testten kaçardı.

### K9 — UYARIR, ENGELLEMEZ (D4)

Fark eksiyse ekran açıkça söylüyor ama onay düğmesi yerinde kalıyor;
test ikisini birden iddia ediyor. **Bütçe her karardan sonra
sunucudan YENİDEN isteniyor**, istemcide toplanmıyor — istemcide
toplansaydı K3'ün geçici retleri, eşzamanlı başka bir karar ya da
K2'nin düşürdüğü onay ekrana yansımaz, GM bayat bir farka bakarak
onay verirdi.

### CARİ SATIRDA SABİT — SONUCU AÇIKÇA YAZILDI

`SatirGuncelleAsync` cariyi parametre olarak **almıyor**; cari satır
açılışında sabitleniyor. D2 ile birleşince şu çıkıyor: plan onaya
sunulduktan sonra bir satırın carisi **hiç** değiştirilemez — satır
silinemediği için yeniden de açılamaz. Ekran bunu gizlemiyor:
taslakta silinip yeniden açılır, onaydaysa satır reddedilir ve ödeme
plan dışı (K5) olarak kaydedilir.

### ÜÇ CIRCIR DÜŞTÜ — CIRCIRLAR DEĞİL, KOD DÜZELTİLDİ

Ekranların ilk sürümü frontend suite'inde **üç cırcırı** birden
düşürdü. Üçünde de çizgi yükseltilmedi (Kural 33):

| Cırcır | İhlal | Ne yapıldı |
|---|---|---|
| `lint cırcırı` (`set-state-in-effect`) | 2 (her sayfada 1) | Sayfalar `lib/data/use-refreshable` kancasına taşındı |
| `liste bileşeni cırcırı` | E1 ham `<table>` yazıyordu | E1 `components/ui/data-table.tsx`e taşındı |
| `servis çağrısı bekçisi` | `${KOK}/...` hesaplanmış önek | Yol sabit yazıldı, değişken yalnız segment |

**`useRefreshable` ilk kez kullanıldı.** Kanca "arayüzün TEK tazeleme
mekanizması" olarak yazılmıştı ama **sıfır çağrı yeri** vardı; lint
çizgisinin 110 dosya taşımasının sebebi de bu. Bu iki ekran çizgiyi
yükseltmiyor.

Kancanın bir sınırı ölçüldü: **parametre değişince yeniden çekmiyor**
(fetcher ref'te sabitleniyor, `run` yalnız `enabled`'a bağlı). E1'de
şirket seçimi bu yüzden durumda değil **ref'te**; tazeleme olay
işleyicisinden `refresh()` ile tetikleniyor. Seçim durumda tutulup bir
efektle tazelenseydi kaçınılan ihlal geri gelirdi.

### YAN BULGU — KABUK ÇÖKMESİ (ÖP/1b DIŞI, AYRI PAKETE DEVREDİLDİ)

`components/erp/erp-shell.tsx:503` içinde `currentUser?.roles[0]`
duruyor: isteğe bağlı zincir `currentUser`'da kesiliyor, `roles`
korumasız. `roles` alanı olmayan bir kullanıcı gelirse **tek bir
sayfa değil, uygulamanın tamamı** beyaz ekrana düşer — kabuk her
ekranı sarıyor.

BULUNMA BİÇİMİ VE SINIRI — ABARTILMASIN. ÖP/1b ekran testinin
`apiClient` taklidi bilinmeyen her yola `[]` dönüyordu; kabuk
oturumu `auth/me` ile çektiği için `currentUser` bir DİZİ oldu,
`[].roles` undefined kaldı ve `undefined[0]` patladı. Yani çökmeyi
üreten **taklidin şekliydi**, canlı sözleşme değil. Taklit
düzeltildi (`auth/me` gerçek şekli dönüyor) ve testler kaynak
düzeltmesi OLMADAN yeşil.

Kırılganlık yine de gerçek: tip `roles`u "her zaman var" sayıyor,
bozuk ya da beklenmedik bir yanıt şekli tüm kabuğu düşürür. Ama bu,
"canlıda olacak" değil "kabuğun tek bir alan yüzünden komple
çökebilmesi" sorunudur — çözümü tek bir `?.` değil, HATA SINIRIDIR.

**Kaynak düzeltmesi bu pakete DAHİL DEĞİL.**

Düzeltme KABUK DAYANIKLILIĞI paketine devredildi: tek karakterlik bir
koruma, uygulamanın tamamını saran bileşene dokunuyor ve ödeme planı
paketiyle birlikte çıkması hangi değişikliğin ne kırdığını
belirsizleştirirdi. O paket ayrıca hata sınırı, hata kaydı ve yarım
isteğe bağlı zincirlerin süpürülmesini taşıyor.

## ÖP/1a — HAFTALIK ÖDEME PLANI (2026-08-27)

### NEDEN CARİ BAZLI

On bir kontrolün hiçbiri fatura ayrıntısına bağlı değil. Satırda
`SupplierInvoiceId` alanı AÇIK ama ZORUNLU DEĞİL (Y3): ileride fatura
bazlı takip gelirse satır faturaya bağlanır, **kurallar değişmez**.

**SİSTEM PLANI KENDİ ÖNERMEZ.** Listeyi muhasebeci kurar. Tek istisna
gelecek hafta vadesi dolan çekler — çekte vade verisi sağlam.

### K2 — PAKETİN OMURGASI

Onaylandığında satırın **onaylanan değerleri ayrıca saklanıyor**:
cari, tutar, yöntem, çek vadesi, **öncelik**, çıkış hesabı. Ödeme
anında güncel değerler bunlarla karşılaştırılıyor; fark varsa ödeme
**yapılmıyor** ve satır yeniden onaya dönüyor.

**Onaydan sonra tutarı değiştirilebilen bir sistemde onay hiçbir şey
ifade etmez.**

ÖNCELİK NEDEN DAHİL (K7): para kısıtlıyken sırayı değiştirmek, kimin
parasını alacağını değiştirmektir — biçim değil, **ödeme kararıdır**.

ÇEK VADESİ **GÜN BAZINDA** karşılaştırılıyor: aynı günün farklı saati
karar değişikliği değildir. Saat farkı yüzünden onay düşürmek kuralı
gürültüye boğar ve susturulmasına yol açardı (Kural 42).

### B1/B2 — PLAN GÖSTERDİĞİ BAKİYEYİ SAKLAR

**ÖLÇÜLDÜ: banka bakiyesi bu sistemde SAKLANMIYOR**, hareketlerden
anlık türetiliyor (`OpeningBalance + girişler − çıkışlar`,
`CashAccountsController.cs:95`). Dolayısıyla "onay anındaki bakiye"
diye bir kayıt yoktu.

Onay bir sayıya bakılarak verilen karardır; yeniden kurulamayan bir
onay **denetlenebilir değildir**. Plan artık gösterdiği bakiyeyi
hesap bazında saklıyor. Bu, K9'un iki hâlini tek mekanizmada
birleştiriyor: bakiye ister hesaplansın ister elle girilsin, plan
GÖSTERİLENİ saklar.

Yeniden hesaplama **açık istekle** oluyor (B2) — ekran her açılışta
bütün hareketleri taramıyor.

### İ2 — ONAY ANAHTARI ADMIN'E DE GİTMİYOR

`RoleCatalog` izinleri **yansımayla** dağıtıyor; kataloğa eklenen her
anahtar rollere sessizce geçiyordu. Hassas kümeye almak da yetmiyordu:
eski `KWithSensitive = [.. K, .. SensitiveKeys]` hassas anahtarları
**Admin'e DE** veriyordu.

Mekanizma değişti — **tek kavram korunarak**: toptan küme kaldırıldı,
her rol aldığı hassas anahtarı KENDİ listesinde gösteriyor.

```
AdminKeys      = [.. K, ChequeEdit, ChequeVoidClosed]
GenelMudurKeys = [.. AdminKeys, PaymentPlanApprove]
```

Yeni hassas anahtar artık hiçbir role kendiliğinden gitmiyor;
unutulursa kimse alamaz — sessiz genişlemenin tersi.

**K4 KAPIDA DEĞİL KODDA:** "hazırlayan kendi satırını onaylayamaz"
izinle çözülemez, çünkü GM hem hazırlayabilir hem onaylayabilir.
Engellenen şey AYNI SATIRDA ikisini birden yapmak. **Satırı son
değiştiren de hazırlayan sayılıyor** — yoksa kural "hazırla,
başkasına onaylat, sonra değiştir" ile atlatılırdı.

### D1 — HAFTALIK TETİKLEYİCİ ELLE DEĞİL

**ÖLÇÜLDÜ:** `DailySummaryBackgroundService` deseni zaten vardı
(her gün 04:00 UTC), yalnız haftalık varyantı yazılmamıştı. Pazartesi
05:00 UTC'de çalışıyor.

`SonrakiTuraKalan` **saf ve `public`**: zamanı içeriden okuyan bir
hesap ancak pazartesi sabahı sınanabilirdi — yani hiç sınanmazdı.

**OTOMATİK TASLAKTA `HazirlayanUserId` BOŞ.** Bir "sistem kullanıcısı"
uydurmak, K4'ün hazırlayan tarafını bir hayalete bağlamak ve kuralı
fiilen boşaltmak olurdu (o hayalet kimse olmadığı için herkes
onaylayabilirdi). Muhasebeci ilk dokunuşta sahipleniyor.

### ÖDEME KAPILARININ SIRASI (Kural 43)

`SatirOdemeKaydetAsync` üç kapıyı şu sırayla geçiriyor:
**K8 (yaşlanma) → K2 (değişim) → K3 (sınır)**.

İlk ikisi **kalıcı ret** — satır yeniden onaya döner. K3 **geçici** —
daha az tutar girilip yeniden denenebilir. Kalıcı ret önce gelmezse
kullanıcı tutarı düşüre düşüre aynı duvara çarpar.

K8 ve K2 yalnız reddetmiyor, **satırı da geri düşürüyor**: sadece
hata fırlatmak satırı "onaylı" gösterirdi.

### SONDALAR — BEŞİ DE ÖNGÖRÜYLE BİREBİR

Her sondanın beklentisi **koşturulmadan önce** yazıldı.

| Sonda | Öngörü | Sonuç | Taşma |
|---|---|---|---|
| S1 K2 karşılaştırması | 9–10, hepsi K2 | **9** | yok |
| S2 K3 sınırı | 3 | **3** | yok |
| S3 K4 kontrolü | 3, adlarıyla | **3** birebir | yok |
| S4 K8 yaşlanması | 3 | **3** | yok |
| S5 İ2 Admin'e açık | 2, adlarıyla | **2** birebir | yok |

**Her sabotaj yalnız kendi kuralını kırdı.** Bu, altı kararı ayrı saf
fonksiyonlara bölmenin ölçülebilir karşılığı — ÇEK/2'de kilit iki
yerde kurulduğu için sonda GEÇERSİZ sayılmıştı (Kural 45).

### GM ⊇ ADMIN — YAPISAL BORÇ

`GenelMudurKeys = [.. AdminKeys, PaymentPlanApprove]` yazıldı, yani
**GM'nin yetkileri Admin'in üst kümesi.** Bugün doğru: GM her şeyi
yapabilir, artı ödeme onayı.

**BORÇ ŞU:** ileride Admin'e verilip GM'ye verilmemesi gereken bir
teknik anahtar çıkarsa (sunucu bakımı, günlük temizliği gibi), bu
kalıp onu da GM'ye taşır. O gün iki liste **birbirinden bağımsız**
yazılmalı; bugün yapılmadı çünkü ortada öyle bir anahtar yok ve
olmayan bir ihtiyaç için ayrım açmak, ayrımın neden var olduğunu
unutturur.

### HAFTALIK PLAN TEKLİĞİ — İŞ KURALI, TEKNİK KISIT DEĞİL

`(CompanyId, HaftaBaslangici)` kısmi benzersiz indeksi "bir haftaya
bir plan" diyor. Bu **teknik bir zorunluluk değil, iş kuralı**:
veritabanı iki plan taşıyabilirdi.

Kural şu düşünceden geliyor: aynı hafta için iki plan varsa "bu
haftanın bütçesi ne" sorusunun iki cevabı olur ve K6'nın iki sayısı
anlamını yitirir.

**İKİNCİ BİR KOŞU GEREKİYORSA YOLU K5'TİR** — plan dışı ödeme.
Görünür, sebebi zorunlu, bir sonraki haftanın planının başında
listeleniyor. Yani ihtiyaç karşılanıyor; karşılanma biçimi
denetlenebilir olanı.

### İ3 — BİLİNÇLİ EKSİK

**Vekâlet/yedek onaycı YOK.** GM yoksa plan bekler. İlk turda
bilinçli olarak yapılmadı; ikinci bir onaycı tanımlamak, "yalnız GM
onaylar" kuralının tek istisnasını açmak demektir ve o istisnanın
kimin elinde olacağı ayrı bir karardır.

## ÇEK/2 — DÜZENLENEBİLİRLİK ALAN SINIFINA GÖRE (2026-08-27)

### SORUN

`EvaluateEditability` alan ayrımı yapmıyordu: kapanmış çekte kaydın
TAMAMI kilitliydi ve kullanıcıya sunulan tek çare "İptal edip yeniden
girin" cümlesiydi.

**Bir yazım hatasını düzeltmek için mali kaydı iptal edip yeniden
üretmek, hatanın kendisinden zararlıdır:** iptal gerçekleşmiş bir para
hareketini storno ile geri alır, numarayı yeniden kullanıma açar ve
deftere iki fiş daha yazar — hepsi keşideci adındaki bir harf için.

### ÇÖZÜM — ÜÇ SINIF, TEK TANIM

`ChequeAlanSiniflari` (`Services/Accounting/ChequeAlanKurallari.cs`):

| Sınıf | Alanlar | Kapanmış çekte |
|---|---|---|
| **Kilitli** | çek no, banka, cari, proje, masraf merkezi, tutar, keşide, vade, hakediş, fatura, para birimi, kur | KAPALI |
| **Tanımlayıcı** | keşideci, şube, açıklama | **AÇIK** |
| **Taşıyıcı** | `RowVersion`, `EditReason` | veri değil, zarf |

**BANKA ADI NEDEN TANIMLAYICI DEĞİL:** şube ve keşideci çekin
üzerindeki yazılardır; banka, çekin hangi yaprak olduğunu söyler ve
ödeme hesabıyla eşleştirilen alandır — canlıdaki 805088 uyuşmazlığı
tam olarak bu eşleşmeydi. K2 listesinde de yok. Kimlik sayıldı.

**SINIF VE ETİKET AYNI SÖZLÜKTE.** Etiketler eskiden `UpdateAsync`
içindeki `Track` çağrılarına elle yazılıydı; ayrı tutulsalardı yeni
alan eklerken birine yazıp diğerine yazmamak mümkün olurdu (K4).

### ÇIRÇIR — SINIFSIZ ALAN GEÇEMEZ

`ChequeAlanSinifiTests` yansımayla çalışıyor:
- `UpdateChequeRequest`'in her özelliği sözlükte olmak zorunda,
- sözlükte istekte karşılığı olmayan alan kalamaz,
- **her kilitli alan TEK TEK değiştirilip yakalandığı doğrulanıyor.**

Sonuncusu asıl olan: sözlükte "Kilitli" yazan ama karşılaştırılmayan
bir alan, ekranda kilitli görünür, denetimde kilitli sayılır ve fiilen
serbesttir. Yansıma testi bunu listeye değil DAVRANIŞA bakarak
kapatıyor ve yeni eklenen alanı kendiliğinden kapsıyor.

### VERİLEN ÇEK KASADAN ÖDENMEZ

`CekOdemeHesabiKurali` — yalnız `Verildi → Ödendi` geçişini
kısıtlıyor. Alınan çeğin elden tahsili gerçek bir akıştır ve kasaya
girebilir; kural oraya taşmıyor (testle sabitlendi: kapsanan geçiş
kümesi tam olarak `Issued:Issued->Paid`).

Bu tek süzgeç, canlıdaki üç yanlış kayıttan **ikisini** (bkz. §5a)
baştan imkânsız kılardı. Kural 39'un kırıldığı yer de burası: alan
artık DOĞRULANIYOR.

### İKİ SIRA KARARI — İKİSİ DE TESTLE SABİTLENDİ

1. **Kilitli alan kapısı, damga kontrolünden ÖNCE.** Sonra olsaydı
   kapanmış çekte tutar değiştiren istek 409 "tutar kilitli" yerine
   400 "damga eksik" alırdı. Mevcut bir test yakaladı (Kural 43).

2. **Mükerrer kontrolü yalnız kimlik anahtarı değişince.** Koşulsuz
   çalıştırmak iptal edilmiş çekte patlıyordu: iptal numarayı yeniden
   kullanıma açıyor, sorgu iptalleri eliyor, dolayısıyla iptal kaydın
   AÇIKLAMASINI düzeltmek "bu numara başkasında" hatası veriyordu. Bu
   yol ÇEK/2'den önce hiç yürünemediği için görünmemişti.

### SONDALAR (Kural 36 ve 45)

| Sonda | Sabotaj altında | Geri alınca |
|---|---|---|
| **A** — K1 kapısı | **GEÇERSİZ**: yalnız eski test düştü | — |
| **A2** — K1 (tek kapıya indirildikten sonra) | 4/24 kırmızı | 24/24 yeşil |
| **B** — K2 (`DescriptiveOnly` → `Blocked`) | 6/23 kırmızı | işaret 0, `cmp` aynı |
| **C** — kasa süzgeci | 1/17 — tam isabet | işaret 0, `cmp` aynı |

**A'NIN GEÇERSİZ SAYILMASI BU PAKETİN ASIL KAZANCI.** Kapıyı devre
dışı bıraktığımda yeni testlerimin hiçbiri kırmızıya dönmedi, çünkü
kilidi farkında olmadan İKİ yerde kurmuştum. Ayrıntı ve çıkan kural:
Kural 45.

## ÇEK/1 — ÖDENEN ÇEK LİSTEDE VE TOPLAMDA KALIYORDU (2026-08-26)

ŞİKAYET (GM): çek "Ödendi" göründüğü hâlde o ayın toplam çek
tutarından düşmüyor ve listede duruyor. Çek bankaya fiilen işlenmiş.

#### KÖK NEDEN: DURUM DOĞRU YAZILIYORDU, HATA OKUMADAYDI

Şüphe "durum belki sadece arayüzde değişiyor" yönündeydi; canlı
veritabanından teyit edildi ve **öyle değildi**. Çek 805088
(`VCK-2026-000025`, 1.000.000 TRY):

| Katman | Durum |
|---|---|
| `cheques.Status` | 11 (Paid) ✓ |
| `cheque_movements` | 10 → 11, tarih 2026-08-26 ✓ |
| Muhasebe fişi `TDI-2026-000051` | 103.01 borç / 102.10 alacak, dengeli ✓ |

Hata **okuma tarafında** ve **iki ayrı yerdeydi**:

1. `ChequeService.GetAllAsync` — durum süzgeci YALNIZ çağıran
   gönderirse uygulanıyordu; ekran açılışta hiçbir durum
   göndermiyordu. Elenen tek şey İptal'di.
2. `lib/cheques/totals.ts` — ekran kendi kuralını yazıyordu
   (`status !== Voided`) ve sunucudaki süzgeçten AYRI karar
   veriyordu.

Üstelik açık küme `GetAllAsync` içinde `isOpen` diye ÜÇÜNCÜ kez
satır içi yazılıydı (gecikme hesabı için).

#### TARİH ALANI KARIŞIKLIĞI YOKTU

Ay gruplaması `chequeMonthKey` = `dueDate.slice(0, 7)` — zaten **vade
tarihine** göre. K3 kapsamında düzeltilecek bir şey çıkmadı.

Aylık toplamı üreten bir SUNUCU UCU DA YOK: `cheques/summary` tarih
süzgeci taşımıyor, durum kırılımı veriyor. Ay toplamı ekranda,
listeden türüyor — bu yüzden listeye ne gelirse toplama giriyordu.

#### KURAL TEK YERE ALINDI: `ChequeStatusRules`

İki soru ayrı, ikisi de tek dosyada:

- **Hangi çekler listelenir** → `AcikDurumlar` =
  {Portföy, Bankada, Faktoringde, Verilen}.
- **Hangi satır toplanır** → `ToplamaGirmeyenDurumlar` = {İptal}.

Kural önce yalnız metottu; EF Core metot çağrısını SQL'e çeviremediği
için sorgu yerlerinde patladı. Dizi hem `IN (...)` olarak çevriliyor
hem metodu besliyor — kural yine tek yerde.

**EKRANIN KURALI TAMAMEN KALKTI.** Sunucu her satırda
`countsTowardTotals` bayrağı dönüyor; `totals.ts` yalnız topluyor.
Ayrışacak ikinci karar yeri kalmadı (K2).

#### KARARLAR

- `Bounced` (karşılıksız) **kapanmış** sayıldı: alacak cariye döndü
  ve orada izleniyor. Açık bırakılsaydı aynı alacak hem cari hesapta
  hem çek yükünde iki kez görünürdü.
- `AtFactoring` (kırdırılmış) **açık** kaldı: parası alınmış olsa da
  çek tedavülde ve rücu riski taşıyor. `CashFlowService` onu beklenen
  tahsilattan çıkarıyor — çelişki değil, farklı soru: nakit akışı
  "ne kadar para gelecek", çek defteri "hangi çekler hâlâ canlı".

#### YENİ SÜZGEÇ ESKİ BİR KAPIYI EZDİ — MEVCUT TESTLER YAKALADI

İptal de kapanmış bir durum. Açık süzgeci onu da eleyince
`includeVoided` kapısı işlevsiz kaldı: kullanıcı "iptalleri göster"
dese bile hiçbir şey gelmiyordu. İki kapı çarpıştığında **dar olan
sessizce kazandı**.

`IptalEdilenCek_VarsayilanListedeYok_IstenirseGelir` ve
`VoidedCheque_StaysVisibleAndFilterable` kırmızıya döndü.
`VarsayilanListeDurumlari` iptali GEÇİRİYOR; iptali eleme kararı
kendi kapısında kalıyor.

#### SONDA

| Sonda | Önce | Sabotajlı | Geri alındıktan sonra |
|---|---|---|---|
| A — sunucudaki varsayılan süzgeç kaldırıldı | 6/6 yeşil | **3 kırmızı** | 6/6 yeşil |
| B — ekran kendi kuralını geri yazdı | 7/7 yeşil | **1 kırmızı** | 7/7 yeşil |

Her iki sonda da yedekle `cmp` ile doğrulandı; artakalan yedek yok.

#### ETKİLENEN KAYIT SAYISI

Canlıda Paid durumunda **tek** çek vardı (1.000.000 TRY).
`Collected` hiç yoktu — alınan çek tarafında aynı hata henüz görünür
olmamıştı ama kural aynıydı.

## M3/2a — MESAJLAŞMA UÇLARI (2026-08-26)

Sekiz uç (`/api/mesajlar`): konuşma listesi, birebir konuşma aç,
mesaj listesi, mesaj gönder, okundu, toplam okunmamış, arama, kişi
arama. Hepsi keyset; `COUNT(*)` yok.

#### YETKİ ANAHTARI AÇILMADI — BİLİNÇLİ

Mesajlaşma "yetkisi olan görür" işi değil: giriş yapmış herkes KENDİ
konuşmasını görür, kimse başkasınınkini göremez. Kapı `[Authorize]`
artı ÜYELİK.

Yeni bir `messaging.use` anahtarı açsaydım `RoleCatalog` yansıması
(`K`/`KWithSensitive`) onu yalnız Admin ve Genel Müdür'e verirdi;
kalan her role elle eklemek gerekirdi ve biri unutulsaydı o rol
**sessizce** mesajlaşamazdı. Sessiz yetki kaybı, gürültülü hatadan
kötüdür.

#### KİME YAZABİLİRİM: KAPSAM. KİMİ OKUYABİLİRİM: ÜYELİK.

İki kapı iki AYRI soruyu cevaplıyor ve ikisi de gerekli:

- **Kişi listesi** kapsamla sınırlı — yalnız kapsamdaki şirketlerde
  personel kaydı olan aktif kullanıcılar. Açık olsaydı bir şirketin
  kullanıcısı diğerinin çalışan listesini arama kutusundan dökerdi;
  mesaj göndermeden, yalnız isimleri görerek.
- **Okuma** üyelikle sınırlı — aynı şirketteki bir yabancı, kapsam
  süzgecini geçse bile başkasının konuşmasını göremiyor.

Üye olmayan için cevap "yetkiniz yok" DEĞİL "bulunamadı": yetki
hatası, konuşmanın VAR OLDUĞUNU söylerdi.

Personel kaydı olmayan (yalnız sistem) kullanıcılar kişi listesinde
çıkmıyor — dar olan seçildi.

#### ÜÇ HARF KURALI SUNUCUDA (M3/1 ölçümünün karşılığı)

`MesajAramaKurali` saf sınıf: `Gecerli`, `Normalize`, `Uyari`.
Ekran kuralı yalnız kolaylık — ekran atlanabilir, uç doğrudan
çağrılabilir; **sunucu atlanamaz**.

Karar saf fonksiyonda çünkü aynı kural iki yerde geçerli. İki yere
gömülseydi eşzamanlılık paketinde yaşadığımızın aynısı olurdu: iki
bariyer birbirini örter, hiçbiri tek başına sondalanamaz ve yeşil
hiçbir şey söylemez (Kural 25).

#### GERÇEK ZAMANLI YAYIN YALNIZ ÜYELERE

Hub'da (M3/0) konuşma başına grup YOK, kullanıcı başına grup var.
Mesaj, o anki AKTİF üyelerin kişisel gruplarına tek tek gönderiliyor.
Konuşma grubuna yayın yapsaydık ayrılan üyenin bağlantısı grupta
kaldığı sürece mesaj almaya devam ederdi — erişim kapısı REST'te
kapalı, yayında açık kalırdı.

#### MEVCUT NÖBETÇİ KURAL GERÇEKTE KORUMUYORDU — SONDA BULDU

`ErisimKapisi_GlobalKapsamKisayoluTasimaz` (M3/1'de yazılmış), erişim
kapısına `bool hasGlobalAccess = false` parametresi eklendiğinde
**yeşil kaldı**. Sebep: kural yalnız `HasGlobalAccess` dizgesini
büyük/küçük harfe DUYARLI arıyordu; küçük harfli parametre adı
eşleşmiyordu. Kural, korumak istediği şeyi değil bir yazım biçimini
izliyormuş.

İki katman eklendi:

1. Dizge araması büyük/küçük harfe **duyarsız**.
2. **İMZA KİLİDİ** — asıl koruma. Dizge aramak kısayolun ADINI izlemek
   demek; ad değişince kural boşalır (Kural 31: sözcüğü değil komutu
   izle). Korunan gözlem: üyelik süzgecinin BYPASS KANALI olmamalı.
   Kanal ancak bir parametreyle açılabilir, o yüzden imza sabitlendi:
   `ApplyMembership` yalnız `(sorgu, Guid userId)` alır.

Sonda B3 bunu kanıtladı: kısayol `denetimModu` diye bambaşka bir adla
eklendiğinde dizge araması kaçırıyor, imza kilidi yakalıyor.

#### SONDA TABLOSU

| Sonda | Sabotaj | Sonuç |
|---|---|---|
| A | Üyelik kapısı kaldırıldı | kırmızı (14'te 2) |
| B | Kapıya küçük harfli global kısayol | **yeşil → kural zayıf, düzeltildi** |
| B2 | Aynı sabotaj, güçlendirilmiş kurala karşı | kırmızı |
| B3 | Kısayol bambaşka adla (`denetimModu`) | kırmızı — imza kilidi |
| C | En az harf 3 → 2 | kırmızı (28'de 4) |
| D | Arama üyelik süzgecini atlıyor | kırmızı |
| E | Okunmamış sayısı kendi mesajımı sayıyor | kırmızı |

#### KAPSAM TARAYICISININ KÖR NOKTASI — ÖLÇÜLDÜ

`CoverageBaselineTests` okumadan sonraki **400 karakterlik pencerede**
kapı arıyor ve yorumları **uzunluğu koruyarak** boşluğa çeviriyor
(`Bosalt` her karakteri boşlukla değiştiriyor). Zincirin İÇİNE yazılan
uzun bir yorum, kapı yerinde dururken tarayıcıyı kör ediyor.

Bu paketteki `sonMesajlar` sorgusunda tam olarak bu oldu: yedi
satırlık gerekçe `.ApplyMembership(userId)` çağrısını pencerenin
dışına itti ve tarayıcı "kapsamsız okuma" dedi. Kapı hep oradaydı.

**Bugünkü çözüm dar:** yorum zincirin ÜSTÜNE taşındı. Tarayıcıyı
düzeltmedim çünkü `Bosalt`u uzunluk korumayan biçime çevirmek 450
kalemlik çizgiyi baştan hesaplatır ve o kendi paketi olmalı.

**Açıkta kalan somut hata (Kural 27):** stok ya da başka bir şirketli
varlığı okurken zincirin içine 400 karakteri aşan yorum yazan biri,
kapıyı koymuş olsa bile testi kırmızıya düşürür; kapıyı KOYMAMIŞ
olan biri ise aynı yorumla testi YEŞİL geçiremez (pencere kapıyı
bulamayınca kural düşer). Yani yön güvenli tarafta — yanlış alarm
üretir, sessiz geçiş üretmez.

#### BOŞ SORGUDA ÇERÇEVENİN MESAJI GÖRÜNÜYORDU

`[FromQuery] string q` zorunluyken boş sorgu ASP.NET model
doğrulamasına takılıyor ve kullanıcı `"The q field is required."`
görüyordu: İngilizce, kuralı anlatmayan, bizim yazmadığımız bir
mesaj. `q` nullable yapıldı; kuralın mesajını kural veriyor.

## M3/1 — MESAJLAŞMA VERİ MODELİ (2026-08-25)

Dört tablo: `conversations`, `conversation_members`, `messages`,
`personnel_department_history`. Artı `personnel.DepartmentId`.

#### ERİŞİM: ÜYELİK, KAPSAM DEĞİL — VE GLOBAL ERİŞİM GEÇMEZ

Sistemdeki diğer her süzgeç `HasGlobalAccess` kısayolu taşıyor:
global erişimli kullanıcıda sorgu olduğu gibi geçiyor. Mesajlaşmada
bu kısayol **YOK ve OLMAMALI** — kimse başkasının konuşmasını
okuyamaz, Genel Müdür dahil.

**İKİ AYRI KAPI, İKİSİ DE GEREKLİ:** kapsam yanlış şirketin verisini
engeller, üyelik doğru şirketteki BAŞKASININ konuşmasını engeller.
Biri diğerinin yerine geçmez.

Kuralı **kaynak taraması** koruyor
(`ErisimKapisi_GlobalKapsamKisayoluTasimaz`): kısayolun bir gün
"tutarlılık olsun" diye eklenmesini yakalıyor. Çalışma zamanı testi
bunu yakalayamazdı — kısayol eklendiğinde yalnız global erişimli
kullanıcı için kırmızıya dönerdi ve öyle bir test kurulmamıştı.

#### AYRILAN ÜYE HİÇBİR ŞEY GÖRMEZ — DAR OLAN SEÇİLDİ

`LeftAtUtc` dolu olan üye, ayrıldığı tarihe kadarki mesajları da
göremiyor. "Ayrıldığı tarihe kadarki geçmişi görür" kuralı departman
KANALLARI bağlamında konuşulmuştu; kanallar M3/3'te gelecek ve orada
yeniden ele alınacak.

Üyelik satırı **silinmiyor, tarihleniyor**: "o tarihte kim
görüyordu" sorusunun tek cevabı o satır.

#### OKUNDU BİLGİSİ AYRI TABLODA DEĞİL

`ConversationMember.LastReadAtUtc`. Mesaj başına okundu satırı
tutmak, mesaj × üye kadar satır üretirdi — mesajlaşmada bu, sistemin
en hızlı büyüyen tablosu olurdu. "Hangi mesajı tam olarak okudu"
bilgisi kayboluyor; o bilgiye ihtiyaç duyan bir gereksinim yok.

#### TÜRKÇE ARAMA — MEVCUT ALTYAPI KULLANILDI

`enderun_fold` fonksiyonu ve `TurkishSearch.cs` C# ikizi **zaten
vardı** (G2 turundan), eşitlikleri testle sabit. Yeniden yazılmadı:
iki katlama ayrışırsa aynı arama bir yerde bulur, diğerinde bulamaz.

`messages.SearchFold` üretilmiş kolon = `enderun_fold("Body")`,
üstünde **GIN trigram** indeksi (migration'da elle — EF üretilmiş
kolon üzerinde GIN tanımlayamıyor).

**NEDEN TRIGRAM, tsvector DEĞİL — ÖLÇÜLDÜ (200.000 satır, PG 16.15):**

**KARAR (2026-08-25, Mehmet):** trigram kalıyor, tsvector eklenmiyor.
Gerekçe: kelime ortası eşleşmesi, öngörülebilir süre, %25 yazma
maliyetinden kaçınma.

**ÖLÇÜM TARİHİ: 2026-08-25.** İki bağımsız koşu — 200.000 ve 500.000
satır, PostgreSQL 16.15.

**YENİDEN ÖLÇÜM EŞİĞİ: `messages` 500.000 satırı geçtiğinde.**
Trigram'ın seçicilik tahmini bugün isabetli (56'ya karşı gerçek 30) ve
kararın dayandığı şey bu isabet. Tahmin, satır sayısı ve kelime
dağılımı değiştikçe kayabilir; kaydığında ölçülen tek şey süre olmaz,
planlayıcının SEÇTİĞİ YOL olur. Kontrol sorgusu:
```sql
SELECT count(*) FROM messages;   -- 500.000'i geçtiyse ölçümü tekrarla
```

`tsvector` de önek eşlemesi yapabiliyor (`to_tsquery('simple','insa:*')`)
ve indeksi kullanıyor. "tsvector yarım kelimeyi bulmaz" doğru değil —
**kelime BAŞINDAN** başlayan yarım kelimeyi bulur.

Ayıran şey hız değil, **kelime ortası**:

| Sorgu | tsvector `:*` | trigram `LIKE` |
|---|---|---|
| `insa` → `inşaat` (kelime başı) | 18 ms, indeksli | 17 ms, indeksli |
| `4783` → `TLP-64783` (kelime ortası) | **0 satır — BULAMIYOR** | 49 satır, 0,2 ms |
| `san` (3 harf) | 18 ms, indeksli | 20 ms, indeksli |
| `be` (2 harf) | 18 ms, **indeksli** | 86 ms, **sıra taraması** |
| iki kelime birlikte | 38 ms | 14 ms |

Mesajlaşmada aranan şeyin büyük kısmı ürün/sipariş kodu parçası ve
kod her zaman kelime başında olmuyor. tsvector bunu **hiç**
bulamıyor — yavaş bulmuyor, bulamıyor. Trigram'ın tek zayıf noktası
2 harflik sorgu; bu bir arama kutusu kuralıyla kapanıyor.

**İKİNCİ ÖLÇÜM (500.000 satır, bağımsız koşu) BUNU DOĞRULADI VE
BİR TUZAK EKLEDİ:** yukarıdaki tablo `ORDER BY ... LIMIT` olmadan
ölçülmüş. Gerçek sorgu her zaman sıralı ve sayfalı olacak. O desende
`tsvector` öneki **4,5 saniye** sürdü.

Sebep indeks değil, **planlayıcının seçimi**: önek `:*` sorgusunun
seçicilik tahmini 2800 satır, gerçek 30 — 93 kat şişik. Bu şişik
tahminle planlayıcı "id'ye göre geriye yürürsem 50 sonucu hemen
bulurum" diyor ve tabloyu neredeyse baştan sona tarıyor. GIN'e
zorlandığında (`enable_indexscan=off`) aynı sorgu **0,30 ms**.

Trigram'ın tahmini isabetli (56'ya karşı gerçek 30) ve aynı sorguda
**0,52 ms**, indeksi kullanarak.

| Nadir kelime + `ORDER BY id DESC LIMIT 50` | Süre | Seçilen yol |
|---|---|---|
| tsvector `veda:*` | **4487 ms** | id indeksinde geriye tarama |
| tsvector `veda:*`, GIN'e zorlanmış | 0,30 ms | GIN |
| trigram `%veda%` | **0,52 ms** | GIN |

Yani tsvector'ün asıl bedeli yavaşlık değil **öngörülemezlik**: aynı
sorgu, aranan kelimenin ne kadar seyrek olduğuna göre 0,3 ms veya
4,5 saniye sürüyor. Trigram'da böyle bir uçurum yok.

Yazma maliyeti de ölçüldü (20.000 mesaj ekleme): indekssiz 249 ms,
yalnız trigram 627 ms, trigram+tsvector 781 ms. İkinci indeks yazmayı
**%25** ağırlaştırıyor — mesaj tablosu sistemin en çok yazılan tablosu.

**ARAMA KUTUSU EN AZ 3 HARF İSTEYECEK** (M3/2). 2 harfte trigram
indeksi devre dışı kalıyor; 200 bin satırda 86 ms, iki milyon satırda
saniyenin altında kalmaz. İkinci indeks açmaktansa sorguyu
engellemek doğru: 2 harflik arama zaten kullanışlı sonuç vermiyor.

**Canlı kanıt (test veritabanı):** `İNŞAAT ŞANTİYESİ ÖLÇÜMÜ` →
`insaat santiyesi olcumu`. `İNŞAAT`, `insaat`, `SANTIYE`, `olcum`
hepsi buluyor; alakasız kelime bulmuyor.

#### KEYSET VE İNDEKSLER

| İndeks (canlıdaki gerçek ad) | Ne için |
|---|---|
| `IX_messages_ConversationId_CreatedAtUtc_Id` | Keyset imleci — mesaj en hızlı büyüyen tablo, COUNT(*) yok |
| `IX_conversations_CompanyId_IsArchived_LastMessageAtUtc` | Konuşma listesi "en son konuşulan üstte" |
| `IX_conversation_members_aktif_benzersiz` | Aynı kişi bir konuşmaya iki kez AKTİF üye olamaz — **kısmi**: `WHERE "LeftAtUtc" IS NULL AND NOT "IsDeleted"` |
| `IX_conversation_members_UserId_LeftAtUtc` | Erişim sorgusu: "bu kullanıcı bu konuşmanın üyesi mi" |
| `IX_messages_arama_trgm` (GIN) | Türkçe arama |

#### CANLIYA ÇIKAN KUSUR VE DÜZELTMESİ (aynı gün)

M3/1 canlıya çıktıktan sonra indeksler tek tek ölçüldü ve
benzersizlik indeksi **koşulsuz** bulundu:
`UNIQUE ("ConversationId","UserId")`, filtre yok. Kaynağı da öyleydi —
`AppDbContext`'te `.HasFilter(...)` hiç yazılmamıştı.

**Sonucu:** konuşmadan ayrılan kişi o konuşmaya BİR DAHA
EKLENEMEZDİ. Üyelik satırı silinmiyor, tarihleniyor; ikinci satırı
koşulsuz benzersizlik reddediyor. `IsDeleted` süzgeci burada yetmez —
benzersizlik veritabanı düzeyinde uygulanıyor, EF sorgu süzgeci oraya
işlemiyor.

Düzeltme: `20260825133424_M3UyelikBenzersizligiKismi`. İki test
indeksi iki taraftan sıkıştırıyor — filtre kaldırılırsa
`AyrilanUye_AyniKonusmayaYenidenEklenebilir`, fazla genişletilirse
`AyniKisi_IkiKezAktifUyeOlamaz` kırmızıya döner.

**BU KUSURU BELGE YAKALADI, TEST DEĞİL.** DURUM.md'ye kısmi indeks
yazılmıştı, canlıda koşulsuzdu; ikisini karşılaştırmak fark ettirdi.
Kural 30 buradan çıktı.

**SONDA DERSİ — VERİTABANI ÜZERİNDEN SABOTAJ İŞE YARAMAZ.** İndeksi
elle koşulsuza çevirip testi koşturmak testi kırmızıya döndürmedi ve
bir an "test açığı" sanıldı. Sebep: fixture her koşuda veritabanını
düşürüp **migration'lardan** yeniden kuruyor; sonda testler
başlamadan siliniyordu. Şemanın kaynağı model değil, migration
dosyası — sonda oraya kurulmalı.

Ayrıca `psql`, SQL hatasında bile 0 çıkış kodu döndürüyor:
`ON_ERROR_STOP=1` olmadan `set -e` sabotajın kurulamadığını
yakalamıyor. Sabotajın KURULDUĞU ayrıca doğrulanmalı.

#### MIGRATION İKİ YÖNDE DE SINANDI

Test veritabanında uygulandı → dört tablo, GIN indeksi ve
`DepartmentId` doğrulandı → geri alındı → **hepsi tümüyle kalktı** →
yeniden uygulandı. Veri değiştirmediği için satır sayısı
doğrulaması (§5 kural 21) gerekmiyor.

#### ARŞİV BİÇİMİ: AYNI VERİTABANINDA SOĞUK TABLO

12 ay çevrimiçi, sonrası soğuk tabloya. Dosyaya çıkarmak yedek ve
geri yükleme yüzeyini ikiye bölerdi — bugün tek `pg_dump` her şeyi
alıyor ve geri yükleme provası da onun üzerinden yapıldı. Arşivdeki
mesaj **aranabilir değil**, okunabilir.

**TAŞIMA MEKANİZMASI KURULMADI:** ortada veri yok ve bu bir saklama
mekanizması, silme kuralına komşu.

## M3/0 — GERÇEK ZAMANLI İSKELET (2026-08-25)

Hub bu turda yalnız **BAĞLANIYOR**. Mesaj, kanal, okundu bilgisi
M3/1 ve sonrasında. İskeletin ayrı deploy edilmesinin sebebi:
altyapının canlıda çalıştığını, üstüne veri modeli koymadan ÖNCE
görmek.

#### KİMLİK ÇEREZDEN — SORGU DİZESİ YOK

`access_token` sorgu parametresi (SignalR'ın yaygın yolu) **bilerek
kullanılmadı**: token URL'e girerse erişim kaydına, tarayıcı
geçmişine ve proxy kayıtlarına düşer — portal token'ında yaşadığımız
sızıntının aynısı. nginx'te `/api/hubs/` için `access_log off` da bu
yüzden var.

Tarayıcı WebSocket el sıkışmasında **özel başlık gönderemez** ama
çerezleri kendiliğinden gönderir. `JwtBearerEvents.OnMessageReceived`
`enderun_token` çerezini okuyor.

**YALNIZ `/api/hubs` YOLUNDA.** Çerez okumayı tüm API'ye açmak CSRF
yüzeyini genişletirdi (çerez `sameSite=lax`, tam koruma değil). REST
uçları başlık istemeye devam ediyor ve
`Cerez_RestUcundaKabulEdilmez` bu sınırı tutuyor — sonda ile
doğrulandı.

**Hub `/api/backend/` altına konamazdı:** orası bir Next.js Route
Handler ve `fetch()` kullanıyor; Route Handler WebSocket yükseltmesi
YAPAMAZ. Bunu M3 Faz 0 denetiminde önerirken doğrulamamıştım,
ölçünce çalışmadığı ortaya çıktı.

#### TEK SUNUCU — REDIS KURULMADI

Backend ve frontend aynı makinede (iki systemd birimi, tek sunucu).
Bellek içi bağlantı takibi yeterli.

**İKİNCİ SUNUCU TETİKLEYİCİSİ:** ikinci bir uygulama sunucusu
eklendiği gün bağlantılar iki makineye dağılır ve bir makinedeki
yayın diğerindeki kullanıcıya ULAŞMAZ. Belirti sessizdir — "bazen
mesaj gelmiyor". O gün Redis backplane şart olur.

#### HIZ SINIRI

`mesaj` politikası: **kullanıcı başına dakikada 30**, kuyruk yok
(`QueueLimit = 0`) — sınırı aşan istek bekletilmez, 429 döner.
Bekletmek, yazan kişiye "gitti" hissi verip mesajı dakikalar sonra
göndermek demekti. Bölümleme kullanıcı kimliğine göre; IP'ye göre
olsaydı aynı ofisten bağlanan herkes tek kotayı paylaşırdı.

#### KULLANICI BAŞINA GRUP

Bağlanan her kullanıcı `kullanici:{id}` grubuna giriyor. Aynı kişinin
iki cihazı iki bağlantı demek; kişiye yayın yapmak isteyen kod
bağlantıları tek tek aramak zorunda kalmasın. Grup adı KİMLİKTEN
türüyor, addan değil — ad kullanılsaydı iki aynı adlı kişi tek gruba
düşer ve birinin mesajı diğerine giderdi.

## YEDEK VE DB ERİŞİMİ (2026-08-25)

#### YEDEK ŞİFRELEMESİ — AKIŞTA, DÜZ KOPYA HİÇ DİSKE DÜŞMÜYOR

**BULGU (2026-08-25):** yedek dizininde 2 Ağustos'tan beri birikmiş
**532 düz veritabanı yedeği** vardı — toplam 1573 şifresiz dosya,
23 GB. İçlerinde aynı gün tablodan, kayıttan ve günlükten temizlenen
token açık metin duruyordu. Diskteki düz kopya, o temizliğin tamamını
anlamsız kılıyor.

`enderun-backup.sh` yeniden yazıldı. `pg_dump` çıktısı **doğrudan
`gpg`'ye akıyor**, düz hali diske hiç düşmüyor:

```
pg_dump ... -F c | gpg --symmetric --cipher-algo AES256 --output x.gpg
```

Önce yazıp sonra şifrelemek arada bir pencere bırakıyordu; süreç o
pencerede ölürse düz dump orada KALIYORDU. Ölçüldü: koşu boyunca
dizin 0,25 sn aralıklarla izlendi, **tek bir düz dosya düşmedi.**

#### ANAHTAR YOKSA YEDEK ALINMAZ — ÖNCEKİ KARARIN TERSİ

Betik önce "anahtar yoksa yedeği yine al, düz bırak, ERROR yaz"
diyordu; gerekçe 2026-08 başında sistemin saatlerce yedeksiz
kalmasıydı. **Karar değişti (Mehmet Karacabey, 2026-08-25):**
şifresiz dump diske hiç düşmeyecek.

Yedeksiz kalma riski nasıl karşılanıyor: betik artık **sessiz
başarısız olmuyor** — `exit 1` ile duruyor, systemd birimi "failed"
durumuna düşüyor, ve **safe-deploy yayını kesiyor.**

`safe-deploy` yedek adımı çıkış kodunu **hiç okumuyordu**: yedek
düşse de yayın devam ediyordu. Yedeğin amacı "yayın bozarsa geri
dön"; yedek yoksa o güvence de yok. Düzeltildi.

#### DOĞRULAMA — YAZILMIŞ OLMAK OKUNABİLİR OLMAK DEĞİL

Her yedek, yazıldıktan sonra **açıldığı doğrulanarak** kabul ediliyor;
açılamıyorsa dosya siliniyor ve betik duruyor. Dizinde
`BOZUK-YARIM_db_20260814` adlı bir dosya duruyor: doğrulanmamış
yedeğin ne demek olduğunun kanıtı.

**`pg_restore --list` KULLANILAMIYOR:** özel biçimli arşivi borudan
okuyamıyor (ölçüldü: borudan çıkış 2, dosyadan çıkış 0). Dosyadan
okutmak düz dump'ı diske yazmayı gerektirirdi. `/dev/shm` de çözüm
değil — bu makinede **takas açık** (4 GB, 1,6 GB kullanımda), tmpfs
sayfası takas dosyası üzerinden diske düşebilir.

Yerine iki aşama: (1) tam çözme — gpg'nin kendi bütünlük denetimi
(MDC) kırpılmış/bozuk/yanlış anahtarlı dosyayı yakalıyor, (2) ilk
5 bayt `PGDMP` mi.

#### GERİ YÜKLEME PROVASI — CANLI ANAHTARLA YAPILDI

| Adım | Sonuç |
|---|---|
| Şifreli yedek **borudan** `pg_restore`'a | `gpg=0 pg_restore=0`, **0 uyarı** |
| Tablo sayısı | canlı 236 / prova 236 |
| `personnel` | 81 / 81 |
| `users` | 13 / 13 |
| `companies`, `projects`, `cheques` | hepsi eşit |
| Kısmi indeks (`aktif_benzersiz`) | provada da filtresiyle mevcut |

**YAN BULGU:** `pg_restore --dbname` boruyu okuyor (`--list` okumuyor).
Yani geri yükleme de düz ara dosya gerektirmiyor — felaket anında
şifreli yedek doğrudan açılıyor.

Prova **ayrı bir veritabanına** yapıldı (`enderun_geri_yukleme_provasi`),
canlıya dokunulmadı, sonunda düşürüldü.

#### ANAHTAR

`/etc/enderunai/backup-key`, **0400 root:root**, 48 baytlık rastgele
değer. `postgres` ve `www-data` kullanıcılarıyla denendi: **ikisi de
okuyamıyor.** Betik anahtarı ÜRETMEZ ve YAZMAZ.

**ANAHTAR BUGÜN YEDEKLERLE AYNI DİSKTE — AÇIK KARAR (BEKLEYEN
KARARLAR 12).** Diski ele geçiren ikisini birden alır. Bugünkü
şifreleme, "diski çalan okuyamasın" korumasını **vermiyor**; yalnız
yanlışlıkla kopyalanan tek bir yedek dosyasını koruyor.

#### GEÇMİŞTEKİ DÜZ YEDEKLER

1573 düz dosya tek tek şifrelendi, **açıldığı doğrulandı**, sonra düz
kopya `shred -u -n 1 -z` ile silindi. Şifreleme veya doğrulama
başarısız olan dosyada düz kopya DURUYOR ve kayda düşüyor — veri
kaybetmemek şifrelemekten önce gelir.

Betiğe **düz dosya nöbeti** eklendi: dizinde şifresiz yedek kalırsa
her koşuda ERROR yazıyor.

#### İKİ ZAMANLAYICI VARDI, BİRİ ÖLÜYDÜ

`/etc/cron.d/enderun-ai-backup` var olmayan bir betiği
(`scripts/backup.sh`) her gece çağırıyor ve `/bin/sh: not found` ile
düşüyordu. Kaldırıldı (kopyası
`/root/enderun-ai-backup.devre-disi-20260825`). Çalışan tek
zamanlayıcı `enderun-backup.timer`, her gece 03:00.

#### BETİĞİN TEK KAYNAĞI REPO

`scripts/enderun-backup.sh`. `safe-deploy` her yayında bu dosyayı
`/usr/local/bin/enderun-backup.sh` olarak **yeniden kuruyor**
(`install -m 700`, öncesinde `bash -n`). Sürüklenme testle değil
**inşa yoluyla** imkânsız: canlıda elle yapılan bir değişiklik bir
sonraki yayında geri alınır.

İçerik nöbetçisi `BackupScriptGuardTests` (5 test): pg_dump boruya
akıyor mu, tar boruya akıyor mu, anahtar yoksa duruyor mu, her yedek
doğrulanıyor mu, borunun iki ucu da kontrol ediliyor mu.

#### YEDEK DİZİNİ İZNİ

`/var/backups/enderun` `drwxr-xr-x` idi — makinedeki her kullanıcı
22 GB'lık dump'ları okuyabiliyordu. `0700`/`0600` yapıldı; `postgres`
kullanıcısıyla denendi, dizin artık açılmıyor.

#### DB BAĞLANTI KAYDI — AÇILDI

`log_connections` ve `log_disconnections` **on** (ALTER SYSTEM +
`pg_reload_conf`, **yeniden başlatma gerekmedi**).

**Kayıt journald'a DEĞİL dosyaya düşüyor:** `logging_collector=off`
ve `log_destination=stderr` olmasına rağmen Debian paketlemesi
stderr'i `/var/log/postgresql/postgresql-16-main.log` dosyasına
yönlendiriyor. Doğrulandı: bağlantı, kimlik doğrulama ve kopma
satırları düşüyor.

Dosya `postgres:adm`, `-rw-r-----` — dünyaya kapalı. Rotasyon
haftalık, 10 kopya (~10 hafta).

**HAFTALIK ÖZET (tek satır):**
```bash
sudo grep 'connection authorized' /var/log/postgresql/postgresql-16-main.log | grep -oE 'user=[a-z_]+ database=[a-z_]+' | sort | uniq -c | sort -rn
```

`log_min_duration_statement` **-1** (kapalı) bırakıldı: sorgu
metinleri kayda düşerse IBAN ve maaş değerleri parametre olarak
oraya sızabilirdi.

---

## İŞEMRİ/2 FAZ 2 — KAPI 1 ÖLÇÜMÜ VE KARARLARI (2026-09-03)

### ÖLÇÜM TESPİTİ DÜZELTTİ

Faz 1 kapanışında *"kaskadın departman yarısı canlıda boş"* diye
yazmıştım. Ölçüm bunu üç yerde düzeltti:

| İddia | Ölçüm |
|---|---|
| "departman verisi yok" | `hr_departments` = **5 kayıt** (FİNANS, İNSAN KAYNAKLARI, MUHASEBE, TEKNİK OFİS, Yönetim) — liste dolu, **bağ** boş |
| "veri girilmemiş" | `Personnel.DepartmentId`'ye **yazan hiçbir yol yok** (bkz. o başlık) |
| "rolden tohumlanabilir" | **imkânsız** — personel↔kullanıcı bağı **0/13**; roller `users` üzerinde, personelin rolü yok |

`hr_positions` = 0, `personnel_department_history` = 0.

### ASIL ÇELİŞKİ: TAKSONOMİ İŞ GÜCÜNÜ KAPSAMIYORDU

Beş departmanın beşi de **ofis** birimi. İş gücünün çoğunluğu saha:
`Profession = SAHA GÖREVLİSİ` **31 kişi**, ayrıca ünvan tarafında
USTA ×12, KALFA ×9, YARDIMCI ×8, ŞOFÖR, FORMEN, Elektrik Ustası.
Bunların gideceği bir departman **yoktu** — kusursuz bir atama ekranı
bile 31+ kişiyi atayamazdı, çünkü sorun veri değil **seçenek
yokluğuydu**.

### TOHUMLAMA ÖLÇÜLDÜ VE KAPATILDI

`Profession` → mevcut departman eşleşmesi, 79 aktif personel:

| Durum | Kişi |
|---|---|
| BOŞ — tohumlanamaz | 38 |
| HEDEF DEPARTMAN YOK (saha) | 31 |
| hedef yok / belirsiz | 4 |
| BELİRSİZ — iki departmana birden (`MUHASEBE-FİNANS`) | 2 |
| **KESİN → Yönetim** | 2 |
| **KESİN → TEKNİK OFİS** | 2 |

**Kesin tohumlanabilir: 79'un 4'ü (%5).**

MEHMET'İN KARARI VE GEREKÇESİ: *"Tohumlama YAPMA. %5 isabetli bir
tohumlama, elle atamadan daha kötüdür: yanlış atananı kimse fark
etmez."* Liste boş başlar, elle doldurulur.

Ayrıca ölçümde bir veri kalitesi işareti çıktı: `MUHASEBE-FİNANS` ve
`MUHASEBE- FİNANS` — aynı meslek, boşlukla ayrışmış iki yazım. Serbest
metin alandan tohumlamanın maliyeti bu.

### KARARLAR (KAPI 1, Mehmet)

1. **Tek bir SAHA departmanı açılır** (5 + 1 = 6). Saha görevlisi,
   usta, kalfa, yardımcı, şoför, formen — hepsi SAHA'ya girer.
   GEREKÇE: saha personelinin asıl çalışma birimi **proje**; M3'te
   proje kanalı o işi görür, SAHA departman kanalı saha geneli
   duyurular için kalır. Daha ince bölünmeyecek; ihtiyaç çıkarsa sonra
   bölünür.
2. **Yazma yolu ayrı ve ÖNCE gelen küçük paket** (DEPARTMAN/1):
   uç + servis + ekran, toplu atama görünümü, `personnel_department_history`
   yazımı, kapsam süzgeci / RowVersion / keyset normal kuralları.
3. **Tohumlama yok.**
4. Faz 2 kaskadı boş bağla **dürüst** davranır (aşağıda).

### KASKADIN DÜRÜST BOŞLUĞU — MESAJIN YERİ DÜZELTİLDİ

İlk tasarımda dürüst mesaj "departman seçici boşsa" durumuna
konacaktı. Ölçüm gösterdi ki seçici **boş değil** (5-6 seçenek);
boş olan, seçimden **sonraki** personel listesi. Mesaj oraya konur:

> "Bu departmana atanmış personel yok — personel departman ataması
> yapılmamış" + toplu atama ekranına bağlantı

Sessiz boş liste yok. "Tümü" her zaman açık; proje kolu bağımsız
çalışır.

### M3 BAĞLANTISI — NOT

**M3 departman kanalları `Personnel.DepartmentId`'ye dayanıyor**
(`Conversation.DepartmentId` modelde var, üyelik türetimi henüz
yazılmadı). Alan boş kaldığı sürece **departman kanalları boş
doğar** — hata vermez, sessizce kimseyi içermez.

**M3/3'ten ÖNCE veri doldurulmuş olmalı.** DEPARTMAN/1 bu yüzden
Faz 2'den de önce geliyor.

---

---

## DEPARTMAN/1 — `Personnel.DepartmentId`'NİN İLK YAZMA YOLU (2026-09-03)

KAPI 1'de ölçülmüştü: alan modelde vardı, göçü uygulanmıştı, canlıda
**79 aktif personelin 0'ında** doluydu ve sebebi veri girme ihmali
değil, **yazan hiçbir yolun olmamasıydı**. Bu paket o boşluğu
kapatıyor.

### YOL BOYUNDA ÇIKAN BULGU — EKRAN "DEPARTMAN" YAZIP MESLEK GÖSTERİYORDU

Personel listesindeki kolon başlığı **"Departman / Pozisyon"**du; hücre
ise `profession` (meslek) ve `jobTitle` (ünvan) gösteriyordu.
Personelin gerçek departmanı ekranda **hiç görünmüyordu.**

**Boşluğun neden fark edilmediğinin bir parçası bu:** ekranda
"Departman" yazan dolu bir kolon vardı. Bakan kişi departmanın girili
olduğunu sanırdı — sütun doluydu, yalnız başka bir şeyle.

Başlık `Meslek / Pozisyon` olarak düzeltildi, yanına gerçek
`Departman` kolonu kondu ve başlığın geri gelmesini engelleyen bir test
yazıldı (`personel-departman-ekran-sozlesmesi`). Testin ilk hâli fazla
genişti ve KENDİ AÇIKLAMA YORUMUMU yakaladı: dosyada hatayı anlatan
yorum da o dizeyi içeriyordu. Test başlığın kendisini (`<TableHead>…`)
arayacak şekilde daraltıldı — yoksa hatayı KAYDETMEK, hatayı geri
getirmek sayılırdı.

### TASARIM KARARLARI VE GEREKÇELERİ

| Karar | Gerekçe |
|---|---|
| `veri-tamamla`'ya EKLENMEDİ, ayrı uç | O uç alan DOLDURMAK için: gönderilmeyen alanı değiştirmiyor ve boşaltma yolu yok. Departman boşaltması meşru (yanlış atananın düzeltilmesi). Ayrıca tarihçe ve sürüm kontrolü orada yok. |
| `null` = departmandan çıkar | `CompletePersonnelDataRequest`'in kuralının TERSİ ve bilinçli. Reddedilseydi yanlış atama düzeltilemezdi. |
| RowVersion = `KayitSurumu` | Bu deponun mevcut deseni; `xmin` daha önce denenip gerekçesiyle reddedilmiş. Yeni göç gerekmedi. |
| Yeni ekran yok, mevcut listeye kolon | 79 satırın tamamına giriş yapılacak; her satır için panel açtırmak 79 × (aç, seç, kaydet, kapat) demekti. |
| Keyset yok | Yeni liste ucu açılmadı. Mevcut ucun sayfalamasızlığı önceden var olan bir durum (79 satır) — bu paket onu ne düzeltiyor ne kötüleştiriyor. |
| Tarihçe ucun İÇİNDE | Ayrı çağrıya bırakılsaydı, çağırmayı unutan ilk yazma yolu M3'ün "ayrıldığı tarihe kadarki geçmiş" kuralını sessizce delerdi. |
| Aynı departman tekrar → 200, tarihçeye yazılmaz | Toplu girişte aynı değeri yeniden seçmek olağan; 400 dönmek var olmayan bir sorun gösterirdi. Ama tarihçeye yazılsaydı, hiç olmamış geçişlerle dolardı. |

### İKİ BAĞLAM, SIFIR YABANCI ANAHTAR

`Personnel` **AppDbContext**'te, `HrDepartment` **HrDbContext**'te —
aynı fiziksel veritabanı, ayrı bağlamlar. EF ikisi arasında yabancı
anahtar kuramıyor: **veritabanı bu bağı doğrulamıyor.** "Departman var
mı, aktif mi, aynı şirkette mi" kontrolünün tamamı uygulama katmanında,
`PersonelDepartmanKurali` içinde ve testli. Aynı sebeple departman adı
liste ucunda LINQ ile birleştirilemiyor; kimlikler projeksiyondan sonra
tek sorguda ada çevriliyor.

### SAHA DEPARTMANI — VERİ GÖÇÜ

`20260903150000_SahaDepartmani`: şirket başına bir `SAHA-001`.
Kimlik gömülmedi, satır `companies` üzerinden türetiliyor — sabit GUID
gömülseydi test veritabanında yanlış şirkete yazardı. `NOT EXISTS` ile
tekrar koşmaya dayanıklı (göç provası canlının kopyasında koşuyor).

`Down` **koşullu**: SAHA'ya atanmış personel ya da tarihçe kaydı varsa
satır silinmiyor. Koşulsuz silseydik, iki bağlam arasında yabancı
anahtar OLMADIĞI için veritabanı itiraz etmez, bağ sessizce kırılırdı.

**DÜRÜST SINIR:** göçün etkisi test veritabanında ölçülemiyor — insert
mevcut şirketlere bağlı, test veritabanında göç anında hiç şirket yok.
Güvence göç provasından (`goc-provasi`, canlının kopyası) ve göç
sonrası canlı ölçümden geliyor.

### BEŞ SONDA — HEPSİ İLAN EDİLDİĞİ GİBİ

| Sonda | Sabotaj | Düşen |
|---|---|---|
| J | kural çağrısı silindi | `OlmayanDepartman`, `PasifDepartman`, `BaskaSirketinDepartmani` ✓ |
| K | tarihçe yazımı silindi | `Atama_Kabul_VE_TarihceyeYazilir`, `TarihcedeOncekiDepartman`, `AyniDepartmanTekrar` ✓ |
| L | sürüm kontrolü silindi | `SurumGonderilmezse`, `EskiSurum_Cakisma` ✓ |
| M | kapsamlı okuma ham `db.Personnel` yapıldı | `DarKapsam_GorunmeyenPersonelinDepartmani_Yazilamaz` ✓ (pozitif kontrol yeşil kaldı) |
| N | `DegisiklikMi` kapısı kaldırıldı | `AyniDepartmanTekrar` ✓ |

Kontrolcü her sondadan sonra **bayt bayt** geri geldi.

### KAPSAM SÜZGECİ — BORÇ BİRİKMEDİ

`personnel.edit` bugün yalnız geniş kapsamlı rollerde (İK Sorumlusu,
Teknik Koordinatör), yani süzgeç canlıda hiçbir isteği reddetmiyor.
İŞEMRİ/2 Faz 1'de aynı durum bir DÜRÜST SINIR olarak yazılmış ve
Mehmet'in şartıyla kapatılmıştı; bu pakette baştan kapatıldı —
`PersonelDepartmanKapsamTests`, rolü değil KAPSAMI daraltıyor.

Burada ayrıca daha ağır: departman yazmak bir personelin M3 kanal
üyeliğini belirleyecek. Kapsam dışı birinin departmanını
değiştirebilmek, onu görmediğiniz bir kanala sokabilmek demek.

### KALAN RİSK — KAYIT, PAKET DEĞİL

Departman **silme** muhafızı (`HrMasterDataController`) alt birim ve
pozisyon kontrol ediyor, **personel kontrol etmiyor**. Alan dolmaya
başladığı gün bir departmanın silinmesi personelleri yetim bırakır.
Liste ucu bunu sessizce boş göstermiyor — çözülemeyen kimlik
`(bilinmeyen departman)` olarak görünüyor — ama muhafızın kendisi
eksik. Departman kullanımı başlamadan önce kapatılmalı.

---

---

## PROVA, UYGULAMANIN KENDİSİYLE AYNI YOLU KULLANMALIDIR (Mehmet onayı, 2026-09-03)

> **PROVA İLE UYGULAMA AYNI KOD YOLUNU VE AYNI ORTAMI KULLANMALIDIR —
> AYNI DOSYA OLMASI GEREKMEZ. AYRIŞAN HER NOKTA (ORTAM DEĞİŞKENİ,
> BAYRAK, İKİLİ, DERLEME) PROVANIN SINAMADIĞI BİR NOKTADIR. PROVANIN
> YEŞİLİ, UYGULAMANIN YEŞİLİ DEĞİLDİR.**

*(Kuralın ilk yazımı "aynı betiği" diyordu. Mehmet düzeltti: ölçüm
gösterdi ki iki betik birbirinin kopyası değil — `goc-provasi.sh`
prova MOTORU, `goc-uygula.sh` onu ÇAĞIRAN düzenleyici. Aranan şey tek
dosya değil, **tek kod yolu**.)*

### DOĞURAN OLAY — PROVA GEÇTİ, UYGULAMA DÜŞTÜ

DEPARTMAN/1'in SAHA göçünde `goc-provasi.sh` **GEÇTİ** (iki bağlam da
canlının kopyasında sorunsuz), hemen ardından `goc-uygula.sh` aynı göçü
canlıya uygularken **DÜŞTÜ**.

Sebep göç değildi. Prova betiği `dotnet ef`e `JWT_SECRET` veriyordu,
uygulama betiği vermiyordu. `AppDbContext`'in tasarım-zamanı fabrikası
olduğu için o adım her iki betikte de çalışıyordu; `HrDbContext`'in
fabrikası yoktu ve uygulamanın Host'una muhtaçtı.

**Provanın yeşili, tam da provanın uygulamadan FARKLI olduğu yerde
anlamsızdı.** Prova o farkı sınayamaz, çünkü fark provanın kendisinde.

### SONUCU ÖZELLİKLE SİNSİYDİ

Göç canlıya **uygulandı** (`Applying migration… Done.`), betik yine de
**çıkış 1** verdi. Yani araç, işin yapılmadığını değil, **yapıldığı
hâlde yapılmadığını** söyledi.

Kaydına güvenilemeyen bir dağıtım aracı, olmayan bir aracın iki katı
zararlıdır: olmayan araç sizi ölçmeye zorlar, yanlış konuşan araç
ölçtüğünüzü sanmanıza yol açar.

### ÖLÇÜM — "YARIM GÖÇ" SANILDI, DEĞİLDİ

Betiğin hatası "yarım kalmış göç" endişesi doğurdu. Ölçüm üç
bağımsız kanıtla aksini gösterdi:

| Ölçüm | Sonuç |
|---|---|
| HrDbContext göç dosyası / uygulanmış | 7 / **7** — bekleyen 0 |
| AppDbContext göç dosyası / uygulanmış | 200 / **200** — bekleyen 0 |
| Geçmişte olup dosyası olmayan (hayalet) | yok |
| `dotnet ef database update -c HrDbContext` | *"No migrations were applied. The database is already up to date."* |
| Servisler / sağlık / son 30 dk hata | active · 200 · **0 hata** |

Betik göçü UYGULARKEN değil, ikinci bağlamı **AÇARKEN** düştü — ve o
bağlamda uygulanacak hiçbir göç yoktu. Şema yarım kalmadı.

### İKİ DERS, İKİSİ DE BU DEPODA TEKRAR EDİYOR

1. **İki kopya zamanla ayrışır.** Prova betiği `JWT_SECRET`'in
   gerekliliğini biliyordu ve gerekçesini yorumuna yazmıştı; uygulama
   betiği aynı bilgiyi taşımıyordu. `GorevAtamaKurali`'nin doğuşu da,
   merkez kuralının PUT'ta ikinci bir kopya taşıması da aynı hataydı.

2. **Yamayı kök çözümle karıştırma.** İlk refleksim uygulama betiğine
   `JWT_SECRET` eklemekti. Bu YAMAYDI: asıl kusur, bir GÖÇÜN hiç
   kullanmadığı bir UYGULAMA SIRRININ varlığına bağlı olmasıydı. Göç
   şemayı taşır, kimlik doğrulamaz. Kök çözüm
   `HrDbContextFactory` — göç yolu artık yalnız `DB_CONNECTION`
   istiyor ve `JWT_SECRET` göç betiklerinden tamamen kalktı.

### UYGULANAN ÇÖZÜM — ÜÇ PARÇA

**1. Kök çözüm:** `HrDbContextFactory` (AppDbContextFactory ile aynı
desen, bilerek). Kanıt: `JWT_SECRET` ortamdan KALDIRILMIŞ hâlde
`dotnet ef database update -c HrDbContext` → çıkış 0.

**2. Ortak kod yolu:** `deploy/scripts/goc-ortak.sh` — tek `ef_kos()`,
tek ortam, tek bayrak kümesi, tek bağlam listesi (`GOC_BAGLAMLAR`).
Prova ve uygulama artık ikisi de onu çağırıyor; hiçbiri `dotnet-ef`i
doğrudan çağırmıyor.

**İKİNCİ AYRIŞMA DA BURADA KAPANDI:** prova `--no-build` ile diskteki
MEVCUT ikiliyi doğruluyor, uygulama ise YENİDEN DERLEYİP başka bir
ikiliden göç uyguluyordu. Kaynak arada değişmişse doğrulanan ile
uygulanan aynı değildi. Artık derleme BİR KEZ başta (`goc_derle`) ve
her iki taraf da aynı ikiliyi okuyor.

**3. Ön koşul denetimi:** `goc_onkosul_dogrula` — göçe BAŞLAMADAN önce
gerekli değişkenler ve HER İKİ bağlamın açılabildiği doğrulanıyor.

SAHA göçünde şema yarım kalmadı ama bu ŞANSTI: HrDbContext'in bekleyen
göçü yoktu. Sıra tersine olsaydı ya da o bağlamda bekleyen göç
olsaydı, gerçekten yarım kalırdı. **Yarıda düşen göç, hiç başlamayan
göçten pahalıdır.**

**SONDA P:** `HrDbContextFactory` çalışma anında hata fırlatacak hâle
getirildi (derleme geçiyor). Gözlem — ilan edildiği gibi:
`AppDbContext: açılabiliyor ✓`, `HATA: HrDbContext AÇILAMIYOR`,
`Göçe BAŞLANMADI`. Geri alındı, ikisi de açıldı, dosya bayt bayt aynı.

**MEKANİK KARŞILIĞI:** `GocBetikleriTutarliligiTests` (7 test) —
ortak `ef_kos` kullanımı, doğrudan çağrı yasağı, `JWT_SECRET`
istenmemesi, ön koşulun uygulamadan ÖNCE gelmesi (sıra testi), tek
derleme, tek bağlam listesi.

### SIR DENETİMİ — DÖNDÜRME GEREKMEDİ, KANITLANDI

Betikte bir `JWT_SECRET` değeri bulunması sır sızıntısı şüphesi
doğurdu. Ölçüm — değerler karşılaştırıldı, gerçek sır HİÇBİR YERE
basılmadan:

| Yer | Uzunluk | sha256(12) | Sonuç |
|---|---|---|---|
| Canlı `JWT_SECRET` | **128** | `2a4c657b0788` | — |
| `goc-uygula.sh` (commit'lenmemiş) | 43 | `86e35cecc5b2` | farklı |
| `goc-provasi.sh` | 44 | `0d9ff19da633` | farklı |
| `safe-deploy.sh` | 40 | `c99ab0465aaf` | farklı |
| `ci.yml` JWT | 54 | `92fd1145d2fb` | farklı |
| `ci.yml` DB parolası | 16 | `8d99a206267e` | farklı (gerçek 10 kr) |
| `README-KURULUM.txt` | 18 | `06bec4cf27e4` | yer tutucu |
| `TestWebApplicationFactory` | 56 | `344c74140bc5` | farklı |

**Gerçek sır ne çalışma ağacında ne git geçmişinde bulundu.** Yedi
adayın hepsi sahte → **döndürme gerekmedi.**

### SIR BEKÇİSİ NEDEN ÖTMEDİ — DESEN DEĞİL, YÜZEY

`SecretInSourceGuardTests` yalnız `EnderunAI.Api` ve
`EnderunAI.Api.Tests` altındaki **`.cs`** dosyalarını tarıyor.
`deploy/scripts/*.sh`, `scripts/*.sh` ve `.github/workflows/*.yml`
kapsam DIŞINDA.

Desen kusurlu değil: `goc-provasi.sh`'deki 44 karakterlik değer
bekçinin aradığı biçime UYUYOR — bir `.cs` dosyasında olsaydı
yakalanırdı. Eksik olan **kapsanan yüzey**. Kabuk betikleri, tıpkı
`enderun-backup.sh`'ın şifreleme muhafızından önceki hâli gibi,
testsiz bir yüzey.

**AÇIK İŞ:** bekçiye `.sh` / `.yml` yüzeyini eklemek.

---

## BEKLEYEN KARARLAR

Yapılmayan işler ve nedenleri. Biçim: `konu | neden yapılmadı | ne gerekiyor`

**2026-08-25'te 13 maddenin 9'u karara bağlandı** (aşağıda "KAPANANLAR").
Eşzamanlılık maddesi aynı gün paket olarak kapatıldı. Açık kalan
**11 madde** (6–11. maddeler 2026-08-26/28'de eklendi):

0. **KULLANICI HESAPLARI — M3'ÜN ÖN KOŞULU** (2026-09-03, KAPI 1'de
   karara bağlandı; ölçüm bu maddede) | Karar verildi, **paket
   yazılmadı** | M3/3'ten ÖNCE bitmeli.

   **KARAR (Mehmet):** önce **ofis + şef/formen** (~15-20 kişi). Saha
   ekiplerine şefleri üzerinden ulaşılır; herkese hesap ileride ayrı
   konu. Gerekçe: *"M3 mesajlaşma bugün 4 kullanıcı arasında kurulur;
   kitle olmadan mobil öncelikli tasarımın karşılığı yok."*

   **ÖLÇÜM TAHMİNİ DÜZELTTİ — İŞ SANILDIĞINDAN KÜÇÜK:**

   | Ölçüm | Sonuç |
   |---|---|
   | Kullanıcı hesabı | **13 var** (4 aktif: `mehmet`, `smemis`, `uakkaya`, `vtepe`) |
   | Pasif hesap | **9** — ve **rolleri zaten atanmış** |
   | Personel↔kullanıcı bağı | **0 / 13** |
   | Ofis + şef/formen kohortu | **10 kişi** (P0001–P0007, P0009, P0058, EMP-001) |

   Yani iş büyük ölçüde **hesap açma değil, ETKİNLEŞTİRME**. Pasif 9
   hesabın rolleri hazır: Sekreterya, Teknik Ofis ×3, Formen, Finans
   Sorumlusu + İK Sorumlusu, Teknik Koordinatör ×2, Araç Sorumlusu.

   **BAĞ KURULABİLİR (ölçüldü):** kullanıcı adı deseni "ad baş harfi +
   soyad". Bu kuralla **13 kullanıcının 10'u** bir personel kaydıyla
   eşleşiyor (P0001–P0009 ve P0045). Eşleşmeyen 3: `mehmet` (yönetici
   hesabı), `ioktem`, `uakkaya` — bu ikisinin soyadıyla **hiç personel
   kaydı yok**.

   **DİKKAT ÇEKEN:** `uakkaya` **aktif** ve **beş rol** taşıyor (Finans,
   Satın Alma, Ön Muhasebe, İK, İSG) ama personel kaydı yok. Yanlış
   olmayabilir (dışarıdan muhasebeci olabilir) — ama bilinerek mi böyle,
   kayıtta yazmıyor.

   **BANA DÜŞEN, KARAR SENDE:** eşleşen 10 bağ **otomatik yazılmasın**
   önerisi — yanlış bir bağ maaş görünürlüğü ve veri kapsamı demek.
   Ekranda "önerilen eşleşme" olarak gösterilip tek tek onaylanması
   daha ucuz ve geri alınabilir. Tohumlama için verdiğin gerekçenin
   aynısı: yanlış olanı kimse fark etmez.

   **HESABI OLMAYAN KOHORT ÜYESİ: 2** — P0058 (ŞOFÖR / MERKEZ OFİS) ve
   EMP-001 (Elektrik Ustası). Kalan 8 kohort üyesinin hesabı var.

1. **KVKK aydınlatma metni** | Kural (e): hukuk metni yazılmıyor |
   Metin Mehmet'te. **Bana düşen: ekranda yerini açmak** —
   "metin bekleniyor" yer tutucusuyla. M3/2'de yapılacak.

2. **Mesaj saklama süresi** | 12 ay çevrimiçi + **süresiz arşiv**
   kalıyor, silme mekanizması KURULMADI | Mehmet hukukçuya soracak:
   ticari kayıt saklama süreleri ile KVKK'nın "gereğinden uzun tutma"
   ilkesi çakışabiliyor. Karar gelince eklenir.

3. **Uzak yedek hedefi** | Betik yazıldı ama `UZAK_YEDEK_ETKIN`
   KAPALI; yurt dışı aktarımı hukukçu cevabına kadar kapalı kalacak |
   **TÜRKİYE HEDEFİ ONAYLANDI** (Mehmet, 2026-08-25): fiyat karar
   ekseni değil, yargı yetkisi. Sağlayıcılara iletilecek soru listesi
   hazır: `ops/belgeler/uzak-yedek-saglayici-sorulari.md` — beş eleme
   şartı, birincisi silme yetkisi olmayan anahtar / nesne kilidi.
   Teklifler Mehmet'te.

4. **Malzeme ve yedek parça zimmette gider yazsın mı** |
   VARSAYIMLA ilerlendi: `Material` ve `SparePart` tükenir sayıldı,
   yalnız `Equipment` dayanıklı | Mehmet'in teyidi. Ters ise
   `ZimmetGiderKurali` içinde tek satır değişir; geçmiş kayıtlar
   etkilenmez çünkü henüz zimmet verilmedi.

5. **§7 personel testi kararsızlığı** | Personel testlerindeki
   ~dörtte bir düşme hâlâ açık; §7b tarih çakışması bunun PARÇASI
   DEĞİLDİ | Ayrı teşhis turu — **M3/2'den sonra**.

6. **EF MIGRATION ARŞİVİNİN BİRLEŞTİRİLMESİ (squash)** |
   Kural (b): geri döndürülemez migration işlemi, kendi başıma
   yapılmıyor | **Mehmet'in kararı bekleniyor.**

   **ÖLÇÜM (2026-08-26):** derlenen kaynağın **%92'si migration.**

   | | |
   |---|---|
   | Toplam derlenen kaynak | 88,4 MB |
   | Migration `.Designer.cs` | **81,5 MB (195 dosya)** |
   | Gerçek uygulama kodu | 5,0 MB |

   Her `.Designer.cs` modelin TAM anlık görüntüsünü taşıyor ve model
   büyüdükçe her yeni migration onu bir kez daha kopyalıyor:
   `InitialCreate` **4 KB**, ortadaki **447 KB**, sonuncusu
   **744 KB**. Roslyn'e verilen şey 195 kez kopyalanmış aynı şema.

   **SONUCU SOMUT:** tek `csc.dll` süreci derlemede **4,9 GB**
   tutuyor. 7,7 GB'lık makinede bu, canlı uygulamayla aynı yerde
   koşan test turunu sürekli sınırın dibinde tutuyor (bkz. Kural 29,
   29a). Eğri de düz değil — her yeni migration ~744 KB ekliyor.

   **RAM EKLEMEK ÇÖZÜM DEĞİL:** semptomu satın alır, eğriyi
   durdurmaz. Ölçüm "8 GB yetersiz" demiyor, "bu derleme anormal"
   diyor.

   **RİSK:** dosya tarafını birleştirmek canlı
   `__EFMigrationsHistory` tablosuyla uyumsuzluk üretirse yeni bir
   makinede kurulum "migration bulunamadı" ile düşer. Kendi yedeği
   ve geri yükleme tatbikatıyla, AYRI bir paket olarak ele
   alınmalı — başka bir işin içine sıkıştırılmamalı.

7. **INDEKS/1 — MODELİN VAR SANDIĞI 14 EKSİK İNDEKS** |
   Ayrı paket, KURULUM/1'e karıştırılmadı | **Ölçüldü
   (2026-08-27), yapılmadı.**

   Model bu indekslerin var olduğunu sanıyor; **canlıda hiçbiri
   yok**:

   ```
   IX_cargo_shipments_ProjectId
   IX_document_attachments_CompanyId
   IX_document_workflows_CompanyId
   IX_hr_job_applications_CandidateId
   IX_incoming_documents_CategoryId
   IX_incoming_documents_ProjectId
   IX_outgoing_documents_CategoryId
   IX_outgoing_documents_ProjectId
   IX_phone_notes_ProjectId
   IX_progress_payments_CompanyId_Status_ProgressPaymentDate
   IX_purchase_orders_SupplierCurrentAccountId
   IX_secretariat_schedule_entries_CompanyId_Type_Status_StartAtUtc
   IX_secretariat_schedule_entries_ProjectId
   IX_visitor_records_ProjectId
   ```

   **ANLIK GÖRÜNTÜ BU İNDEKSLERİ "VAR" SAYDIĞI İÇİN D1 SONSUZA
   KADAR YEŞİL VERİR.** D1 modeli anlık görüntüyle karşılaştırır,
   veritabanıyla değil (bkz. Kural 46).

   Bugün tablolar küçük olduğundan görünmüyor; **veri büyüdükçe
   sessiz tam tablo taramaları olarak ortaya çıkacak.** Çoğu
   `ProjectId` gibi yabancı anahtar indeksi.

   Ayrışmanın diğer 4 kalemi (elle başka adla var olanlar) HR
   bölgesinde ve **KURULUM/1'de kapandı**.

8. **KURULUM/1'DEN ARTAN BENZERSİZLİK KALEMLERİ** |
   Yeni eksen (Kural 49) listeyi yeniden eledi; bunlar karara
   bağlanmadı | **Ölçüldü, yapılmadı.**

   **(i) PAKETTEN ÇIKAN İKİ KALEM — yeni eksenle yeniden
   değerlendirilecek:**
   - `tool_service_requests (CompanyId, RequestNumber)` — bugün
     SÜZGEÇLİ, belge numarası olduğu için süzgeçsiz olmalı gibi
     görünüyordu; **0 satır**.
   - `vehicles (CompanyId, PlateNumber)` — bugün SÜZGEÇLİ, plaka
     dış kimlik olduğu için süzgeçsiz olmalı gibi görünüyordu;
     **0 satır**.

   İkisi de boş olduğu için **bugün hiçbir pratik etkileri yok**.
   Sıkılaştırma yönünde oldukları için çakışma riski de yok
   (ölçüldü: tekrar eden anahtar yok).

   **(ii) 16 `LineNumber` KALEMİ** — belge içi satır sırası.
   `LineNumber` bir kimlik değil **SIRA**dır; belge satırı
   silinince numaranın yeniden kullanılması normal olabilir.
   Ayrı ölçüm ister: satır numarası dışarıya (rapor, PDF, karşı
   taraf) taşınıyor mu?

   **(iii) 14 KARIŞIK KALEM** — dönem/numara bileşimleri
   (`progress_payments`, `project_measurements`,
   `AccountingPeriods`, `hr_performance_reviews`,
   `subcontractor_contracts` …). Aralarında **tutarsızlık** var:
   aynı kavram iki farklı şekilde kurulmuş — sözleşme numarası
   `subcontractor_contracts`'ta süzgeçsiz, `isg_osgb_contracts`'ta
   süzgeçli.

   **DÖRT ÖKSÜZ TABLO ölçümü YAPILDI** — bkz. §5c (üçü boş, biri
   tek satır, yolu kaynakta bulunamadı).

9. **SQUASH/1 — ERTELENDİ (İPTAL DEĞİL)** |
   Gerekçesi ortadan kalktı, bedeli fazla | **Ölçüldü
   (2026-08-27), yapılmadı.**

   Squash'ın gerekçesi derleme belleğiydi; o sorun makine
   sertleştirmesiyle çözüldü (takas, `OOMScoreAdjust`, bellek
   tavanı, `UseSharedCompilation=false`, tek-örnek kapısı) ve son
   temiz derleme sorunsuz geçti. Squash artık **onarım değil
   iyileştirme**, ve bedeli 116 ham SQL nesnesini elle taşımak.

   **ÖLÇÜMÜN ÜRETTİĞİ ASIL BULGU — EF MODELİ, ŞEMANIN BÜYÜK BİR
   KISMININ KAYNAĞI DEĞİLDİR.**

   | | |
   |---|---|
   | Modelden üretilen şema betiğinin indeksi | **491** |
   | Canlıdaki indeks (PK hariç) | **594** |
   | **Canlıda var, modelde YOK** | **116** |

   Bu 116 nesne yalnızca göçlerin **ham SQL bloklarında** yaşıyor;
   model ve anlık görüntü onlardan habersiz. Aralarında:
   `IX_cheques_aktif_benzersizlik` (çek mükerrerlik koruması),
   KURULUM/1'de kurulan üç süzgeçli benzersizlik,
   `IX_hr_salary_definitions_Personnel_Start`,
   `IX_sales_invoices_..._OfficialInvoiceNumber`.

   Modelden üretilecek bir temel göç bu 116 korumayı **sessizce
   düşürür**. Z1 kabul şartı bunu yakalar — süreç doğru kuruldu.

   **SIRA:** önce ham SQL yüzeyini küçült (**SEMA-KAYNAK/1**),
   sonra kalan için temel göçü **CANLI ŞEMADAN** üret.
   **Modelden üretip 116 nesneyi elle eklemek YAPILMAYACAK** —
   elle aktarım tam da sessiz eksik üreten yöntemdir.

10. **SEMA-KAYNAK/1 — HAM SQL YÜZEYİNİ KÜÇÜLT** | Öncelik düşük |
   **Ölçüldü, yapılmadı.**

   116 nesnenin türe göre dağılımı ölçüldü:

   | Tür | Adet | Modele taşınabilir mi |
   |---|---|---|
   | Düz indeks | **100** | **Evet** — `HasIndex` |
   | Kısmi (`WHERE`) | **11** | **Evet** — `HasFilter` |
   | İfade/fonksiyon tabanlı | 2 | Hayır |
   | `gin`/`gist` (trigram) | 3 | Hayır |

   **116'nın 111'i taşınabilir.** Kalan 5 (artı `enderun_fold`
   fonksiyonu, `pg_trgm` uzantısı ve üretilmiş sütunlar) ham SQL
   kalır ve bu **bilinçli bir istisnadır** — DURUM.md'de
   listelenecek.

   Bu paket bittiğinde squash ucuzlar.

11. **ÖDEME PLANINDA "ONAYDAN GERİ ÇEKME" YOLU** | Şimdi
   açılmadı, ihtiyaç ölçülmedi | **Karar bekliyor.**

   Plan onaya sunulduktan sonra **satır EKLEME/SİLME kapalı** (D2):
   yeni satır K2'nin göremediği yerden girer, çünkü
   karşılaştırılacak bir onay anlık görüntüsü yoktur.

   **Satır DÜZENLEME ise açık** (D1) — değişen satırın onayı K2
   gereği düşer, diğerleri etkilenmez.

   Unutulan bir ödemenin yolu bugün **K5** (plan dışı ödeme):
   sebebi zorunlu, ertesi haftanın planının başında listeleniyor.

   **K5 VARKEN GERİ ÇEKME YOLUNUN GEREKLİ OLUP OLMADIĞI
   ÖLÇÜLMEDİ.** Birkaç hafta gerçek kullanımdan sonra belli olur;
   şimdiden yazmak, kullanılmayabilecek bir kapıyı bakım yüküne
   çevirir.

### ERTELENENLER (kapanmadı, sıraya girmedi)

- **Disk şifrelemesi** | Yeniden kurulum ve bakım penceresi
  gerektiriyor. Yedek şifrelemesi + sunucu dışına kopyalama aynı
  riskin daha büyük kısmını daha ucuza kapatıyor | Madde açık kalıyor,
  bugün sırada değil.
- **Sipariş PDF'i** (28 Temmuz dallarından) | Küçük ve faydalı; ayrı
  madde olarak bekliyor.

### KAPANANLAR (2026-08-25)

| Madde | Karar |
|---|---|
| `bank_account.view` kimde | **Yalnız Finans.** İK bordroyu hazırlar, ödemeyi Finans yapar |
| Tam IBAN anahtarı | **Ayrı `bank_account.reveal`** — maskeli görmek ile açmak farklı yetki |
| Hesap planı aktarımı | Mevcut kod GÜNCELLENMEZ, üst hesap OLUŞTURULMAZ, ayrı `chart.import` anahtarı |
| Depodan Zimmet | Stoktan düşer, şirket varlığından çıkmaz ("zimmet" konumu); fiş TÜRE GÖRE |
| DB bağlantı kaydı | `d54a9467` ile kapandı — listeden düşürüldü |
| 28 Temmuz dalları | **Yeniden yazılmayacak.** Proje bütçesi = boşluk analizi 1 numara (taahhüt bütçe kontrolü), ayrı paket. Hızır eylem motoru kapsam dışı |
| Yarım koşu tespiti | safe-deploy'a **eklenecek** |
| Anahtar kopyası | Sunucu + parola yöneticisi + kasada basılı — onaylandı |
| Mesaj arşivinden silme | Şimdilik silme kurulmuyor (yukarıda madde 2 olarak açık) |
| Stok kapılarında eşzamanlılık | **Kapandı.** Kilit `IStokSatirKilidi`'ne taşındı, altı mutasyon noktasının hepsi kullanıyor, nöbetçi metot bazında tarıyor |

## KARAR KAYDI

Kendi verdiğim iş kuralı kararları. Teknik kararlar (test, indeks,
isimlendirme) buraya yazılmaz.

`tarih | konu | karar | dayandığım varsayım | geri alması kolay mı`

- `2026-08-24 | Banka hesabı izni | Yeni dar anahtar bank_account.view açıldı, YALNIZ Admin+GM'e (yansımayla). Finans/İK'ya verilmedi. | IBAN kitlesini genişletmemek, bordro engelini kaldırmaktan öncelikli (kural c). | EVET — RoleCatalog'a iki satır`
- `2026-08-24 | IBAN maskeleme | Liste ucunda son dört hane; tam IBAN ayrı uçtan, tek hesap, her çağrı denetim kaydına. Kayda IBAN yazılmıyor. | Banka adı + hesap sahibi + son dört hane, ödeme ekranında hesabı ayırt etmeye yeter. | EVET`
- `2026-08-24 | Ödeme eylemi görünürlüğü | bank_account.view olmayan rolde "Gerçek Ödeme" düğmesi HİÇ render edilmiyor (403 yerine yokluk). | Bozuk ekran göstermek, eylemi gizlemekten kötü. | EVET`
- `2026-08-24 | Kapsam alanı eksik satır | hr-dashboard ve zimmet kutusunda "alan yoksa satırı al" deseni "alan yoksa ELE" olarak değişti. | Şirket izolasyonunda varsayılan kapalı olmalı; bugün tek şirket olduğu için görünür etki yok. | EVET`
- `2026-08-24 | Yarım özellikler | ai-analysis/site-analysis servisleri ve fiyat farkı hesaplama işlevleri SİLİNDİ (ekranda karşılığı yoktu); hesap planı aktarımı KALDIRILMADI, devre dışı + "Hazırlanıyor". | Ekranda görünen yarım özellik kaldırılmaz, görünmeyen ölü kod silinir. | EVET — git geçmişi`
- `2026-08-25 | Mesaj erişimi | Üyelik kapısı; global veri kapsamı BU KAPIYI AÇMAZ, GM dahil kimse başkasının konuşmasını okuyamaz. | Erişim politikası kararı açıktı; tutarlılık uğruna kısayol eklemek politikayı kâğıt üstünde bırakırdı. | HAYIR — kısayol eklenirse geçmiş okumalar geri alınamaz`
- `2026-08-25 | Ayrılan üye | Hiçbir şey göremez; ayrıldığı tarihe kadarki mesajları da göremez. | "Ayrıldığı tarihe kadar görür" kuralı departman kanalları için konuşulmuştu; kanallar M3/3'te. Dar olan seçildi. | EVET`
- `2026-08-25 | Okundu bilgisi | Mesaj başına satır değil, üye satırında LastReadAtUtc. | "Hangi mesajı tam okudu" bilgisine ihtiyaç duyan gereksinim yok; ayrı tablo mesaj×üye kadar satır üretirdi. | EVET — ayrı tablo sonradan eklenebilir`
- `2026-08-25 | Arşiv biçimi | Aynı veritabanında soğuk tablo; aranabilir değil, okunabilir. Taşıma mekanizması KURULMADI. | Dosyaya çıkarmak yedek/geri yükleme yüzeyini ikiye böler. | EVET`
- `2026-08-25 | bank_account.view kitlesi | YALNIZ Finans Sorumlusu. İK Sorumlusu IBAN görmez; bordroda "Gerçek Ödeme" onda çalışmaz — bu eksik değil, doğru ayrım. | İK bordroyu hazırlar, ödemeyi Finans yapar; IBAN İK'nın işini yapmak için gerekli değil. | EVET`
- `2026-08-25 | Tam IBAN yetkisi | Ayrı bank_account.reveal anahtarı; maskeli görmek ile tam IBAN'ı açmak farklı yetkilerdir. Denetim kaydı her açılışta yazılmaya devam eder. | Kural (c): en dar seçenek. | EVET`
- `2026-08-25 | Hesap planı aktarımı | Mevcut hesap kodu gelirse GÜNCELLENMEZ — satır atlanır, raporda "zaten var" listelenir. Üst hesap yoksa OLUŞTURULMAZ — hata verilir, satır atlanır. İzin: ayrı chart.import, muhasebe yönetimi düzeyinde. | Muhasebe hesabını aktarımla değiştirmek ciddi iş, elle yapılmalı; sessizce hiyerarşi üretmek hesap planını bozar ve fark edilmez. | EVET — aktarım satır atlar, veri değiştirmez`
- `2026-08-25 | Depodan Zimmet: stok | Stoktan DÜŞER ama şirket varlığından ÇIKMAZ: "zimmet" konumuna taşınır. İade edilince geri döner. | Üç seviyeli konum yapısı buna uygun; depo stoğu doğru görünür, malzeme kaybolmaz. | EVET — konum taşıma geri alınabilir`
- `2026-08-25 | Depodan Zimmet: muhasebe | Fiş TÜRE GÖRE. Sarf kategorisi → çıkışta gider yazılır (150/740 deseni). Dayanıklı taşınır → gider YAZILMAZ, demirbaş/zimmet kaydı olarak durur; amortisman varsa oradan yürür. | Mevcut muhasebe kuralının aynısı; kategori zaten stok kartında var, yeni alan açılmıyor. | HAYIR — gider yazılan fiş muhasebe kaydıdır`
- `2026-08-25 | Mesaj arşivi | Süresiz arşiv; silme mekanizması KURULMADI. | Ticari kayıt saklama süreleri ile KVKK "gereğinden uzun tutma" ilkesi çakışabiliyor; hukukçu cevabı bekleniyor. | EVET — silme sonradan eklenebilir, silinen veri geri gelmez`
- `2026-08-25 | Zimmet gider ekseni | Karar InventoryItem.Type üzerinden veriliyor (Ekipman hariç her tür tükenir), InventoryCategory.AccountingKind'e ÜÇÜNCÜ DEĞER EKLENMEDİ. | O enum'u ikili varsayan 15 çağrı yeri var; üçüncü değer mal kabul, sayım ve mutabakat muhasebesini de sessizce kaydırırdı. Type alanına bugün hiçbir muhasebe kararı bağlı değil. | EVET`
- `2026-08-25 | Malzeme/yedek parça sınıflandırması | Material ve SparePart TÜKENİR sayıldı (gider yazılır); yalnız Equipment dayanıklı. | Malzeme işin içine giriyor, yedek parça takıldığında bitiyor. VARSAYIM — Mehmet'in onayı alınmadı, ters ise tek satır değişir. | EVET`
- `2026-08-25 | Zimmet iptali | Gerekçe ZORUNLU; iptal ayrı uçta, denetim kaydında iadeden ayrı eylem adıyla. | İptal en çok suistimal edilebilecek eylem: malzeme kişide kalırken kayıt kapatılmış görünebilir. | EVET`
- `2026-08-25 | Zimmet uçlarının izni | Yeni anahtar açılmadı, inventory.edit kullanılıyor. | Yeni anahtar kitle kararıdır; mevcut anahtar kitleyi değiştirmiyor. | EVET`

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

**G3 — KAPSAM (ŞİRKET İZOLASYONU) AÇIĞI (2026-08-22).**

TARAMA: `CompanyId` taşıyan 96 varlık var; kontrolcü ve servislerde bu
varlıklara 462 okuma yapılıyor ve **439'unda kapsam süzgeci
(`ApplyScope`) YOK**. Kapsam pratikte yalnız satın alma ailesinde
uygulanıyordu. GET uçları: 2 yeşil, 26 sarı (zorunlu parametre ya da
rota kimliği), **22 kırmızı** (isteğe bağlı/süzgeçsiz).

**G3/1a — YAYINDA.** Cırcır bekçisi + ek ücret ucu.

- `CoverageBaselineTests` + `kapsam-temel-cizgi.txt` (**439 satır**).
  Üç test: (a) temel çizgide olmayan yeni kapsamsız okuma eklenemez,
  (b) toplam sayı artamaz, (c) kapatılan satır çizgiden silinmezse
  düşer — dosya borcun GERÇEK boyutunu göstermek zorunda, yoksa araya
  sessizce yenisi girer. Gerekçeli istisnalar AYRI listede ve gerekçe
  alanı zorunlu.
- `hr-compensation-components` (ek ücret = maaş bilgisi): kapsam
  süzgeci liste VE tekil kayıt ucunda; sunucu sayfalaması; katlanmış
  sunucu araması; `IX_hr_compensation_components_liste`.
  ÖLÇÜLDÜ (10.000 satır): 1. sayfa 5,3 ms → **0,068 ms**.

**G3/1b — SIRADAKİ:** para/maaş uçları (FinanceDashboard ×3,
RetailSales ×2, ProgressPayments/previous-context,
ProjectCostTransactions ×2) ve para/maaş dışa aktarımları
(HakedisExport ve `File(` uçları). Her uç için A/B şirketi testi
(liste + tekil + dışa aktarım) ve "kim ne kaybedecek" listesi.

**G3/2, G3/3, G3/4 — ERTELENDİ (Mehmet Karacabey kararı).**

Kapsam açığı gerçek ama bugün GİZİL: canlıda tek şirket ve dört
kullanıcı var, dördü de global kapsamlı. Kapsam süzgeci bugün fiilen
hiçbir veriyi ayırmıyor; kapsamsız uçlardan şu an kimseye sızan bir şey
yok. Sızıntı, ikinci şirket eklendiği ya da kapsamı sınırlı bir
kullanıcı tanımlandığı gün başlayacak.

Bu nedenle: G3/1a çıktı (bugünden itibaren YENİ kapsamsız okuma
eklenemiyor, borç 439'da sabit), G3/1b yapılıyor (ikinci şirket
eklendiği gün en pahalıya patlayacak olanlar bunlar; ücret ve para
verisi sızdığında geri alınamaz), G3/2-3-4 cırcırın koruması altında
bekliyor.

**ERTELEMEYİ SONA ERDİRECEK KOŞUL** — ikisinden biri gerçekleşirse
G3/2-3-4 derhal öne alınır ve F4 dahil her şeyin önüne geçer:
  1) Sisteme ikinci bir şirket eklenmesi,
  2) Kapsamı sınırlı (global erişimi olmayan) bir kullanıcı tanımlanması.
Bu iki olay TETİKLEYİCİDİR; kapsam paketleri tamamlanmadan ikinci
şirket canlıya ALINMAMALIDIR.

Erteleme "bu iş gereksiz" demek değildir; "bugün zarar üretmiyor ve
cırcır büyümesini durdurdu, sırası bekleyebilir" demektir.

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

19. **PANO/ÖZET UÇLARINDA KAPSAM TESTİ, SATIR KÜMESİNİ DEĞİL HER
    METRİĞİ AYRI AYRI DOĞRULAR.** Tek bir toplam kontrolü yeterli
    değildir — bir metrik süzgeçten kaçarsa diğerleri doğru olduğu için
    gözden kaçar.

    G3/1b'de ölçüldü: `financial-dashboard` ucunda ciro doğru çıkarken
    gider 25.000 yerine 85.000 gösteriyordu. Ciro `ProgressPayments`
    üzerinden, gider ise `ProjectCostTransactions` + `ExpenseEntries`
    üzerinden geliyor — üç ayrı sorgu yolu, üçü de ayrı ayrı sızabilir.
    Test yalnız ciroyu doğrulasaydı yeşil kalırdı.

    Panoda sızıntı SATIR olarak görünmez, RAKAM olarak görünür: ekranda
    tanımadığın bir kayıt durmaz, yalnız toplam sessizce büyür. Bu
    yüzden pano uçlarında süzgeç, TOPLAMA SORGUSUNUN İÇİNDE olmak
    zorundadır; sonuç üzerinde ayıklama yapılamaz.

    Test biçimi: A şirketinin rakamları okunur → B şirketine veri
    EKLENİR → A'nın rakamları yeniden okunur ve HİÇBİRİ DEĞİŞMEMELİDİR.
    "Toplam şu sayıya eşit" demek kırılgandır (veritabanında başka
    testlerin kayıtları da var); DEĞİŞMEZLİK kesindir. Yanına bir de
    "süzgeç her şeyi silmiyor" kontrolü konur — yoksa her zaman 0
    döndüren bir sorgu da testi geçerdi.

20. **DIŞA AKTARIM UCU LİSTE UCUNDAN AYRI KODDUR — AYRI SÜZGEÇ, AYRI
    TEST.** Liste ucunu kapsamlamak dışa aktarımı kapsamlamaz: dışa
    aktarım kendi sorgusunu kurar ve kaydı çoğu zaman doğrudan KİMLİKLE
    çeker. G3/1b'de `hakedis-export` tam olarak böyleydi — liste
    süzülmüş olsa bile kullanıcı listede hiç göremediği bir hakedişin
    Excel'ini indirebiliyordu. Her modülde ikisi ayrı ayrı kontrol
    edilir ve ayrı testleri olur. Sonda ile kanıtlandı: yalnız dışa
    aktarımın süzgeci kaldırıldığında liste testi YEŞİL kalıyor.

21. **VERİ DÜZELTEN HER MIGRATION ETKİ SAYISINI DOĞRULAR.**

    ŞEMA migration'ı çalıştıysa çalışmıştır: kolon ya eklenir ya
    hata verir. VERİ migration'ı ise çalışıp HİÇBİR ŞEY YAPMAMIŞ
    olabilir ve yine "SUCCESS" döner.

    2026-08-23'te tam olarak bu oldu: M1/1 migration'ı mevcut tek
    görev kaydını iptal edecekti, `WHERE` koşuluna
    `AssignedByUserId IS NULL` de koymuştum, Hızır kaydı oluştururken
    göndereni DOLDURUYOR — güncelleme 0 satıra dokundu, migration
    başarıyla tamamlandı ve kayıt bozuk kaldı. Uygulama sonrası
    canlıda ölçmeseydim sessizce onay kuyruğunda, kimin bitirdiği
    belirsiz bir satır olarak duracaktı.

    KURAL — UPDATE/INSERT içeren her migration için:

      a) **Beklenen satır sayısı migration dosyasına yorum olarak
         ÖNCEDEN yazılır.** Sayıyı sonradan "her neyse o" diye kabul
         etmek, doğrulamayı doğrulama olmaktan çıkarır.

      b) **Migration'ın KENDİSİ doğrular** ve tutmazsa patlar:

         ```sql
         DO $$
         DECLARE etkilenen integer;
         BEGIN
             UPDATE ... ;
             GET DIAGNOSTICS etkilenen = ROW_COUNT;

             -- IDEMPOTENT TEKRAR: ikinci çalıştırmada 0 beklenir,
             -- çünkü iş zaten yapılmıştır. Ayrım "hedef durumda
             -- kaç satır var" ile yapılır, etkilenen sayısıyla değil.
             IF etkilenen <> BEKLENEN
                AND NOT EXISTS (SELECT 1 FROM ... WHERE hedef_durum)
             THEN
                 RAISE EXCEPTION
                     'VERİ MIGRATION DOĞRULAMASI: beklenen %, etkilenen %.',
                     BEKLENEN, etkilenen;
             END IF;
         END $$;
         ```

         Desen denendi: koşulu tutmayan bir UPDATE ile migration
         gerçekten patlıyor.

      c) **Uygulandıktan sonra gerçek sayı ÖLÇÜLÜR ve rapora yazılır.**

      d) **Beklenen ile gerçek tutmuyorsa bu bir HATADIR** — "geçti"
         denmez, migration başarılı görünse bile.

22. **MIGRATION `postgres` İLE UYGULANIRSA YENİ TABLOLARIN SAHİBİ
    YANLIŞ OLUR — VE YEDEK ALINAMAZ HALE GELİR.**

    2026-08-23: M1/1 migration'ını `sudo -u postgres psql` ile
    uyguladım. Yeni üç tablonun (`attachments`, `task_comments`,
    `notification_recipients`) sahibi `postgres` oldu; sistemdeki
    diğer 229 tablonun sahibi `enderun_user`.

    SONUÇ: `enderun-backup.sh` yedeği `enderun_user` ile alıyor ve
    `pg_dump` bir sonraki çalıştırmada **"permission denied for table
    attachments"** ile PATLADI. Yani canlı sistem birkaç saat boyunca
    YEDEKSİZ kaldı — ve bunu ancak bir sonraki deploy'un yedek adımı
    gösterdi.

    Daha önceki migration'larda sorun çıkmamasının sebebi yalnızca
    KOLON eklemiş olmam; sahiplik yalnız yeni TABLO oluşturulduğunda
    devreye giriyor.

    KURAL — tablo oluşturan bir migration `postgres` ile uygulandıysa:

      a) Uygulama biter bitmez sahiplik düzeltilir:
         `ALTER TABLE <tablo> OWNER TO enderun_user;`

      b) Doğrulama sorgusu koşulur — sıfır dönmeli:
         ```sql
         SELECT count(*) FROM pg_tables
         WHERE schemaname = 'public' AND tableowner <> 'enderun_user';
         ```

      c) **Yedek deploy'dan ÖNCE bir kez alınır ve BAŞARILI olduğu
         görülür.** Yedeğin kendisi de bir doğrulama adımıdır: bu
         hatayı yakalayan tek şey oydu.

      d) Aynı düzeltme TEST veritabanında da yapılır; yoksa aynı hata
         orada uykuda bekler.

23. **"HİÇ ÇAĞRILMADI" İDDİASI SAYAÇLA KANITLANIR, FIRLATAN SAHTEYLE
    DEĞİL.** 2026-08-24, günlük özet kuru koşusu. "dryrun'da SMTP
    istemcisi hiç çağrılmasın" iddiasını sınamak için `SendAsync`
    çağrılınca hata fırlatan bir sahte istemci yazdım. Sonda —
    dryrun'daki `return`'ü kaldırıp gönderim yolunu bilerek açmak —
    testi **kırmadı**:

    - `RunAsync` her alıcıyı kendi `try/catch`'inde çalıştırıyor
      (kural: "bildirim yazma işin kendisini çökertmesin"),
    - fırlatılan hata orada yutuldu,
    - gönderim başarısız sayıldığı için `gonderilen` sayacı artmadı,
    - test `Assert.Equal(0, gonderilen)` diyordu ve YEŞİL kaldı.

    Yani sahtenin fırlattığı hata, sınamak istediğim davranışın
    kanıtı değil, sistemin dayanıklılık mekanizmasının yemi oldu.
    Sonda olmasaydı bu testin bir şey ölçmediğini hiç göremezdim.

    **KURAL:** Hata yutan (`try/catch`, `Polly`, `ContinueWith`)
    bir yolda "şu bağımlılık çağrılmadı" iddiası, yalnızca
    **çağrı sayacı** ile sınanır — sayaç yutulamaz. Fırlatan sahte,
    ancak hatanın yukarı çıktığı kanıtlanmış yollarda geçerlidir.
    Genel biçim: bir iddiayı sınayan sahte, iddianın ihlalini
    başarısızlık yoluyla değil, GÖZLEM yoluyla göstermelidir.

24. **SONDA TUZAĞINDA YOLLAR MUTLAK OLUR — `cd` GERİ ALMAYI SESSİZCE
    ÖLDÜRÜR.** 2026-08-24, M1/6. Sonda düzeneğim şuydu:

    ```bash
    cd frontend/enderun-ai && F="app/gorevler/[id]/page.tsx"
    cp "$F" "$F.probe-bak"
    trap 'mv -f "$F.probe-bak" "$F" 2>/dev/null; echo GERI-ALINDI' EXIT
    sed -i '...' "$F"
    cd /var/www/enderun-ai/backend && dotnet test ...
    ```

    `trap` EXIT'te koşuyor, ama o an ÇALIŞMA DİZİNİ `backend`.
    Göreli yol oradan çözülüyor, `mv` hedefi bulamıyor,
    `2>/dev/null` hatayı yutuyor ve **`echo GERI-ALINDI` yine de
    basılıyor**. Yani düzenek "geri aldım" diye rapor ederken
    sabotaj ağaçta kalıyor.

    Sonuç iki katmanlı oldu:
      - Sabotaj ağaçta kaldı ve SONRAKİ iki sonda üstüne yazdı;
        ikincisinin `cp ... .probe-bak` yedeği artık KİRLİ bir
        kopyaydı, yani geri alma noktası da bozulmuştu.
      - Bekçi testi "yakalamıyor" sanıldı. Yakalıyordu: ilk sabotaj
        `entityType={"X" as never}` biçimindeydi ve bekçinin deseni
        `entityType="X"` arıyor. İkinci sabotaj hiç uygulanmadı
        (aradığı metin ilk sabotaj yüzünden dosyada yoktu) ama
        `grep -c` ilk sabotajın metnini sayıp "indi" dedi.

    **KURALLAR:**
      a) `trap` içindeki HER YOL MUTLAK olur — değişken atanırken
         mutlak yazılır, `cd` sonrası da doğru çözülsün.
      b) `mv`'nin çıkış kodu KONTROL EDİLİR; `2>/dev/null` ile
         susturulup ardından koşulsuz "GERİ-ALINDI" basılmaz.
      c) Sonda turundan sonra `grep` ile SABOTAJ METNİ değil,
         `git status` + `git diff` ile AĞACIN KENDİSİ doğrulanır
         (kural 14 bunu zaten söylüyordu; ihlal edilen oydu).
      d) Sabotajın "indiğinin" kanıtı `diff -q` ile ESKİ YEDEĞE
         karşı alınır ve yedeğin TEMİZ olduğu ayrıca bilinmelidir —
         kirli bir yedeğe karşı alınan diff hiçbir şey söylemez.

25. **İKİ BAĞIMSIZ BARİYER AYNI SONUCU ÜRETİYORSA, O SONUCU ÖLÇEN
    TEST HANGİ BARİYERİN ÇALIŞTIĞINI KANITLAMAZ.** 2026-08-24,
    M1/7-0. Yorum kapısının "bilinmeyen tip → REDDET" varsayılanını
    uçtan sınıyordum. Sabotaj — varsayılanı serbest yapmak — testi
    KIRMADI, çünkü bilinmeyen tipi `EntityContextResolver` de
    reddediyor ve uç yine 404 dönüyordu. İki bariyer aynı gözlemi
    ürettiği için test, sınadığını sandığı şeyi hiç ölçmüyordu.

    Asıl tehlike gözden kaçıyordu: biri
    `EntityContextResolver.SupportedTypes`'a tip ekleyip izin
    tablosunu unutursa, çözümleyici tipi TANIR — ikinci bariyer
    devreye girmez — ve serbest varsayılan kapıyı ardına kadar açar.

    **KURAL:** sınanacak bariyer, diğerini NÖTRLEYEN bir yolla ayrı
    ölçülür. Karar saf bir fonksiyona çıkarılır ve test, diğer
    bariyeri etkisiz kılan bir girdiyle koşar — burada
    `ErisebilirMi(tip, izinVarMi: _ => true)`, yani TÜM İZİNLERE
    SAHİP taklit kullanıcı: reddin sebebi yetersiz izin OLAMAZ, tek
    sebep tipin tabloda olmamasıdır.

    Kural 23'ün akrabası: orada hata YUTULUYORDU, burada sonuç
    GÖLGELENİYOR. İkisinde de "test yeşil" bilgi taşımıyor.

26. **BİR SAYFA YÜKLEME DURUMUNDAN ÇIKIŞI GARANTİ ETMELİDİR.**
    Erken çıkış ve hata yollarında da yükleme kapanır; kapanmıyorsa
    KUSURDUR. 2026-08-24, `/yapilacaklar` canlıda "Yükleniyor…"
    durumunda kilitlendi.

    **KÖK NEDEN — ÖLÇÜLDÜ, TAHMİN EDİLMEDİ.** Üç şüpheli testle
    ayrıldı:

    | Şüpheli | Kararlı mı | Kilitliyor mu |
    |---|---|---|
    | `useModuleActions` nesnesi | KARARSIZ | **EVET** |
    | `useModuleActions.can` | kararlı | — |
    | `usePermissions.has` | kararlı | hayır — taşınmıyor |
    | `usePermissions` nesnesi | KARARSIZ | hayır |
    | Erken çıkışta sıfırlama yok | — | gizli ikinci kilit |
    | Yanlış servis yolu (404) | — | hayır (yalıtım tuttu) |

    Mekanizma: ekranın `useCallback` bağımlılık dizisinde NESNENİN
    KENDİSİ vardı, alanları değil. `can` `useCallback` ile sarılıydı
    ama SARMALAYAN NESNE değildi; her render'da yenilenen nesne
    callback'i, callback efekti, efekt yeni bir render'ı doğurdu.
    Ölçüm: 1,5 saniyede 1831 istek. Ekran hata göstermedi çünkü
    ORTADA HATA YOKTU — istekler 200 dönüyordu.

    **İKİ TEST HATASI DA BURADAN ÇIKTI:**

    a) **Gecikmesiz taklit yanıltır.** Anında dönen sahte uçla her
       turun `setLoading(false)` çağrısı bir sonrakinin `true`'sundan
       önce yetişiyor ve test "yükleme bitti" görüyordu. Canlıda
       uçlar 30-250 ms sürüyor. Zamanlamaya bağlı kusurlar
       GERÇEKÇİ GECİKMEYLE sınanır.

    b) **`waitFor` insanın gördüğünü ölçmez.** DOM'u doğrudan
       yokluyor ve mikro pencereleri yakalıyor; tarayıcı 60 fps'te
       boyuyor ve 16 ms'den kısa pencereyi kullanıcıya HİÇ
       göstermiyor. "Bir an kayboldu" ile "kullanıcı görmedi" aynı
       şey değil. Doğru iddia: kaybolmalı VE KAYBOLMUŞ KALMALI
       (sürekli örnekleme).

    **SÜPÜRME:** aynı desen (yükleyici, kapanıştan önce çıplak
    `return` ile dönebiliyor) 5 ekranda, 10 çıkış yolunda daha var.
    `tests/silent-loading-ratchet.test.ts` sayıyı donduruyor; yeni
    ekran bu desenle doğamaz. Düzeltme bu turda YAPILMADI, karar
    beklemede.

27. **BİR BEKÇİYİ KAPSAM DIŞI BIRAKMAK, KAPSAM DIŞINI GÜVENLİ
    YAPMAZ.** 7a'da rota bekçisi yazılırken API uçları bilerek
    dışarıda bırakıldı ve TEMIZLIK-TARAMASI.md'ye "sonraki tur"
    kalemi olarak yazıldı. **Aynı gün** `/yapilacaklar` ekranı
    `project-sites/daily-reports/pending-approval` çağırırken canlıda
    404 aldı; doğrusu `site-reports/pending-approval` idi.

    Erteleme kararı yanlış değildi — kapsamı bölmek doğru. Yanlış
    olan, ertelenen kapsamın **hangi hataları serbest bıraktığını
    yazmamaktı**. Bir bekçi kapsamı daraltılıyorsa, dışarıda kalan
    sınıfın SOMUT hata örneği not edilir; "sonraki tur" demek
    yetmez.

    Karşılığı: `tests/endpoint-guard.test.ts`. İlk ölçümde
    **8 gerçek kırık servis çağrısı** buldu (var olmayan uçlar ve
    bir yanlış yol) — hepsi canlıda duruyordu.

---

28. **HER FAZIN BAŞINDA AĞACIN TEMİZ OLDUĞUNU DOĞRULA.**
    `git status --short` boş değilse: **üzerine yazma.** Ne olduğunu
    raporla, karar birlikte verilsin.

    Bu kural bir günde ÜÇ KEZ tekrarlanan bir hatadan çıktı: ağaçta
    daha önce yazılmış, commit edilmemiş kod vardı; ben onu
    hatırlamadan aynı işi sıfırdan yazdım. M3/1'de sonuç iki farklı
    tasarımın çakışmasıydı (`ConversationMember` /
    `ConversationParticipant`) ve derleme kırıldı. Teşhis pahalıydı:
    hata mesajı "isim bulunamadı" diyor, "iki tasarım çarpıştı"
    demiyor.

    Yarım kalmış kod, üzerine yazılınca **kaybolmaz — gizlenir.**
    Ağaç temiz değilse tek doğru hamle durup bakmaktır.

29. **DERLEME VE TEST `scripts/derleme-kos.sh` ÜZERİNDEN KOŞAR —
    DOĞRUDAN `dotnet build/test` ÇAĞRILMAZ.**

    **GÖREV DURDURMAK SARMALAYICIYI ÖLDÜRÜR, ALT SÜREÇLERİ ÖLDÜRMEZ.**
    Kesilen her `dotnet build`/`test` arkada ~4,5 GB'lık yetim süreç
    bırakır; ikincisi aynı `obj/` kilidinde buluşur ve 8 GB'lık makine
    OOM'a girer. **Bu oturumda ÜÇ KEZ oldu** (2026-08-26 21:39, 22:17,
    22:37 — çekirdek günlüğünde `Out of memory: Killed process …
    (dotnet)`). Durdurma, süreç **AĞACINI** sonlandırmalıdır.

    **YETİMİN KİM OLDUĞU ÖLÇÜLDÜ** ve "msbuild" değil:
    - `csc.dll` (Roslyn derleyicisi), **PPID=1**, 3,9 GB — ebeveyni
      öldü, kendisi yaşamaya devam etti.
    - `VBCSCompiler` (Roslyn'in **KALICI** derleyici sunucusu), 2,9 GB
      — tasarımı gereği derleme bittikten sonra da ayakta kalır.

    Koşucu üç kapı koyuyor, üçü de sondayla kanıtlandı:

    | Kapı | Mekanizma | Sonda |
    |---|---|---|
    | Tek örnek | sabit adlı systemd scope | ikinci koşu çıkış **75**, başlamadı |
    | Süreç ağacı | her şey scope'un cgroup'unda | 4 süreç → `systemctl stop` → **4/4 öldü**, cgroup silindi |
    | Bellek tavanı | `MemoryMax` | `constraint=CONSTRAINT_MEMCG`, çıkış **137** |

    Ayrıca `MSBUILDDISABLENODEREUSE=1` ve `UseSharedCompilation=false`:
    kalıcı derleyici sunucusu hiç doğmaz. Derleme yavaşlar; bu makinede
    hız değil **sınır** önceliklidir.

    Kilit dosyası KULLANILMIYOR: kilidi tutan süreç OOM ile ölürse
    dosya yalan söyler. systemd birimi ölünce ad da serbest kalır.

29a. **CANLI UYGULAMA İLE TEST KOŞUSU AYNI MAKİNEDE.**

    3a (takas), 3b (`OOMScoreAdjust=-500`) ve 3c (bellek tavanı) bunu
    **hafifletir, ORTADAN KALDIRMAZ.** Üç OOM'da da kurban test süreci
    oldu ve canlı API ayakta kaldı — ama bu şansa bırakılamaz.
    **Kalıcı çözüm ayrı makinedir.**

30. **MIGRATION CANLIYA UYGULANDIKTAN SONRA ŞEMAYI TEK TEK ÖLÇ —
    TABLO VARLIĞI YETMEZ.**
    ```
    select indexname, indexdef from pg_indexes where tablename in (...);
    ```
    Dört tablonun da var olması "migration doğru" demek değil.
    M3/1'de tablolar geldi, `IX_..._aktif_benzersiz` diye
    belgelediğim KISMİ indeks ise koşulsuz çıktı — EF'e filtre hiç
    yazılmamıştı ve ad da uydurulmuştu. Kusur canlıya çıktı.

    **Yakalayan şey belgeydi:** DURUM.md kısmi diyordu, veritabanı
    koşulsuz diyordu. Belge ile ölçümü karşılaştırmadan "deploy
    başarılı" denmeyecek. Ad, filtre ve kolon sırası tek tek
    okunacak.

    `safe-deploy` migration'ları OTOMATİK UYGULAMAZ (betiğin
    başında yazılı). "Yayın BAŞARILI" satırı şemanın güncel olduğunu
    söylemez; `dotnet ef database update` elle çalıştırılacak ve
    sonucu ölçülecek.

31. **KAYNAK TARAYAN BİR NÖBETÇİ KELİMEYİ DEĞİL KOMUTU GÖZLEMELİ.**
    Yorumlar kelimeyi hayatta tutar. `betik.Contains("PIPESTATUS")`
    diyen test SONDAYI GEÇTİ: atamalar `DURUM=(0 0)` ile
    etkisizleştirildiği hâlde kelime yorumlarda yaşadığı için test
    yeşil kaldı — üstelik yorumu oraya AÇIKLAMA olsun diye ben
    yazmıştım.

    Aranan şey **atamanın veya çağrının kendisi** olmalı; yorum ve
    boş satırlar önce ELENMELİ. Kural 23'ün kaynak tarama biçimi:
    "kelime var" ile "denetim çalışıyor" ayrı şeyler.

    Ayrıca: kabuk betiğinde **satır devamlarını (`\`) birleştirmeden**
    satır satır bakmak yanıltır — `pg_dump ... \` satırında boru
    görünmez, boru alt satırdadır.

32. **SABOTAJIN UYGULANDIĞINI DOĞRULA, SONRA TESTİ KOŞ.**

    Sonda testlerinde sabotajı yaptıktan sonra, testi koşmadan ÖNCE
    değişikliğin gerçekten dosyaya yazıldığını doğrula (diff ya da
    grep ile satırı göster). Komut sessizce hata verip satırı
    değiştirmezse test yeşil kalır ve bu yeşil bir kanıt değil,
    **yanılgıdır.**

    Bu oturumda ÜÇ kez farklı biçimde yaşandı:

    - kural doğru ama hiçbir yerden çağrılmıyor (maskeleme)
    - sabotaj yanlış yere uygulanmış (`tr-TR` yerine `toLowerCase`)
    - sabotaj hiç uygulanmamış (`sed` ayracı `||` ile çakıştı)

    Üçü de aynı hata sınıfı: **testin yeşil olması, korumanın var
    olduğunu göstermez.**

    **DÖRDÜNCÜ BİÇİM — AYNI DOSYAYA İKİ SONDA:** aynı dosyaya arka
    arkaya iki sabotaj uygulanırken ikinci yedek, orijinali değil
    BİRİNCİ SABOTAJLI hâli kaydetti. Geri alma o bozuk hâli yazdı ve
    sabotaj ağaçta kaldı. Bir dosya için yedek YALNIZ BİR KEZ alınır
    ve her sondadan sonra ağaç doğrulanır:
    ```
    grep -c "<sabotaj izi>" <dosya>   # 0 olmalı
    ```

33. **CIRCIR KIRMIZISI BİÇİM DÜZENLEMESİYLE KAPATILAMAZ.**

    Kapsam cırcırı (`CoverageBaselineTests`) statik METİN taramasıdır:
    okumadan sonraki 400 karakterlik pencerede kapı arar ve yorumları
    uzunluğu koruyarak boşluğa çevirir. Bu, pencere sınırının
    **kozmetik değişikliklerle oynayabileceği** anlamına gelir —
    zincirin içine uzun bir yorum yazmak kapıyı pencerenin dışına
    iter ve cırcır kapı yerindeyken kırmızıya döner.

    **Kırmızı, yorumu kısaltarak ya da satırları kaydırarak
    KAPATILMAZ.** Yalnız iki meşru kapanış vardır:
    1. Sorguyu gerçekten kapsamlandırmak (`ApplyScope` /
       `ApplyMembership`),
    2. İstisna listesine GEREKÇESİYLE eklemek.

    Yorumu taşımak yalnızca kapının ZATEN yerinde olduğu ölçülüp
    kanıtlandığında meşrudur ve o zaman bile ölçüm rapora yazılır.
    Aksi hâlde "testi susturdum" ile "hatayı düzelttim" birbirine
    karışır.

34. **AÇIK KULLANICI İSTEĞİ VARSAYILAN SÜZGECİ EZER. TERSİ ASLA
    OLMAZ.**

    Varsayılan süzgeç bir kolaylıktır; kullanıcı bir şeyi açıkça
    istediğinde kolaylık susar.

    Bunun uygulama biçimi de bağlayıcı: **iki bağımsız süzgeci VE ile
    birleştirme.** VE ile birleşen iki süzgeçte her zaman DAR OLAN
    SESSİZCE KAZANIR. Gösterilecek küme tek bir fonksiyonda çözülür,
    sorgu tek satır olur (`WHERE X IN (...)`).

    ÇEK/1'de yaşandı: "açık olmayanı ele" ile "iptal olanı ele" VE
    ile birleşince, kullanıcı "iptalleri göster" dese bile ekran boş
    geliyordu. Yama ("iptali de geçir") çalışıyordu ama çarpışmayı
    ortadan kaldırmıyordu — üçüncü bir süzgeçte aynı hata yeniden
    doğardı.

35. **FAKTORİNGDEKİ ÇEKİN İKİ FARKLI SAYIDA GÖRÜNMESİ KASITLIDIR.**

    Çek defterinde **AÇIK** sayılır: çek tedavülde ve rücu riski
    sürüyor. Nakit akışında **beklenen tahsilat DEĞİLDİR**: para
    faktoringden zaten geldi.

    İki sayının farkı hata değil; iki ayrı soruya verilen iki ayrı
    doğru cevaptır — "hangi çekler hâlâ canlı" ile "ne kadar para
    gelecek". Yazılmasaydı altı ay sonra tutarsızlık sanılıp
    "düzeltilir" ve biri bozulurdu.

36. **SONDA GEÇERLİLİK KURALI.**

    Her sonda, sabotajın gerçekten UYGULANDIĞINI kanıtlamak
    zorundadır: hedef blok eşleşti mi, dosya yedeğinden farklı mı.

    **Sabotaj uygulanamadıysa sonuç YEŞİL DEĞİL, GEÇERSİZDİR.**
    Uygulanamayan sabotaj kanıt üretmez; kanıt ürettiğini sanmak
    daha tehlikelidir — koruma olmadığı hâlde "sondadan geçti"
    denmiş olur.

    **KOD DEĞİŞİNCE SONDA BETİKLERİ DE ESKİR.** Metin hedefli
    sabotaj, hedeflediği blok yeniden yazıldığında sessizce
    ıskalar. ÇEK/1'de yaşandı: tek kapı düzeltmesi Sonda A'nın
    hedeflediği iki süzgeç bloğunu ortadan kaldırdı ve sabotaj
    tutmadı. Düzenek "GEÇERSİZ" dedi, sonda yeni koda göre
    (A2) yeniden yazıldı ve ilk turun rakamı raporda GERİ ÇEKİLDİ.

    Pratikte: `cmp` ile yedek karşılaştırması, `assert count == 1`
    ile hedef tekilliği, ve sabotaj sonrası "dosya değişti mi"
    kontrolü. `git diff` commit edilmemiş ağaçta ölçüm aracı
    DEĞİLDİR (bkz. Kural 32 eki).

37. **DURUM ETİKETLERİ SIFAT OLARAK KULLANILMAZ.**

    Durum etiketleri isimden önce sıfat olarak kullanılmaz;
    **parantez içinde ya da iki nokta sonrasında** durur.

    ÇEK/1'de yaşandı: `${etiket} çekler toplamı` biçimi "Ödendi
    çekler toplamı" üretti — sayı doğru, cümle bozuk. Etiketler durum
    ADI ("Verildi", "Tahsil edildi", "İade alındı") ve Türkçede
    isimden önce çekim gerektiriyorlar.

    **SIFAT KARŞILIĞI LİSTESİ AÇILMAZ.** İkinci bir alan, yeni bir
    durum eklendiğinde karşılığını yazmayı unutan biri yüzünden aynı
    bozukluğu geri getirir — ve unutulduğu fark edilmez, çünkü ekran
    boş kalmaz, yalnız cümle bozulur. `Toplam (Ödendi)` biçimi
    hiçbir durumda çekim gerektirmez.

38. **YAYIN PAKET DEĞİL, BİRİKİMDİR.**

    Bir paketi deploy etmek, son yayından beri biriken **TÜM**
    commit'leri canlıya taşır. Her yayın öncesi, taşınacak paketlerin
    listesi **ÖNDEN bildirilir ve onay alınır.**

    Bu yayın (`1bb59ef4`) ÇEK/1'in yanında **Depodan Zimmet**,
    **hesap planı aktarımı** ve **M3/2a mesajlaşma uçlarını** da
    taşıdı; bunlar için ayrıca onay alınmamıştı.

    İki ayrı sayı karıştırılmasın: `origin/main`e göre "kaç commit
    ileride" ile son BAŞARILI yayına göre "ne taşınacak" farklı
    şeylerdir. Ölçüm `git log <son-yayin>..HEAD` ile yapılır;
    push durumu yayın kapsamını göstermez.

39. **ZORUNLU OLMAYAN + DOĞRULANMAYAN + MUHASEBE FİŞİ ÜRETEN
    ALAN, EN TEHLİKELİ BİLEŞİMDİR.**

    Üçü tek başına zararsızdır. Birlikte olduklarında alan, kimsenin
    doldurmak zorunda olmadığı, yanlış doldurulduğunda hiçbir şeyin
    itiraz etmediği, ama yanlışlığın **yevmiye kaydına geçtiği** bir
    kanal hâline gelir. Hata ekranda değil, defterde birikir; ve
    defterde biriken hata, fark edildiğinde artık düzeltilebilir bir
    kayıt değil, düzeltme fişi gerektiren bir olaydır.

    Çekin ödeme hesabı tam olarak bu alandı: seçilmesi zorunlu değil,
    seçilen hesabın çekin bankasıyla tutup tutmadığı denetlenmiyor,
    ama seçildiği anda o hesabın muhasebe kodunu alacaklandıran fiş
    kesiliyor.

    **KURAL:** bu üçlüyü taşıyan her alan için üçünden **en az biri**
    kırılacak — ya zorunlu olacak, ya doğrulanacak, ya da fiş
    üretmeyecek. Yeni alan eklenirken üçü birden sağlanıyorsa, bu
    tasarım hatasıdır ve kapatılmadan geçilmez.

40. **`VBCSCompiler` KALICI BİR DERLEYİCİ SUNUCUSUDUR; SÜREÇ
    AĞACINI ÖLDÜRMEK ONU TEMİZLEMEZ.**

    Derleme bitince ÖLMEZ — sonraki derlemeleri hızlandırmak için
    bekler ve PPID=1'e bağlanır. "Derlemeyi durdurdum, temizdir"
    varsayımı bu yüzden yanlıştır.

    **OOM'UN GERÇEK KAYNAĞI ÖLÇÜLDÜ ve sanılan değildi:**

    | Süreç | RSS |
    |---|---|
    | `csc.dll` (Roslyn derleyici, PPID=1) | **3,9 GB** |
    | `VBCSCompiler` (kalıcı sunucu) | **2,9 GB** |
    | `dotnet build` — suçlu sanılan | **10 MB** |

    **YAYIN/CI KOŞULARINDA PAYLAŞIMLI DERLEME KAPALI OLACAK**
    (`/p:UseSharedCompilation=false`), ya da her koşu sonunda
    `dotnet build-server shutdown` çağrılacak. `scripts/derleme-kos.sh`
    ikisini birden yapıyor: kuşak da takıyor, askı da (bkz. Kural 29).

41. **cgroup `MemoryMax` .NET'TE BASİT BİR TAVAN DEĞİLDİR.**

    Sınırı gören çalışma zamanı **GC yığınını sınırın %75'ine çeker**
    ve cgroup'a ÇARPMADAN `OutOfMemoryException` atar. Yani koyduğunuz
    tavanın dörtte biri sessizce kaybolur.

    **ÖLÇÜLDÜ — ikisi de aynı hatayla düştü:**

    | Tavan | Fiilî yığın | Zirve | Sonuç |
    |---|---|---|---|
    | 4G | ~3,0 GB | 3,46 GB | OutOfMemoryException |
    | 6G | ~4,6 GB | 4,80 GB | OutOfMemoryException |
    | 6G + `GCHeapHardLimitPercent=0x5A` | ~5,4 GB | **5,48 GB** | **geçti** |

    .NET iş yüküne konan tavan, gerçek zirve kullanımın **belirgin
    üstünde** olmalıdır — yoksa "makine yetmiyor" sanılır. Ben tam
    olarak bunu sanacaktım ve RAM yükseltmesi önerecektim.

42. **BİR KORUMA DEVREYE ALINMADAN ÖNCE GERÇEK İŞ YÜKÜ ÜZERİNDE
    ÖLÇÜLÜR. ÖLÇÜLMEDEN KONAN KORUMA, KORUDUĞU ŞEYİ KIRAR.**

    İki kez yaşandı:

    - **3G tavanı** `safe-deploy`'un **HER** yayınını test aşamasında
      düşürecekti. Sayıyı tahminle koymuştum; gerçek ihtiyaç 5,48 GB
      çıktı.
    - **Kapsam cırcırının 400 karakterlik penceresi** yorum uzunluğu
      değişince yanlış kırmızı verdi — nöbetçi, koruduğu koda
      dokunulmadığı hâlde alarm üretti.

    Ölçülmeden konan nöbetçi, güven kaybettirir: birkaç yanlış
    alarmdan sonra insanlar onu susturur ve gerçek alarmı da
    kaçırırlar.

43. **KALICI RET, GEÇİCİ RETTEN ÖNCE GELİR.**

    Bir istek hiçbir koşulda kabul edilmeyecekse (kilitli alan,
    yetkisiz işlem, kapanmış belge), bu ret; yeniden denenirse
    geçebilecek retlerden (damga uyuşmazlığı, eşzamanlılık, geçici
    kilit) **ÖNCE** döndürülür.

    Aksi halde kullanıcı asla başarılı olmayacak bir denemeye
    yönlendirilir: damgayı tazeler, sayfayı yeniler, yeniden dener ve
    aynı duvara çarpar — üstelik her seferinde duvarın ne olduğunu
    öğrenmeden.

    **Kontrol sırası bir tercih değil, korunması gereken bir
    davranıştır ve testle kilitlenir.**

    Kaynak: `ChequeReversalTests.PaidCheque_AmountCannotBeChanged`.
    ÇEK/2'de kilitli alan kapısını damga kontrolünün ARKASINA
    koymuştum; ödenmiş çekte tutar değiştirmeye çalışan istek
    409 "tutar kilitli" yerine 400 "damga eksik" alıyordu. Testi
    ben yazmadım — mevcut test yakaladı.

44. **BASH BETİKLERİ ARTIMLI OKUNUR: KOŞAN BİR BETİĞİ DÜZENLEMEK O
    TURU SESSİZCE BOZAR.**

    Yorumlayıcı dosyayı baştan sona bir kerede almaz; çalıştıkça
    okur. Koşarken düzenlenen betikte, henüz okunmamış kısmın
    kayması yorumlayıcıyı satırın ortasından devam ettirir. Sonuç
    hata mesajı bile olmayabilir — betik yanlış şeyi yapıp
    sıfırla çıkar.

    **BETİK DÜZENLEMESİ TUR BİTENE KADAR BEKLER.** Kaynak dosyaları
    (`.cs`, `.ts`) için bu geçerli değil: onlar derlenmiş hâlde
    koşar, düzenleme koşan turu etkilemez — yalnız o turun sonucu
    artık kaynağı temsil etmez, o da ayrı bir tuzaktır.

45. **SONDA YALNIZ MUHAFIZI DEĞİL, TESTİ DE ÖLÇER: SABOTAJ ALTINDA
    KIRMIZIYA DÖNMEYEN TEST O MUHAFIZI KANITLAMIYORDUR.**

    Kural 32 "sabotajın uygulandığını kanıtla" der, Kural 36 "uygulanmamış
    sabotaj GEÇERSİZDİR" der. Bu kural üçüncüsünü ekliyor: **HANGİ
    testlerin kırmızıya döndüğüne bak.** Yanlış testin kırmızı olması,
    yeşil kalanların sağlam olduğu anlamına gelmez — tam tersini
    gösterir.

    **ÇEK/2'DE YAŞANDI.** K1 kapısını devre dışı bıraktım; **yalnız
    ESKİ bir test** (`PaidCheque_AmountCannotBeChanged`) düştü, o kapı
    için yazdığım YENİ testlerin hepsi yeşil kaldı. Sebep: kilidi
    farkında olmadan İKİ yerde kurmuştum ve ikinci bariyer aynı isteği
    yine reddediyordu (Kural 25). Testlerim muhafızı değil, "istek
    reddediliyor mu" sorusunu ölçüyordu.

    Tek kapıya indirince aynı sabotaj **1 yerine 4** testi kırdı ve
    ikisi benim yeni testlerimdi.

    **İKİNCİ BARİYERİN NEREDEN GELDİĞİ ÖĞRETİCİ:** çek numarasını
    kapıda NORMALİZE ederek, atamada HAM hâliyle işliyordum. İkinci
    bariyer, birincinin kendi tutarsızlığını yamıyordu. **Çözüm sapmayı
    yakalamak değil, İMKÂNSIZ KILMAK oldu** — kapı artık atamayla
    birebir aynı değere bakıyor.

    "Derinlemesine savunma" niyetiyle eklenen ikinci kontrol, çoğu
    zaman birincinin ölçülmesini engelleyen bir perdedir.

46. **BİR KARŞILAŞTIRMA, İKİ TARAFIN DA AYNI KUSURU TAŞIMASI
    İHTİMALİNE KARŞI ÜÇÜNCÜ BİR REFERANS İÇERMELİDİR.**

    **İKİ ÖZDEŞ YANLIŞ "ÖZDEŞ" ÇIKAR.** Karşılaştırma yeşil verir,
    kabul şartı sağlanır, sonuç yanlıştır.

    SQUASH/1'de yakalandı. Z1 "A (mevcut göçler) = B (temel göç)"
    diye kurulmuştu. İkisi de AYNI YORDAMLA kurulacaktı; yordam
    eksikse (ör. iki `DbContext`ten yalnız birinin göçlerini
    uygulamak) eksiklik HER İKİ tarafta da oluşur ve karşılaştırma
    bunu **göremez**.

    Düzeltilmiş şart: karşılaştırma **ÜÇ TARAFLI** — A / B / **CANLI**.
    Canlı ile göçler arasındaki her fark bir bulgudur.

    Genel kural: bir referansı kendisinden türeyen bir şeyle
    karşılaştırmak, ortak atadan gelen hatayı asla göstermez.
    Üçüncü taraf, zincirin dışından gelmelidir.

47. **DENETİM ARACININ YANLIŞ ALARMI, BULGUNUN KENDİSİ KADAR ACİL
    DÜZELTİLİR.**

    Yanlış alarm üreten bir araç, çıktısını ciddiye almamayı
    öğretir; birkaç boş alarmdan sonra gerçek bulgu da "yine odur"
    diye geçilir. Araç o noktada koruma değil, **gürültü
    kaynağıdır** — ve sustuğu gün kimse fark etmez.

    Bu oturumda üç kez oldu: kapsam cırcırı 400 karakterlik
    penceresiyle yanlış kırmızı verdi; koşucu nöbetçisi bir HATA
    MESAJI metnini ihlal saydı; Z1 aracı `md5(prosrc)` ile GİRİNTİ
    farkını şema farkı sandı ve `enderun_fold` için yanlış bir
    "bulgu" raporlamama yol açtı.

    **METİN KARŞILAŞTIRMASI İLK ELEME, HÜKÜM DAVRANIŞTAN.**
    Boşluk normalleştirmesi yetmez — boşluk, dize sabitlerinin
    İÇİNDE anlamlıdır ve normalleştirme oradaki gerçek farkı da
    siler. Fonksiyonlar için doğru kurgu: sabit bir girdi kümesini
    iki tarafta da çalıştır, çıktıları karşılaştır. **Aynı girdilere
    aynı cevabı veren iki fonksiyon aynıdır.**

48. **SIFIR SONUÇ, YOKLUĞUN KANITI DEĞİLDİR.**

    Boş dönen bir arama ya da sorgu iki şeyi birden gösterebilir:
    aranan şey yoktur, **ya da sorgu yanlıştır.** İkisi ekranda
    birbirinin aynısıdır.

    Bu yüzden her boş ölçüm, **VAR OLDUĞU KESİN BİLİNEN** bir örnek
    üzerinde tekrarlanır (olumlu denetim). O da boş dönüyorsa ortada
    bulgu değil **arıza** vardır.

    **KAYNAK — bu oturumda en pahalı hatam:**
    `git log --all -- '*Migrations/$f*'` deseni
    `Migrations/HumanResources/` **alt dizinini kapsamadı** ve altı
    dosyanın her biri için `0` döndürdü. Bunu "hiç commit
    edilmemiş" diye okudum ve canlıya kayıt dışı göç uygulandığı
    gibi ağır bir bulgu raporladım. Doğru yolla sorunca **üç
    commit** çıktı; göçler baştan beri depodaydı.

    Olumlu denetimi yapsaydım — var olduğunu bildiğim herhangi bir
    dosyayı aynı desenle aratsaydım — desenin bozuk olduğunu ilk
    adımda görürdüm.

    **KURAL 36'NIN ÖLÇÜM TARAFINDAKİ İKİZİDİR:** hiç uygulanmamış
    sabotaj kanıt üretmez; hiç eşleşmemiş sorgu bulgu üretmez.

49. **YUMUŞAK SİLİNEBİLEN TABLODA BENZERSİZLİĞİN SÜZGEÇLİ Mİ
    SÜZGEÇSİZ Mİ OLACAĞI, ANAHTARIN TÜRÜNE BAĞLIDIR.**

    **(a) KULLANICININ SEÇTİĞİ KOD** — proje, departman, cari, depo,
    pozisyon kodu: **SÜZGEÇLİ** (`WHERE "IsDeleted" = false`).
    Kullanıcı bir kaydı sildikten sonra aynı kodu yeniden
    kullanabilmelidir. Süzgeçsiz benzersizlik, silinmiş kaydın kodunu
    **rehin tutar**.

    **(b) SİSTEMİN ÜRETTİĞİ BELGE NUMARASI** — çek `InternalNumber`,
    fatura no, sipariş no, fiş no: **SÜZGEÇSİZ**. Belge numarası bir
    kez verildiyse, kayıt silinse bile **ASLA** yeniden verilmez;
    muhasebe ve denetim tekilliği kalıcıdır.

    **(c) DIŞ KİMLİK** — TC kimlik no, vergi no: ayrı karar
    gerektirir, **varsayılan SÜZGEÇSİZ**. Aynı kişi için ikinci bir
    kayıt açmak yerine silinmiş kaydı geri getirmek doğrudur.

    **AYNI TABLODA İKİ ANAHTAR FARKLI SINIFA GİREBİLİR:**
    `cheques.ChequeNumber` (a) → süzgeçli;
    `cheques.InternalNumber` (b) → süzgeçsiz.

    Sınıfı belirlerken **alanı kimin doldurduğuna** bakılır:
    `DocumentNumberService` üretiyorsa (b), kullanıcı formda
    yazıyorsa (a).

    **AYNI SÜTUNLARDA HEM SÜZGEÇLİ HEM SÜZGEÇSİZ BENZERSİZLİK**
    bulunması, katı olanın gevşek olanı **sessizce ezmesi** demektir:
    süzgeçli indeks orada durur ama hiçbir işe yaramaz.

50. **BELGEDE, YORUMDA VEYA README'DE YAZAN BİR KOMUT, O KOMUTUN
    ÇALIŞTIĞININ KANITI DEĞİLDİR.**

    Bir davranışın var olduğu **metinle değil, çalıştırılan kodla**
    kanıtlanır. Bu oturumda **dört kez** yorum metni çağrı sanıldı.

    Kaynak: `safe-deploy.sh:22` — `"dotnet ef database update"` bir
    çağrı değil, göçlerin **ELLE** uygulanması gerektiğini söyleyen
    bir nottu. Ben onu bir çağrı sanıp "iki bağlamla hata veriyor"
    diye rapor ettim; çağrı yoktu ki hata versin.

    Aynı hatanın diğer üç yüzü: nöbetçi testi bir hata mesajı
    metnini ihlal saydı; kapsam cırcırı yorum uzunluğu değişince
    yanlış kırmızı verdi; `pgrep` ölçüm kabuğunun kendi komut
    satırını saydı. Hepsi tek cümlede toplanır: **metin, davranış
    değildir** (bkz. Kural 31, 47).

## 5b. DÜŞEN BULGULAR — HR-ŞEMA/1 FAZ 0 (2026-08-27)

**Bu bölüm, kayıtta kalmış olsaydı ileride birinin üzerine iş
yapacağı ÜÇ YANLIŞ BULGUYU ve hangi ölçüm hatasından geldiklerini
yazıya geçirir.** Yanlış bir bulgu, kuralsızlıktan kötüdür.

| Rapor ettiğim | Gerçek | Ölçüm hatası |
|---|---|---|
| "Altı HR göçü hiç commit edilmemiş" | Göçler **git'te var**, üç commit'te eklenmişler | `git log --all -- '*Migrations/$f*'` deseni `Migrations/HumanResources/` **alt dizinini kapsamadı**, 0 döndü (Kural 48) |
| "`HrDbContext`'in kaynakta 0 göçü var" | **6 göç + anlık görüntü** var, `Migrations/HumanResources/` altında | Aynı hata: yalnız `Migrations/*.Designer.cs` düzlemine baktım |
| "Temiz kurulum sessizce kırık" | Kurulum **çalışıyor** | A veritabanına yalnız `AppDbContext` göçlerini uygulamıştım; iki bağlamla kurulan veritabanı canlıyla **6652 satırda 0 fark** verdi |

Bir bulgu daha düştü: **`enderun_fold` gövdeleri farklı** dediğim
şey yalnız GİRİNTİ farkıydı; Z1 aracım `md5(prosrc)` ile
karşılaştırıyordu (Kural 47).

**AYAKTA KALAN GERÇEK BULGULAR:** kurulum yordamının `--context`
söylememesi, ve süzgeçsiz benzersizlik kapsamı (bkz. Kural 49).

### B2 OKUNURLUK BORCU — İKİ BAĞLAM TEK GEÇMİŞ TABLOSU

`AppDbContext` ve `HrDbContext` aynı `__EFMigrationsHistory`
tablosunu paylaşıyor (202 kayıt). Bir kimliğin hangi bağlama ait
olduğu tablodan okunamıyor; **beni yanıltan yapı buydu** — HR
kayıtları `AppDbContext` göçleriyle karşılaştırılınca "kaynakta
karşılığı yok" gibi göründü.

**AYRILMADI, KASITLI.** Canlı bir veritabanının göç defterini
kozmetik sebeple taşımak, kazandırdığından fazlasını riske atar:
taşıma sırasında bir kaydın düşmesi, EF'in o göçü yeniden
uygulamaya kalkması demektir. Kusur değil, **okunurluk borcu**
olarak kaydedildi.

Ayrımı bugün gereksiz kılan şey: `safe-deploy`'un göç kapısı iki
bağlamın göç dosyalarını birlikte okuyup tek geçmiş tablosuyla
karşılaştırıyor, yani bağlam ayrımına ihtiyaç duymuyor.

52. **ÇÖKEN ARAÇ ZARARSIZDIR; TEHLİKELİ OLAN, HATA VERMEDEN
    MAKUL GÖRÜNEN YANLIŞ ÇIKTI ÜRETEN ARAÇTIR.**

    Çöken araç durur ve görünür. Yanlış çıktı üreten araç,
    sonucunuza sessizce karışır ve kararınızın parçası olur.

    **ÖLÇÜM ÇIKTISININ BİÇİMİ DE SONUCUN PARÇASIDIR:** satır
    sayısı, sütun sayısı ve hizalama doğrulanmadan tablo okunmaz.

    Kaynak: sınıflandırma tablosunu üreten betikte `grep -c` sıfır
    dönünce (çıkış kodu 1) `|| echo 0` **ikinci bir satır** bastı;
    tablo kaydı, sütunlar birbirine karıştı. 16 kalemlik
    sınıflandırma o tabloya göre yapılacaktı — biçim bozukluğunu
    fark etmeseydim yanlış tablodan karar çıkacaktı.

    Aynı sınıf: `pgrep`in kendi ölçüm kabuğumu sayması, 400
    karakterlik pencerenin `SELECT`i kesmesi, `git log` deseninin
    alt dizini kapsamaması. Hepsi **çökmedi**; hepsi makul
    görünen yanlış sayı üretti (bkz. Kural 48).

## 5c. DÖRT ÖKSÜZ TABLO — ÖLÜ AĞIRLIK (2026-08-27)

Göçlerde var, canlıda var, **hiçbir `DbContext` yönetmiyor**.
Kaynakta yalnız göç dosyalarında geçiyorlar.

| Tablo | Satır |
|---|---|
| `approval_workflow_definitions` | **1** |
| `hr_certificate_definitions` | 0 |
| `hr_competency_definitions` | 0 |
| `hr_training_definitions` | 0 |

Üçü boş: **yarım kalmış özellik kalıntısı.** SQUASH/1'de temel
göçte korunacaklar; silme kararı ayrıca verilecek.

**DOLU OLAN TEK SATIRIN YOLU BULUNAMADI** ve bu kayda değer:
`PURCHASE_REQUEST_APPROVAL`, 25.07.2026 12:04:59,
`CreatedByUserId` **NULL**.

- Kodda **hiç geçmiyor** (`git log --all -S` deponun tamamında
  bulamadı; olumlu denetim aracın çalıştığını doğruladı).
- Tabloları yaratan göç saat **10:24:37**, satır **12:04:59** —
  göç de ekmemiş.

Geriye en olası ihtimal **elle çalıştırılmış bir `INSERT`**
kalıyor. **BUGÜN AÇIK BİR KAPI YOK:** o tabloya dokunan hiçbir
kod yok, dolayısıyla yazmaya devam eden bir mekanizma da yok.

51. **"ÖNCE GÖÇ, SONRA KOD" SIRASI YALNIZCA GÖÇ GERİYE
    UYUMLUYSA GÜVENLİDİR.**

    Eski kodun yeni şemayla çalıştığı bir pencere **her göçte**
    vardır — göç uygulanır, yayın henüz yapılmamıştır.

    **İndeks tanımı değiştiren göçlerde bu pencere zararsızdır:**
    eski kod indeksin şeklini umursamaz, aynı sorguları çalıştırır.

    **Sütun veya tablo ekleyen/kaldıran göçlerde tehlikelidir** ve
    her seferinde **AYRICA** değerlendirilir; varsayılmaz.

    KURULUM/1 yalnız indeks değiştirdiği için güvenliydi. **ÖP/1a
    tablo ve sütun ekleyecek** — orada aynı sıra otomatik olarak
    geçerli sayılmayacak.

## 5d. KURULUM/1 — DEPLOY SONRASI DOĞRULAMA (GM listesi)

Bu paketten sonra canlıda **gözle** doğrulanacak:

1. **Bir departmanı sil, aynı kodla yenisini aç → GEÇMELİ.**
   (Paketten önce bu mümkün değildi: silinmiş kaydın kodu rehin
   kalıyordu.)
2. **İki aktif departman aynı kodu alamamalı → TEMİZ HATA.**
   (Kısıtın gevşemediğinin kanıtı. Bu adım atlanırsa "süzgeç
   ekledik" diye benzersizliği tümden kaldırmış olmak mümkün.)

Aynı ikisi pozisyon, vardiya tanımı, doküman kategorisi, stok
kategorisi/özniteliği ve depo hiyerarşisi (bölge/raf/seviye) için
de geçerli.

**DEĞİŞMEMESİ GEREKENLER** — bunlar hâlâ reddetmeli: muhasebe
hesabı, proje, stok kalemi, depo, şirket, kasa, şube, cari,
mühendislik pozisyonu kodları silinse bile yeniden
kullanılamamalı.

## 5e. ÖDEME PLANI, NAKİT AKIŞI PROJEKSİYONUNA BAĞLANMADI (2026-08-27)

**ÖLÇÜLDÜ, YENİDEN KULLANILMADI — ve bu bilinçli.**

`CashFlowProjectionService`'i ödeme planına bağlamak cazipti.
Tanımları karşılaştırıldı, **örtüşmüyorlar**:

| | Nakit akışı projeksiyonu | Ödeme planı |
|---|---|---|
| Eksen | **tarih** (gün gün bakiye) | **cari** (kime ne kadar) |
| Kapsam | çek + vergi + hakediş + bordro + düzenli gider | tedarikçi borçları |
| Kesinlik | kesin ↔ tahmini karışık | tahminle plan yapılmaz |
| **Elden ödeme** | **DAHİL, maskesiz tam tutar** | dahil değil |
| Cevapladığı soru | "hangi gün açığa düşeriz" | "bu hafta kime ödeyeceğiz" |

**EN KESKİN AYRIM BİR YETKİ MESELESİ.** Projeksiyon elden
ödemeleri `LoadEffectiveExtraPaymentsAsync` ile **maskesiz**
topluyor; kendi yorumu gerekçeyi yazıyor: *"Yetki KAPIDA
çözülüyor (`cashflow.view`), tablo içeride tek ve eksiksiz."*

Ödeme planı **farklı bir kapıdan** ve **daha geniş bir kitleye**
açılıyor. Servisi bağlamak, elden ödeme tutarlarını o kitleye
sızdırırdı — maaş verisinin izleyici kitlesini genişletmek,
karar gerektiren bir iştir, yan etki olarak yapılmaz.

**TEK SERVİSİN İKİ ANLAM TAŞIMASI, İKİ AYRI SERVİSTEN KÖTÜDÜR.**
Aynı ilkenin daha önceki uygulaması: faktoringdeki çek, çek
defterinde AÇIK ama nakit akışında beklenen tahsilat DEĞİL —
iki sayı farklı soruya cevap veriyor ve fark kasıtlıdır
(Kural 35).

53. **K8 SINIRI: 21 GÜN DOLDUĞUNDA ONAY DÜŞER.**

    Kural "üç haftayı aşan onay düşer" diyordu; sınır **21 gün DAHİL
    düşer** olarak uygulandı (`< 3*7`). Yani 20 gün geçerli, 21 gün
    geçersiz.

    Alternatif okuma ("21 hâlâ geçerli, 22 düşer") mümkündü;
    belirsizlik testte açıkça sabitlendi
    (`K8_UcHaftayiAsanOnayDuser`, `[InlineData(21, false)]`), böylece
    karar değiştirilmek istenirse tek satır olur.

54. **SAF KARAR SONDALARI KURALLARI KANITLAR, KURALLARIN BİRLİKTE
    UYGULANIŞINI KANITLAMAZ.**

    **YARIŞLAR TUTKALDA YAŞAR:** okuma ile yazma arasındaki aralıkta,
    işlem sınırlarında, kilitsiz güncellemelerde. Bir pakette saf
    kural sondaları yeşilse, ayrıca **BİRLEŞTİRME SONDASI** gerekir.

    ÖP/1a'da K2 ve K3'ün **beş sondası da geçti** (S1–S5, beşi de
    öngörüyle birebir). Delik `SatirOdemeKaydetAsync` içinde,
    **kuralların arasındaydı**: okuma ile yazma arasında kilit yoktu,
    K3 **bayat** `OdenenTutar` üzerinden hesaplıyordu. İki eşzamanlı
    istek K2'yi "onaylandığı gibi", K3'ü "kendi payına" geçiyor ve
    toplamda onaylanandan fazla ödeme yazılıyordu.

    **BİRLEŞTİRME SONDASINI KURMAK ÜÇ DENEME ALDI** — ve bu, kuralın
    asıl ağırlığı:

    | Deneme | Sabotaj altında |
    |---|---|
    | İki `Task` sal, "toplam aşmadı" de | **YEŞİL** — yarış penceresi mikrosaniye, iki istek fiilen sırayla koştu |
    | Dışarıdan kilitle, "bloke oluyor mu" de | **YEŞİL** — PostgreSQL `FOR UPDATE`li satıra `UPDATE`i zaten bloke ediyor; test açık kilidi örtük olandan ayıramadı |
    | **Bayat okuma**: kilit sahibi tutarı sınıra çekip commit eder | **KIRMIZI** ✓ |

    Ayırt edici soru şuydu: **servis kilidi aldıktan SONRA mı
    okuyor?** İlk iki test bunu sormuyordu; var olmayan bir korumayı
    doğruluyor gibi görünüyorlardı.

    Kurallar yalıtılabilir; **aralarındaki aralık yalıtılamaz** ve
    orada "ölçtüğünü sandığın şey" ile "gerçekte ölçtüğün şey"
    kolayca ayrışır.

### ÖLÇÜM DE BİR İDDİADIR VE DOĞRULANMALIDIR

Aşağıdaki kurallar **tek bir şeyin farklı yüzleridir**: ölçtüğünü
sandığın şey ile gerçekte ölçtüğün şeyin ayrışması. Biri tanıdık
geliyorsa diğer dördünü de oku.

| # | Yüzü |
|---|---|
| **55** | üstüne yazılan dosya: yeni sandığın ad boş olmayabilir — takım yeşil kalır, kapsam eksilir |
| **58** | vekil küme: muhafız girdisini kendi eliyle kurarsa kapsamı yazıldığı anda donar |
| **59** | gözlemsiz muhafız: düzeltmeyle doğan test, kusuru yakaladığını kanıtlamaz |
| **60** | anlamayan okuyucu: bir kodlama genişlerken eski okuyucunun düştüğü taraf tasarımın parçasıdır |
| **61** | eksik sonda raporu: yalnız kırmızıları saymak, sabotajın sınırlı kaldığını göstermez |

**BİTİŞİK DEĞİLLER.** Aradaki 56 ve 57 bu gruba ait değil — onlar
yetkilendirme alanının kuralları. Grup numaralarıyla tanımlanıyor,
sırasıyla değil.

**ORTAK SORU İKİ YÖNLÜ:**

> *"Bu ölçümü geçiren şey benim kodum mu, yoksa başka bir şey mi?"*
>
> ve tersi:
>
> *"Bu ölçüm boş/yeşil dönerse, bunun iki açıklaması var mı?"*

**BU TURDAKİ DÖRT BEKÇİ DE BU SINIFTAN KAÇIRDI:** vekil küme, eksik
kapsam, dar okuma, eksik yazım biçimi. Dördü ayrı yerlerde, ayrı
biçimlerde ve ayrı günlerde bulundu — ama aynı hata.

---

55. **YENİ DOSYA YAZMADAN ÖNCE O ADIN BOŞ OLDUĞU ÖLÇÜLÜR.**

    `cat > dosya` var olanı sessizce siler; ne uyarı verir, ne de
    silinenin ne olduğunu söyler. Testler yeşil kaldığı için hata
    **yeşilin arkasına saklanır**: silinen bekçiler koşmadıkları için
    kırmızı da veremezler.

    ÖP/1b'de `OdemePlaniUcIzinTests` yazılırken `OdemePlaniIzinTests.cs`
    adı seçildi ve ÖP/1a'nın **altı test niteliği** üstüne yazıldı —
    "onay anahtarı Admin'de YOK", "yalnızca GM'de", "hazırlayan roller
    onay iznini almaz" dahil, yani paketin asıl güvenlik bekçileri.
    Tam suite **2865/2865 yeşil** verdi; sayı doğruydu, **kapsam
    eksikti**.

    YAKALATAN: `git status` çıktısında dosyanın `??` değil **`M`**
    görünmesi. Yeni sandığın dosya "değişmiş" görünüyorsa yeni değildir.

    İKİ SONUÇ:
    - Yazmadan önce `ls` ya da `git status`; ad çakışıyorsa **ayrı
      dosya**, üstüne yazma yok.
    - Kapsamı değişmiş bir suite'in eski sayısı **sonuç olarak
      sunulmaz**; geri alma sonrası suite YENİDEN koşar.

    Ad çakışması burada bir işaretti: katalog düzeyi ("hangi rol hangi
    anahtarı taşıyor") ile uç düzeyi ("anahtar uçta gerçekten aranıyor
    mu") zaten iki ayrı sorudur. Katalog doğru olup uçtaki attribute
    unutulsaydı, birinci dosya yeşil kalırdı.

    **MEKANİK KARŞILIĞI: TEST SAYISI CIRCIRI** (bkz. o başlık).
    `git status`taki `M` harfi bu sefer yakaladı ama o şanstı. Cırcır
    arka uç ve ön yüz için ayrı çizgi tutuyor ve sayı düşerse kırmızı
    veriyor. Yakalayamadığı şey de kayıtlı: çizginin sessizce
    düşürülmesi — koruma orada usule ait, mekanizmada değil.

    **D FIKRASI — GEVŞEKLİK SIFIRDA TUTULUR** (Mehmet onayı,
    2026-09-03):

    > **TEST EKLEYEN HER PAKET, ÇIRA ÇİZGİSİNİ KENDİ ÖLÇTÜĞÜ GERÇEK
    > SAYIYA TAŞIR; ÇİZGİ GERÇEK SAYININ GERİSİNDE BIRAKILMAZ. ÇIRA
    > HER KOŞUDA KENDİ GEVŞEKLİĞİNİ BASAR.**

    Çizgi bir TABANDIR: gerçek sayı üstündeyse cırcır susar. Aradaki
    fark GEVŞEKLİKTİR ve cırcır, **gevşeklik tükenene kadar sessizdir**
    — o kadar test silinebilir, hiçbir şey ötmez.

    DOĞURAN OLAY (ölçüldü, İŞEMRİ/2 Faz 1): çizgi 2798'de dururken
    HEAD'in gerçek sayısı **2824**'tü. 26 testlik gevşeklik **beş
    commit** boyunca birikmişti; her biri kuralı çiğnememişti, çünkü
    çizgi yukarı serbestti ve güncellemek zorunlu değildi. Sonuç: o gün
    26 test silinebilir ve cırcır susardı. Cırcır bir cırcır değil, bir
    süstü.

    NEDEN BASKI, NEDEN KIRMIZI DEĞİL: gevşekliği kırmızıya çevirmek
    çizgiyi bir TAVANA dönüştürür ve "yukarı serbest" kuralını iptal
    ederdi. Aranan şey engel değil **görünürlük** — düşüşün bir
    konuşmaya dönüşmesi gibi, gevşekliğin de bir konuşmaya dönüşmesi.

    NEDEN HATIRLAMAYA BIRAKILMIYOR (Mehmet'in gerekçesi): *"kuralı
    hatırlamaya bırakırsan unutulur; sayıyı ekrana basarsan
    unutulamaz."* 26'lık gevşeklik ancak çıranın hareketi
    KALEMLENMEYE çalışılırken fark edildi; basılıyor olsaydı ertesi
    gün görülürdü.

    BİÇİM (`tests/test-sayisi-ratchet.test.ts`, `describe` gövdesinde —
    bir testin içinde değil, ki başka bir test patladığında da bassın):

        çıra · arka uç: çizgi 2849 · gerçek 2849 · gevşeklik 0
        çıra · ön yüz : çizgi  410 · gerçek  410 · gevşeklik 0

    İLK İŞİNİ HEMEN GÖRDÜ: eklendiği koşuda `gevşeklik 3` bastı (bu
    paketin `PersonelKapsamSuzgeciTests`'i), çizgi ona taşındı. Sayı
    elle sayılmadı — çıranın kendi çıktısından okundu.

56. **YETKİLENDİRME DEĞİŞİKLİKLERİ ANINDA GÖRÜNMEZ.**

    Mevcut oturumlar eski jetonla çalışmaya devam eder; yeni davranış
    ancak jetonlar dolduğunda (burada 12 saat sonra) ortaya çıkar. Bu
    yüzden rol/izin değişikliği içeren bir yayının doğrulaması,
    uygulamanın "hâlâ çalışıyor" olmasıyla **YAPILAMAZ** — **TEMİZ BİR
    OTURUMLA YENİDEN GİRİŞ** gerektirir.

    **KAYNAK:** ÖP/1a — `payment.plan.approve` Admin'den çıkarıldı,
    Admin 141'den 140 izne düştü, "hepsine sahip" bayrağı devre dışı
    kaldı, 140 izin tek tek jetona yazıldı, jeton 4096 baytı aştı,
    tarayıcı çerezi **SESSİZCE** attı, giriş döngüye girdi. **Yayın
    günü hiçbir belirti yoktu**; arıza, eldeki jetonun süresi dolduğu
    gün ortaya çıktı.

57. **BİR ROLÜN JETON MALİYETİ İZİN SAYISIYLA DÜZGÜN ARTMAZ.**

    "Hepsine sahip" kısayolu bir **uçurum** yaratır. Tam yetkili bir
    rolden **TEK** bir izin çıkarmak, jetonu on kat büyütebilir:
    bayrak devre dışı kalır ve bütün liste jetona yazılır.

    **İZİN ÇIKARMAK, İZİN EKLEMEKTEN DAHA TEHLİKELİDİR.** Ekleme
    doğrusal büyütür; çıkarma bir eşiği aşağıdan yukarı geçirebilir.

58. **BİR MUHAFIZ, GİRDİSİNİ KENDİ ELİYLE KURUYORSA, KAPSAMI YAZILDIĞI
    ANDA DONAR.**

    Elle kurulmuş bir küme (`AllPermissionKeys()`, `Take(44)`, sabit
    liste) gerçeğin **VEKİLİDİR**; yalnızca vekil ile gerçek örtüştüğü
    sürece geçerlidir. Örtüşme bozulduğunda muhafız susmaz — **YEŞİL
    KALMAYA DEVAM EDER**, çünkü hâlâ vekili sınamaktadır.

    Muhafızlar **gerçek kayıttan** sürülmelidir (`RoleCatalog.Roles`,
    `DbSet` listesi, dosya taraması); böylece sistem değiştiğinde
    kapsam kendiliğinden genişler.

    **KAYNAK:** `TokenCookieSizeTests` dört testle jeton boyutunu
    koruyordu; üçü `AllPermissionKeys()` geçiyordu — o küme her zaman
    "hepsine sahip" bayrağını tetikler ve jetonu küçültür. Dördüncüsü
    `Take(44)` ile elle yazılmış bir sayı kullanıyordu. ÖP/1a'da Admin
    katalogdan ayrılınca vekil geçersizleşti, testler yeşil kaldı,
    canlıda giriş kırıldı.

    Kural 48'in ("sıfır sonuç yokluğun kanıtı değil") kardeşi: orada
    boş küme her iddiayı doğruluyordu, burada sabit küme her
    değişikliği görmezden geliyor. İkisi de **ölçtüğünü sandığın şey
    ile gerçekte ölçtüğün şeyin** ayrışmasıdır.

59. **YENİ BİR MUHAFIZIN İLK GÖZLEMİ GERÇEK KUSURA KARŞI OLMALIDIR.**

    Düzeltmeyle birlikte doğan bir test, kusuru yakaladığını
    KANITLAMAZ — yalnız düzeltme sonrası yeşili gösterir. Sonda ile
    taklit edilen kusur, gerçeğinin yerini tam tutmaz: sabotaj senin
    kurduğun bir şeydir, gerçek kusur değildir.

    **KIRMIZI GÖZLENİR, sonra düzeltilir, sonra TEK SEFERDE commit
    edilir.** Kırmızı commit edilmez ama gözlemsiz de geçilmez.

    KAYNAK: JETON/1. Jeton boyutu testi düzeltmeden önce yazıldı ve
    gerçek kusura karşı kırmızıya döndüğü gözlendi — `Admin` rolü
    **4394 bayt**, paylı eşik 3500, tarayıcı sınırı 4096. Diğer 14 rol
    yeşil kaldı; hepsi kırmızı olsaydı test rolleri değil başka bir
    şeyi ölçüyor olurdu.

60. **BİR KODLAMA GENİŞLETİLDİĞİNDE, ONU ANLAMAYAN OKUYUCUNUN HANGİ
    TARAFA DÜŞTÜĞÜ TASARIMIN PARÇASIDIR.**

    **Kapalı** tarafa düşen eksik yetki GÖRÜNÜR ve düzeltilir; **açık**
    tarafa düşen fazla yetki GÖRÜNMEZ. Yeni alanlar, eski okuyucuyu
    KAPALI tarafa düşürecek şekilde şekillendirilir.

    KAYNAK: JETON/1. Tümleyen kodlaması önce `all_permissions: true` +
    `not_permissions` biçiminde tasarlanmıştı. `not_permissions`ı
    bilmeyen bir okuyucu bayrağı görüp HER ŞEYİ verirdi — Admin'e ödeme
    onayı dahil, yani İ2'nin tam tersi.

    **Bu teorik değil:** safe-deploy sağlık kontrolü düşerse ön yüzü
    GERİ ALIYOR, ama kullanıcıların çerezindeki yeni biçimli jeton
    **12 saat** yaşıyor. O pencerede eski middleware yol korumasını
    tamamen açardı.

    Kodlama değiştirildi: tümleyen kullanıldığında bayrak
    GÖNDERİLMİYOR. Anlamayan okuyucu ne bayrak ne liste görür, izin
    kümesi boş kalır, kullanıcı ekrana giremez.

    OKUMA SIRASI DA TASARIMIN PARÇASI: tümleyen ÖNCE bakılır. Sıra
    tersine olsaydı ve bir gün ikisi birden gelirse, bayrağı önce
    okuyan kod tümleyeni yok sayıp fazla yetki verirdi. Sıra o hatayı
    yapısal olarak imkânsız kılıyor.

    İLGİLİ: gidiş-dönüş iddiaları **küme eşitliği** olmalı, kapsama
    değil. "çözülmüş ⊇ gerçek" fazla yetkiyi yakalamaz.

61. **SONDA RAPORU KIRMIZILARLA BİRLİKTE YEŞİLLERİ DE TAŞIR.**

    Kırmızıya dönen testler sabotajın **UYGULANDIĞINI** kanıtlar;
    yeşil kalanlar sabotajın **SINIRLI KALDIĞINI** kanıtlar.

    Bir sondada her şey kırmızıya dönüyorsa ölçülen şey hedef değil,
    **koşum düzeneğidir** — derleme kırılmış, ortak fikstür bozulmuş
    ya da testler aynı bağımlılığa takılmış olabilir.

    Her sonda için **beklenen kırmızı VE beklenen yeşil önden yazılır**,
    ikisi de doğrulanır.

    KAYNAK: JETON/1. Sonda A'da gidiş-dönüş testinin yeşil kalması,
    testlerin gerçekten rolleri ölçtüğünü gösterdi. Sonda C'de
    `SinirAltindakiJeton_Uretilir`in yeşil kalması, muhafızın HER
    jetonu değil yalnız eşiği aşanı reddettiğini gösterdi — o test
    olmasaydı, her jetonu reddeden bozuk bir muhafız da sondayı
    geçerdi ve kimse giriş yapamazdı.

    **EK — DAR SORU TUZAĞI:** bir ölçüm doğru olabilir ama sorulan
    soru dar olabilir. Çıktıyı okurken *"bu ne diyor"* kadar *"bunun
    tersini soran biri ne görürdü"* de sorulur.

    Bunun bedeli aynı pakette ödendi: `Tumleyen_YoksayilirsaFazlaYetki
    Dogar` testi yeşilken "tümleyeni okumak şart" diye okundu. Aynı
    çıktı "tümleyeni okumayan HER ŞEYİ görür" de diyordu — yani
    kodlamanın açık tarafa düştüğünü. Ölçüm doğruydu, soru dardı.

62. **DÜZENLENEBİLİR BİR KONTROL BİR SÖZDÜR.**

    Bir alan sözleşmeden (DTO) çıkarıldığında, o alanın ekrandaki
    kontrolü de KALKMALI ya da SALT OKUNUR olmalıdır.

    Kontrol ekranda kalıp isteğe konmazsa: kullanıcı değeri
    değiştirir, sistem **"güncellendi"** der ve **hiçbir şey olmaz.**
    Bu, hatadan KÖTÜDÜR — hata görülür, yalan görülmez.

    **NE DERLEME NE TYPESCRIPT BUNU YAKALAR:** form durumu alanı hâlâ
    taşır, yalnız istek gövdesine koymaz. İki taraf da geçerli kod.

    TERS YÖNDE DE GEÇERLİ: serviste var olan bir yetenek ekranda
    düğmesi yoksa, kullanıcı için YOKTUR.

    KAYNAK: HP/1. K1 (kod değişmez) ve K3 (aktiflik tek kapıdan) arka
    uçta uygulandı; ekranda "Durum" seçicisi ve "Hesap Kodu" girişi
    **düzenlenebilir kaldı ama gönderilmiyordu.** Kullanıcı "Pasif"
    seçip kaydetseydi "güncellendi" mesajı alır, hesap aktif kalırdı.
    Testlerin hepsi yeşildi.

63. **OLUMSUZ İDDİA DA ÖLÇÜM İSTER.**

    *"Gerekmez", "boştur", "yoktur", "dokunmuyor"* — bunlar kanıt
    gerektirmiyormuş gibi hissettirir, çünkü gösterilecek bir şey
    yoktur. Oysa **yanılması en kolay iddialar bunlardır.**

    Ve yanılmanın bedeli **İKİ KATIDIR**: olumsuz iddia üzerine
    genellikle bir KONTROL KALDIRILIR — yani hem yanılırsın hem
    korumasız kalırsın.

    KAYNAK: HP/1 · K8. Önce *"xmin göç gerektirmez"* denildi ve Kural
    51 değerlendirmesi bu bilgiyle DÜŞÜRÜLDÜ. `BekleyenModelDegisikligi
    Tests` yakaladı: göç gerekiyordu. Sonra *"göç var ama Up'ı boş"*
    denildi; `Up()` okununca **sistem sütunu oluşturmaya çalışıyordu**
    (`AddColumn<uint>("xmin", type: "xid")`). Canlıda
    `column name "xmin" conflicts with a system column name` ile
    düşerdi.

    İki olumsuz iddia arka arkaya, ikisi de ölçümsüz, ikincisi
    birincinin düzeltmesi olarak söylendi.

    Kural 48'in kardeşi: **orada sıfırı ölçüm döndürüyordu, burada
    sıfırı biz varsayıyoruz.**

64. **MUHAFIZ TESTLERİ SONUCUNU DA YAZAR.**

    Bir testin **adı** neyi kontrol ettiğini söyler; **kırılırsa NE
    OLACAĞINI** söylemez. Bir muhafızı kıran kişi genellikle onu yazan
    kişi değildir ve kırdığı şeyin bedelini bilmez.

    Sıradan davranış testleri için gerekmez. Ama sonucu iddiadan
    okunamayan muhafızlarda — **TOLERANS, SIRA, SÜZGEÇ, İZİN SINIRI,
    EŞZAMANLILIK** — teste tek cümlelik bir *"bu kırmızıya dönerse şu
    olur"* notu girer.

    KAYNAK: `KayitSurumu` tolerans testleri.

    | Tolerans | Sonuç |
    |---|---|
    | Çok dar (tam eşitlik) | **her istek çakışır**, kimse kaydı düzenleyemez — görünür ama felç |
    | Çok geniş (saniye) | **gerçek çakışma kaçar**, kayıp güncelleme sessizce olur — görünmez ve zararlı |

    İkisi birlikte yazılmadan toleransın bir **ARALIK** olduğu
    anlaşılmıyor; tek kenar yazılırsa diğeri "gereksiz karmaşıklık"
    gibi görünür ve kaldırılır.

65. **KARŞILAŞTIRMA, ARADIĞIN DEĞİŞİKLİĞİN ÇÖZÜNÜRLÜĞÜNDE YAPILIR.**

    Aradığın şeyden **bir kademe kaba** bir karşılaştırma sessizce
    geçer:

    | Kaba | İnce |
    |---|---|
    | durum | içerik |
    | sayı | kimlik |
    | ad | gövde |

    *"Değişmiş dosya listesi aynı"* ile *"dosyaların içeriği aynı"*
    FARKLI iddialardır; birincisi ikincisini KANITLAMAZ.

    Bir doğrulama kurarken sor: **yakalamaya çalıştığım fark, bu
    karşılaştırmanın gördüğü seviyede mi?**

    KAYNAK: 2026-08-30, sonda kalıntı kontrolü. `git status` sabotajlı
    ve sağlam hâlde AYNI `M` satırını veriyordu — dosya zaten
    değişmişti, sabotaj onu bir kez daha değiştirdi ve durum satırı
    kımıldamadı. Ayırt eden `md5` oldu.

    Aynı hata bu oturumda başka kılıklarda da çıktı: muhafız `"alan"`
    arayıp `.alan` yazımını kaçırdı; dedektör `ad:` arayıp kısayol
    `ad,` sözdizimini kaçırdı; yetim bekçisi `Ad(` arayıp metot grubu
    kullanımını kaçırdı. Hepsinde arama, aradığı şeyin alabileceği
    biçimlerden dardı.

66. **İZLEME KANALI, İZLEDİĞİ ŞEYLE AYNI ARIZAYI PAYLAŞMAMALIDIR.**

    Paylaşıyorsa **sessizliği sağlıkla ayırt edilemez** hâle gelir:
    *"hata bildirimi gelmiyor"* hem **"hata yok"** hem **"kanal ölü"**
    demektir ve ikisi aynı görünür.

    Bir hata/izleme kanalı, izlediği sistemin kırıldığı yerden
    **BAĞIMSIZ** doğrulanır — düzenli bir canlılık sinyaliyle ya da
    kanalı doğrudan sınayan bir testle.

    **"Rapor gelmiyor" ASLA sağlık kanıtı sayılmaz.**

    KAYNAK: 2026-08-30. `istemci-hatalari` ucu başarıda **204**
    dönüyordu ve 204/502 proxy hatasının kurbanlarından biriydi.
    **21 kırık ucu bize bildirecek kanal, o 21'den biriydi.**
    Ekranlar çöküyor, bildirmeye çalışıyor, bildirim de 502 alıyordu.

    Kural 48'in bir başka yüzü: orada sıfır sonuç yokluğun kanıtı
    değildi; burada sıfır rapor sağlığın kanıtı değil.

    **EK — YENİ KANALIN İLK SINAVI:** `istemci-hatalari` kanalı
    **doğduğu andan itibaren** kırıktı ve 14 saat fark edilmedi;
    çünkü yeni bir kanalın ilk sınavı "çalışıyor mu" değil, **sessiz
    olması**ydı. Sessizlik başarı sanıldı.

    Yeni kurulan her bildirim kanalının **İLK testi, bilerek bir hata
    üretip kanalın taşıdığını görmek** olmalıdır.

67. **MUHAFIZIN ÜÇ SONUCU VARDIR: GEÇTİ / İHLAL / KARAR VEREMEDİ.**

    Bir muhafız işini bitiremezse — zaman aşımı, erişilemeyen kaynak,
    eksik girdi — sonucu **İHLAL DEĞİLDİR** ve **GEÇTİ de değildir.**

    Üçüncü durumu ayrı bildirmeyen bir muhafız iki yoldan biriyle
    zarar verir: ya **yanlış alarm** üretir ve okunmamayı öğretir
    (Kural 47), ya **sessizce geçirir** ve yanlış güven verir
    (Kural 48).

    **Kırmızının SEBEBİ, kırmızının kendisi kadar rapor edilir.**

    KAYNAK: jeton tek-yer muhafızı tam takımda 5 sn sınırını aştı
    (~800 kaynak dosya tarıyor) ve **gerçek ihlalle aynı göründü** —
    ayırt eden tek şey hata metnindeki `Test timed out` satırıydı.

    UYGULAMA: proxy duman kontrolü üç sonuçlu kuruldu — 204 GEÇTİ,
    502 İHLAL (yayın durur), başka her şey KARAR VEREMEDİ (uyarır,
    yayını durdurmaz). Gerekçe: kontrolün hedefini bulamaması bir
    YAYIN sorunu değil, KONTROL sorunudur.

68. **TESTLER SERVİSİ DOĞRUDAN ÇAĞIRIR, PROXY'DEN GEÇMEZ.**

    Proxy, **kapsamı sıfır olan bir katmandır** ve tek gözü tarayıcı
    doğrulamasıdır. Yayın sonrası duman kontrolü proxy üzerinden ve
    **en az bir 204 dönen uçtan** geçmelidir.

    KAYNAK: 204/502. 18 Temmuz'dan 30 Ağustos'a — **altı hafta** —
    on paketin yazma uçları canlıda 502 veriyordu ve **2865 test
    yeşildi.** Sağlık kontrolü `/api/health`e bakıyordu ve o gövdeli
    cevap döndüğü için kusuru hiç görmedi.

## 5a. CANLIDA YANLIŞ ÜÇ ÇEK KAYDI — VERİ BOZUK DEĞİL, GİRİŞ YANLIŞ

Bu kaydı ileride veritabanına bakan biri "veri bozulmuş" sanmasın
diye bırakıyorum. **Veri tutarlıdır; girilen bilgi yanlıştır.**

| Çek | Yaprağın bankası | Seçilen ödeme hesabı | Sorun |
|---|---|---|---|
| 805088 | Garanti | Fibabanka hesabı | Yanlış BANKA |
| VCK-2026-000020 | Garanti | Kasa hesabı | Banka yerine KASA |
| VCK-2026-000022 | Garanti | Kasa hesabı | Banka yerine KASA |

Son ikisi iptal edilmiş kayıtlardır. Karışıklığın yalnız bankalar
arasında değil, **kasa ile banka arasında da** yaşandığını
gösterdikleri için buraya yazıldılar — acil yamanın etiketine türün
(`Banka · …` / `Kasa · …`) girmesinin sebebi budur.

**SEBEP KULLANICI DİKKATSİZLİĞİ DEĞİL:** şirketin altı banka
hesabının `Name` alanı birebir aynıydı ("Ankara Merkez TL Hesabı").
Açılır listede altı özdeş satır görünüyordu; seçen kişinin ayırt
etmesini sağlayacak hiçbir işaret yoktu.

**GEÇMİŞE DOKUNULMAYACAK.** Bu üç kayıt kodla düzeltilmez, migration
ile düzeltilmez, elle UPDATE edilmez. Yanlış fiş kesilmişse karşılığı
**düzeltme fişidir** ve bu bir muhasebe işlemidir — Mehmet'in kararı
(2026-08-26). Yazılım tarafının borcu ileriye dönüktür: seçimi ayırt
edilebilir yapmak (acil yama, `1ab5293b`) ve verilen çekte kasa
hesaplarını listeden çıkarmak (ÇEK/2).

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

## 7b. KARARSIZLIĞIN BİR PARÇASI ÇÖZÜLDÜ — TARİH ÇAKIŞMASI (2026-08-25)

**Bugün deploy iki kez üst üste durdu**, hep aynı iki testte:
`ForeignCurrencyInvoiceTests.Import_ForeignInvoiceWithoutDeclaredRate_UsesArchivedTcmbRate`
ve `...ManualInvoice_ForeignCurrencyWithoutRate_UsesArchiveAndPostsInLira`.
Hata: `Expected 47.4881, Actual 44.000000`.

**İLK TEŞHİSİM YANLIŞTI.** "Bilinen kararsızlık" dedim ve yeniden
deploy ettim; ikinci koşu AYNI iki testte düşünce yanlış olduğu
anlaşıldı. Kararsızlık rastgeledir; bu yeniden üretilebilirdi.

**KÖK NEDEN — TARİHE BAĞLI ÇAKIŞMA:**

`CommodityPriceTests:319` kendi günlerini GÖRELİ seçiyor:
`DateTime.UtcNow.Date.AddDays(-20)`. 25 Ağustos'ta bu **2026-08-05**
ediyor — `ForeignCurrencyInvoiceTests`'in SABİT tarihi. Birincisi
kuru **44** olarak ÜZERİNE YAZIYOR (`SetRateAsync` upsert),
ikincisi ise "varsa dokunma" dediği için kendi kurunu (47,4881) hiç
tohumlayamıyor.

Dün aynı hesap 08-04 veriyordu; gece yarısından ÖNCE koşan tam tur
2629/2629 geçti, sonrakiler düştü. Kusur takvime bağlı ve ayda
birkaç gün kendini gösteriyor (bir sonraki çakışma: `-25` gün
hesabıyla 2026-08-30).

**DÜZELTME:** `ForeignCurrencyInvoiceTests.EnsureRateAsync` yetkili
hale getirildi — "varsa dokunma" yerine kendi kurunu her koşuda
yazıyor. Sabit tarih DEĞİŞTİRİLMEDİ: `UsdRate` o günün gerçek TCMB
bültenine ait, değiştirmek testin anlamını bozardı.

**SONDA:** eski "varsa dokunma" davranışı geri kondu → tam olarak o
iki test kırmızıya döndü. Teşhis kesin.

**DERS:** paylaşılan test veritabanında bir test kendi verisini
"varsa dokunma" ile tohumlarsa, o veriyi ÜZERİNE YAZAN başka bir
teste karşı savunmasızdır. Tohumlama YETKİLİ olmalı: test kendi
önkoşulunu garanti eder, varlığını varsaymaz. §7'deki "fixture
yalıtımsızlığı" adayının somut bir örneği bu.

**§7'DEKİ KARARSIZLIĞIN TAMAMI BU DEĞİL.** Oradaki ölçüm (personel
testlerinde ~dörtte bir oranında düşme) ayrı bir belirti ve hâlâ
açık.

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

---

## İŞEMRİ/2 FAZ 1 — ÖLÇÜM KAYDI (2026-09-02)

### KAPI 1: ölçüm tasarımın gerekçesini düzeltti

Tasarım notu şöyleydi: *"Detayda 'Yapacak — ' boş… İŞEMRİ/2'nin
dolduracağı alan tam olarak orası."* Ölçüm slotun **neden** boş
olduğunu değiştirdi:

`app/gorevler/page.tsx:393` — görev formu `assignedToUserId: null`
gönderiyor, **sabit**. Formda atama alanı hiç yok. Yani slot personel
alanı eksik olduğu için değil, **hiçbir yolun atama yazmaması** yüzünden
boştu. Canlı doğruluyor: iki görev kaydının ikisinde de
`AssignedToUserId` NULL.

Bu paketi geçersiz kılmıyor — gerekçesini düzeltiyor. Paketin
içeriği (tür + personel alanı, tek göç, yazma yolları) bağımsız olarak
isteniyordu.

### İKİ KİMLİK UZAYI, ARALARINDA SIFIR BAĞ

Canlıda ölçüldü:

| Ölçüm | Değer |
|---|---|
| Aktif personel (`Status=1`) | 79 |
| İşten ayrılmış (`Status=4`) | 2 |
| Kullanıcı hesabı | 13 |
| `AppUser.PersonnelId` dolu olan kullanıcı | **0** |
| `DepartmentId` dolu olan aktif personel | **0** (5 departman tanımlı) |
| Aktif şantiye ataması olan personel | 25 |

`AppUser.PersonnelId` alanı **var ama hiçbir satırda dolu değil**.
Yani "bu kullanıcı şu kişidir" sorusunun bugün cevabı yok. Personelin
ezici çoğunluğuna `AssignedToUserId` ile iş verilemiyordu.

**Kaskadın departman yarısı canlıda boş.** Beş departman tanımlı, hiçbir
aktif personel bir departmana bağlı değil. Proje/şantiye yarısı dolu
(25 personel). Faz 2'nin seçicisi buna göre kurulacak; departman
süzgeci yazılacak ama bugün hiçbir şeyi süzmeyecek.

### "YAPACAK" SLOTUNUN İKİ KAYNAĞA DÜŞME TEHLİKESİ — KAYNAKTA KAPATILDI

İki atama alanı tek slotu besleseydi, bugün dördüncü kez düzeltilen
desen (ETİKET/1) veri katmanında yeniden doğardı. Alınan karar:
**görüntü katmanında öncelik kuralı yok; çelişki yazma yollarında
reddediliyor.** Öncelik kuralı yazılsaydı, kapı bir gün gevşediğinde
hangisinin doğru olduğunu sessizce seçerdi.

### "ÜÇ YAZMA YOLU" BİR SAYIMDI VE EKSİKTİ

Kapsam POST, PUT ve Hızır olarak konmuştu. Ölçüm **dördüncüsünü**
gösterdi: `POST /api/tasks/{id}/delegate` de `AssignedToUserId`
yazıyor ve kuraldan geçmiyordu. Personele atanmış bir görev bir
kullanıcıya devredilince **iki alan da dolu kalırdı** — kural isteğin
içindeki çelişkiyi reddederken, bu yol çelişkiyi **kaydın içinde**
üretiyordu.

Bu, ACIL/2'nin dersinin genişletilmiş hâli: bir alanın kapısı
kurulurken o alanı YAZAN bütün fiiller aranır, yalnız POST ve PUT
değil. `grep -n "AssignedToUserId" ` ile doğrulanabilir bir sayım
olsaydı dördüncüsü baştan görünürdü.

### YANLIŞ KIRMIZI — İLAN EDİLEN KIRMIZI BU DEĞİLDİ

Kural 61 gereği "mevcut POST testleri tür göndermediği için 400 alacak"
diye ilan edildi. İlk koşuda 29 testin **hepsi** düştü — ama sebep kapı
değildi: `TEST_DB_CONNECTION` tanımsızdı ve `TestWebApplicationFactory`
daha kurulurken patlıyordu. Kapıya hiç ulaşılmadı.

"Kırmızı verdi" ile "kapı çalıştı" aynı şey değildir. Ortam kurulunca
gerçek ölçüm çıktı: **6 düştü / 6 geçti**, düşenlerin hepsi kayıt
oluşturabilen testler.

### ÖLÇÜLMEDİĞİ SÖYLENEN ŞEY: PERSONEL KAPSAM SÜZGECİ — **KAPANDI**

**İLK KAYIT (KAPI 2 raporunda dürüst sınır olarak yazılmıştı):**
`PersonelAtanabilirMiAsync` kapsamlı okuma yapıyor
(`scoped.PersonnelAsync`), ama bu iddia **bugün sınanamıyor**:
`tasks.manage` izni canlıda yalnız **Admin** ve **Genel Müdür**
rollerinde, ikisi de geniş kapsamlı. Dar kapsamlı bir kullanıcı görev
zaten oluşturamıyor. Süzgeç yerinde duruyor ama dar kapsamda ne yaptığı
ÖLÇÜLMEMİŞ. *"Yetki genişlediği gün ayrı bir sonda gerekir."*

**MEHMET'İN DÜZELTMESİ (KAPI 2 onayının 2. şartı, 2026-09-03):**

> *"Rol değişikliğini bekleme, süzgeci daraltılmış kapsamla doğrudan
> çağır. Testsiz savunma bırakma."*

Doğru olan buydu ve gerekçesi benim kendi kaydımda duruyordu: bu kod
tabanının tekrar eden yarası testsiz savunma (`2d90c946`). "Bugün
devreye girmiyor" ile "doğru çalışıyor" aynı şey değil — üstelik aynı
dersi bu dosyanın başka bir yerinde **`MANUAL` kaçışı** için zaten
yazmıştım: *"bir kaçışın bugün kullanılmıyor olması, kapatılmasını
erteleme gerekçesi değildir."* Aynı hatayı tersinden yapıyordum.

**YÖNTEM: ROLÜ DEĞİL, KAPSAMI DARALT.** Yeni bir rol uydurmak yerine
`ICurrentDataScopeService` test konağında değiştirilip daraltılmış bir
anlık görüntü döndürüldü. İzin katmanına hiç dokunulmadı: aynı Admin,
aynı uç, aynı istek — tek değişen kullanıcının **veri kapsamı**.

İZOLASYON ÖLÇÜLDÜ, VARSAYILMADI: POST gövdesinde kapsam kullanan TEK
yer `PersonelAtanabilirMiAsync` (`WorkTasksController:1134`). Masraf
merkezi doğrulaması ham `db.ProjectSites` okuyor. Yani bu testlerde
gelen 400'ün sebebi başka bir kapı olamaz.

`PersonelKapsamSuzgeciTests` — 3 test:

| Test | İddia |
|---|---|
| `DarKapsam_GorunmeyenPersonele_Atama_Reddedilir` | Personel VAR, AKTİF, aynı şirkette — tek eksiği kapsamda olmaması. 400. |
| `DarKapsam_GorunenPersonele_Atama_Kabul_POZITIF_KONTROL` | Aynı kapsam, şantiyeye ATANMIŞ personel. 200. Bu olmadan yukarıdaki test boştur. |
| `Kapsam_Cozulemezse_HicbirPersonel_Atanamaz_FAIL_CLOSED` | Kapsam hiç çözülemediğinde kapı AÇILMIYOR. `ScopedData`'nın docstring'i bunu vaat ediyordu; vaat artık test edilir. |

**SONDA I** — `scoped.PersonnelAsync` yerine ham `db.Personnel`
yazıldı. İlan: iki kırmızı (ret + fail-closed), bir yeşil (pozitif
kontrol). Gözlem: **tam olarak o iki test düştü**, pozitif kontrol
ayakta kaldı, kontrolcü geri alındıktan sonra bayt bayt aynı.

**KALAN SINIR (gizlenmiyor):** bu testler süzgecin *dar kapsamda doğru
süzdüğünü* gösteriyor; canlıda `tasks.manage`'in dar kapsamlı bir role
verilmesinin **başka** sonuçları olur (görev listesi, devretme, gelen
kutusu). Onlar bu paketin kapsamında değil ve o gün ayrıca ölçülecek.

### DÜRÜST SINIR: ÖN YÜZ TEK KAYNAK TESTİ

`tests/gorev-turu-tek-kaynak.test.ts` arka ucun C# enum'unu **metin
olarak** okuyor. Arka uç bir değeri hesaplayarak üretirse
(`IsEmri = 1 << 0`) ayrıştırma çöker — sessizce yeşile düşmez, açık
hata verir.

---

## ÜÇ KAYIT — ÖLÇÜM USULÜ (2026-09-02, İŞEMRİ/2 Faz 1 sırasında)

### 1. Mehmet'in ölçüm hatası — kendi kaydıyla birlikte

Mehmet, ETİKET/1'in "sessizce kaybolduğunu" sordu. Kaybolmamıştı:
`b9d125e0` commit edilmiş, itilmiş ve **canlıya çıkmıştı** —
`/var/lib/enderun-ai/last-deployed-commit` tam olarak o commit'i
gösteriyordu.

Kendi sözleriyle: *"bir işin yapılmadığını, yapıldığını gösteren kaydı
aramadan iddia ettim."*

NEDEN BURAYA YAZILIYOR: bu, aynı gün dört kez tekrarlanan desenin
kendisidir — **yokluk iddiası bir ölçüm gerektirir**. Kural 48'in
("boş sonuç yokluğun kanıtı değildir") insan tarafındaki karşılığı.
Kaydı arayan tek bir komut vardı ve çalıştırılmamıştı.

İKİNCİ DERS, BENİM TARAFIMDA: rapor yapılmış işi görünür kılmadıysa,
soruyu doğuran şey raporun kendisidir. ETİKET/1 bir önceki turda
bitmişti ve o turun raporu bu turda görünmüyordu; "bitti" demek
yetmiyor, **nerede durduğunu gösteren kayıt** raporda olmalı.

### 2. SONDALAR TEST DEĞİLDİR

**Sonda bir test değildir; testin ısırdığını kanıtlayan bir deneydir.**

Test kod tabanında yaşar ve her koşuda çalışır. Sonda bir kez yapılır,
sonucu kaydedilir ve geri alınır — kod tabanında iz bırakmaz. Bir
paketin "kaç test ekledi" sorusuna sondalar **sayılmaz**.

NEDEN ÖNEMLİ: ikisi karıştırılırsa iki ayrı yanılgı doğar. Sondayı
test sanmak, çırayı sonda sayısıyla şişirir — silinmesi hiçbir şeyi
bozmayan sayılar. Testi sonda sanmak daha kötüsü: bir kez kırmızı
verdiği görülen bir testin *kalıcı* olarak koruduğu varsayılır, oysa
hiç eklenmemiş olabilir.

İŞEMRİ/2 Faz 1'in sayıları bu ayrımla: **24 test eklendi**
(19 arka uç + 5 ön yüz), **10 sonda koşuldu** (A–G arka uçta,
F1–F3 ön yüzde). Sondaların hiçbiri kod tabanında durmuyor.

### 3. İKİ ÇİZGİ ARASINDAKİ FARK BİR KATKI ÖLÇÜSÜ DEĞİLDİR

Çıra bir **taban**tır, bir sayaç değil. "Çizgi 2798'di, şimdi 2843"
cümlesi 45 testin eklendiğini söylemez — yalnız tabanın nereye
taşındığını söyler.

ÖLÇÜLDÜ (çıranın kendi sayım kuralı, HEAD'in ayrı kopyasına
uygulanarak; kural çalışma ağacında 2843'ü birebir ürettiği için
doğrulandı):

| commit | sayı | paket |
|---|---|---|
| `d202eab3` | **2798** | İŞEMRİ/1 — çizgi burada yazıldı, **gerçek sayıya eşitti** |
| `2d90c946` | 2809 | MERKEZ/1 (+11) |
| `9b3c0a3c` | 2817 | MERKEZ/1 sondaları (+8) |
| `2e981381` | 2820 | ACIL/1 (+3) |
| `6ed68a43` | 2823 | ACIL/2 (+3) |
| `761f7eb2` | **2824** | ACIL/2 (+1) |
| `f9b61709` | 2824 | BAĞ/1 (ön yüz — arka uçta +0) |
| `b9d125e0` | 2824 | ETİKET/1 (ön yüz — arka uçta +0) |

**BOŞLUĞUN KAYNAĞI:** çizgi `d202eab3`'te doğruydu. Sonraki **beş**
commit toplam **26 test ekledi ve çizgiyi güncellemedi**. Bu kural
ihlali DEĞİL: çıra "aşağı inmesin" diyor, "gerçek sayıya eşit olsun"
demiyor — yukarı taşımak serbest, zorunlu değil.

**AMA BEDELİ ÖLÇÜLEBİLİR VE CİDDİ:** o beş commit boyunca arka uç
çırasının **26 testlik boşluğu** vardı. Yani o dönemde **26 test
silinebilir ve çıra hiçbir şey söylemezdi.** Gevşekliği olan bir
taban, gevşeklik tükenene kadar ısırmayan bir tabandır.

Bu boşluğun tam olarak hangi paketlerde biriktiği ayrıca dikkat
çekici: MERKEZ/1, ACIL/1 ve ACIL/2 — yani **sessizce silinen
savunmanın bulunduğu ve kapatıldığı paketlerin tam kendisi**
(`2d90c946` 26 satırlık atama kapısını metin aralığıyla kesmişti).
O paketler çırayı güncelleseydi, boşluk o günlerde sıfır olurdu.

Ön yüzde bu boşluk yok: `b9d125e0`'de çizgi 405, gerçek sayı 405.
Fark ETİKET/1'in çizgiyi ölçerek taşımasından geliyor.

**BOŞLUK TEK SEFERDE DEĞİL, BEŞ COMMIT BOYUNCA BİRİKTİ.** Her adım tek
başına küçüktü — 11, 8, 3, 3, 1 — ve hiçbiri kendi başına dikkat
çekecek boyutta değildi. Kimse toplamı görmedi çünkü toplam hiçbir
yerde YAZMIYORDU. Boşluk ancak bir sonraki paket çırayı kalemlemeye
çalışınca, yani üç gün sonra fark edildi.

Bu, ölçümün nerede durduğuyla ilgili bir ders: gevşeklik **hesaplanan**
bir sayı değil, **basılmayan** bir sayıydı. İki çizgiyi de gören tek
şey çıranın kendisiydi ve ikisinin farkını hiç söylemiyordu.

---

## SONDA G — İLAN EDİLEN KIRMIZI GELMEDİ (2026-09-02)

**Bu bölüm bir başarıyı değil, bir yanılgının ölçümle düzeltilmesini
kaydediyor.**

### İlan ve sonuç

Kural 61 gereği ilan edilmişti: *"Ad çözücünün erken çıkışından
`personeller.Count == 0` koşulu silinirse `S3d` kırmızı verir."*

**SONDA YEŞİL GELDİ** — 11/11. Sabotaj uygulandı, hiçbir test düşmedi.

### Sebep — ölçüldü, tahmin edilmedi

Erken çıkışın baktığı `liste`, dört alandan besleniyor:
`AssignedToUserId`, `AssignedByUserId`, `ApprovedByUserId`,
`DelegatedFromUserId`. Bunlardan **`AssignedByUserId` her iki yazma
yolunda da HER ZAMAN yazılıyor** —
`WorkTasksController:352` (`= currentUser.UserId`) ve
`HizirActionTools:204` (`= context.UserId`).

Yani API'den doğan hiçbir görevde `liste` boş olamaz. `liste.Count == 0`
hiç sağlanmıyor, erken çıkış hiç tetiklenmiyor ve eklediğim koşul
**ulaşılamaz koddu**. `S3d` onu sınamıyordu; başka bir şeyi sınıyordu
(merkezsiz görevde adın NORMAL yoldan çözülmesi — değerli ama farklı
bir iddia).

### Neden kaldırılmadı

Koşul DOĞRU, yalnızca bugün ulaşılamaz. İki seçenek vardı:

  (a) Ulaşılamaz olduğu için sil.
  (b) Ulaşılabilir bir şekil bul ve SINA.

(b) seçildi. `S3e` kaydı doğrudan veritabanına yazıyor — hiçbir
kullanıcı kimliği taşımayan, yalnız personele atanmış bir görev. Bu
şekil bugün üretilmiyor ama mümkün: bir içe aktarma ya da arka plan
işi, isteyeni olmayan bir görevi personele atayabilir.

(a) daha temiz görünüyordu ama bu kod tabanının yarası tam olarak
**testsiz savunma**: `2d90c946` 26 satırlık atama kapısını sessizce
sildi ve 2965 testin hiçbiri görmedi, çünkü o kod TESTSİZDİ. Doğru bir
savunmayı silmek yerine test edilebilir kılmak, aynı yaranın tekrarını
önlüyor.

### Asıl ders

**"Sonda kırmızı vermedi" iki farklı şey demek olabilir: savunma
sağlam, ya da SONDA YANLIŞ YERE VURUYOR.** İkisini ayıran şey, sondanın
kırmızı verdiğini görmüş olmaktır — G'de görülmedi.

Bu, `goc-provasi.sh`'deki KARAR VEREMEDİ ayrımının test tarafındaki
karşılığı: yeşil bir sonda, savunmanın çalıştığının kanıtı değildir.
Kanıt, sabotajın kırmızı ürettiğini GÖRMEKTİR.

`S3d`'nin yorumu düzeltildi: artık erken çıkışı koruduğunu iddia
etmiyor. Yanlış bir yorum, olmayan bir yorumdan daha zararlıdır —
sonraki okuyucu o iddiaya güvenip sınamayı atlar.
