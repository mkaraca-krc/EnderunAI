import { readFileSync, readdirSync, statSync } from "node:fs";
import { join, relative } from "node:path";

import { describe, expect, it } from "vitest";

/*
 * ÇİZİM SIRASINDA BELİRSİZ DEĞER — HİDRASYON UYUŞMAZLIĞI MUHAFIZI.
 *
 * NEDEN VAR: `components/ui/data-table.tsx` çıktı üst bilgisine
 * `new Date().toLocaleString("tr-TR")` yazıyordu — ÇİZİM SIRASINDA.
 * Sunucu geçişi derleme anında koşuyor, istemci geçişi kullanıcının
 * ekranı açtığı anda. Aradaki fark her yüklemede hidrasyon
 * uyuşmazlığı üretiyordu (React #418).
 *
 * ÖLÇÜLEN MALİYET: 144 statik önçizilen rotanın 26'sı derleme anının
 * saatini HTML'ine dondurmuştu — /gorevler, /finans/odeme-planlari,
 * /hakedis, /cariler, /muhasebe/fisler dahil. 11 gün boyunca (7c5b25bc,
 * 19 Ağustos) hiçbir takım, hiçbir muhafız görmedi; çünkü test takımı
 * hidrasyon çalıştırmaz.
 *
 * İroni kayda değer: damgayı ekleyen paketin adı "F5: çıktı
 * dürüstlüğü"ydü. Çıktının dürüstlüğü için konan damga 26 ekranı bozdu.
 *
 * NE ARIYOR: çizim gövdesinde (`return`'ün içinde, JSX'te) kullanılan
 * belirsiz değerler. Aynı çağrı `useEffect` içinde, olay işleyicisinde
 * ya da bir sunucu bileşeninde GÜVENLİDİR — ayrım kodla yapılıyor,
 * elle değil.
 *
 * ═══ DÜRÜST SINIR — ÖLÇÜLDÜ, TAHMİN DEĞİL ═══
 *
 * Bu muhafız BUGÜN 146 çağrıyı muaf tutuyor ve 0 ihlal bildiriyor.
 * Oran kabul edilemez ve sebebi biliniyor: `cizimdeMi` bir satırdan
 * YUKARI doğru `return (` arıyor. Ama `useState` başlatıcıları
 * `return`'ün ÜSTÜNDE durur — arama hiçbir işaret bulamaz ve varsayılan
 * "çizim değil" olur. Yani muhafız AÇIK tarafa düşüyor.
 *
 * Somut örnek (bugün muaf, ama gerçek):
 *   app/depo-stok/donemsel-sayim/page.tsx:38
 *   const [countDate, setCountDate] = useState(new Date().toISOString()…)
 * Bu satır her iki geçişte de koşar ve tarih değişince uyuşmazlık üretir.
 *
 * BUGÜN YAKALADIĞI: JSX ifadelerinin (`{...}`) içindeki doğrudan
 * çağrılar — `data-table.tsx:565` böyle bulundu.
 * BUGÜN KAÇIRDIĞI: `useState` başlatıcıları, bileşen gövdesinde
 * `return`'den önce hesaplanan sabitler, dolaylı çağrılar.
 *
 * Muafiyet sayısı her koşuda basılıyor (aşağıdaki ilk test). Sayı
 * görünmeseydi bu körlük fark edilmezdi — nitekim ilk sürümde
 * fark edilmedi ve sayaç eklenince ortaya çıktı.
 *
 * KAPSAM GENİŞLETMESİ AYRI İŞ: 146 muafın kaçının gerçek olduğunu
 * ölçmeden sezgiyi değiştirmek, belirsiz büyüklükte bir düzeltme
 * kuyruğu açar.
 */

const KOK = join(__dirname, "..");
const DIZINLER = ["app", "components"];

/** Sunucu ve istemcide farklı sonuç veren çağrılar. */
const BELIRSIZ = /\b(?:new\s+Date\s*\(\s*\)|Date\.now\s*\(\s*\)|Math\.random\s*\(\s*\)|crypto\.randomUUID\s*\(\s*\))/;

type Bulgu = { dosya: string; satir: number; metin: string };

function dosyalar(dizin: string, biriktir: string[] = []): string[] {
  for (const ad of readdirSync(dizin)) {
    const tam = join(dizin, ad);
    if (statSync(tam).isDirectory()) {
      if (ad === "node_modules" || ad === ".next") continue;
      dosyalar(tam, biriktir);
    } else if (ad.endsWith(".tsx")) {
      biriktir.push(tam);
    }
  }
  return biriktir;
}

/**
 * Bir satır JSX çizim gövdesinde mi?
 *
 * ÖLÇÜT: satır bir JSX süslü ifadesinin içinde (`{` ile açılmış, `<`
 * ile başlayan bir bloğun kapsamında) ve bir işleyiciye (`onX={`) ya da
 * `useEffect`/`useMemo`/`useCallback` gövdesine ait DEĞİL.
 */
