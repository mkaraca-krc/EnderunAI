# AJAN/1 — SALT OKUYAN CANLI ÖLÇÜM KİMLİĞİ (teklif)

**Durum:** teklif. Hiçbir şey kurulmadı, hiçbir kullanıcı açılmadı.
**Sınır (Mehmet Bey'in kararı):** **SALT OKUMA. Canlıda yazma YOK —
hiçbir kapsamda, "asgari izinle" bile değil.**

## Neden salt okuma — kararın gerekçesi kayda geçiyor

1. **Hesap verebilirlik.** END0003'ün ve 9 kartın düzeltmesini Mehmet
   Bey'e bırakmamızın sebebi, denetim izinde **gerçek bir insanın**
   kimliğinin durmasıydı. Yazabilen bir ajan, bütün bu düzeltmeleri
   "AJAN" imzalı yapar ve hesap verebilirlik **tam da inşa ettiğimiz
   yerde** kaybolur.
2. **Sürekli duran anahtar, sürekli duran risktir.** Bu hafta İKİ KEZ
   "geçici" bir ayarın kalıcılaştığını gördük (rig ayarı: 150/153
   `RequiresProject` kapatması; arşiv UPDATE'i: 9 kartın pasife
   alınması).
3. **Yazma yetkisi olan ajan er geç "şunu hızlıca düzelteyim" der.**
   O an, ölçmeden düzeltmenin başladığı andır.

**Tek seferlik canlı yazma gerekirse:** ayrı, adı konmuş, süreli ve
günlüğe düşen bir işlem olarak Mehmet Bey'e gelir — **duran bir hesap
olarak değil.**

---

## 1. İZİNLER — ALTI TANE, HER BİRİ BİR ÖLÇÜMDEN TÜRETİLDİ

Katalogda 147 izin var, 47'si okuma. Teklif **6** tanesini istiyor.
Her satırın gerekçesi, bu hafta **fiilen yapmak zorunda kaldığım** bir
ölçüm:

| # | izin | ne için — bu hafta hangi ölçüm |
|---|---|---|
| 1 | `AccountingView` | 150/153 bayrak durumu; fiş satırlarının hangi hesaba/tutara düştüğü (E4, beyan sapma sayacı) |
| 2 | `InventoryView` | stok kartları, depo mevcudu, stok hareketleri, ortalama maliyet (STOK/1 uçtan uca) |
| 3 | `PurchasingReceiptsView` | mal kabul belgesinin durumu ve fiş bağı (E4 doğrulaması) |
| 4 | `FinanceView` | çek kayıtları — DENETIM/2'nin "paranın yaşadığı yer" ayağı |
| 5 | `CurrentAccountsView` | cari kartları; 320/120 hesaplarının kullanıldığı yollar |
| 6 | `AuditLogView` | denetim olaylarının gerçekten düştüğünü canlıda görmek (DENETIM/2 doğrulaması) |

### HASSASİYET SIRALAMASI — altı izin eşit değil

Daraltma gerekirse **tartışma değil, kayıt konuşsun** diye sıra
önceden yazılıyor:

| sıra | izin | neden bu sırada |
|---|---|---|
| **1 — EN HASSAS** | `audit-log.view` | **Tüm şirkette kimin ne yaptığını okur.** Tek bir izinle bütün kullanıcıların eylem geçmişi görünür. |
| 2 | `finance.view` · `current-accounts.view` | Ticari veri: çek kayıtları, cari bakiyeler. |
| 3 | `accounting.view` · `inventory.view` · `purchasing-receipts.view` | Yapılandırma ve operasyon verisi; kişi ya da bakiye taşımıyor. |

> **ÇİZGİ DARALIRSA İLK DÜŞECEK OLAN `audit-log.view`'DİR.**
> Onsuz DENETIM/2'nin canlı doğrulaması yapılamaz; o doğrulama
> Mehmet Bey'in ekrandan bakmasıyla da yapılabilir. Diğer beşi
> ölçümün kendisidir, bu biri ölçümün kolaylığıdır.

**AÇIKÇA İSTENMEYENLER** — teklifin bir parçası olarak yazılıyor:
`SalaryView`, `PayrollView`, `AttendancePayrollView`,
`PersonnelDocumentView`, `PersonnelView`, `MesajlarView`,
`EmployerPortalView`. Ücret, özlük, mesaj ve portal verisi ölçüm için
gerekmiyor; gerekmeyen erişim istenmiyor.

**"Tümü" yok.** `hasAllPermissions` verilmeyecek — o bayrak
`canAccessRoute`ta bütün kapıları açıyor.

---

## 2. YAZMANIN REDDEDİLDİĞİNİ GÖSTEREN ÖLÇÜM

Kimlik kurulduğunda **ilk iş** bu sonda. Pozitif kontrolsüz kabul
edilmez: "yazamadı" sonucu, kimliğin kısıtlı olduğunun değil **ucun
bozuk olduğunun** da kanıtı olabilir.

| ayak | çağrı | beklenen |
|---|---|---|
| okuma ÇALIŞIYOR (pozitif kontrol) | `GET /api/accounting-accounts?pageSize=5` | **200** + gövde dolu |
| okuma ÇALIŞIYOR | `GET /api/inventory/items` | **200** |
| yazma REDDEDİLİYOR | `PUT /api/accounting-accounts/{150}` | **403** |
| yazma REDDEDİLİYOR | `POST /api/inventory/items` | **403** |
| yazma REDDEDİLİYOR | `POST /api/inventory/adjustments` | **403** |
| kapsam dışı okuma REDDEDİLİYOR | `GET /api/hr/payroll…` | **403** |

Son satır önemli: yalnız "yazamıyor" değil, **istemediğimiz veriyi de
okuyamıyor.** Altı ayak da geçmeden kimlik kullanıma alınmaz.

---

## 3. KİMLİK BİLGİSİ NEREDE DURACAK

- **Parolayı Mehmet Bey üretir.** Ajan sır üretmez (yerleşik kural).
- Parola `/etc/enderunai/ajan.env` dosyasında durur: **sahip root,
  izin 0600**, tıpkı `backend.env` gibi.
- Betikler onu `--parola "$(cat /etc/enderunai/ajan.env)"` gibi
  **argümanla** alır; `hesap-bayragi.sh`ta kurulan desen.
- **YAZILMAYACAĞI yerler, tek tek:** sohbet, `DURUM.md`, `CANLI-1.md`,
  commit mesajı, günlük, betik içi sabit, ortam değişkeni dökümü.
  Betikler parolayı **asla ekrana basmaz** — bugünkü `hesap-bayragi.sh`
  yalnız hesap kodunu ve önce/sonra değerini basıyor.
- Parola sohbete düşerse **yanmış sayılır**: Mehmet Bey değiştirir.

---

## 4. AJANIN KENDİ DENETİM İZİ

**Dürüst sınır:** okuma işlemleri denetim kaydı ÜRETMEZ.
`security_audit_events` yalnız `Created`/`Updated`/`Deleted` yazıyor —
ve salt okuyan bir kimlik bunların hiçbirini üretmez. Yani DENETIM/2
genişlemesi bu kimliği KAPSAMAZ.

İz bu yüzden **erişim günlüğünden** gelir:

- Kimlik bütün isteklerine ayırt edici bir başlık koyar:
  `User-Agent: EnderunAjan/1 (salt-okuma)`.
- Böylece `nginx` erişim günlüğünde tek `grep` ile **hangi uçları,
  kaç kez, hangi durum koduyla** çağırdığı çıkar.
- **BİLİNEN BOŞLUK:** nginx günlüğü bugün yalnız **15 gün** tutuluyor
  (ölçüldü; 14–29 Ağustos yok). Ajanın izi de o kadar geriye gider.
  Bu, zaten açık olan "günlük saklama süresi" bekleyen maddesine
  bağlıdır ve AJAN/1'i kurmadan önce karara bağlanması **önerilir**.

---

## 5. KAPATMA YOLU — TEK KOMUT

İki kademe, ikisi de tek komut:

1. **ANINDA (ajanın kullanımını keser, canlıya dokunmaz):**
   ```
   shred -u /etc/enderunai/ajan.env
   ```
   Parola dosyası gidince ajan giriş yapamaz. Canlıda **hiçbir
   değişiklik gerekmez**, kimse onay vermez, iz bırakmaz çünkü
   canlıya dokunulmaz.

2. **KALICI (hesabı kapatır):** Mehmet Bey kullanıcı yönetimi
   ekranından hesabı **pasife alır**. Aktör gerçek insan olur, denetim
   izine düşer.

Birinci kademe ajanın kendisinin de koşabileceği tek komuttur — yani
"bu kimliği durdur" demek için kimseyi beklemek gerekmez.

---

## 6. ÇIRA — İZİN SAYISI ARTAMAZ

`deploy/scripts/ajan-izin-cizgisi.sh` (bu teklifle birlikte yazıldı):
canlıda ajan kimliğinin **etkin izin sayısını** sayar ve çizgiyle
karşılaştırır.

- Çizgi: **6** (`deploy/bekci/ajan-izin-cizgisi.txt`).
- Sayı **6'nın üstündeyse KIRMIZI** — biri ajana izin eklemiş.
- Sayı 6'nın **altındaysa da KIRMIZI** — gevşeklik bırakmak, ajanın
  neye eriştiğini görünmez kılar (şema sapma circirinin aynı deseni).
- `hasAllPermissions` ya da herhangi bir **yazma** izni görürse
  **doğrudan KIRMIZI**, sayıya bakmadan.
- Kimlik henüz yoksa **ÖLÇEMEDİ (çıkış 3)** — "izin yok" ile "kullanıcı
  yok" aynı görünmemeli.
- **Kimlik GEÇERLİLİK TARİHİNDEN eskiyse KIRMIZI** (aşağı).

### GEÇERLİLİK TARİHİ — duran hesap amacından uzun yaşar

Kimliğe bir **son kullanma tarihi** verilir:
`deploy/bekci/ajan-gecerlilik.txt` içinde tek satır `YYYY-AA-GG`.
Çıra, kimliğin `CreatedAtUtc`'sine değil **bu tarihe** bakar: bugün o
tarihten sonraysa **KIRMIZI**.

**Neden:** bu hafta "geçici"nin kalıcılaştığını İKİ KEZ ölçtük — rig
ayarı (150/153 `RequiresProject` kapatması) ve arşiv UPDATE'i (9 kartın
pasife alınması). Üçüncüsü bu olmasın.

