import { readFileSync } from "node:fs";
import { join } from "node:path";

import { describe, expect, it } from "vitest";

const ROOT = join(__dirname, "..");

const page = readFileSync(join(ROOT, "app/finans/cekler/page.tsx"), "utf8");
const service = readFileSync(join(ROOT, "services/cheque.service.ts"), "utf8");

/**
 * ÇEK EKRANI — SÖZLEŞME.
 *
 * Buradaki maddelerin hepsi tek tek yaşanmış kusurlar:
 * damga gönderilmeyen bir istek korumayı fiilen kapatıyor, iptal
 * edilmiş çekin listede varsayılan görünmesi toplamları kirletiyor,
 * ekranın kendi "düzenlenebilir mi" kuralını yazması uçla ayrışıyor.
 *
 * Tip sistemi damganın ZORUNLU olduğunu zaten tutuyor; buradaki test
 * damganın DOĞRU KAYNAKTAN — sunucunun döndüğü detaydan — geldiğini
 * kontrol ediyor. Sabit bir tarih yazmak da tipi memnun ederdi.
 */
describe("çek ekranı sözleşmesi", () => {
  it("düzenleme isteği damgayı detaydan alıp gönderiyor", () => {
    expect(page).toContain("rowVersion: detail.rowVersion");
  });

  it("iptal isteği hem sayılabilir nedeni hem damgayı gönderiyor", () => {
    const call = page.slice(page.indexOf("chequeService.void("));

    expect(call.slice(0, 400)).toContain("reasonKind");
    expect(call.slice(0, 400)).toContain("rowVersion: detail.rowVersion");
  });

  it("damga reddedildiğinde yenileme teklif ediliyor", () => {
    // Yalnız hatayı göstermek kullanıcıyı aynı hataya tekrar sürüyor:
    // elindeki veri artık eski.
    expect(page).toContain("Bu çek siz açıkken güncellendi");
    expect(page).toContain("Sayfayı Yenile");
  });

  it("iptaller varsayılan gizli, anahtar listeye taşınıyor", () => {
    // Başlangıç değeri AÇIKÇA kontrol ediliyor: `useState(true)`
    // yazıldığında iptaller sessizce listeye dönerdi.
    expect(page).toContain("const [showVoided, setShowVoided] = useState(false);");
    expect(page).toContain("includeVoided: showVoided");
    expect(service).toContain('query.set("includeVoided", "true")');
  });

  it("düzenleme kararı sunucudan geliyor, ekran kendi kuralını yazmıyor", () => {
    expect(page).toContain("!detail.canEdit");
    // Kapalıysa neden kapalı olduğu SUNUCUNUN cümlesiyle gösteriliyor.
    expect(page).toContain("detail.editBlockedReason");
  });

  it("filtre değişince sayfa başa dönüyor (iptal anahtarı dahil)", () => {
    const reset = page.slice(page.indexOf("resetKey="));

    expect(reset.slice(0, 120)).toContain("showVoided");
  });
});
