import { describe, expect, it } from "vitest";

import {
  chequeMonthKey,
  chequeTotalLabel,
  summarizeCheques,
} from "@/lib/cheques/totals";
import { ChequeStatus, type ChequeListItem } from "@/services/cheque.service";

/**
 * Çek defteri toplamları.
 *
 * NEDEN VAR: üst satırdaki "Listelenen toplam" ile ay alt toplamları
 * AYRI hesaplanıyordu ve kuralları farklıydı — üst toplam iptal
 * edilmiş çekleri sayıyor, ay toplamları saymıyordu. Aynı ekranda
 * birbirini tutmayan iki rakam vardı ve hiçbir test bunu yakalamıyordu.
 *
 * Kilitlenen kural: iptal edilen çek HİÇBİR toplama girmez, satırı
 * ise denetim izi olarak listede kalır.
 */

let counter = 0;

function cheque(
  overrides: Partial<ChequeListItem> & { dueDate: string; amountTry: number }
): ChequeListItem {
  counter += 1;

  const status = overrides.status ?? ChequeStatus.Issued;

  return {
    id: `cheque-${counter}`,
    chequeNumber: `CEK-${counter}`,
    direction: 1,
    status,
    // SUNUCUNUN YOLLADIĞI BAYRAK TAKLİT EDİLİYOR.
    //
    // Kural artık ekranda değil sunucuda (`ChequeStatusRules`); test
    // verisi de sunucunun ürettiği hâli taşımalı, yoksa test var
    // olmayan bir sözleşmeyi sınar. Sunucudaki kural "iptal dışında
    // her şey toplanır".
    countsTowardTotals: overrides.countsTowardTotals
      ?? status !== ChequeStatus.Voided,
    // amountTry ve dueDate zaten overrides'ta zorunlu; aşağıdaki
    // yayılma onları getirir. amount ise yalnız varsayılan olarak
    // TRY tutarına eşitlenir, istenirse override edilebilir.
    amount: overrides.amountTry,
    currencyCode: "TRY",
    ...overrides,
  } as ChequeListItem;
}

describe("çek toplamları", () => {
  /**
   * Kullanıcının istediği asıl güvence: iptalli bir listede üst
   * toplam ile ay toplamlarının toplamı BİRBİRİNE EŞİT ve ikisi de
   * iptal tutarını dışarıda bırakıyor.
   */
  it("iptalli senaryoda üst toplam = ay toplamlarının toplamı", () => {
    const items = [
      cheque({ dueDate: "2026-01-15", amountTry: 40_000 }),
      cheque({ dueDate: "2026-01-20", amountTry: 25_000, status: ChequeStatus.Voided }),
      cheque({ dueDate: "2026-02-10", amountTry: 60_000 }),
      cheque({ dueDate: "2026-02-18", amountTry: 15_000, status: ChequeStatus.Voided }),
    ];

    const { listTotal, groups } = summarizeCheques(items);

    const sumOfGroups = groups.reduce((sum, group) => sum + group.total, 0);

    expect(listTotal).toBe(sumOfGroups);

    // İptaller (25.000 + 15.000) hariç: 40.000 + 60.000.
    expect(listTotal).toBe(100_000);
  });

  it("iptal edilen çek ay toplamına ve adedine girmez", () => {
    const items = [
      cheque({ dueDate: "2026-03-05", amountTry: 10_000 }),
      cheque({ dueDate: "2026-03-09", amountTry: 7_500, status: ChequeStatus.Voided }),
    ];

    const [march] = summarizeCheques(items).groups;

    expect(march.total).toBe(10_000);
    expect(march.count).toBe(1);
  });

  /**
   * İptal satırı LİSTEDE KALIR: mali etkisi yok ama kaydın kendisi
   * denetim izi. Satırı gizlemek, "bu çek hiç var olmadı" demek olurdu.
   */
  it("iptal edilen çekin satırı listede kalır", () => {
    const items = [
      cheque({ dueDate: "2026-04-01", amountTry: 5_000 }),
      cheque({ dueDate: "2026-04-02", amountTry: 3_000, status: ChequeStatus.Voided }),
    ];

    const [april] = summarizeCheques(items).groups;

    expect(april.rows).toHaveLength(2);
    expect(april.count).toBe(1);
  });

  it("tamamı iptal olan ayda toplam sıfırdır", () => {
    const items = [
      cheque({ dueDate: "2026-05-01", amountTry: 9_000, status: ChequeStatus.Voided }),
    ];

    const { listTotal, groups } = summarizeCheques(items);

    expect(listTotal).toBe(0);
    expect(groups[0].total).toBe(0);
    expect(groups[0].count).toBe(0);
    // Ay yine görünür: satır denetim izi olarak duruyor.
    expect(groups[0].rows).toHaveLength(1);
  });

  /**
   * Defter değeri toplanır, ham tutar değil. Üst toplam eskiden
   * `amountTry || amount` kullanıyordu; kur karşılığı sıfır olan bir
   * dövizli çekte ham tutarı ekleyip ay toplamından ayrışırdı.
   */
  it("dövizli çekte TL karşılığı toplanır, ham tutar değil", () => {
    const items = [
      cheque({
        dueDate: "2026-06-01",
        amountTry: 35_000,
        amount: 1_000,
        currencyCode: "USD",
      }),
    ];

    expect(summarizeCheques(items).listTotal).toBe(35_000);
  });

  it("boş listede toplam sıfır, grup yok", () => {
    const { listTotal, groups } = summarizeCheques([]);

    expect(listTotal).toBe(0);
    expect(groups).toHaveLength(0);
  });

  /**
   * TOPLAM KARARI SUNUCUNUN — EKRAN KENDİ KURALINI YAZMIYOR.
   *
   * ÇEK/1'in kök nedeni iki ayrı karar yeriydi: sunucu listeye neyi
   * koyacağına, ekran neyi toplayacağına ayrı karar veriyordu ve
   * ikisi ayrışmıştı. Bu test ekranın artık kendi kuralı OLMADIĞINI
   * kanıtlıyor: durumu "Ödendi" olan bir satır, sunucu saydırdığı
   * için toplanıyor; iptal durumundaki bir satır sunucu saymadığı
   * için toplanmıyor. Karar tek yerde.
   */
  it("toplam, durumu değil sunucunun bayrağını izler", () => {
    const items = [
      // Ödenmiş çek: kullanıcı "Ödendi" süzgecini seçtiğinde sunucu
      // bunu listeye koyar VE saydırır — dolu liste + sıfır toplam
      // anlamsız bir ekran olurdu.
      cheque({
        dueDate: "2026-03-10",
        amountTry: 30_000,
        status: ChequeStatus.Paid,
        countsTowardTotals: true,
      }),
      // Sunucu saymadığını söylediyse ekran saymaz — durumu ne olursa
      // olsun.
      cheque({
        dueDate: "2026-03-12",
        amountTry: 99_000,
        status: ChequeStatus.Issued,
        countsTowardTotals: false,
      }),
    ];

    const { listTotal, groups } = summarizeCheques(items);

    expect(listTotal).toBe(30_000);
    // Sayılmayan satır LİSTEDE kalır: gizlemek yok saymak değil.
    expect(groups[0].rows).toHaveLength(2);
    expect(groups[0].count).toBe(1);
  });
});

