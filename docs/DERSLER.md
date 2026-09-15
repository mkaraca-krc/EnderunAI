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

- **KURAL 85 — PROVA ZEMİNİ CANLI SIRLARLA DEĞİL, KENDİ ÜRETTİĞİ
  GERÇEK OLMAYAN DEĞERLERLE KURULUR.** Canlı sırrı okuması gereken bir
  rig, rig'in değil YAPILANDIRMANIN kusurudur.

  Mehmet Bey'in kuralı (2026-09-13, OTURUM/1 (b)). Otomatik kip, süreç
  ortamını dosyaya kopyalamayı ve `backend.env`'den bağlantı türetmeyi
  reddetti. **Engel doğrudur, aşılmaz.** Doğru yol sırsız zemindir:

    · veritabanı → Postgres YEREL SOKETİ + `peer` kimlik doğrulaması
      `Host=/var/run/postgresql;Database=<prova>;Username=postgres`
      (süreç `sudo -u postgres` ile koşar, hiçbir yerde parola yok)
    · JWT        → SABİT, gerçek olmayan prova dizgesi

  SABİT olması şart, rastgele değil: `duzen-testi.sh:254` her başlatmada
  rastgele `JWT_SECRET` üretiyor; canlıda sır `backend.env`de sabittir ve
  yeniden başlatmayı sağ geçer. Rastgele sırlı bir zeminde yeniden
  başlatma ölçümü YALANCI 401 verir (bkz. Kural 81).

  Böyle kurulamayan bir zemin çıkarsa DOLANILMAZ, Mehmet Bey'e gelinir.

