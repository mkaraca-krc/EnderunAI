# DERSLER — ölçülmüş tuzakların indeksi

> **Yeni bir YARDIMCI ya da SONDA yazmadan önce bu dosyayı oku.**
> Kapı yok, muhafız yok — bu bir okuma alışkanlığı. Zorlamaya
> kalkılırsa tören olur, işe yaramaz.

## Bu dosya neden var

2026-09-08'de **dört kez** aynı şey oldu: ölçülmüş bir ders kodun
yorumunda duruyordu ve yeni kod yazılırken okunmadı. Dördü de bir rig
turuna ya da düşmüş bir yayına mal oldu.

Yorumların yeri doğru — o kodu **düzenleyen** kişi görüyor. Sorun,
**başka bir yerde yeni kod yazan** kişinin görmemesi. Eksik olan yasak
değil, **indeks**.

## Kural

- Her ders **tek satır** + kaynağı. Gerekçe **kodda kalır**.
- **Kopya tutma.** İki yerde duran gerekçe ayrışır ve hangisinin güncel
  olduğu bilinmez.
- **Bu dosyayı doldurmaya çalışma.** Yalnız "ders koddaydı, okumadım"
  vakası yaşandığında satır eklenir.

  **Satır sayısı artıyorsa iyi. Artmıyorsa kimse eklemiyordur** — dosya
  bitmiş demek değil, kimsenin bakmadığı demek. Bu dosyanın sağlığı
  içeriğiyle değil, BÜYÜME HIZIYLA ölçülür. Aylardır aynı satırda
  duruyorsa ya kimse yeni yardımcı yazmıyordur (olası değil) ya da
  yazanlar buraya dönmüyordur (olası).

---

## Ölçüm disiplini

- **Yeni ölçüm tasarlamadan önce, eldeki ölçümlerin NE KANITLADIĞINI
  tek tek yaz.** 2026-09-08'de iki kez fazladan ölçüm yazmaya kalkıldı
  ve ikisinde de eldeydi: "gecikmesiz ayak" = birinci testin kendisi,
  "izolasyon turu" = kırmızı turların yapılandırması
  → `frontend/enderun-ai/tests/duzen/mesaj-paneli-acilir.spec.ts`

  **Bu, bu dosyanın çözdüğü sınıfın KARDEŞİ ama aynısı değil.**
  Yukarıdaki maddeler "başkasının yazdığı dersi okumamak"; bu madde
  "kendi ürettiğin ölçümün ne söylediğini çıkarmamak". İkisi de elde
  olanı görmemek — biri dışarıdan geleni, öteki kendi ürettiğini.

- **Bir adayı elerken hangi yolla elediğini yaz: ÖLÇÜLDÜ mü, KOD
  OKUMASINA GÖRE ZAYIF mı.** İkisi farklı ağırlıkta; karıştırılırsa
  çıkarım ölçüm gibi sunulur. (PANEL/1'de dört aday kod okuyarak
  "elendi" denildi, gerçek kusur dördü de değildi.)

- **Kırmızı, bulgunun değil ENGELİN işareti olabilir.** Test hiç koştu
  mu, sonda doğru yere mi vurdu, altyapı meşgul müydü — kırmızıyı ürüne
  yazmadan önce bunları ayır
  → `deploy/scripts/duzen-testi.sh` (publish adımının ÖLÇEMEDİ kodu)