**Süre uzatmak bilinçli bir karar olsun:** tarihi ileri almak, dosyayı
elle düzenlemeyi ve commit'lemeyi gerektirir. Unutulmuş bir hesap
kırmızı yanar; uzatılmış bir hesabın kararı kayıtta durur.

Önerilen ilk süre: **kimliğin açıldığı günden 30 gün.**

`ucuz-kapilar.sh`a kimlik kurulduğu gün eklenir.

---

## KURULUM SIRASI (onay gelirse)

1. Mehmet Bey kullanıcıyı açar: `ajan-olcum`, ad "Ölçüm Ajanı (salt
   okuma)", parolayı kendisi üretir, **6 izni** verir, `hasAllPermissions`
   VERMEZ.
2. Parolayı `/etc/enderunai/ajan.env` dosyasına yazar (root, 0600).
3. Ajan **2. maddedeki altı ayaklı sondayı** koşar ve sonucu yazar.
4. `ajan-izin-cizgisi.sh` `ucuz-kapilar.sh`a eklenir, çizgi 6'da kilitlenir.
5. Ölçüm yoksa kimlik kullanılmaz; duran bir anahtar değil, **ölçüm
   için alınıp bırakılan** bir araçtır.


---

## 7. KİLİTLENME DEĞİŞMEZİYLE ÇAKIŞMA (B paketiyle kesişim)