- **LİSTEYE ÖLÇÜLMEMİŞ İŞ KOYMAK, ölçülmemiş hüküm yazmanın listedeki
  hâlidir.** (Mehmet Bey'in kendi adına yazdırdığı ders, 2026-09-13.)

  OTURUM/1 (a) "yapılacak iş" diye yazılmıştı; ölçülünce ZATEN CANLIDA
  çıktı. Bir kapının kırmızı yanmadan var sayılmaması gibi, bir işin de
  ölçülmeden "yapılacak" sayılmaması gerekir.

  UYGULAMA: pakete iş eklerken o işin HÂLÂ yapılmamış olduğunun ölçümü
  yazılır; yoksa madde "ölçülmedi" diye işaretlenir.

- **`.next/static` ARTIK BİRİKTİRİR: "dosyada var" CANLIDA VAR DEMEK
  DEĞİLDİR.** Eski yapıların parçaları silinmeden kalıyor; bir kalıbı
  pakette görmek onun yayında olduğunu KANITLAMAZ.

  OTURUM/1 (a): `"mesai-disi"` karşılaştırmasını içeren paketler
  2026-09-11 12:13 (canlı yapı), eski `!isAllowed` kalıbını taşıyanlar
  2026-09-10 04:11 (bayat artık). Kanıt kod okuması değil DOSYA
  TARİHLERİ oldu.

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

- **KURAL 84 — OKUMA ALETİ, ÖLÇTÜĞÜN AYRIMI KORUMAK ZORUNDADIR.**
  `Passed!` ile `Failed!`'ı aynı dizgeye indiren bir süzgeç, ölçümü
  değil ÖLÇÜMÜN CEVABINI siler.

  Mehmet Bey'in koyduğu genel hâl. Doğuşu: `sed 's/.*EnderunAI\.Api\.Tests\.//'`
  ile test özetini okudum; süzgeç `Passed!  - ... - EnderunAI.Api.Tests.dll`
  satırını `dll (net8.0)`a indirdi — ve aynı süzgeç `Failed!` satırını da
  AYNI dizgeye indirirdi. Ayırt etmem gereken iki hâl tek hâle çökmüştü.
  Önceki hâli (2026-09-13, STOK/1): "bir süzgeç, kapsamadığı şeyi 'yok'
  diye gösterir" — kapsam sorunu. Genel hâli daha geniş: süzgeç kapsasa
  bile AYRIMI yok edebilir.

  UYGULAMA: bir ölçümün cevabını taşıyan alanı kırpan hiçbir süzgeç
  kullanılmaz. Önce ham çıktı dosyaya alınır, süzgeç ondan sonra gelir.

- **ALT MADDE (Kural 84) — BİR DOĞRULAMAYI KOŞMADAN ÖNCE O KOŞUNUN
  ORTAM GEREKSİNİMLERİNİ KURUN.** 17 testin 38 ms'de düşmesi bulgu
  değil, DÜZENEK ARIZASIDIR.

  `dotnet test`i `TEST_DB_CONNECTION`/`JWT_SECRET` kurmadan çağırdım;
  17 test 38 ms'de kırmızı yandı. Bir an gerçek bir bulgu sandım.
  Süre tek başına ele verir: 17 test 38 ms'de KOŞAMAZ.

  UYGULAMA: kırmızıyı bulguya çevirmeden önce süreye ve ilk
  başarısızlığın gerekçesine bak. Gerekçesiz toplu kırmızı = düzenek.

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

- **KURAL 91 — BİR KAPI, KULLANICININ GÖRDÜĞÜ KATMANI ÖLÇMÜYORSA,
  ÖLÇTÜĞÜ KATMAN TEMİZKEN DE YALAN SÖYLER.** Her kapı için tek satır:
  **hangi katmanı görür.**

  Mehmet Bey'in kuralı (2026-09-15). Doğuşu: yayın kesinti kapısı
  *"yayın boyunca ön yüz parçası ve arka uç sağlığı hatasız"* dedi ve
  **doğru söyledi** — ölçtüğü iki yüzey gerçekten temizdi. Aynı
  pencerede **nginx 8 adet 502** kaydetti (`auth/me`, `companies`,
  `user-preferences`, `masraf-merkezleri`, `work-hours-status`) ve
  oturumdaki kullanıcı onları gördü.

  **Sorun eşikte değil KATMANDAYDI.** Cümle "hiç hata olmadı" diye
  okunuyordu; ölçtüğü o değildi.

  UYGULAMA: kapı vekil (nginx) katmanına bağlandı ve cümlesi ölçtüğü
  katmanı **adıyla** söylüyor:
  *"GEÇTİ · UYGULAMA KATMANI … · VEKİL KATMANI (nginx): N adet 502
  (takas penceresi, beklenen), 502 dışı 5xx: M."*
  Ölçemezse **ÖLÇEMEDİ** der — "kullanıcının gördüğü katman hakkında
  hüküm YOK".

  AYRIM: **502** takas penceresidir, beklenen ve ölçülmüş bedeldir
  (6–14 sn); sayılır, kırmızı yakmaz. **502 dışı 5xx** uygulama
  hatasıdır; kapı ona karşı serttir.

- **KURAL 90 — SÜZGEÇLİ TEST KOŞUSU, KOŞMADIĞIN TESTLER HAKKINDA
  HİÇBİR ŞEY SÖYLEMEZ.** Kural 84'ün test hâli.

  Doğuşu (2026-09-14): `PsqlCizgisiTests` **iki gündür kırmızıydı**
  (paket C'nin kurtarma betiği 5 doğrudan `psql` çağrısı ekledi, çizgi
  0'dı). Görmedim, çünkü paket C'den beri her koşum `--filter` ile
  yapıldı. Tam takımı ilk kez yayın hazırlığında koşturdum.

  `safe-deploy` tam takımı süzgeçsiz koşuyor — yani bu kırmızı **yayın
  sabahı 04:30'da**, yayın penceresinin içinde görünecekti. İyi bir
  kapı, ama pahalı bir saat.

  UYGULAMA: **gece tam takım** kuruldu (`scripts/gece-tam-takim.sh` +
  `enderun-tam-takim.timer`, her gece 01:00 UTC). Damga dosyası sonucu
  **ve yaşını** yazar; koşu olmadıysa `OLCEMEDI` yazar —
  **sessizlik yeşil değildir.** Derleme kilidi meşgulse (çıkış 75) de
  `OLCEMEDI`: "yeşil" demek en tehlikeli yanlış olurdu.

  *Yayın sabahı öğrenilen her şey, bir gece önce öğrenilebilirdi.*

- **KURAL 89 — İLK GÜN GÜRÜLTÜYLE KIRMIZI YANAN KAPI, YARIN KİMSENİN
  BAKMADIĞI KAPIDIR.** Bir kapının eşiği, kontrol ETTİĞİMİZ şeye
  çizilir; kontrol etmediğimiz gürültüye değil.

  Mehmet Bey'in tasarım ilkesi (2026-09-13). Doğuşu: sorgu dizgesi
  çırasının ilk sürümü, adı sır çağrıştıran her parametreyi koşulsuz
  kırmızı yakıyordu ve **ilk koşuda kırmızı yandı** — `passwd`.
  Ölçüldü: `/cgi-bin/nas_sharing.cgi` yoluna gelen bir saldırı
  yoklamasıydı ve **404** almıştı. Saldırganın ne gönderdiğini biz
  belirlemiyoruz.

  Kural ölçüme dayandırıldı: sırlı ad **2xx ile SUNULDUYSA** kırmızı;
  değilse adıyla BİLGİ kovasında sayılır. Aynı ilkenin ikinci
  uygulaması: etiketsiz hüküm çırası artışta **kırmızı yakmaz**, bilgi
  verir — yeni kod yazmayı cezalandıran bir kapı ilk haftada devre dışı
  bırakılır.

- **KURAL 87 (ADAY — Mehmet Bey'in metni) — DOĞRULARKEN ÖLÇTÜĞÜN
  KÜMENİN İSTENEN KÜMEYLE AYNI OLDUĞUNU ÖNCE GÖSTER.** *"Hepsi
  yapıldı" cümlesi, maddelerin tek tek adıyla eşleştirilmeden
  kurulamaz.*

  Doğuşu (2026-09-13): bir turda "bu mesaj bir öncekinin aynısı, hepsi
  yapıldı" yazdım. Ölçtüğüm şeyler gerçekten oradaydı; ama küme
  düzeyinde baktım, madde madde eşleştirmedim — ve bilerek karara
  bıraktığım bir maddeyi ("saklama süresi") "hepsi" cümlesi örttü.
  Yanlış kümeye bakan bir doğrulama, eksiği "tamam" diye gösterir
  (Kural 84'ün doğrulama tarafı).

  UYGULAMA: tamamlandı raporu, istenen maddelerin ADIYLA yazılır; her
  adın karşısında ya sonuç ya "yapılmadı, sebebi şu" durur. "Hepsi",
  "tamamı", "eksik yok" sözcükleri tek başına rapor değildir.

- **KURAL 88 — ETİKETSİZ HÜKÜM YAZILMAZ.** Her hüküm satırının yanında
  ya ÖLÇÜM YÖNTEMİ ya `[ÖLÇÜLMEDİ]` etiketi bulunur. **Bu kural kod
  yorumlarına da uygulanır.**

  Gerekçesi KAYIT/1: bu hafta üç yanlış kayıt TESADÜFEN yakalandı —
  K10 ("istek atmıyor"), GÜNLÜK/1 ("sorgu dizgeleri yazılmıyor"),
  S1 (kod yorumu "zaten `isActive` ile ayırıyor"). Üçü de hükümdü,
  üçünün de yanında ölçüm yoktu.

  **BİR YORUM DA BİR KAYITTIR VE YANLIŞ OLABİLİR.** S1'de ölçülen
  cümle şuydu: ***yorum, yapıldığı SANILAN bir şeyi anlatıyordu.***
  Kod okuyan herkes o yoruma güvenip tabloyu kontrol etmedi.

  Araç: `deploy/scripts/kayit-taramasi.py` — hüküm adaylarını
  ÖLÇÜLMÜŞ / İDDİA diye ayırır, kapsamı ve pozitif kontrolü basar.
  Hüküm AVLAR, hüküm KANITLAMAZ.

- **KURAL 86 — BİR ALETİN YANLIŞ KIRMIZISI, YANLIŞ YEŞİLİ KADAR
  TEHLİKELİDİR.** Felaket anında sağlam yedeği reddettirir. Kurtarma
  yolundaki her alet, doğru yeşil kadar doğru kırmızı da vermek
  zorundadır.

  Mehmet Bey'in kuralı (2026-09-13, paket C). Doğuşu: kurtarma betiğinin
  ön denetimi `gpg --decrypt | head -c 5 | grep -q '^PGDMP'` yazıyordu.
  `head` boruyu kapatınca `gpg` SIGPIPE ile ölüyor ve `pipefail` açıkken
  boru, `grep` EŞLEŞSE BİLE başarısız sayılıyor. Sağlam bir yedek
  "AÇILAMADI" diye reddedildi.

  UYGULAMA: bir kapının yalnız kırmızı yanabildiğini göstermek yetmez;
  DOĞRU durumda YEŞİL yandığı da ayrıca gösterilir. Kurtarma, yedek ve
  yayın yolundaki her denetimde iki yön de sınanır.

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

- **KODDA DURAN MEKANİZMA, KURULABİLEN MEKANİZMA DEĞİLDİR.**
  "Bu yol kodda var" ile "bu yol çalışıyor" ayrı cümlelerdir; ikincisi
  ancak zincir uçtan uca ÇAĞRILARAK söylenir. Bir halkayı okumak,
  önündeki halkaların ona izin verdiğini göstermez. *(Benim adıma,
  10 Eylül.)*

  MESAİ/1'de "okunamayan satır → 200 `isAllowed:false` → kalıcı çıkış"
  zinciri kodda okundu ve "doğrulandı" diye bildirildi. Çağrılınca
  izleyici ucu 401 HesapPasif döndü: izin ara katmanı eksik kullanıcıyı
  controller'a ulaşmadan reddediyordu. Zincir yoktu. Ölçüm ise iki
  BAŞKA kusur buldu (yanlış "mesainiz bitti" etiketi; HTML-200'de çerez
  silme) — tahmin edilen yerde değil, çağrılan yerde.

  Aynı gece aynı hata ters yönde de yapılmıştı ("kapı yok" / "kapı
  var"). Yönü önemli değil: OKUMAYLA VERİLEN HER HÜKÜM ŞÜPHELİDİR,
  "var" da "yok" da.

- **ÖLÇÜM ARACININ KENDİ UYARISINI SÜZGEÇLE SİLME.** Bir aracın "bu
  çıktı eksik" diyen satırı, çıktının EN önemli satırıdır. *(Benim
  adıma, 10 Eylül.)*

  JETON/1'de `vt-sorgu.sh` sonucu 50 satırda kesti ve başlığında
  "⚠ BU ÇIKTIDAN SONUÇ ÇIKARMA — eksik satırlar sonucu değiştirir"
  yazdı. Ben çıktıyı temiz görmek için `grep -v "^\[vt"` ile başlığı
  atıyordum; karşılaştırma "34 izin kaydı kaldırılmış" dedi — oysa
  hiçbiri kaldırılmamıştı. Bir kullanıcının oturumu bu yanlış farka
  dayanılarak düşürülebilirdi. `count(*)` ile iki tarafın 84 olduğu
  görülünce yakalandı.

  UYGULAMA: bir aracın çıktısını süzüyorsan, önce süzülen satırlara
  bak; karşılaştırmadan önce iki tarafın sayısını bağımsız bir sayımla
  eşitle (tarama sağlığı sayacı).

- **BİR BİRİM ÖLÇÜMÜ, O BİRİMİN GERÇEKTEN SÜREÇ SAHİBİ OLDUĞU
  DOĞRULANMADAN HİÇBİR ŞEYİ KANITLAMAZ.** *(Benim adıma, 13 Eylül.)*

  BELLEK/1'de "PostgreSQL'in OOM önceliği 0, en zayıf nokta veritabanı"
  diye kayda geçtim. Ölçtüğüm birim `postgresql.service`'ti — o bir
  **meta birim**, hiçbir süreci yok, yalnız gerçek küme birimini
  tetikliyor. Meta birim her alanı boş/varsayılan gösterir ve bu
  "koruma yok" gibi okunur. Gerçek birim `postgresql@16-main.service`
  ve `OOMScoreAdjust=-900`; postmaster'ın `oom_score_adj` değeri −900,
  çocuk süreçlerinki 0 — yani Postgres'in kendi tavsiye ettiği asimetri
  dağıtım tarafından zaten kurulmuş. Yanlış kayda dayanarak canlı
  veritabanına "koruma ekleme" işi yapılacaktı; gereksiz bir yeniden
  başlatma riskiydi.

  UYGULAMA: bir systemd biriminin bir alanını okumadan önce
  `systemctl show -p MainPID <birim>` ile birimin bir süreci olduğunu
  göster; sonra değeri `/proc/<pid>/` altından, yani çekirdeğin gördüğü
  yerden OKU. Birim dosyası niyeti, `/proc` gerçeği söyler.

- **SIKIŞTIRILMIŞ BİR SÜREÇTEN "İHTİYACI NE KADAR" SORUSU
  CEVAPLANMAZ — DAİRESELDİR.** *(Mehmet Bey'in düzeltmesi, 13 Eylül.)*

  BELLEK/1'de canlı API'nin yerleşiği 20 MB ölçüldü ve "demek ki 640
  MB'lık ayırma fazla" diye yazdım. Yanlış: süreç 20 MB tuttuğu için
  değil, derleme onu takasa ittiği için 20 MB'taydı (455 MB'ı takasta).
  Soğuk hâlden çalışan küme boyutu türetmek, sonucu sebep sanmaktır.

  UYGULAMA: bir sürecin bellek ihtiyacını ölçmeden önce ONU ISIT;
  yerleşik kümeyi ısınmış hâlde ölç; ayırmayı (`memory.min`) o sayıdan
  türet. Sırayı bozarsan ölçtüğün şey ihtiyaç değil, baskının izidir.

- **GÖRMEDİĞİM DURUMU, O ŞEYİN YOKLUĞU SANDIM.** Bir şeyin YOKLUĞUNU
  iddia etmeden önce, onu GÖSTERECEK KOŞULUN kurulduğunu göstermeliyim.
  *(Mehmet Bey'in kendi düzeltmesi, 13 Eylül — bu ailenin üçüncü
  örneği.)*

  STOK/1'de "sol menüde DEPO VE STOK altında tek bağlantı var" diye
  ölçüldü ve `/depo-stok/depolar` ile `/depo-stok/sayim` "menüde yok"
  sayıldı. Ölçüm menü grubu KAPALIYKEN alınmıştı; kenar çubuğu bir
  akordeon ve kapalı grup yalnız başlığını gösteriyor. Sonra "açayım"
  diye tıklanan şey grubu kapatmıştı. Grup gerçekten açıkken 14
  bağlantı var, ikisi de içinde.

  Üç bağımsız ölçüm aksini söylüyordu — depo HEAD'i, canlıda yayında
  olan commit, ve canlıda SERVİS EDİLEN `.next` yapısı — ve ikisi
  `route-permissions` listesinde hiç geçmediği için izne de bağlı
  değildi. Yani "yok" hükmü, aracın değil GÖZLEM KOŞULUNUN eksikliğiydi.

  UYGULAMA: "X yok" demeden önce bir POZİTİF KONTROL kur — aynı koşulda
  VAR OLDUĞUNU BİLDİĞİN bir kardeşi göster. Menüde bir öğe arıyorsan,
  o grupta gördüğün başka bir öğeyi de say; sıfır çıkıyorsa aradığın
  şey yok değil, GÖREMİYORSUNDUR.

  AYNI AİLENİN ÖNCEKİ İKİSİ: (1) K5 kapısı yalnız 390 px ölçüyordu ve
  1201–1440 arasındaki taşmayı "yok" diye raporluyordu — kusur kapının
  BAKMADIĞI genişlikteydi. (2) `postgresql.service` meta birimi
  okunup "OOM koruması yok" denmişti — koruma vardı, ölçülen birimin
  süreci yoktu. Üçünde de ölçüm aleti çalışıyordu; ÖLÇÜM KOŞULU
  kurulmamıştı.

- **ÜÇÜNCÜ KEZ TEKRARLAYAN BİR İNSAN/ARAÇ HATASI, KURALLA DEĞİL ARAÇLA
  KAPATILIR.** *(Mehmet Bey'in kuralı, 13 Eylül.)*

  Desenli süreç öldürme (`pkill` + tam-komut-satırı bayrağı) bu depoda
  BEŞ kez çağıran kabuğun kendisini öldürdü — desen, çağıran komutun
  kendi satırında da geçiyor. Dördüncüden sonra doğru şey yapılmıştı:
  `deploy/scripts/surec-durdur.sh` yazıldı (kendini ve atasını dışlar)
  ve `PkillYasagiTests` muhafızı kondu.

  **Beşincisi yine de oldu (13 Eylül) ve sebebi öğretici: hata
  tekrarlamadı, ARAÇ BİR KANALI KAPSAMIYORDU.** Muhafız yalnız depodaki
  `.sh` dosyalarını tarıyor; etkileşimli kabuk çağrılarını göremez. Araç
  vardı, muhafız yeşildi, kanal açıktı.

  UYGULAMA: bir tekrar daha olduğunda önce "yasak neden tutmadı" diye
  değil, **"aracın kapsamadığı kanal hangisi"** diye sorun. Buradaki
  cevap bir PreToolUse kancası oldu
  (`deploy/scripts/pkill-kancasi.sh`): komut konumundaki desenli
  öldürme çağrısını reddediyor, aracı adıyla söylüyor, ve deseni
  BAHSEDEN komutları (belge yazmak gibi) engellemiyor — kendi belgesini
  yazmayı yasaklayan bir kapı kullanılamaz hâle gelir. 12 varyantla
  sınandı; kanca ateşlendiği ölçülerek doğrulandı.

- **BİR MUHAFIZIN VARLIĞI DEĞİL KAPSAMI ÖLÇÜLÜR.** Yeşil bir muhafız,
  KAPSAMADIĞI kanalı da yeşil gösterir. Her muhafız için tek satır:
  **hangi kanalları görür, hangilerini görmez.** *(Mehmet Bey'in
  kuralı, 13 Eylül.)*

  `PkillYasagiTests` yeşildi ve gerçekten çalışıyordu — ama yalnız
  depodaki `.sh` dosyalarını tarıyor. Etkileşimli kabuk çağrıları onun
  kör noktasıydı ve beşinci olay tam oradan geçti. "Araç vardı, muhafız
  yeşildi, kanal açıktı."

  Bu depodaki muhafızların kapsamı (13 Eylül itibarıyla):

  | muhafız | GÖRÜR | GÖRMEZ |
  |---|---|---|
  | `PkillYasagiTests` | `deploy/` + `scripts/` altındaki `.sh` | etkileşimli kabuk, Python/JS betikleri |
  | `pkill-kancasi.sh` (PreToolUse) | bu oturumun Bash çağrıları | başka oturumlar, doğrudan terminal |
  | `stok-hareket-etiketi` yapısal muhafazası | `app/components/services/hooks` içindeki `.ts(x)` | `lib/`, arka uç, e-posta/PDF şablonları |
  | `malzeme-tipi` yapısal muhafazası | aynı dört dizin | aynı boşluklar |
  | K5 düzen kapısı | 390 / 768 / 1280 / 1536 px | aradaki genişlikler (1366, 1440) |

  UYGULAMA: yeni bir muhafız yazarken yorumuna GÖRMEDİĞİ kanalı da yaz.
  Kapsam yazılmazsa, muhafızın yeşili "kusur yok" diye okunur — oysa
  yalnız "baktığım yerde kusur yok" demektir (Kural 82'nin muhafız hâli).

- **BİR MUHAFIZIN YANLIŞ ALARMI, ONUN GERÇEKTEN OKUDUĞUNUN KANITIDIR.**
  *(2026-09-13.)* Malzeme tipi muhafızı ilk koşusunda bir `<h3>Demirbaş</h3>`
  BAŞLIĞINI ihlal saydı. Desen daraltıldı — ama o yanlış alarm olmasaydı
  muhafızın dosyaları gerçekten açtığını değil, yalnız yeşil yandığını
  görmüş olurduk. Sessiz yeşil, gürültülü kırmızıdan daha az bilgi taşır.

- **KENDİYLE ÇELİŞEN BELGE, YANLIŞ BELGEDEN KÖTÜDÜR.** Yanlış belge bir
  kez yanıltır; çelişen belge HER OKUYUCUYU FARKLI yanıltır ve kimse
  çeliştiğini fark etmez. *(Mehmet Bey'in kuralı, 13 Eylül.)*

  `docs/MEHMET-BEY-150-153-TALIMAT.md`in şemasına "BU KUTUYU AÇIN"
  yazdım; aynı belgenin 4. adımı "işaretini kaldırın" diyordu. İkisi
  ters. Hata ölçümde değildi — talimatı ekranı OKUYARAK yazmıştım ve
  ölçüm doğruydu. Hata GÖRSEL YARDIMCIDAYDI: şemanın yanına ölçülen
  DURUMU değil NİYETİ yazdım, ve "kutuyu aç" Türkçede "işaretle" diye
  okunuyor.

  UYGULAMA: bir talimatta aynı eylem iki kez anlatılıyorsa (metin +
  şema, metin + örnek), ikisi de ÖLÇÜLEN durumdan yazılsın. Şema
  "şu an böyle / iş bitince böyle" diye İKİ hâli göstersin; tek hâl
  gösteren şema, okuyucunun hangisi olduğunu tahmin etmesini ister.

- **`pg_stat_*` GÖRÜNÜMLERİ LİSTE İÇİN GÜVENLİ, SAYI İÇİN DEĞİL.**
  *(Mehmet Bey'in kuralı, 13 Eylül.)*

  `pg_stat_user_tables.n_tup_ins` `security_audit_events` için **116**
  dedi; tabloda **2107** satır vardı. Sayaçlar geri yükleme, `pg_restore`
  ve `COPY` sonrası eksik kalıyor ve `pg_stat_database.stats_reset`in
  "HİÇ" olması onları güvenilir YAPMIYOR.

  Aynı gün bu alete dayanarak "audit_logs hiç yazılmadı, sayaç
  güvenilir" diye hüküm vermiştim. Hüküm doğruydu ama DAYANAĞI
  çürüktü; koda yeniden dayandırıldı (varlık sınıfı yok, `DbSet` yok,
  0 eşleşme, snapshot tanımıyor, `count(*)=0`).

  UYGULAMA: tablo ADLARINI `pg_stat_*`ten almak güvenli — yedekleme ve
  geri yükleme tatbikatı tam bunu yapıyor ve sayıyı `count(*)` ile
  ayrıca hesaplıyor. SAYI lazımsa `count(*)` yazın.

  VE GENEL KURAL: **bozuk çıkan bir alet, o aletle ölçülmüş her şeyi
  şüpheli yapar.** Alet bozulunca kayıtların tamamı taranır; bulaşan
  hüküm ya yeniden dayandırılır ya "dayanaksız" diye işaretlenir.
