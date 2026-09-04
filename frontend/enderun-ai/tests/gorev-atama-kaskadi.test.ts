import { readFileSync } from "node:fs";
import { join } from "node:path";

import { describe, expect, it } from "vitest";

/**
 * İŞ EMRİ ATAMA KASKADI — İŞEMRİ/2 FAZ 2.
 *
 * ── FAZ 1 NE BIRAKMIŞTI ──
 *
 * Faz 1 arka uçta personel atamasını açtı (`AssignedToPersonnelId`,
 * kural, dört yazma yolu). Ama ölçüldü ki EKRAN onu hiç kullanmıyordu:
 * görev formu `assignedToUserId: null` gönderiyor ve atama için
 * hiçbir alan taşımıyordu. Yani yetenek vardı, kapısı yoktu.
 *
 * ── KASKADIN DÜRÜST BOŞLUĞU ──
 *
 * Mesajın YERİ ölçümle düzeltildi: ilk tasarımda "departman seçici
 * boşsa" durumuna konacaktı. Ölçüm gösterdi ki seçici BOŞ DEĞİL
 * (canlıda 6 departman); boş olan, seçimden SONRAKİ personel listesi.
 */
const KOK = join(__dirname, "..");
const EKRAN = join(KOK, "app", "gorevler", "page.tsx");

const ekran = readFileSync(EKRAN, "utf8");

describe("iş emri atama kaskadı", () => {
  it("ekran okunabiliyor — POZİTİF KONTROL", () => {
    expect(ekran.length).toBeGreaterThan(5000);
    expect(ekran).toContain("atamaKaynagi");
  });

  it("TÜM PERSONEL seçeneği her zaman açık", () => {
    /*
     * Kaskadın bir kolu boş olsa bile ekran kullanılabilir kalmalı.
     * ÖLÇÜLDÜ (2026-09-04): departman bağı canlıda 0/79 idi; kaskadı
     * zorunlu kılsaydık HİÇ KİMSE görev atayamazdı.
     *
     * Varsayılan da "tumu" — kullanıcı hiçbir şey seçmeden atama
     * yapabiliyor.
     */
    expect(ekran).toContain('atamaKaynagi: "tumu"');
    expect(ekran).toContain('<option value="tumu">Tüm personel</option>');
  });

  it("boş liste SESSİZ geçmiyor — sebebini söylüyor", () => {
    /*
     * Boş bir liste, sebebini söylemezse kullanıcı KENDİ hatasını
     * arar — oysa sorun verinin girilmemiş olmasıdır. Mesaj ayrıca
     * ne yapılacağını da söylüyor: personel ekranına bağlantı ve
     * "Tüm personel" seçeneği.
     */
    expect(ekran).toContain("Bu departmana atanmış personel yok");
    expect(ekran).toContain("/insan-kaynaklari/personeller");
    expect(ekran).toContain("Bu projede görevli personel yok");
  });

  it("kaynak değişince SEÇİM temizleniyor", () => {
    /*
     * Aksi hâlde artık listede olmayan bir kişi seçili kalırdı ve
     * kullanıcı bunu göremezdi — form, göstermediği bir değeri
     * gönderirdi.
     */
    const blok = ekran.slice(
      ekran.indexOf("atamaKaynagi: event.target"),
      ekran.indexOf("</select>", ekran.indexOf("atamaKaynagi: event.target")),
    );

    expect(blok).toContain('assignedToPersonnelId: ""');
    expect(blok).toContain('atamaDepartmanId: ""');
    expect(blok).toContain('atamaProjeId: ""');
  });

  it("atama İSTEĞE BAĞLI — atanmadı seçeneği var", () => {
    // Faz 1'in kuralı atamasız görevi kabul ediyor; ekran onu
    // reddetmemeli.
    expect(ekran).toContain('<option value="">Atanmadı</option>');
  });

  it("kaskad durumu sunucuya GÖNDERİLMİYOR", () => {
    /*
     * `atamaKaynagi`, `atamaDepartmanId`, `atamaProjeId` ekranın
     * kendi durumu. Sunucuya giden tek şey `assignedToPersonnelId`.
     *
     * Gönderilselerdi, sunucuda karşılığı olmayan alanlar sözleşmeyi
     * kirletirdi ve bir gün "neden bu alan var" sorusu doğardı.
     */
    const gonderim = ekran.slice(
      ekran.indexOf("await workTaskService.create({"),
      ekran.indexOf("});", ekran.indexOf("await workTaskService.create({")),
    );

    expect(gonderim).toContain("assignedToPersonnelId");
    expect(gonderim).not.toContain("atamaKaynagi");
    expect(gonderim).not.toContain("atamaDepartmanId");
    expect(gonderim).not.toContain("atamaProjeId");
  });

  it("Yapacak sütunu SUNUCUNUN hesabını gösteriyor", () => {
    /*
     * Değer sunucuda hesaplanıyor (`assignedToDisplayName`). Ekran
     * kullanıcı adı ile personel adı arasında SEÇİM YAPMIYOR — çünkü
     * Faz 1'de çelişki KAYNAKTA reddedildi: iki atama alanı asla
     * birlikte dolamaz.
     *
     * Ekranda bir "ya öbürü doluysa" mantığı, kaynakta olmayan bir
     * belirsizliği uydurmak olurdu.
     */
    expect(ekran).toContain('header: "Yapacak"');
    expect(ekran).toContain("item.assignedToDisplayName");

    // İKİ ALANDAN BİRİNİ SEÇEN BİR MANTIK YOK.
    expect(ekran).not.toContain("assignedToName ??");
    expect(ekran).not.toContain("assignedToPersonnelName ??");
  });
});
