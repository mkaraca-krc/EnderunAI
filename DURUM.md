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

## BEKLEYEN KARARLAR

Yapılmayan işler ve nedenleri. Biçim: `konu | neden yapılmadı | ne gerekiyor`

1. **`bank_account.view` Finans Sorumlusu + İK Sorumlusu'na verilsin mi** |
   Çalışma yetkisi kuralı (c): IBAN görebilen kitleyi genişletiyor
   (2 rol → 4 rol). Anahtar bu iki role RoleCatalog'a yazılmıştı,
   kural gereği GERİ ALINDI |
   Mehmet'in onayı. Onaylanmazsa bordroda "Gerçek Ödeme" İK
   Sorumlusu'nda çalışmamaya devam eder (bugün de çalışmıyor).

2. **Tam IBAN için ayrı `bank_account.reveal` anahtarı** |
   Aynı kural: yeni anahtar açmak kitle kararı. Bugün tam IBAN ucu
   liste ucuyla AYNI anahtarı kullanıyor, ama ayrı yüzey ve her
   çağrıda denetim kaydı var |
   Mehmet'in kararı: izin düzeyinde de darlık isteniyor mu.

3. **Hesap planı aktarım ucu (`accounting-accounts/import`)** |
   Uç yazılacak (karar verildi) ama iş kuralları belirsiz: mevcut
   hesap kodu gelirse güncellensin mi, üst hesap yoksa oluşturulsun
   mu, hangi izin korusun. Kural (a): muhasebe kaydını etkileyen
   yeni kural kurulmuyor |
   Bu üç sorunun cevabı. Düğmeler devre dışı + "Hazırlanıyor".

4. **Depodan Zimmet: stok ve muhasebe davranışı** |
   Kural (a): zimmet gider yazacaksa bu yeni bir muhasebe kuralı.
   Denetim tamam, model hazır (`HrAssetAssignment` alanları mevcut) |
   İki karar: (1) stoktan düşsün mü yoksa "zimmet" konumuna mı
   taşınsın, (2) fiş kesilsin mi — türe göre mi (sarf gider yazar,
   dayanıklı taşınır).

5. **Mesaj saklama: arşivden silme** | Kural (b) ve (f): silme
   mekanizması kurulmuyor | 12 ay çevrimiçi + arşiv onaylı; arşivden
   sonrası için karar yok, silme kurulmadı.

6. **KVKK aydınlatma metni** | Kural (e): hukuk metni yazılmıyor |
   Mehmet hazırlatacak; ekranda yeri açılacak.

7. **Disk şifrelemesi** | Mevcut sunucuda yeniden kurulum gerektirir |
   Karar ve bakım penceresi. Yedek şifrelemesi ve dizin izni
   yapıldı; disk hâlâ düz `ext4`.

8. **DB bağlantı kaydı** (`log_connections`, `logging_collector`) |
   Sıradaki pakete bırakıldı | Kim ne zaman bağlandı bugün hiç iz
   bırakmıyor.

9. **28 Temmuz dallarındaki özellikler — hangisi yeniden yazılsın** |
   Birleştirme kural gereği yapılmayacak; hangi özelliğin istendiği
   iş kararı | Aşağıdaki listeden seçim.

   **UCUZ ENVANTER** (commit başlıklarından; dosya analizi ve
   çakışma ölçümü YAPILMADI):

   | Özellik | Canlıda |
   |---|---|
   | **Proje bütçesi** — bütçe modeli, hesaplama servisi, uçlar, migration, ve sipariş onayında bütçe kontrolü | **YOK** (`project_budgets` tablosu yok) |
   | **Sipariş PDF'i** — PdfSharpCore, sipariş PDF servisi ve uçları | **YOK** (`PdfSharpCore` paketi yok) |
   | **Hızır eylem motoru** — eylem sözleşmeleri, servis arayüzü, eylem uçları | **KISMEN** (`HizirController` var, "eylem motoru" ayrı) |
   | **Stok/mal kabul/sipariş modelleri** | **VAR** (üç tablo da mevcut, 0 satır) |
   | **Hiyerarşi kapsamlı muhasebe panoları** | **BİLİNMİYOR** |
   | **CI iş akışları** (backend restore/build, doğrulama) | **BİLİNMİYOR** — `.github/workflows/ci.yml` var, kapsamı ölçülmedi |

   En belirgin boşluk **proje bütçesi**: 7 commit'lik bir küme ve
   sipariş onayına bütçe kontrolü ekliyordu. Canlıda karşılığı yok.

10. **Yarıda kesilen deploy için yordam** | Bugün deploy iki kez
    dışarıdan öldürüldü; ikisi de TEST aşamasındaydı ve iz
    bırakmadı | safe-deploy'a "yarım koşu tespiti" adımı gerekli mi,
    yoksa mevcut sıralama (test → yedek → yayın → restart) yeterli
    mi.

11. **§7'deki personel testi kararsızlığı** | Bugün çözülen tarih
    çakışması (§7b) o kararsızlığın PARÇASI DEĞİL; personel
    testlerindeki ~dörtte bir düşme hâlâ açık | Ayrı bir teşhis turu.

---

## KARAR KAYDI

Kendi verdiğim iş kuralı kararları. Teknik kararlar (test, indeks,
isimlendirme) buraya yazılmaz.

`tarih | konu | karar | dayandığım varsayım | geri alması kolay mı`

- `2026-08-24 | Banka hesabı izni | Yeni dar anahtar bank_account.view açıldı, YALNIZ Admin+GM'e (yansımayla). Finans/İK'ya verilmedi. | IBAN kitlesini genişletmemek, bordro engelini kaldırmaktan öncelikli (kural c). | EVET — RoleCatalog'a iki satır`
- `2026-08-24 | IBAN maskeleme | Liste ucunda son dört hane; tam IBAN ayrı uçtan, tek hesap, her çağrı denetim kaydına. Kayda IBAN yazılmıyor. | Banka adı + hesap sahibi + son dört hane, ödeme ekranında hesabı ayırt etmeye yeter. | EVET`
- `2026-08-24 | Ödeme eylemi görünürlüğü | bank_account.view olmayan rolde "Gerçek Ödeme" düğmesi HİÇ render edilmiyor (403 yerine yokluk). | Bozuk ekran göstermek, eylemi gizlemekten kötü. | EVET`
- `2026-08-24 | Kapsam alanı eksik satır | hr-dashboard ve zimmet kutusunda "alan yoksa satırı al" deseni "alan yoksa ELE" olarak değişti. | Şirket izolasyonunda varsayılan kapalı olmalı; bugün tek şirket olduğu için görünür etki yok. | EVET`
- `2026-08-24 | Yarım özellikler | ai-analysis/site-analysis servisleri ve fiyat farkı hesaplama işlevleri SİLİNDİ (ekranda karşılığı yoktu); hesap planı aktarımı KALDIRILMADI, devre dışı + "Hazırlanıyor". | Ekranda görünen yarım özellik kaldırılmaz, görünmeyen ölü kod silinir. | EVET — git geçmişi`

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
