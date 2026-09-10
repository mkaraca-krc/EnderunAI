# CANLI/1 — pilota çıkma kapısı

> **Bu dosya kapının kendisidir.** Konuşmada tutulan kapı, kapı
> değildir (KY3, 2026-09-08). Her turun sonunda güncellenir ve
> rapordaki tablo buradan üretilir.

Her madde üç şey taşır: **bugünkü durum**, **yeşil sayılması için
gereken ölçüm**, ve o ölçümün **bugünkü sonucu**. Durum üç değerden
biri: `YEŞİL` (ölçüldü, geçti) · `KISMEN` (bir kısmı ölçüldü) ·
`AÇIK` (ölçülmedi ya da düştü).

Son güncelleme: 2026-09-09

| # | madde | durum |
|---|---|---|
| K1 | Yetki ısırıyor | **YEŞİL** |
| K2 | Hassas ekranlar izne bağlı | KISMEN |
| K3 | Yayın güvenilir | KISMEN |
| K4 | Kırık ekran yok | YEŞİL |
| K5 | Telefondan kullanılabiliyor | KISMEN |
| K6 | Yedek kanıtlı | YEŞİL |
| K7 | Hesap tablosu | YEŞİL |
| K8 | İzleme (NÖBET/1) | YEŞİL |
| K9 | Testler canlıya dokunmuyor | YEŞİL |
| K10 | Sonsuz yeniden yükleme döngüsü yok | KISMEN |

---

## ⚠ KATALOG ARTIK ROL İZİNLERİNİN KAYNAĞIDIR (KATALOG/1, 2026-09-08)

**`RoleCatalog.cs`'ten bir (rol, izin) çifti kaldırmak, CANLI YETKİYİ
DEĞİŞTİRİR.** Her servis yeniden başlatmasında uzlaştırıcı koşuyor ve
katalogdan çıkan her çift veritabanından siliniyor.

Bu istenen davranıştır — katalog kaldırması bir aydır veritabanına
ulaşmıyordu (AC1). Ama yeni bir yüzey açıyor: dikkatsiz bir düzenleme
bir sonraki yeniden başlatmada yetkileri **sessizce** değiştirir.
KATALOG/1'den önce tohumlayıcı yalnız eklediği için bu risk yoktu.

**Bunu okuyan kişi neyi değiştirdiğini bilsin.** `RoleCatalog.cs`te bir
satır silmek, o rolü taşıyan kullanıcıların yetkisini kaldırmaktır.

**Kapı var, ama silmeyi engellemiyor — beyansız silmeyi engelliyor:**
yayın kuru koşuyu koşturuyor, silinecek çift varsa tam listeyi basıyor,
ve işleme mesajında satır başında `KATALOG-SİLME:` yoksa İHLAL veriyor.
Silmek meşru bir iş; sessizce silmek değil.

**İSTİSNA YOK:** elle verilmiş izinler (`role_manual_permission_grants`)
uzlaştırıcı tarafından korunuyor. Katalog dışı bir izni kalıcı kılmanın
yolu matristen açmaktır — toggle o kaydı yazar.

## K1 — Yetki ısırıyor

**Yeşil için gereken ölçüm:** kısıtlanmış bir kullanıcının kısıtlı
ekranı GERÇEKTEN açamadığı, kod okuyarak değil ÇAĞIRARAK gösterilmeli
(Kural 70); ve bir rol kaldırması yeniden başlatmadan SAĞ ÇIKMALI.

**Bugünkü sonuç:** SEED/1(b) canlıda; rig'de iki ayak yeşil
(`/dashboard` ve `/muhasebe` kısıtlıyken açılmıyor). Kalıcılık ayağı
ölçüldü: kaldırma kaydı tablosu var, tohumlayıcı ona bakıyor.

**YEŞİL (2026-09-09).** Üç yön de kapandı ve üçü de ölçüldü:

| yön | paket | kanıt |
|---|---|---|
| kullanıcı Deny çalışıyor | YETKİ/1 | rig'de çağırarak |
| rol kaldırması kalıcı | SEED/1b | yeniden başlatmada sağ çıkıyor |
| katalog kaldırması ULAŞIYOR | KATALOG/1 | canlıda uygulandı |

KATALOG/1 yayını (`70bb6e96`, 2026-09-09) `projects.delete` iznini
iki rolden kaldırdı ve ölçüm İKİ BAĞIMSIZ KAYNAKTAN uyuştu:

    sunucu SQL (ben)     : role_permissions 605 -> 603
    tarayıcı (Mehmet Bey): toplam grant     579 -> 577
    projects.delete      : 4 rol -> 2 rol (Admin, Genel Müdür)

İki sayı arasındaki 26 fark, 7 gizli ölü iznin rol kayıtları; yayından
önce ayrıca uzlaştırılmıştı. Denetim izi de düştü:
`security_audit_events` içinde `RolePermission/Deleted` 6 -> 8.

Uzlaştırıcının iki yönü de rig'de sondayla ölçüldü (U4): katalogda
olmayan çift SİLİNİYOR, elle ekleme kaydı olan çift KALIYOR — iki satır
arasındaki tek fark o kayıttı.

## K2 — Hassas ekranlar izne bağlı

**Yeşil için gereken ölçüm:** her ekranın izin bağlantısı, kontrolü
GERÇEKTEN çağırarak ölçülmeli; bağlantısız ekran sayısı sıfır olmalı.

**Bugünkü sonuç:** 188 ekran tarandı, 15'i bağsızdı; KARAR 2 ile 4 yeni
izin üretilip bağlandı. Muhasebe granülerliği (K2 alt maddesi) hâlâ
açık.

## K3 — Yayın güvenilir

**Yeşil için gereken ölçüm:** yayın boyunca kullanıcıya bozuk cevap
gitmemeli (kesinti kapısı), ve göç zinciri canlı şemayı birebir
üretmeli.

**Bugünkü sonuç:** kesinti kapısı kalıcı ve son üç yayında 0 hata;
atomik takas hem ön yüzde hem arka uçta. Göç zinciri canlı şemayı
birebir üretiyor (5801=5801, 0/0).

**Neden KISMEN:** SQUASH/1 uygulanmadı; yeniden başlatmanın ~2 sn'lik
boşluğu kabul edilmiş risk olarak duruyor (S5 uyarısı var, engel yok).

## K4 — Kırık ekran yok

**Yeşil için gereken ölçüm:** yayın sırasında ve sonrasında parça
(chunk) hatası sayısı sıfır olmalı, ve ölçüm ZİYARET EDİLMEMİŞ bir
yoldan yapılmalı.

**Bugünkü sonuç:** DAĞITIM/1 ile 24→0, sonra 10→0; son üç yayında 0.

## K5 — Telefondan kullanılabiliyor

**Yeşil için gereken ölçüm:** gerçek tarayıcıda, gerçek çözünürlükte
(390x664 gibi) ölçüm — jsdom değil, grep değil.

