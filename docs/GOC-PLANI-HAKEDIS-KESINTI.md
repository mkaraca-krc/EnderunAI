# GÖÇ PLANI — HAKEDİŞ KESİNTİ TÜRLERİ (2026-09-17, **sadeleşmiş hâl**)

**Durum: PLAN. Kod yazılmadı, göç üretilmedi, şema değişmedi.**

> **BU PLAN İKİNCİ HÂLİDİR.** İlk hâli iki hesap alanı, proje
> tarihlerinden vade türetme ve uzun-vade fail-closed'ı içeriyordu.
> Mali müşavirin cevabı üçünü birden gereksiz kıldı (aşağıda). İlk
> tasarım `deploy/park/KesintiVadesi.cs` içinde, gerekçesiyle duruyor.

---

## MÜŞAVİR CEVABI — İKİ SORU BİRDEN KAPANDI

| soru | cevap |
|---|---|
| uzun vade hesabı 293 mü 295 mi? | **295 doğru, 293 sürçmeymiş** (ölçümün gösterdiği) |
| mahsup ne zaman sınıflandırılır? | **"Siz 193'e yazın, biz ayırırız"** — müşavir dönem sonunda ayırıyor |

**Sonuç: sistem HER ZAMAN 193'e yazar, 295'e hiç yazmaz.** 295, müşavirin
dönem sonunda kullandığı hesaptır; **koda girmez.**

### Bunun iptal ettikleri

- ❌ İkinci hesap alanı (`LongTermAccountingAccountId`) — **gerekmiyor**
- ❌ Vade türetme kuralı (bitiş yılı > başlangıç yılı) — **yok**
- ❌ Uzun vade hesabı seçilmemişse fail-closed — **konusuz**
- ❌ Bitiş tarihi NULL ise fiş üretmeme — **düştü**

---

## YILLARA SARİ BAYRAĞI — TÜRETİLMEZ, AÇIKÇA SEÇİLİR

Mehmet Bey (17.09): *"Boş bitiş tarihinden çıkarım yapmak sessiz yanlış
sonuç üretir — kaçındığımız kusurun aynısı."*

Bayrak **açık** olacak, **varsayılanı AÇIK**, kullanıcı kapatabilecek.

**Yeni kolon gerekmiyor:** `progress_payment_deduction_rules.IsActive`
zaten var ve tam bu işi görüyor — kural satırı aktifse stopaj uygulanır,
kapatılırsa uygulanmaz. Tek yılda biten bir işte Mehmet Bey satırı
kapatır.

---

## DEĞİŞİKLİKLER

### 1. Enum — üç yeni üye (plan aynen korundu)

Sona eklenir; **mevcut değerler DEĞİŞMEZ** (değer kaydırmak canlı
satırların türünü sessizce değiştirirdi). `OhsPenalty = 7` **sabit** —
gecikme cezası ondan ayrı tür.

```
IncomeTaxWithholding = 10   // STOPAJ
StampDuty            = 11   // DAMGA VERGİSİ
ContractPenalty      = 12   // GECİKME CEZASI (sözleşme)
```

### 2. Göç — kural tablosuna TEK hesap alanı

```
ALTER TABLE progress_payment_deduction_rules
  ADD COLUMN "AccountingAccountId" uuid NULL;
```

**Tür başına tek hesap.** İki alanlı ara tasarım iptal edildi.

Kolon **NULL** açılıyor; tabloda **0 satır** var (ölçüldü), yani veri
taşıma yok.

### 3. Tür → hesap eşlemesi

| tür | oran | hesap |
|---|---|---|
| Stopaj | **%5** | **193**, her zaman |
| Damga vergisi | **varsayılan yok** | **193** |
| Gecikme cezası | **varsayılan yok** | **689** |
| Teminat *(mevcut)* | %5 | **226** |
| KDV tevkifatı | — | **kesinti satırı yok** |

Oran ve hesap **kural satırından** gelir, kod sabitinden değil.
`DEDUCTION_TYPE_OPTIONS`taki `defaultRate` yeni türler için **0** kalır
— gerekçesiz varsayılan oran yasağı (`tests/kesinti-varsayilan-oran.test.ts`)
onları da kapsıyor. Stopajın %5'i **kural satırına** yazılır.

---

## GERİ ALINABİLİRLİK

| adım | geri alınışı |
|---|---|
| enum üyeleri | kod; commit geri alma |
| tek yeni kolon | `DROP COLUMN` — **veri kaybı yok** (0 satır, NULL) |
| kural satırları | `IsActive=false` ya da soft delete |

Göç **yıkıcı değil**; `YIKICI-BEYAN` gerekmiyor. `goc-provasi.sh`
koşturulacak.

---

## SIRA

1. Plan onaylanır.
2. Kod + göç yazılır, `goc-provasi.sh` koşar.
3. Dağıtım **19:00 sonrası**, ve **A (K5) önce iner**.

---

# EK GÖÇ PLANI — STOPAJ HESABI (2026-09-17)

**Durum: PLAN. Kod yazılmadı.** Mehmet Bey'in kararı (c): adlandırılmış
hesap alanı.

## Neden şema değişikliği

`TaxPayableAccountId` bir **veritabanı kolonudur**
(`company_finance_settings`), `GetOrCreateFinanceSettingsAsync` içinde
şirket başına **bir kez** `"360"`dan tohumlanıp saklanıyor. Adlandırılmış
yeni alan da aynı tabloda bir kolon demek.

## Değişiklik

```
ALTER TABLE company_finance_settings
  ADD COLUMN "IncomeTaxWithholdingAccountId" uuid NULL;
```

- Tohumlama: `FindAccountIdAsync(companyId, "193")`
- `AccountingIntegrationService:1006` bu alanı kullanır
- `FindAccountIdAsync(companyId, "360")` stopaj yolundan **tamamen
  çıkar** — asıl kusur koda hesap kodu yazılmasıydı
- **FAIL-CLOSED:** alan boşsa fiş üretilmez, hata verilir
  (`TaxPayableAccountId`e geri düşüş YOK — o geri düşüş bugünkü kusurun
  ta kendisi)

`TaxPayableAccountId` **silinmiyor**: bordro yolu onu kullanıyor
olabilir; yalnız stopaj yolundan çıkıyor.

## Ölçülmüş zemin

| ölçüm | sonuç |
|---|---|
| 360'a yazılmış fiş satırı | **0** |
| stopaj açıklamalı fiş satırı | **0** (hiç stopaj fişi yazılmamış) |
| stopaj oranı > 0 olan hakediş | **0** (1 hakediş var) |

**Geçmiş kayıt düzeltilmeyecek — düzeltilecek kayıt yok.**

## Geri alınabilirlik

Kolon NULL açılıyor, `DROP COLUMN` ile geri alınır, veri kaybı yok.
Yıkıcı değil.