describe("toplam başlığı", () => {
  const etiketler = { 10: "Verilen", 11: "Ödendi", 90: "İptal" };

  /**
   * BAŞLIK SÜZGECİ TAKİP ETMELİ.
   *
   * "Bu Ayın Çek Yükü" yazıp altında ödenmişlerin toplamını
   * göstermek, sayı doğru olsa bile cümleyi yalan yapar. Kullanıcı
   * rakamı değil başlığı okur.
   */
  it("varsayılanda açık çekleri söyler", () => {
    expect(chequeTotalLabel("", false, etiketler)).toBe("Açık çekler toplamı");
  });

  /**
   * DURUM ETİKETİ PARANTEZ İÇİNDE — SIFAT OLARAK DEĞİL.
   *
   * "Ödendi çekler toplamı" sayı doğru ama cümle bozuktu. Etiketler
   * durum adı ve isimden önce sıfat çekimi gerektiriyor. Sıfat
   * karşılığı listesi açılmadı: yeni durum eklendiğinde karşılığını
   * yazmayı unutan biri aynı bozukluğu geri getirirdi.
   */
  it("durum seçiliyse etiketi parantez içinde verir", () => {
    expect(chequeTotalLabel("11", false, etiketler)).toBe("Toplam (Ödendi)");
    expect(chequeTotalLabel("10", false, etiketler)).toBe("Toplam (Verilen)");
    expect(chequeTotalLabel("90", false, etiketler)).toBe("Toplam (İptal)");
  });

  /**
   * Kapanmışlar açıkken liste artık "açık çekler" değil; başlık da
   * öyle dememeli. Yanlış başlık, düzeltmeye çalıştığımız hatanın
   * ekran tarafındaki hâli olurdu.
   */
  it("kapanmışlar açıkken açık çek demez", () => {
    expect(chequeTotalLabel("", true, etiketler)).toBe("Listelenen toplam");
  });
});

describe("ay gruplaması", () => {
  /**
   * AY VADE TARİHİNE GÖRE — KEŞİDE TARİHİNE GÖRE DEĞİL.
   *
   * "Bu ayın çek yükü" sorusu "bu ay hangi çekleri ödeyeceğiz"
   * demektir; çekin ne zaman yazıldığı değil ne zaman ödeneceği
   * önemli. Keşide tarihine göre gruplansaydı Ağustos'ta yazılıp
   * Kasım'da ödenecek çek Ağustos yüküne girer ve iki ay birden
   * yanlış olurdu.
   *
   * ÇEK/1'de bu alan ölçüldü ve DOĞRU çıktı; test onu kilitliyor.
   */
  it("ay anahtarı vade tarihinden gelir", () => {
    const item = cheque({
      dueDate: "2026-11-15",
      amountTry: 1_000,
      issueDate: "2026-08-01",
    });

    expect(chequeMonthKey(item)).toBe("2026-11");
  });

  it("toplamlar vade ayına düşer, keşide ayına değil", () => {
    const { groups } = summarizeCheques([
      cheque({ dueDate: "2026-11-15", amountTry: 1_000, issueDate: "2026-08-01" }),
      cheque({ dueDate: "2026-08-20", amountTry: 2_000, issueDate: "2026-08-02" }),
    ]);

    const kasim = groups.find((g) => g.key === "2026-11");
    const agustos = groups.find((g) => g.key === "2026-08");

    expect(kasim?.total).toBe(1_000);
    expect(agustos?.total).toBe(2_000);
  });
});