**Bugünkü sonuç:** MESAJ/3 Parça 1 canlıda (`7b6b0fd4`); composer
görünen alanda kalıyor, ölçüm gerçek tarayıcıyla yapıldı.

**Neden KISMEN:** yalnız mesajlaşma ekranı ölçüldü. Diğer ekranlar
için telefon çözünürlüğünde ölçüm YAPILMADI.

## Yayınlanan düzeltmeler ve doğrulama durumu (2026-09-09)

| paket | yayın | Mehmet Bey'in canlı doğrulaması |
|---|---|---|
| PANEL/1 (panel mesaj yüklemiyordu) | `6ad8d54a` | **DOĞRULANDI** |
| PN4 (yazma alanı panelden taşıyordu) | `5847b45e` | **DOĞRULANDI** |
| YT4 Adım 3 (Admin kısayolu kaldırıldı) | `c753d797` | **DOĞRULANDI** |

**PN4 + PANEL/1 dayanağı** (canlı, 1536×695, Mehmet Bey'in ölçümü):

    panel        {üst 48,  alt 535}
    yazma alanı  {üst 138, alt 182}   PANEL_İÇİNDE: true
    taşma        -353  (düzeltme öncesi +80 DIŞARIDA idi)
    mesajlar geldi (2 mesaj) · "henüz mesaj yok" yalanı YOK · ataç düğmesi var

**YT4 Adım 3 dayanağı** (canlı, yetki matrisi API'si, Mehmet oturumu):

    payment.plan.approve : yalnız Genel Müdür, Admin'de YOK
    Admin                : 140 iznin 139'u — eksik olan tek izin o
    Genel Müdür          : 140/140
    /user-management/users · /permission-matrix · /company-settings ·
    /odeme-planlari : hepsi 200. Kısayol kalkınca kimse kilitlenmedi.

## Admin ayrıcalığı — YETKİ/3 · YT4 (2026-09-09)

`PermissionAuthorizationMiddleware` içinde bir kısayol vardı:
`roleNames.Contains("Admin")` görünce BÜTÜN izin kontrollerini
atlıyordu. Kaldırıldı.

**Bu bir "fazladan yetki" değildi, YAZILI BİR KARARIN ÇİĞNENMESİYDİ.**
`RoleCatalog.SensitiveKeys` "ödeme onayı Admin'e GİTMEZ (ÖP/1a · İ2)"
diyordu; middleware "Admin her şeyi yapar" diyordu ve kazanan
middleware'di. İki yerde iki ayrı karar, hangisinin kazandığı hiçbir
yerde yazılı değildi.

### Kaldırmadan önce ölçülenler (2026-09-09 15:37 UTC)

    kod: PermissionCatalog.Keys      147
    vt : permissions                 147   (kod ile birebir)
    vt : Admin rolünün grant sayısı  146
    fark                               1 -> payment.plan.approve

    o izni isteyen uç : TEK — OdemePlanlariController.cs:180
    Admin rolündeki kullanıcı : TEK — mehmet
    mehmet'in rolleri : Admin + Genel Müdür
    payment.plan.approve'u taşıyan rol : Genel Müdür
    mehmet'in Deny kaydı : 0

Middleware kısayolun ötesinde YALNIZ izin kontrolü yapıyor; hesap-aktif
kontrolü kısayoldan ÖNCE geliyor ve atlanmıyordu. Veri kapsamı, üyelik
ve mesai kontrolleri başka katmanlarda.

### DAVRANIŞ DEĞİŞİKLİĞİ — altı ay sonra okuyan için

**Adım 3'ten sonra, YALNIZ Admin rolü verilen bir kullanıcı ödeme planı
onaylayamaz** (`payment.plan.approve` Genel Müdür'de). **Bu kasıtlıdır —
İ2 kararı.** Bugün `mehmet` etkilenmiyor çünkü her iki rolü de taşıyor.

Yeni bir Admin hesabı açıp "neden onaylayamıyorum" diye soran biri
olursa cevabı budur: eksiklik değil, karar. Onay yetkisi isteniyorsa
Genel Müdür rolü verilir ya da İ2 kararı açıkça gözden geçirilir.

İki davranış daha bilerek değişti:
1. Admin'e konulan bir **Deny kaydı artık ısırır** (bugün Deny yok).
2. `sub` çözümlenemeyen bir jeton eskiden kısayoldan geçiyordu;
   artık 403 alır.

### Henüz YAZILAMAYAN cümle

"Admin artık ayrıcalıklı değildir; matriste Admin'den kaldırılan bir
izin gerçekten kalkar." Bu cümlenin İKİNCİ yarısı henüz doğru değil:
`PermissionMatrixController:101` Admin sütununun değiştirilmesini
reddediyor. Adım 5 (matrisi Admin sütununa açmak) bitmeden bu cümle
yazılmayacak — yarısı doğru bir cümle, yanlış cümleden kötüdür.

## Yayın kaynağı ağaçtır — engelleyen kapı VAR (ölçüldü 2026-09-09)

`safe-deploy` `dotnet publish`'i DEPO KÖKÜNDEN koşturuyor; yani çalışma
ağacındaki izlenmeyen bir dosya da derlenip canlıya çıkabilirdi.

**Bunu engelleyen kapı ZATEN VARDI ve fail-closed:**
`require_clean_git_tree`, `git status --porcelain` boş değilse yayını
durduruyor — `--porcelain` izlenmeyen dosyaları (`??`) da listeler.
İkinci bir kontrol daha var: `agac_hala_ayni_mi` derlemeden hemen önce
aynı soruyu tekrar soruyor ve başlangıç commit'iyle karşılaştırıyor
(yayın ortasında ağaca dokunulmasına karşı).

**NASIL ÖLÇÜLDÜ:** iki izlenmeyen dosya ağaçtayken yayın başlatıldı.

    [ERROR] Repo'da commit edilmemiş değişiklikler var — ...
    Sonuç: UNKNOWN · Süre: 0s

Sıfırıncı saniyede durdu; test turuna ve derlemeye hiç girmedi.

**"KAPATILDI" DEĞİL — ZATEN KAPALIYMIŞ.** Bu bulgu, "kapı yok"
çıkarımının ölçülmeden yazılmasından doğdu; ölçüm tersini söyledi.

**EKLENEN TEK ŞEY:** kapı artık GEÇTİĞİNDE de günlüğe yazıyor —
`Ağaç temiz, yayınlanan commit: <sha>`. Satırı kapının kendi kod yolu
yazıyor, ayrı bir `echo` değil: ayrı yazılsaydı satır doğru görünürken
kapı devre dışı olabilirdi. Sessiz bir kapı ile kaldırılmış bir kapı
günlükte ayırt edilemez; artık edilebiliyor.

## GÖRÜNÜRLÜK/1 — "7 ölü izin" YANLIŞ İFADEYDİ (2026-09-09)

KATALOG/1'den beri hem Mehmet Bey hem ben "7 gizli ÖLÜ izin" diyorduk.
**Bu ifade ölçülmeden aylarca taşındı ve yanlıştı.** Doğrusu:
**7 gizli ama YÜRÜRLÜKTE izin.**

`KullanimdanKalkti` işareti, UYGULAMADAN kalktı anlamına gelmiyordu.

### Yedi anahtar — iki olgu, ayrı ayrı ölçüldü

| anahtar | `role_permissions`'ta | matris API'sinde | rol sayısı |
|---|---|---|---|
| `accounting.manage` | VAR | **YOK** | 4 |
| `attendance.manage` | VAR | **YOK** | 3 |
| `finance.manage` | VAR | **YOK** | 3 |
| `hakedis.manage` | VAR | **YOK** | 4 |
| `payroll.manage` | VAR | **YOK** | 3 |
| `projects.manage` | VAR | **YOK** | 4 |
| `purchasing.manage` | VAR | **YOK** | 5 |

**`RoleCatalog`'da YOK DEĞİLLER — VARLAR.** `K` yansımayı
`PermissionCatalog.Keys` *sabitleri* üzerinde yapıyor, `Permissions`
meta listesi üzerinde değil. Yani uzlaştırıcı bu satırları her açılışta
YENİDEN ÜRETİYOR. "Kalan artık satır" değil, **etkin verilen izinler**.
Gizli olan tek şey MATRİS GÖRÜNTÜSÜYDÜ.

### Yürürlükte olduklarının kanıtı

    açık RequirePermission niteliğinde:
      attendance.manage 6 uç · accounting.manage 5 uç · finance.manage 4 uç

DÜZELTME (2026-09-09): burada ikinci bir dayanak daha yazılmıştı —
"middleware yoldan türetmede 7/7 anahtarı döndürüyor". O cümle KOD
OKUMASIYDI, ölçüm değil. Ölçünce gerçek uçların HİÇBİRİNİN türetmeye
düşmediği görüldü (aşağıda). Sonuç değişmiyor: anahtarlar 15 ucun
açıkça istemesiyle yürürlükte. Ama dayanağın biri yanlıştı.

`accounting.manage`'i bir rolden kaldırmak isteyen kullanıcı onu
ekranda BULAMIYORDU; middleware ise onu aramaya devam ediyordu.

### Toplam uzlaştırma — fark 7 değil, 26

    role_permissions toplam : 603
    matris API grant        : 577
    fark                    :  26

26 = yedi anahtarın rol sayıları toplamı (4+3+3+4+3+4+5).
**Yalnız Admin'de değil — 9 farklı rolde:** Admin, Genel Müdür,
Finans Sorumlusu, Ön Muhasebe, İK Sorumlusu, Teknik Koordinatör,
Teknik Ofis, Depo Sorumlusu, Satın Alma Sorumlusu, Şantiye Şefi.

Aynı fark izin sayısında da görünüyor: Admin `role_permissions`'ta
**146**, matris API'sinde **139**. İki sayı FARKLI KÜMELERİ sayıyor;
aradaki 7, yukarıdaki anahtarların Admin'deki kayıtları.

### Nereden geldi

`32a0a9a2` (2026-09-08, "SEED/1(b) + KARAR 2 + KARAR 3(b)") bu yedi
izni `KullanimdanKalkti: true` işaretledi. **Kataloğdan düşürülmediler**
— "satırlar neden kaldı" sorusunun cevabı yok, çünkü kaldırılmadılar.

### Ne yapıldı

`c6433e58` — bayrak kaldırıldı, izinler matriste göründü:
izin 140 -> 147 · hücre 2100 -> 2205 · grant 577 -> 603.
Uygulama davranışı DEĞİŞMEDİ; yalnız görünürlük.
Muhafız: `PermissionMatrisiGorunurlukTests` (matrisi çağırarak ölçüyor).

### Yoldan türetme — ölçülen tam tablo (2026-09-09)

**ENVANTER, YANSIMAYLA.** `EndpointDataSource` çalışma anında
numaralandırıldı, nitelik `GetOrderedMetadata` ile okundu —
middleware'in izin ararken kullandığı ÇAĞRININ AYNISI. Grep DEĞİL.

    toplam uç 824 · hepsi RouteEndpoint (kaçan tip yok)
    niteliksiz api/ uç : 32   (ilk sayım 27'ydi; süzgeç `api/` arıyordu,
                               hub rotaları `/api/hubs/...` diye geliyor
                               ve KAÇMIŞLARDI — süzgecin kendisi de
                               ölçülmesi gereken bir şeymiş)
    bunlardan kaba anahtara türetilen : 0

**"DALLAR ULAŞILAMAZ" DEĞİL.** Eşleşmeyen yollara YAZMA yöntemiyle
gidildiğinde 7 kaba anahtardan 6'sı üretiliyor:

    POST /api/muhasebe/olmayan-uc    -> 403  accounting.manage
    POST /api/finans/olmayan-uc      -> 403  finance.manage
    POST /api/hakedis/olmayan-uc     -> 403  hakedis.manage
    POST /api/puantaj/olmayan-uc     -> 403  attendance.manage
    POST /api/bordro/olmayan-uc      -> 403  payroll.manage
    POST /api/satin-alma/olmayan-uc  -> 403  purchasing.manage

Bu yollarda İŞLEYİCİ YOK — kapıdan sonra 404. Veriye ulaşılamıyor.

**A4 — TÜRETME NULL DÖNÜNCE KAPI AÇIK (fail-open), ÖLÇÜLDÜ.**
İzni SIFIR kullanıcı (katalog dışı boş rolle kurulmuş) gerçek uçlarda:

    GET /api/user-preferences        -> 200  (veri döndü)
    GET /api/auth/me                 -> 200
    POZİTİF KONTROL, aynı kullanıcı:
    GET /api/accounting-accounts     -> 403  requiredPermission: accounting.view

**A5 — `UcKapisi`'NİN KÖR NOKTASI YOK, ÖLÇÜLDÜ.**
İki geçici uç eklendi, ikisi de niteliksiz: biri middleware yol
kalıbına DÜŞEN (`api/accounting/...`), biri DÜŞMEYEN (`api/zzz-...`).

    UÇ KAPISI — UYGULAMA AÇILAMAZ.
    BEYANSIZ UÇ (2):
      - SondaKalibaDusen.Getir      (api/accounting/sonda-kaliba-dusen)
      - SondaKalibaDusmeyen.Getir   (api/zzz-sonda-kaliba-dusmeyen)

Kapı yol kalıplarına BAKMIYOR; her `api/` ucundan beyan istiyor.
Uçlar silinince açılış geri geldi (kaldırmanın pozitif kontrolü).

**ZİNCİR, ÜÇ HALKASI DA ÖLÇÜLMÜŞ:** middleware fail-open → ama
`UcKapisi` beyansız uç bırakmıyor → dolayısıyla fail-open yalnız
BİLEREK muaf edilmiş uçlara uygulanıyor.

TEK HALKA OLDUĞU İÇİN KIRILGAN: kapı bir bayrakla kapatılırsa ya da
açılış sırası değişirse fail-open sessizce geri gelir. İkinci halka
(A7) sıraya alındı — henüz YOK.

**A6 — `projects.manage` kalıbı DOĞRU.** Kalıp `"/api/projects"` /
`"/api/project"` arıyor; gerçek rotalar da İngilizce
(`api/projects/{id}/...`, `api/project-extra-works`). `/api/projeler`
diye bir rota yok — 404 alması beklenen davranış, kalıp kusuru değil.

### Deny sızıntısı ARANDI, BULUNMADI

Soru: `accounting.edit` üzerinde Deny olan bir kullanıcı, rolünde
`accounting.manage` varsa muhasebe kaydını düzenleyebiliyor mu?

    Deny VAR  -> 403          (Deny ısırıyor)
    Deny YOK  -> 403 değil    (pozitif kontrol: uç bu role açık)

**Sızıntı yok** — ama ilk turda bu YANLIŞ AYAKLA ölçülmüştü: seçilen uç
`[RequirePermission(AccountingEdit)]` TAŞIYORDU, yani türetmenin hiç
devreye girmediği durum ölçülüp sonuç genellenmişti. Yukarıdaki envanter
doğru ayağı ölçüyor: türetmeye düşen 32 ucun 0'ı kaba anahtar üretiyor.
`accounting.edit` 7 ucu, `accounting.manage` 5 FARKLI ucu koruyor;
kesişmiyorlar.

DÜRÜST SINIR: bir uçta ölçüldü, tüm uçlarda değil.

### EKSİK/1'e yazıldı: izin taksonomisi sızıntısı

Eşleşmeyen yollarda 404 yerine 403 dönülüyor ve yanıt gövdesi
`requiredPermission` alanını taşıyor. Kimlikli bir kullanıcı, bir yol
ailesinin hangi izni istediğini öğrenebiliyor:

    POST /api/bordro/olmayan-uc -> 403 {"requiredPermission":"payroll.manage"}

Veri sızmıyor; sızan şey YETKİ HARİTASI. Pilot öncesi engelleyici
değil, sıraya alındı — DOKUNULMADI.

### Sırada: KABA-İZİN/1

7 kaba izni ince izinlere devredip gerçekten emekliye ayırmak.
15+ ucun yetkisine dokunur; pilot baskısı varken YAPILMAZ.
Yalnız ölçüm ve tasarım, Mehmet Bey'in onayı beklenecek.

## K6 — Yedek kanıtlı

**Yeşil için gereken ölçüm:** yedeğin GERİ YÜKLENEREK doğrulanması —
"dosya var" yeterli değil.

**Bugünkü sonuç:** gecelik yedek + üç ayda bir geri yükleme tatbikatı
koşuyor; tatbikat satır damgasıyla karşılaştırıyor. Şifreleme
akışta, anahtarsız yedek alınmıyor (`44fbec4d`).

**Not (2026-09-08, düzeltme):** "SIZINTI/1 yedeği şişiriyor" varsayımı
ÖLÇÜLDÜ ve doğru çıkmadı — tar+gzip ile tüm `uploads/` 41 MB, yalnız
gerçek dosyalar 40 MB. Fark ~1 MB. Şişme yedekte değil, DİSK BLOKLARINDA:
21 KB'lık `project-files/` diskte 34 MB yer kaplıyordu.

**Açık kalan bağımlılık:** AK-3 — yedekler, anahtar ve canlı veritabanı
aynı diskte. Tek disk arızası hepsini götürür.

## K7 — Hesap tablosu

**Yeşil için gereken ölçüm:** tüm hesaplar (yalnız aktifler değil),
rolleri, veri kapsamı, son giriş, durum, kişisel izin ve kısıt
sayılarıyla listelenmeli.

**Bugünkü sonuç (2026-09-08):**

| kullanıcı | ad | roller | kapsam | son giriş | durum | kişisel izin | kişisel kısıt |
|---|---|---|---:|---|---|---:|---:|
| mehmet | Mehmet Karacabey | Admin + Genel Müdür | 1 | 2026-09-08 | aktif | 0 | 0 |
| smemis | Sedat MEMİŞ | Teknik Ofis | 1 | 2026-07-27 | aktif | 0 | 84 |
| uakkaya | Uğur AKKAYA | Finans + Satın Alma + Ön Muhasebe + İK + İSG | 1 | 2026-09-07 | aktif | 0 | 19 |
| vtepe | Veysel TEPE | Araç Sorumlusu | 1 | hiç | aktif | 0 | 0 |
| asakcak | Afife SAKÇAK | Sekreterya | 1 | 2026-07-27 | PASİF | 2 | 96 |
| cboran | Cennet BORAN | Teknik Ofis | 1 | hiç | PASİF | 0 | 0 |
| ccihan | Cuma CİHAN | Formen | 1 | 2026-08-04 | PASİF | 0 | 7 |
| dyildirici | Duygu YILDIRICI | Finans + İK | 1 | 2026-08-10 | PASİF | 5 | 0 |
| hkutlu | Haluk KUTLU | Teknik Koordinatör | 1 | 2026-07-27 | PASİF | 0 | 0 |
| ioktem | İsmail ÖKTEM | Teknik Ofis | 1 | hiç | PASİF | 0 | 0 |
| iyavuzkanat | İsmail YAVUZKANAT | Teknik Ofis | 1 | 2026-08-13 | PASİF | 9 | 7 |
| oturkmen | Özlem TÜRKMEN | Finans + Satın Alma + İK | 1 | 2026-08-12 | PASİF | 27 | 2 |
| ralici | Rıdvan ALICI | Teknik Koordinatör | 1 | 2026-08-03 | PASİF | 0 | 0 |

**Tablodan çıkan iki not:**
- `vtepe` aktif ama HİÇ giriş yapmamış.
- Dört pasif hesabın kişisel izin/kısıt yükü duruyor (`asakcak` 96
  kısıt, `oturkmen` 27 ek izin). Pasif hesap yeniden açılırsa o ayarlar
  da geri gelir.

**AC1 ile bağlantı:** `projects.delete` bugün dört rolde. Teknik Ofis'i
taşıyan tek aktif kullanıcı `smemis` ve onda bu izin kişisel olarak
KISITLI (canlıda doğrulandı: `smemis | projects.delete | DENY`).
Teknik Koordinatör'ü taşıyan aktif kullanıcı yok. Yani fiilî açık kapı
yok — ama onu kapatan şey TESADÜF. KATALOG/1 sapmayı kapatıyor.

## K8 — İzleme (NÖBET/1)

**Yeşil için gereken ölçüm:** bir birim düştüğünde haberin GERÇEKTEN
gittiği gösterilmeli; ve gürültü sınırı ile "düzeldi" bildirimi
çalışmalı.

**Bugünkü sonuç:** `enderunai-backend`, `enderunai-frontend` ve
`enderun-backup` OnFailure taşıyor; sonda kuru koşuda dört ayakta
yeşil (düştü → susturuldu → düzelmedi → düzeldi). Kanal teslimi
2026-09-07'de gerçek gönderimle kanıtlandı.

**Kapsam kararı (Mehmet, 2026-09-08):** derin sonda (kimlik
doğrulamalı uç) YAPILMIYOR — AK-6 izleme hesabı pilot öncesi
açılmıyor, yeni kimlik yüzeyi istenmiyor. K8 bu kapsamda yeşil.

**Dürüst sınır:** nöbetçi izlediği sunucunun İÇİNDE koşuyor. Sunucu
ölürse nöbetçi de ölür. "Servis bozuldu"yu yakalar, "sunucu öldü"yü
yakalamaz — dışarıdan bakan göz AK-7.

## K9 — Testler canlıya dokunmuyor

**Yeşil için gereken ölçüm:** testler koşarken canlı dizinlerin dosya
sayısı DEĞİŞMEMELİ; ve ölçümün pozitif kontrolü olmalı (dosyalar
gerçekten yazıldı, sadece başka yere).

**Bugünkü sonuç:** SIZINTI/1 kapandı.

| | önce | sonra |
|---|---:|---:|
| düzeltmeden önce, aynı suite | 16 615 | 16 655 (+40) |
| düzeltmeden sonra | 16 696 | 16 696 (0) |
| `project-files/` | 3 250 | 3 250 (0) |

Pozitif kontrol: testlerin geçici kökünde 28 dosya — yükleme gerçekten
oldu. Rig tam koşumda 24/24 geçti, canlı dizinler değişmedi.

Fail-closed kapı: `enderun_ai_test`e bağlı bir süreç canlı yazma
köküne bakıyorsa BAŞLAMIYOR (sonda: çıkış 134).

---

## TOHUM/1 — tohum parolası durumu

**Veritabanından KANITLANAMIYOR, sebebi ölçüldü.**
`PasswordChangedAtUtc` kolonu 2026-09-03 göçüyle geldi; parola yazan
bir yol ise 2026-07-27'den beri var. Aradaki beş haftada yapılmış bir
değişiklik hiçbir iz bırakmaz.

**Kesin olanlar:**
- `SEED_ADMIN_PASSWORD` canlı ortam dosyasında hâlâ tanımlı.
- Tohumlayıcı mevcut kullanıcının parolasını hiç güncellemiyor —
  yalnız oluştururken yazıyor.
- 13 hesabın 11'i 2026-09-04'ten beri parolasını değiştirmemiş;
  değiştiren ikisi de bugün pasif. Dört aktif hesabın dördü de
  değiştirmemişler listesinde.

Kesin cevap ancak kimlik bilgisini denemekle alınır; denenmedi.

## K10 — hiçbir ekran sonsuz yeniden yükleme döngüsüne girmez

**Yeşil için gereken ölçüm:** oturumsuz `/login` 10 saniye açık
tutulduğunda belge (document) isteği sayısı **1** olmalı; ve arka uç
hata verdiğinde ekran kullanıcıya anlaşılır bir mesaj göstermeli.

**Bugünkü sonuç (2026-09-08):** kusur canlıda ölçüldü ve kapatıldı.

| | ölçüm |
|---|---|
| düzeltmeden önce, canlı, TEK tarayıcı, 60 sn | 1256 istek, **358** `/login` yüklemesi |
| düzeltmeden sonra (rig testi) | belge isteği **1** olmalı |

**Zincir:** kök layout `MesajBaloncugu`u her rotada monte ediyordu →
baloncuk `auth/me` ve `user-preferences` çağırıyor → oturumsuzken 401
→ `api-client` koşulsuz `/login`e yönlendiriyor → sayfa baştan → başa
dön. Baloncuğun `if (!user) return null` koruması ARAYÜZÜ gizliyordu;
`useEffect` render'ın dönüşünden bağımsız koştuğu için istekler yine
gidiyordu.

**Konsol sessizdi** — her tam yükleme konsolu siliyordu. Kusuru yalnız
ağ kaydı gösterdi; test de oradan bakıyor.

**503 ayağı da yeşil, ve sebebi önemli.** Yayın sırasında arka uç 503
verirken giriş ekranı 10 saniye açık tutuldu (Mehmet, canlı ölçüm):
sayfanın kendi attığı istek **0**, belge yüklemesi **1**. Bir gün önce
aynı koşulda 10 saniyede **862** istek vardı.

Sayfa 503'ü *zarifçe karşılamadı* — 503'e yol açan çağrıyı **hiç
yapmadı**. ~~İkinci katman (baloncuk oturumsuzken istek atmıyor) devrede.~~

> **KAYIT DÜZELTMESİ (2026-09-10) — üstü çizili cümle YANLIŞTI.**
> Nasıl yakalandı: DASHBOARD/1 ölçümünde giriş akışının ağ kaydı
> sırayla ve zaman damgasıyla alındı (rig, `enderun_ai_test`). Çerezsiz
> `/login` sayfası açıldıktan ~1 sn sonra **bir** `GET
> /api/backend/auth/me` atılıyor ve **401** alıyor.
> Kimin attığı: `/login`'de monte olan kök layout bileşenlerinden
> `auth/me` soran YALNIZ `MesajBaloncugu` (taşıdığı `useCurrentUser`,
> `lib/use-current-user.ts:43`); `TaslakDeposuSaglayici` ve `HataSiniri`
> sormuyor. Baloncuğun koruması (`mesaj-baloncugu.tsx:257`) kendi VERİ
> isteklerini (`user-preferences`, konuşmalar) tutuyor — ama oturumun
> var olup olmadığını öğrenmek için sorduğu `auth/me`'yi tutmuyor, tutamaz.
> Doğrusu: **baloncuk oturumsuzken tek bir oturum sorusu (`auth/me`)
> atıyor; veri isteği atmıyor.**
> Döngü yine YOK: 401 `/login`'de yönlendirme üretmiyor (birinci katman,
> `api-client.ts` giriş ekranı koruması) — ölçümde belge yüklemesi 1.
> Yani döngüyü tutan asıl katman birinci katman; ikinci katman sanıldığı
> kadar değil, kısmen devrede.
> **Açıklanmamış çelişki:** yukarıdaki 9 Eylül canlı ölçümü "sayfanın
> kendi attığı istek 0" diyor; bugünkü rig ölçümü 1 buldu. Fark
> ölçülmedi — o günkü ölçümün süzgeci, 503 koşulu ya da o tarihten beri
> değişen kod olabilir; hiçbiri sınanmadı. Davranış bu turda
> DEĞİŞTİRİLMEDİ (zararsız); yalnız kayıt gerçeğe döndürüldü.
>
> **HİPOTEZ ÖLÇÜLDÜ (2026-09-10, Mehmet Bey'in hipotezi):** "canlıda eski
> SW `auth/me`'yi önbellekten verdi, istek ağda görünmedi." Canlı,
> oturumsuz `/login`, beş kip (SW serbest ilk yükleme; SW kontrol ederken
> iki yeniden yükleme; SW engelli iki yükleme): her kipte `auth/me`
> hem Playwright'ta hem DevTools'un kendi ağ kaydında (CDP `Network`)
> **tam bir kez, 401** görünüyor. **Normal koşulda hipotez ÇÜRÜDÜ** —
> eski SW isteği ağ kaydından gizlemiyor (ağ-önce çalışıyor; önbellekten
> yalnız ağ hatasında veriyor). 9 Eylül'ün koşulu (yayın sırasında 503)
> yayın olmadan üretilemediği için ÖLÇÜLMEDİ; çelişki o koşul için açık.

**Yapılmayan çağrının hatasında döngü olamaz** ve bu, "hatayı iyi
karşıla"dan daha sağlam bir sonuçtur: iyi karşılama kodu bozulabilir,
hiç yapılmayan çağrı bozulamaz.

**Kapı:** `safe-deploy` içindeki `giris_dongu_kapisi`, yayın sonrası
10 saniyede `/login` belge isteği 20'yi aşarsa İHLAL veriyor. Sağlık
kontrolü bunu yakalayamıyordu: tek istek atıp 200 görüyor, döngü ancak
sayfa açık tutulunca çıkıyor.

**Kalan borç (Kural 79):** yönlendirme kuralının üç ikinci okuyucusu
var — `app/sirketler/page.tsx:8`, `app/subeler/page.tsx:8`,
`app/cariler/page.tsx:153`. Üçü de kimlikli sayfalarda, `/login`de
koşmuyor; ama kanonik korumayı atlıyorlar.

## MESAİ/1 — okunamayan satır "mesai dışı" değildir (2026-09-10)

**Kural (Mehmet Bey):** okunamayan bir kullanıcı satırı "mesai dışı"
DEĞİLDİR. Geçici hatada oturum düşürülmez, yeniden denenir. Kalıcı
çıkışı yalnız gerçek bir "mesai dışı" kararı üretebilir.

### Önce bir düzeltme — 10 Eylül gecesi söylenen yanlış cümle

"Okunamayan satır → izleyici ucu 200 `isAllowed:false` → izleyici
çıkış çağırır → kalıcı çıkış" zinciri **kod okunarak** "doğrulandı"
diye bildirildi. **Çağrılınca kurulamadığı görüldü:** satırı silinmiş
kullanıcı izleyici ucunda **401 HesapPasif** alıyor — izin ara katmanı
onu controller'a ulaşmadan reddediyor. Geçici veritabanı hatası da
istisna fırlatıyor → 500 → izleyici çıkış YAPMIYOR (ölçüldü).

Yani o gece anlatılan yol yoktu. Ama kuralın kendisi iki **başka**,
ölçülmüş kusura uyuyordu:

### Ölçülen iki kusur

| # | kusur | ölçüm |
|---|---|---|
| 1 | Satırı okunamayan kullanıcı "**Mesai saatiniz sona erdiği için oturumunuz kapatıldı**" alıyor; denetime sahte `WorkHoursSessionRejected` yazılıyordu | xUnit, gerçek HTTP hattı: KIRMIZI → yeşil |
| 2 | İzleyici `isAllowed` taşımayan **her** 200 cevabında çerezi siliyordu (HTML gövde, eksik gövde) | gerçek tarayıcı, rig: çerez silindi → KIRMIZI; düzeltmeden sonra yeşil |

### Ne değişti

- Mesai değerlendirmesi üç değerli: **izinli / mesai-disi / belirlenemedi**.
  Satır okunamazsa `belirlenemedi` — mesai kapısı karar vermekten
  çekilir, hesabın varlığını soran izin ara katmanı cevaplar
  (401 HesapPasif, günlüğe doğru sebeple).
- İzleyici ucu kararı açıkça söylüyor (`karar`). `belirlenemedi` → 503.
- İzleyici **yalnız** `karar: "mesai-disi"` ile çıkış yapıyor.

### DAVRANIŞ DEĞİŞİKLİĞİ — altı ay sonra okuyan için

- İzleyici artık "0 dakika kaldı" hesabıyla **kendiliğinden** çıkış
  yapmıyor. Pencere kapanınca çıkışı bir sonraki yoklamada (en geç
  60 sn) sunucunun "mesai-disi" kararı yapar; o arada arka uç ara
  katmanı her isteği zaten kesiyor.
- Satırı silinmiş bir kullanıcının mesajı artık "mesainiz bitti" değil,
  "hesap pasif veya bulunamadı".

**Yayında (Mehmet Bey kararı, madde 5 — kayda geçsin):** `6dec0211`,
2026-09-10 10:53 UTC. İzleyici artık kendiliğinden çıkış YAPMIYOR;
kararı sunucu veriyor; pencere kapandıktan sonra çıkışın gecikmesi
**en fazla 60 saniye** (izleyicinin yoklama aralığı).

**Yayın öncesi ölçüm (madde 1):** canlıda 4 aktif kullanıcı; 1'i
ad üzerinden muaf (Admin/GM), **3'ü muaf değil** ve üçü de o anda
pencere içindeydi (09:00–18:00, İstanbul 12:59; açık geçici erişim
yok). Kapanışa 5 saat vardı. Yayından sonra canlı günlükte
`sebep=MesaiDisi` satırı 0, ön yüzde `CIKIS` satırı 0, denetimde mesai
reddi 0 — kimse işinin ortasında atılmadı.

### Nasıl ölçüldü

| ayak | düzeltmeden önce | sonra |
|---|---|---|
| gerçek arka uç, pencere kapatıldı → çıkış VAR (pozitif) | KIRMIZI (`karar` yok) | yeşil — gerçek arka uç `mesai-disi`, çerez silindi, adres `/login?reason=work-hours`, ön yüz günlüğü `CIKIS … tetikleyen=mesai-izleyicisi` |
| düzenek kontrolü (yakalanmış mesai-disi görülüyor) | yeşil | yeşil |
| 503 / 500 / ağ hatası → çıkış YOK | yeşil | yeşil |
| 200 HTML / 200 karar-yok → çıkış YOK | **KIRMIZI** (çerez silindi) | yeşil |

Doğuştan yeşil muhafızların kırmızısı **mutasyonla** gösterildi
(arka uç: 3/3 kırmızı, pozitif kontrol yeşil kaldı; ön yüz: `catch`
dalı çıkış yaparsa 4/4 kırmızı; "izinli değilse çık" yapılırsa 3/3
kırmızı). Her ayak en az bir kez kırmızı yandı.

### Bu düzeltmenin DAYANDIĞI şey — ve B ile bağı

Mesai kapısının `belirlenemedi`de çekilmesi, sıradaki izin ara
katmanının eksik kullanıcıyı reddetmesine BAĞLI. Bu bağ
`OkunamayanSatirMesaiDisiDegildirTests` ile kilitli: hesap katmanı
eksik kullanıcıyı geçirirse test 200 görür ve kırmızı yanar (mutasyonla
gösterildi). **B'nin çözücü önbelleği bu bağı etkiler** — öneri: B'nin dört
ayaklı sondasına bu test de girsin.

### Henüz yazılamayan cümle

"Mesai dışı kalan personelin oturumu doğru sebeple kapanıyor" — canlıda
muaf olmayan bir kullanıcıyla gözlenmedi. Rig'de gerçek arka uçla
ölçüldü; canlıda değil.

### GÜNLÜK/1 deliği — aynı ölçüm yolunda bulundu

GÜNLÜK/1 "her 401/403 kararı tek satır" diye yayına alındı, ama mesai
ara katmanının 401'i **hiç satır yazmıyordu** (ölçüldü: gerçek hatta
günlük sağlayıcısı dinlendi; `JetonYok` satırı görüldü, mesai satırı
yok). Dokuzuncu sebep eklendi: `MesaiDisi`. 10 Eylül'deki "bir sonraki
oluşta sistem sebebi kendisi yazacak" cümlesi, muaf olmayan personel
için bu düzeltme yayına girene kadar **eksikti**.

### Ölçüm sırasında bulunan, DÜZELTİLMEYEN üç şey (BAK-VE-KARAR)

**1. Service worker kimlikli cevapları saklıyor — ÖLÇÜLDÜ.** `public/sw.js`
her GET cevabını Cache Storage'a koyuyor. Rig'de bir oturumdan sonra
**26 `/api/backend/*` cevabı** önbellekteydi (`auth/me`, `hr/personnel`,
`projects/profitability-summary`, `mesajlar/konusmalar`, `ai/dashboard`
dahil) ve **çıkıştan sonra 26'sı da duruyordu.** MESAJ/4 için konan
kural ("SW hiçbir şeyi önbelleğe almaz") zaten var olan bir SW
tarafından ihlal ediliyor.
→ **SW/1 ile düzeltildi** (aşağıda, "kişisel veri"); (a) sızıntısı orada
ÇAĞRILARAK ölçüldü.
ÖLÇÜLMEYEN iki sonuç (kod okuması, hüküm değil): SW ağ hatasında bu
kayıtları geri veriyor — (a) aynı tarayıcıda sonraki kullanıcıya
öncekinin cevabı dönebilir; (b) önbellekteki bayat bir `mesai-disi`
gövdesi ağ hatasında izleyiciye verilirse, MESAİ/1'in kuralı SW
üzerinden yeniden ihlal edilir.

**2. Mesai kapanınca yönlendirme yarışı — ÖLÇÜLDÜ.** 401 alan her
bileşen `apiClient` üzerinden SEBEPSİZ `/login`e, izleyici
`/login?reason=work-hours`e yönlendiriyor. Bir koşuda ~0,8 saniyede
25 belge yüklemesi (16 tamam, 9 iptal) görüldü. Hangi adresin
kazandığı yarışa bağlı: kullanıcı mesai sebebini görmeyebilir.
Bu, 10 Eylül'deki "ilk gözlemde TAM URL" dersini de etkiler:
**`reason` yokluğu, sebebin mesai olmadığını kanıtlamaz.**

**3. Arka uç çıkış çağrısı mesai reddi diye kayda geçiyordu — DÜZELTİLDİ
(Mehmet Bey kararı, aynı yayın).** Mesaisi biten kullanıcının çıkışında
Next rotası arka uca `POST /api/auth/logout` gönderiyor; mesai ara
katmanı onu 401 ile reddedip denetime `WorkHoursSessionRejected`
yazıyordu. İz, olmayan bir olayı anlatıyordu (Kural 80). Yol muafiyet
listesine alındı; test önce KIRMIZI, sonra yeşil.

**İlk yazımdaki yanlış:** burada "arka uçta çıkış kaydı YERİNE mesai
reddi düşüyor" yazmıştım. Ölçüm düzeltti: **arka uçta çıkış ucu HİÇ
YOK** — muaf kullanıcının çağrısı 404 dönüyor (rig günlüğü). Yani
muafiyetten sonra sahte ret kalkar ama arka uç denetiminde çıkış kaydı
YİNE olmaz; çıkışın tek izi ön yüz günlüğündeki
`CIKIS kullanici=… tetikleyen=…` satırı (GÜNLÜK/1). Next rotasının
yorumu da bunu söylüyor: "backend does not expose token revocation yet".

## SW/1 — KİŞİSEL VERİ: tarayıcı önbelleğinde kimlikli cevap kalmaz (2026-09-10)

### Kişisel veri (KVKK)

Şantiye tabletleri **ortak kullanılıyor**. Önceki service worker her
GET cevabını tarayıcının Cache Storage'ına yazıyordu; bir oturumdan
sonra **26 kimlikli API cevabı** (kimlik bilgisi `auth/me`, personel
listesi `hr/personnel`, kârlılık özeti, mesaj listesi…) cihazdaydı ve
**çıkıştan sonra da duruyordu.** Bu, bir çalışanın ve şirketin kişisel/
ticari verisinin, oturumu kapatılmış ortak bir cihazda kalması demekti.

**Ölçülen sızıntı (çağrılarak, rig):** A kullanıcısı oturum açıp gezdi,
oturum düğmesiz kapandı (jeton süresi dolması gibi), B aynı tarayıcıda
oturum açtı ve ağ kesildi. B'nin `auth/me` çağrısı **200 döndü ve
içinden A'nın kimliği çıktı.** Ortak tablette bir sonraki kişi,
öncekinin verisini görüyordu. (Önceki kayıtta bu "kod okuması,
ölçülmedi" diye yazılmıştı; artık ölçüldü.)

### Karar (Mehmet Bey, 2026-09-10)

a) Kimlikli hiçbir cevap önbelleğe alınmaz (MESAJ/4 kuralı zaten buydu).
b) Yeni SW etkinleşirken eski önbellekler silinir — kod düzeltmesi tek
   başına bugünkü kayıtları temizlemez.
c) Çıkışta da temizlik.
d–f) Sondalar: aşağıda.