function cizimdeMi(satirlar: string[], indeks: number): boolean {
  // Yukarı doğru en yakın bağlam işaretini ara.
  for (let i = indeks; i >= 0 && i > indeks - 40; i--) {
    const s = satirlar[i];

    // Olay işleyicisi: onClick={...}, onSubmit={...}
    if (/\bon[A-Z]\w*\s*=\s*\{/.test(s)) return false;

    // Kancalar: useEffect(() => { ... }) — çizim değil.
    if (/\buse(Effect|LayoutEffect|Callback|Memo)\s*\(/.test(s)) return false;

    /*
     * FONKSİYON GÖVDESİ — ÇİZİM DEĞİL.
     *
     * İlk sürümüm yalnız `async function` arıyordu ve
     * `hakedis-editor.tsx:323`'ü yanlış bildirdi: orası
     * `function fillFromSummary(...)` — bir olay işleyicisi, çizim
     * değil. Düz `function ad(` bildirimi de sınırdır.
     *
     * Bileşenin kendisi de `function ...` ile başlar; ama bileşenin
     * gövdesinde JSX'e ulaşmadan önce bir `return (` görürüz ve
     * yukarıdaki dal onu çizim sayar.
     */
    if (/\basync\s+function\b|\bconst\s+\w+\s*=\s*async\b/.test(s)) return false;
    if (/^\s*(?:export\s+)?function\s+[a-z]\w*\s*\(/.test(s)) return false;

    // JSX'e girdik: `return (` ya da bir etiket açılışı
    if (/^\s*return\s*\(/.test(s) || /^\s*<[A-Za-z]/.test(s)) return true;
  }
  return false;
}

function tara(): {
  bulgular: Bulgu[];
  dosyaSayisi: number;
  jsxSatiri: number;
  muaf: Bulgu[];
  sunucuBileseni: number;
} {
  const bulgular: Bulgu[] = [];
  const muaf: Bulgu[] = [];
  let dosyaSayisi = 0;
  let jsxSatiri = 0;
  let sunucuBileseni = 0;

  for (const dizin of DIZINLER) {
    for (const yol of dosyalar(join(KOK, dizin))) {
      const icerik = readFileSync(yol, "utf8");
      dosyaSayisi += 1;

      // Sunucu bileşeni: "use client" yoksa çizim sunucuda, hidrasyon yok.
      if (!/^\s*["']use client["']/m.test(icerik)) {
        sunucuBileseni += 1;
        continue;
      }

      const satirlar = icerik.split("\n");
      for (let i = 0; i < satirlar.length; i++) {
        if (/^\s*(\/\/|\*|\/\*)/.test(satirlar[i])) continue;
        if (!BELIRSIZ.test(satirlar[i])) continue;
        jsxSatiri += 1;

        const kayit = {
          dosya: relative(KOK, yol),
          satir: i + 1,
          metin: satirlar[i].trim().slice(0, 100),
        };

        // MUAF TUTULAN DA SAYILIYOR: fazla muaf tutan muhafız sessizce
        // kördür. Sayı görünmezse körlüğü fark edemeyiz (Kural 70).
        if (cizimdeMi(satirlar, i)) bulgular.push(kayit);
        else muaf.push(kayit);
      }
    }
  }

  return { bulgular, dosyaSayisi, jsxSatiri, muaf, sunucuBileseni };
}

const sonuc = tara();

describe("çizimde belirsiz değer", () => {
  it("tarama boşa düşmüyor ve muafiyet sayısı görünür", () => {
    /*
     * MUAFİYET SAYISI BASILIYOR — SÜS DEĞİL.
     *
     * Bu muhafız iki kümeye ayırıyor: çizimde olanlar (ihlal) ve
     * olay işleyicisi/kanca içinde olanlar (muaf). Sezgi metin
     * tabanlı; fazla muaf tutarsa muhafız sessizce körleşir ve
     * bunu kimse fark etmez. Sayı her koşuda görünsün ki oran
     * kayarsa gözle yakalanabilsin.
     */
    console.log(
      `[çizimde belirsiz değer] taranan .tsx: ${sonuc.dosyaSayisi} · ` +
        `sunucu bileşeni (atlanan): ${sonuc.sunucuBileseni} · ` +
        `belirsiz çağrı: ${sonuc.jsxSatiri} · ` +
        `çizimde (ihlal): ${sonuc.bulgular.length} · ` +
        `MUAF: ${sonuc.muaf.length}`,
    );
    for (const m of sonuc.muaf) {
      console.log(`  muaf → ${m.dosya}:${m.satir}  ${m.metin}`);
    }

    // POZİTİF KONTROL: dizinler taşınırsa ya da desen bozulursa
    // aşağıdaki iddia boş kümede sessizce yeşil kalırdı.
    expect(sonuc.dosyaSayisi).toBeGreaterThan(150);
    expect(sonuc.jsxSatiri).toBeGreaterThan(0);
  });

  it("istemci bileşenlerinin çizim gövdesinde belirsiz değer yok", () => {
    const liste = sonuc.bulgular.map(
      (b) => `${b.dosya}:${b.satir}  ${b.metin}`,
    );

    expect(
      liste,
      liste.length === 0
        ? ""
        : "ÇİZİM SIRASINDA BELİRSİZ DEĞER — HİDRASYON UYUŞMAZLIĞI ÜRETİR:\n" +
          liste.join("\n") +
          "\n\nDÜZELTME: değeri çizimde üretme. Bağlanma sonrası " +
          "(useEffect) doldur ya da olay anında (yazdırma/indirme) üret.",
    ).toEqual([]);
  });
});