B paketinin değişmezi: *"`user-management.edit`'i fiilen taşıyan en az
bir **ETKİN KULLANICI** kalmalı."* Amacı, sistemin kendini kilitlemesini
önlemek — yetkiyi son taşıyan kişinin yetkisi alınamaz.

**AJAN/1 o sayıya DAHİL EDİLMEYECEK.**

Bugün ajanın `user-management.edit` izni yok, yani çakışma pratikte
doğmuyor. Ama kural **bugünün izin listesine değil İLKEYE** bağlanıyor:

> **SERVİS KİMLİKLERİ, İNSAN GEREKTİREN DEĞİŞMEZLERDE İNSAN SAYILMAZ.**

Gerekçe: değişmezin koruduğu şey "bir hesap var mı" değil, **"yetkiyi
kullanabilecek bir İNSAN var mı"**. Parolası bir dosyada duran, salt
okuyan, son kullanma tarihi olan bir kimlik o soruya cevap veremez.
Sayıya dahil edilirse, değişmez **kağıt üstünde sağlanır ama fiilen
kilitlenme yaşanır**: son insan yetkisini kaybeder, sistem "ajan var"
diye buna izin verir ve kimse içeri giremez.

**B'nin sondasına eklenecek ayak:** *servis kimliği son taşıyıcı
konumuna geçemez* — yani son `user-management.edit` taşıyıcısı bir
servis kimliğine indirgenirse **KIRMIZI**.

Bu ayak B paketiyle birlikte yazılacak; AJAN/1 kurulmadan önce
yazılması ŞART değildir (ajanın o izni yok), ama **AJAN/1'e herhangi
bir yönetim izni eklenmesi düşünülürse önce bu ayak yazılmalıdır.**