### Ne yapıldı

- `public/sw.js` artık **hiçbir şeyi önbelleğe almıyor**; `fetch`
  dinleyicisi yok, istekler SW'ye hiç uğramıyor. Çevrimdışı "son
  cevap" özelliği bilinçli olarak bırakıldı: bayat ya da BAŞKASININ
  cevabını göstermektense hata göstermek doğrudur.
- Etkinleşirken **köken üzerindeki bütün önbellekler** siliniyor
  (uygulama Cache Storage'ı SW dışında kullanmıyor — ölçüldü).
- Çıkış (`lib/auth/cikis.ts`, düğme ve mesai izleyicisi aynı yoldan)
  önbelleği boşaltıyor; temizlik başarısız olsa da çıkış yapılır.

### Nasıl ölçüldü

| sonda | eski SW | yeni SW |
|---|---|---|
| (d) gezinti sonrası kimlikli kayıt | **27** (KIRMIZI) | 0 |
| (d) çıkış sonrası önbellek | — (önceki adımda düştü) | boş; kırmızısı mutasyonla gösterildi (temizlik kaldırılınca kayıt kaldı) |
| (d) tarayıcı kapat-aç sonrası kimlikli kayıt | — | 0 (kalıcılık bir nöbetçi kayıtla kanıtlandı) |
| (e) sabotaj: eski adla + yabancı adla doldurulmuş önbellek, yeni SW etkinleşir | `enderun-erp-v1` KALDI (KIRMIZI) | hepsi silindi |
| (e') GERÇEK güncelleme yolu: eski SW kayıtlı ve önbelleği kendisi doldurmuş, yeni `sw.js` iniyor | — | 168 → **0** |
| (f) ağ kesikken B'ye dönen | **A'nın kimliği, 200** (KIRMIZI) | hiçbir şey (`fetch` hatası); çevrimiçi kontrolde B kendi kimliğini alıyor |

(e') bir ayrıntı gösterdi: güncellemenin indiği ilk gezinmede, yeni SW
etkinleşene kadar ESKİ SW kaydetmeye devam ediyor (106 → 168). Yeni SW
etkinleşince hepsi siliniyor ve yeniden yüklemeden sonra da 0 kalıyor.

### Sınırlar — dürüst cümle

- Eski kayıtlar bir cihazdan ancak o cihaz uygulamayı **bir kez daha
  açtığında** silinir (yeni SW o anda iner). Uygulamayı bir daha hiç
  açmayan bir tablette dünün kayıtları kalır; sunucu bunu uzaktan
  silemez.
- Çıkış temizliği yalnız çıkış düğmesi ve mesai izleyicisi yolunda
  koşuyor. Jeton süresi dolması gibi DÜĞMESİZ oturum sonlarında
  temizlik yok — ama yeni SW hiçbir şey kaydetmediği için temizlenecek
  bir şey de birikmiyor ((f) ayağı tam bu durumu ölçüyor).

### DAVRANIŞ DEĞİŞİKLİĞİ — altı ay sonra okuyan için

Uygulama çevrimdışıyken artık son görülen sayfayı/cevabı GÖSTERMİYOR;
ağ yoksa hata görünür. Bu bilinçli: önbellekten dönen cevap bayat da
olabilir, başka bir kullanıcınınki de.

### PWA kurulabilirliği bozulmadı — ölçüldü

`fetch` dinleyicisini kaldırmanın uygulamanın "ana ekrana eklenebilir"
olmasını bozup bozmadığı tahmin edilmedi: Chrome'un kendi
`Page.getInstallabilityErrors` cevabı eski ve yeni SW'de alındı. İkisinde
de kurulabilirlik hatası **YOK**, manifest hatası **0**.

## ÖLÇÜM 6 — izin değişince hangi kapı ne zaman görür (2026-09-10)

**Soru (Mehmet Bey):** Genel Müdür izni canlıda değiştiriyor ve ANINDA
etki bekliyor. İniş kararını ara katman jetondan veriyorsa, izin
değişikliği ne zaman geçerli olur?

**Ölçüm:** rig, dar rol, oturum KAPATILMADAN izin değiştirildi; her
geçişte t+0 ve t+65 sn. 13 satırın 13'ü önceden ilan edilen beklentiyle
birebir.

| geçiş | sayfa kapısı (Next ara katmanı, JETON) | menü (`auth/me`) ve veri kapısı (arka uç) |
|---|---|---|
| T1 rolle izin VERİLDİ | `/yetkisiz` — ESKİ karar sürüyor | menü hemen görüyor |
| T2 rolden izin ALINDI | `/dashboard` AÇILIYOR — eski karar | menü hemen kaybediyor; `GET /projects` **hemen 403** |
| T3 kişisel Deny EKLENDİ | `/dashboard` açılıyor — eski karar | menü hemen kaybediyor |
| T4 kişisel Deny KALDIRILDI (uakkaya senaryosu) | `/yetkisiz` — eski karar | menü hemen görüyor |
| T5 yeniden giriş | güncel | güncel |

**Mekanizma (ölçüldü + okundu):**
- Jeton girişteki izinleri İÇİNDE taşıyor; ömrü **12,0 saat**
  (`exp−iat`, ölçüldü). Jetonu yeniden yazan yalnız iki yer var: giriş
  ve parola değişimi (`app/api/auth/login/route.ts:78`,
  `change-password/route.ts:92`) — arada TAZELEME YOK. 65 sn ölçüldü;
  12 saat, jetonun ömrü ve tazeleme yokluğundan çıkarım.
- ~~Arka uç izni jetondan OKUMUYOR~~ — **EKSİKTİ, aynı gün düzeltildi.**
  Doğru olan: ANA veri kapısı (`PermissionAuthorizationMiddleware` →
  `UserAuthorizationService`, main'de önbelleksiz) izni her istekte
  veritabanından okuyor; T2'de jeton hâlâ `projects.view` taşırken uç
  403 verdi. AMA `ICurrentUserService.HasPermission / IsInRole / Roles`
  JETONDAN okuyor (`CurrentUserService.cs:61`) ve bununla karar veren
  arka uç yolları var: çek geri alma (`ChequesController:151,199`),
  sipariş işlemi (`PurchaseOrderService:334`), tedarikçi faturası GM onayı
  (`SupplierInvoiceService:284`, rol), satın alma onay aşaması
  (`ProcurementApprovalService:1215–1220`, izin + rol), KPI görünürlüğü
  (`ManagementKpiService:270`), yorum/ek erişimi
  (`CollaborationController:128`). **ÇAĞRILARAK ölçüldü (xUnit, gerçek
  hat):** `salary.view` rolden silindi, AYNI jetonla `auth/me`
  "salary.view yok" derken `GET /api/yonetim/kpi` "Bordro maliyeti"
  KPI'sını VERMEYE DEVAM ETTİ; yeniden girişte kayboldu. Yani bu yollar
  için veri kapısı da jeton ömrü boyunca eski. Nasıl yakalandı: C'nin
  (a) adımında "jetondaki izni okuyan her yer" sayılırken.

**Sonuç:** sayfa kapısı jeton ömrü boyunca (≤12 sa) ya da yeniden
girişe kadar ESKİ kararla çalışıyor; menü ve veri kapısı anında. İki
yönde de menü ile sayfa kapısı birbirinden farklı şey söylüyor. Bu,
DASHBOARD/1'i de kapsayan daha büyük kusur: iniş kararı jetondan
verilirse, uakkaya'nın Deny'i kaldırıldığında da 12 saate kadar
dashboard'a inemez.

**B ile bağı:** B'nin çözücü önbelleği veri kapısını da TTL kadar
eskitir. B'nin dört ayaklı sondasına T2 ve T3 ayakları girmeli (öneri).

**Karar bekliyor:** düzeltme yolu (Mehmet Bey).
