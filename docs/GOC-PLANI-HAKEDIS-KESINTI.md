# GÖÇ PLANI — HAKEDİŞ KESİNTİ TÜRLERİ (2026-09-17)

**Durum: PLAN. Kod yazılmadı, göç üretilmedi, şema değişmedi.**
Mehmet Bey'in onayı bekleniyor.

---

## ⚠ ÖNCE BİR SORU — 293 mü 295 mi?

Karar metni: *"Hesap 193; sözleşme uzun vadeye yayılıyorsa **293**."*

**ÖLÇÜM BUNUNLA ÇELİŞİYOR.** Bu şirketin CANLI hesap planında:

```
193  PEŞİN ÖDENEN VERGİLER VE FONLAR        (Diğer Dönen Varlıklar)
293  GELECEK YILLAR İHTİYACI STOKLAR        ← STOK hesabı
295  PEŞİN ÖDENEN VERGİLER VE FONLAR        ← 193'ün uzun vadeli karşılığı
```

`295` canlıda **var** (1 adet, ölçüldü). Tekdüzen planda 193'ün duran
varlık karşılığı 295'tir; 293 stoktur.

**293'e stopaj yazmak, peşin ödenen vergiyi stok hesabına kaydetmek
olur.** Büyük olasılıkla 295'in sürçmesi — ama tahmin etmiyorum.

> **BU SORU CEVAPLANMADAN uzun vade ayağı kodlanmayacak.** Kısa vade
> (193) ayağı tartışmasız; istenirse önce o çıkar.

---

## İYİ HABER — GEREKENİN ÇOĞU ZATEN VAR

Ölçüldü: `progress_payment_deduction_rules` **zaten proje kapsamlı** ve
aradığımız alanların çoğunu taşıyor.

| ihtiyaç | mevcut alan | durum |
|---|---|---|
| sözleşme/proje bazlı kural | `ProjectId` | **VAR** |
| oran | `Rate` | **VAR** |
| açık/kapalı bayrağı | `IsActive` | **VAR** |
| otomatik uygulansın mı | `IsAutomatic` | **VAR** |
| hesaplama tabanı | `CalculationBase` | **VAR** |
| **muhasebe hesabı** | — | **YOK** ← eklenecek |

Hakediş `ProjectId`ye bağlı (sözleşmeye değil), yani kural tablosunun
kapsamı zaten doğru yerde.

Vade bilgisi de var: `projects.PlannedStartDate/PlannedEndDate`,
`ContractDate`, `ContractDeadlineDate`.

---

## DEĞİŞİKLİKLER

### 1. Enum — üç yeni üye (kod, göç değil)

`HakedisDeductionType`e **sona** eklenir; mevcut değerler DEĞİŞMEZ
(değer kaydırmak canlı satırların türünü sessizce değiştirirdi):

```
IncomeTaxWithholding = 10   // STOPAJ
StampDuty            = 11   // DAMGA VERGİSİ
ContractPenalty      = 12   // GECİKME CEZASI (sözleşme)
```

`OhsPenalty = 7` **dokunulmaz** — gecikme cezası ondan AYRI tür.

### 2. Göç — kural tablosuna hesap alanı

```
ALTER TABLE progress_payment_deduction_rules
  ADD COLUMN "AccountingAccountId"         uuid NULL,
  ADD COLUMN "LongTermAccountingAccountId" uuid NULL;
```

**İKİ ALAN, TEK ALAN DEĞİL — mimari notun karşılığı.** Mehmet Bey:
*"Tek alanlı tasarım uzun vadeli sözleşmede sessizce yanlış hesaba
yazar."* Kısa vade hesabı ve uzun vade hesabı ayrı tutuluyor; hangisinin
kullanılacağını sözleşme vadesi belirliyor.

İkisi de **NULL** açılıyor: mevcut 0 satır var, yani veri taşıma yok.

### 3. Vade çözümü — KURAL, KOD SABİTİ DEĞİL

```
uzunVadeli = (PlannedEndDate ?? ContractDeadlineDate) yılı
             > (PlannedStartDate ?? ContractDate) yılı
```

Yani **yıl atlıyorsa** uzun vadeli. Hesap seçimi:

```
uzunVadeli && LongTermAccountingAccountId != null
    -> LongTermAccountingAccountId
    -> aksi hâlde AccountingAccountId
```

**FAIL-CLOSED:** uzun vadeli bir sözleşmede uzun vade hesabı seçilmemişse
**fiş üretilmez, hata verilir.** Sessizce kısa vade hesabına yazmak, tam
da mimari notun uyardığı kusurdur.

### 4. Varsayılan kural satırları — tohumlama DEĞİL, EKRAN

| tür | varsayılan oran | varsayılan bayrak | hesap |
|---|---|---|---|
| Stopaj | **%5** | **AÇIK** | 193 / *(uzun vade: soru açık)* |
| Damga vergisi | **YOK** | kapalı | 193 |
| Gecikme cezası | **YOK** | kapalı | 689 |
| Teminat (mevcut) | %5 | mevcut | **226** (müşavir onayladı) |

**Stopajın varsayılanı AÇIK** çünkü işler genelde yıllara sari; ama
bayrak **kapatılabilir** — tek yılda biten işte uygulanmaz.

**Kod sabiti yazılmıyor.** Bu satırlar projeye kural satırı olarak
düşecek; ekrandan değiştirilebilir. `DEDUCTION_TYPE_OPTIONS`taki
`defaultRate` alanı yeni türler için **0** kalır (bkz.
`tests/kesinti-varsayilan-oran.test.ts` — gerekçesiz varsayılan oran
yasak).

### 5. KDV tevkifatı — EKLENMİYOR

Kesinti satırı yazılmayacak. Gerekçe: 4/10 oranı **391 tevkifatlı satış
KDV** hesabında izleniyor; bu fatura/KDV katmanı ve hakedişin net
tutarını düşürmüyor. Kesinti satırı **çifte düşüm** olurdu.

---

## GERİ ALINABİLİRLİK

| adım | geri alınışı |
|---|---|
| enum üyeleri | kod; geri alma = commit geri alma |
| iki yeni kolon | `DROP COLUMN` — **veri kaybı yok** (0 satır, NULL) |
| kural satırları | `IsActive=false` ya da soft delete |

Göç **yıkıcı değil**; `YIKICI-BEYAN` gerekmiyor. Yine de göç provası
(`goc-provasi.sh`) koşturulacak.

---

## SIRA

1. **293/295 sorusu cevaplanır.**
2. Göç planı onaylanır.
3. Kod + göç yazılır, `goc-provasi.sh` koşar.
4. Dağıtım **19:00 sonrası**, ve **A (K5) önce iner**.