- **Tek ölçüm noktasından türetilen formül hipotez değildir; o noktanın
  başka bir yazılışıdır.** Ayırt edici güç ancak TÜRETİM KÜMESİNİN
  DIŞINDAKİ noktalarda ölçülür.
  *(Bu satır Mehmet Bey'e ait — kendi hipotezi için yazdı.)*

  9 Eylül, PN4: h=800'de ölçülen 80 px taşmadan türetilen
  `max(0, 100vh - 45rem)` formülü yalnız h=800'de tuttu; 700/900/1000
  çürüttü. AYNI TURDA benim `max-height:70vh` alternatifim de tek
  noktadan türetilmişti ve hiçbir noktada tutmadı. İki hipotez, aynı
  hata: türetildiği yerde "doğrulanmış" görünmek.

  Kardeş ders, aynı sayfada: ölçemediğin noktayı "ötekiler gibidir"
  diye geçme. İkisi de tek şeyi söylüyor — **ölçülmemiş nokta,
  ölçülmüş noktanın kopyası değildir.**
  → `frontend/enderun-ai/tests/duzen/mesaj-paneli-tasma.spec.ts`
    (`PN4_YUKSEKLIK=1` — türetim kümesinin dışındaki üç nokta)

- **Bir işareti, anlamını ölçmeden taşıma.** `KullanimdanKalkti`
  ("kullanımdan kalktı") işareti UYGULAMADAN kalktı anlamına
  gelmiyordu. Yedi izin bu işaretle matristen gizlendi; yedisi de
  middleware'de ve `RequirePermission` niteliklerinde YÜRÜRLÜKTEYDİ.
  Kullanıcı kaldıramadığı bir yetkiyi taşımaya devam etti.
  *(9 Eylül. Bu ders ikimize de ait: "7 ölü izin" ifadesini aylarca
  ikimiz de ölçmeden kullandık — gizleyen işareti okuduk, gizlenen
  şeyin ne yaptığını sormadık.)*
  → `backend/EnderunAI.Api.Tests/PermissionMatrisiGorunurlukTests.cs`

- **Bir SAYIMDAN bir SONUÇ çıkarma.** *(Bu satır Mehmet Bey'e ait,
  kendi hatası için yazdı; ikimizin adına duruyor.)*

  > "'Gerçek uçlardan hiçbiri düşmüyor' ÖLÇÜMDÜ; 'o kod hiç çalışmıyor'
  > benim ÇIKARIMIMDI. Sayım ölçümdür; sayımın SEBEBİ hakkındaki cümle
  > ölçüm değildir."

  9 Eylül, A: 32 gerçek ucun 0'ı kaba anahtara türetiliyordu — doğru
  sayım. Oradan "dallar ulaşılamaz" sonucu çıkarıldı. Ölçünce dalların
  6'sının EŞLEŞMEYEN YOLLARDA çalıştığı görüldü.
  → `backend/EnderunAI.Api.Tests/KabaAnahtarTuretmeEnvanteriTests.cs`

- **Düzenlemeden önce desenin KAÇ YERE uyduğunu ölç — ve HİÇ UYUP
  UYMADIĞINI.** İki yüzü var, ikisi de SESSİZ:
  · fazla eşleşme -> ilgisiz bir yeri de bozar
  · sıfır eşleşme -> hiçbir şey yapmaz, ama "düzelttim" sanırsın

  9 Eylül, ikinci yüz: `duzen-testi.sh`'e ÖLÇEMEDİ yolu eklediğimi
  sandım; desenim `5156` literalini arıyordu, kod `${ARKA_PORT}`
  kullanıyordu. `str.replace` bulamadığında HATA VERMEZ. Yamanın
  tuttuğunu varsaysaydım, kapıyı ölü bırakıp "canlandırdım" diye
  yazacaktım — o gece canlandırdığımız kusurun birebir tekrarı.
  Ölçüm (çıkış 1, ÖLÇEMEDİ satırları yok) yakaladı.

  KURAL: her yamaya `assert` koy; tutmayan yama, yapılmamış yamadır.

- **(ilk yüz)** "Bir yere
  uyuyordur" varsayımı, ölçmeden yapılan her varsayım gibi, er geç
  ilgisiz bir yeri de yakalar.
  9 Eylül: `PermissionMatrixController`'da bir `SaveChanges` desenini
  değiştirdim; desen İKİ yere uyuyordu ve ilgisiz bir eylemi (rolün
  veri kapsamı güncellemesi) de değişmeze bağladım. Fark edip geri
  aldım.

- **İki kısıt çatışınca birini seçme — çatışmanın DOĞMADIĞI yapıya geç.**
  9 Eylül: kilitlenme koruması işlem içindeki değişikliği görmeliydi
  (aynı bağlantı şart) ama geri alırken ilgisiz bekleyen yazımları
  düşürmemeliydi (ayrı bağlantı şart). İkisi aynı anda sağlanamıyordu.
  Çözüm ikisinden birini seçmek değil, mutasyonu korumanın devrettiği
  işin İÇİNE almak oldu — o zaman bekleyen ilgisiz yazım hiç olmuyor.

- **DÖRDÜNCÜ SONUÇ SINIFI: BAK-VE-KARAR.** *(Mehmet Bey adlandırdı,
  9 Eylül.)* Üç sonucumuz vardı — GEÇTİ / İHLAL / ÖLÇEMEDİ. Dördüncüsü
  bunların hiçbiri değil.

  **Tanım:** iddianın bozulması, kusurun VARLIĞINI değil, ölçümün
  GEÇERLİLİĞİNİN BİLİNEMEZ olduğunu gösterir. "Dünya iyileşmiş de
  olabilir, ölçüm ölmüş de olabilir."

  **Doğru tepki:** koşarak düzeltmek DEĞİL — bakıp hangisi olduğuna
  karar vermek.

  **Raporda İHLAL'den AYRI SATIRDA görünür.** Ayrılmazsa bir gün biri
  onu ihlal sanıp "düzeltir" ve iyi haberi geri alır.

  Bugünkü örnekler (bozulduklarında ne anlama geldikleriyle):
  · `adaylar.Count > 0` — türetmeye düşen uç kalmadı. Her uca nitelik
    konmuş OLABİLİR (iyi haber) ya da süzgeç kırılmıştır.
  · `tumUclar.Count > 800` — uç numaralandırması öldü ya da uygulama
    gerçekten küçüldü.
  · K5 `olculen.length >= 14` — bir ekran bilerek kaldırılmış olabilir
    ya da tarama erken bitmiştir.
  · *(zaten vardı, adı yoktu)* `test-sayisi-ratchet` → "tarama boşa
    düşmüyor" ve "çıranın saydığı metot koşucununkiyle tutuyor".
    İkincisinin mesajı bunu zaten sezmiş: "KOŞUCU SAYIMI ESKİ OLABİLİR".

- **"KAPI YOK" CÜMLESİ, KAPININ OLMADIĞININ ÖLÇÜMÜ OLMADAN YAZILAMAZ.**
  Yokluk iddiası da pozitif kontrol ister — hem YAZANA hem KABUL EDENE.
  *(Ders ikimize ait, 9 Eylül.)*

  Ben: "`publish` depo kökünden derliyor" ölçümdü; "demek ki işlenmemiş
  kod sessizce canlıya çıkar" ÇIKARIMDI ve yanlıştı —
  `require_clean_git_tree` yayının başında zaten fail-closed duruyordu.
  Mehmet Bey: o çıkarımı doğrulatmadan üstüne yeni bir kapı kurmamı
  istedi; yokluk iddiasını pozitif kontrolsüz kabul etti.

  ÖLÇÜM (sonradan yapıldı): iki izlenmeyen dosya ağaçtayken yayın
  denendi, SIFIRINCI SANİYEDE durdu. Kapı vardı ve çalışıyordu.
  → `deploy/scripts/safe-deploy.sh` (`require_clean_git_tree`)

## Test ve rig

- **`page.request` çerez taşımaz**; veri uçlarını sayfa içinden `fetch`
  ile çağır (giriş `page.request.post` ile yapılabilir)
  → `frontend/enderun-ai/tests/duzen/mesaj-sesi.spec.ts:47`

- **Test zemini, uygulamanın o veriyi okuduğu yoldan kurulur.**
  Veritabanına doğrudan yazmak yalnız uygulamanın hiç yazmadığı
  veriler için meşrudur
  → `frontend/enderun-ai/tests/duzen/mesaj-paneli-acilir.spec.ts`
    (`tercihleriKur`)

- **Testler sunucuda kalıcı durum bırakır**; her test kendi başlangıç
  varsayımını kurmalı, yoksa sıraya gizlice bağımlı olur
  → `frontend/enderun-ai/tests/duzen/mesaj-paneli-acilir.spec.ts`
    (`paneliAc`, sıra bağımlılığı gerekçesi)

- **Bir elemanın yokluğunu bildirmeden önce, var olduğu bilinen bir
  durumda seçicinin onu bulduğunu göster** (Kural 48'in DOM tarafı)
  → `frontend/enderun-ai/tests/duzen/mesaj-paneli-acilir.spec.ts`
    (seçici pozitif kontrolü)

- **VAR OLMAYAN BİR ADLA ARAYIP "YOK" SONUCUNA VARMA.** Aradığın adın
  gerçekten var olduğunu önce göster.
  · 8 Eylül: `.mesaj-baloncuk-panel` seçicisiyle arandı, "panel
    açılmıyor" denildi; seçici bayattı, panel açılıyordu.
  · 9 Eylül (A6): `/api/projeler/...` yoluna istek atıldı, 404 geldi ve
    "kalıp tutmuyor" sanıldı. Gerçek rotalar İngilizce
    (`api/projects/...`); kalıp DOĞRUYDU, yanlış olan test yoluydu.
    Bu kez çıkarım yapılmadan önce yakalandı.

- **Yeni sonda yazarken, çalışan bir sondanın kurulumundan BAŞLA;
  hatırladığını yazma.** 2026-09-09'da PN4 sondası sekiz tur döndü ve
  altısı ÖLÇEMEDİ ile bitti: giriş adımını, eşik değerini, panelin
  iplik davranışını ve tam sayfanın liste davranışını dördünü de kodda
  okuyarak değil DÜŞEREK öğrendim. Dördü de yanı başımdaki çalışan
  sondada ya da kaynakta yazılıydı
  → `frontend/enderun-ai/tests/duzen/mesaj-paneli-acilir.spec.ts`
    (`girisYap`, `tercihleriKur`, `paneliAc` yorumları)

- **Bir TARAMA, ölçemediği noktada durup ötekileri kaybetmemeli.**
  Nokta başına sonuç ver: ölçülen sayıyla, ölçülemeyen sebebiyle.
  Aynı koşuda hem bulgu hem ÖLÇEMEDİ olabilir
  → `frontend/enderun-ai/tests/duzen/mesaj-paneli-tasma.spec.ts`

- **Değiştirdiğini sandığın değişkeni ÖLÇ.** "2 mesajla koştum" dedim,
  konuşmada 41 mesaj vardı ve yanlış sonuç raporladım. Sayımı sondaya
  ekleyince çıktı
  → aynı dosya (`konuşmada ÖLÇÜLEN N mesaj` başlığı)

## Kabuk ve süreç

- **`pkill -f <desen>` kendi kabuğunu da öldürür**; `surec-durdur.sh`
  kullan (port / PID dosyası / desen + kendini dışlama)
  → `deploy/scripts/surec-durdur.sh:7`

- **`psql` SQL hatasında bile 0 döner**; tohumlama betiklerinde
  `-v ON_ERROR_STOP=1` şart, yoksa kapı sessizce açık kalır
  → `deploy/scripts/duzen-testi.sh:224`

- **`grep -c .` boş dosyada "0" basar ama çıkış kodu 1 döner**;
  `|| echo 0` ile birleşince "0\n0" üretir. Sayım için `wc -l` kullan
  → `deploy/scripts/safe-deploy.sh` (`kesinti_izleyicisi_bitir`)

- **`timeout` sarmalayıcıyı öldürür, süreç ağacını değil**; ağır
  derlemeleri `derleme-kos.sh` üzerinden koştur (kendi cgroup'u var)
  → `scripts/derleme-kos.sh`

## Derleme ve ölçüm

- **`dotnet ef ... --no-build` bayat ikili okur**; ölçümden önce her
  zaman derle, yoksa kaynağı değil eski çıktıyı ölçersin
  → `deploy/scripts/sema-sapma-kapisi.sh:75`

- **`dotnet ef` derleme hatasını göstermez** ("Build failed" der);
  gerçek hata metni için ayrı bir `dotnet build` koş
  → bu dosya (2026-09-08, KATALOG/1 göçü iki denemede kayboldu)

- **Veritabanı adını asla tahmin etme**; ölçüm `vt-sorgu.sh` üzerinden
  geçer (ad zorunlu, bakım veritabanları reddedilir, her çıktının
  başında `current_database()`)
  → `deploy/scripts/vt-sorgu.sh`

- **Kendi çıktını `tail`/`head` ile kırpıp sonuç çıkarma**;
  `vt-sorgu.sh` 50 satırdan fazlasını basmaz, tam listeyi dosyaya yazar
  → `deploy/scripts/vt-sorgu.sh` (satır sınırı gerekçesi)

- **Prova zemini şema sadık olabilir ama veri sadık olmayabilir**;
  gerçek kimlik gerektiren iddiaları `prova-zemini.sh --veri` ile kur
  → `deploy/scripts/prova-zemini.sh`

## Kapılar

- **BİR KAPI, EN AZ BİR KEZ KIRMIZI YANMADAN VAR SAYILMAZ.**
  Kapının VARLIĞI, İŞLERLİĞİ değildir. *(Kural Mehmet Bey'e ait,
  9 Eylül; ikimizin adına duruyor.)*

  O gün üç kez düşüldü:
  · ben "kapı yok" dedim — VARDI (`require_clean_git_tree`,
    fail-closed; kirli ağaçla yayın 0. saniyede durdu)
  · Mehmet Bey "kapı var" dedi — ÖLÜYDÜ (`duzen-testi.sh`'in üçüncü
    sonuç yolu: `set -euo pipefail` altında çıplak çağrı düşünce betik
    sonlanıyor, `publish_kodu=$?` satırına HİÇ GELİNMİYORDU)
  · ikimiz de KODU OKUYARAK karar verdik

  Ölü kapı ancak koşturarak görüldü: çıkış 75, ÖLÇEMEDİ satırlarının
  hiçbiri basılmadı. Düzeltmeden sonra aynı koşullarda çıkış 3 ve üç
  satır da bastı. Tek fark `|| publish_kodu=$?`.

  KIRMIZISI KURULAMAYAN KAPI, "KAPI" DEĞİL "OKUMA"DIR — öyle
  etiketlenir (bkz. kaba-anahtar iddiası: kırmızı koşulu `UcKapisi`
  ayaktayken kurulamıyor).

- **KABUK DESENİ ŞÜPHESİ:** `set -e` / `pipefail` altında çıplak bir
  çağrı, ardından `$?` yakalayan her yer aynı ölü kapı şeklini
  taşıyabilir. Grep bunları ŞÜPHELİ olarak bulur; kanıt değildir.
  Her biri ateşlenerek sınanır (ATEŞLEME/1).

- **"Kapı var" ile "kapı ölçebiliyor" ayrı şeyler.** Her kapı için
  ayrıca sor: ölçtüğü şey **taze** mi, ve **ölçemediğinde** bunu
  söylüyor mu (Kural 67'nin üçüncü sonucu)
  → `deploy/scripts/safe-deploy.sh` (`eski_parca_kapisi` öz-sınaması)
