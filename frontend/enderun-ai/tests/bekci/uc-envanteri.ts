import { readFileSync, readdirSync, statSync } from "node:fs";
import { join, relative } from "node:path";

/**
 * SERVİS ÇAĞRISI BEKÇİSİ — ORTAK ARAÇLAR.
 *
 * 7a'daki rota bekçisinin API karşılığı. O tur SAYFA rotalarını
 * kapsamış, uç noktaları kapsam dışı bırakmıştı; aynı gün canlıda
 * bir 404 çıktı (`project-sites/daily-reports/pending-approval`,
 * doğrusu `site-reports/pending-approval`) ve kapsam dışı bırakma
 * kararı teorik bir borç değil, gerçek bir arıza olarak geri döndü.
 *
 * UÇLAR BACKEND KAYNAĞINDAN TÜRETİLİYOR — Swagger çıktısından ya da
 * çalışan sunucudan değil. Kaynak tek gerçek referans; çalışan
 * sunucuya bakan bir bekçi CI'da sessizce boşa düşerdi.
 */

export const ONYUZ_KOK = join(__dirname, "..", "..");
const BACKEND_KOK = join(ONYUZ_KOK, "..", "..", "backend", "EnderunAI.Api");

function dosyalar(dizin: string, desen: RegExp): string[] {
  const bulunan: string[] = [];

  let girdiler: string[];
  try {
    girdiler = readdirSync(dizin);
  } catch {
    return bulunan;
  }

  for (const girdi of girdiler) {
    if (girdi === "node_modules" || girdi === ".next" || girdi === "obj" || girdi === "bin") {
      continue;
    }

    const yol = join(dizin, girdi);

    if (statSync(yol).isDirectory()) {
      bulunan.push(...dosyalar(yol, desen));
      continue;
    }

    if (desen.test(girdi)) bulunan.push(yol);
  }

  return bulunan;
}

/** `{id:guid}` / `{id}` -> tek segment yer tutucu. */
function segmentleriNormalize(yol: string): string {
  return yol
    .split("/")
    .filter(Boolean)
    .map((p) => (/^\{.*\}$/.test(p) ? "X" : p))
    .join("/");
}

export function backendDosyaSayisi(): number {
  return dosyalar(join(BACKEND_KOK, "Controllers"), /Controller\.cs$/).length;
}

/**
 * Backend uç envanteri — normalize edilmiş yollar kümesi.
 * Örnek: `api/tasks/X/approve`
 */
export function uclar(): Set<string> {
  const bulunan = new Set<string>();

  for (const yol of dosyalar(join(BACKEND_KOK, "Controllers"), /Controller\.cs$/)) {
    const kod = readFileSync(yol, "utf8");

    /*
     * BİR DENETLEYİCİDE BİRDEN FAZLA [Route] OLABİLİR ve hepsi
     * geçerlidir. `PersonnelController` hem `api/personnel` hem
     * `api/hr/personnel` taşıyor; yalnız ilkini almak, ön yüzün
     * kullandığı ikinci yolu "yok" göstermişti.
     */
    const tabanlar: string[] = [];
    const rota = /\[Route\("([^"]+)"\)\]/g;

    // `[controller]` yer tutucusu: sınıf adından türetilir.
    const sinifAdi = /class\s+(\w+?)Controller/.exec(kod)?.[1] ?? "";

    let r: RegExpExecArray | null;
    while ((r = rota.exec(kod)) !== null) {
      tabanlar.push(r[1].replace("[controller]", sinifAdi.toLowerCase()));
    }

    /*
     * DENETLEYİCİ [Route] TAŞIMAYABİLİR ve eylemleri MUTLAK yol
     * verebilir (`ProjectSchedulesController`: sınıfta rota yok,
     * her eylemde `[HttpGet("api/is-programi/...")]`). Dosyayı
     * tümden atlamak, o denetleyicinin TÜM uçlarını envanterden
     * düşürüyordu ve ön yüzün 14 çağrısı "kırık" görünüyordu.
     */
    const eylem = /\[Http(?:Get|Post|Put|Delete|Patch)(?:\("([^"]*)"\))?\]/g;

    let m: RegExpExecArray | null;
    while ((m = eylem.exec(kod)) !== null) {
      const parca = m[1] ?? "";

      /*
       * EYLEM MUTLAK YOL VEREBİLİR: `[HttpGet("api/is-programi")]`.
       * Bu durumda denetleyicinin rotası ÖNE EKLENMEZ — eklenirse
       * `api/project-schedules/api/is-programi` gibi var olmayan
       * bir yol üretilir ve gerçek uç envanterde hiç görünmez.
       */
      if (parca.startsWith("api/") || parca.startsWith("/")) {
        bulunan.add(segmentleriNormalize(parca));
        continue;
      }

      for (const taban of tabanlar) {
        bulunan.add(segmentleriNormalize(parca ? `${taban}/${parca}` : taban));
      }
    }
  }

  return bulunan;
}

export type Cagri = {
  ham: string;
  normal: string;
  dosya: string;
  /** Yol bir değişkenle BAŞLIYORSA önek bilinmiyor: doğrulanamaz. */
  hesaplanmis: boolean;
};

/**
 * `${...}` ifadelerini AYIRIR — iç içe süslü parantezi de sayar.
 *
 * Naif `\$\{[^}]*\}` yetmiyor: `${x ? "a" : "b"}` ve
 * `${buildQuery({ a })}` gibi ifadelerde ilk `}` ile kesiyor ve
 * geriye anlamsız bir kuyruk bırakıyor. İlk ölçümde 221 "kırık"
 * çağrının çoğu bu yüzden çıkmıştı.
 */
function ifadeleriBol(sablon: string): { metin: string; ifadeler: number[] } {
  let sonuc = "";
  const ifadeler: number[] = [];

  for (let i = 0; i < sablon.length; i++) {
    if (sablon[i] === "$" && sablon[i + 1] === "{") {
      let derinlik = 1;
      let j = i + 2;

      while (j < sablon.length && derinlik > 0) {
        if (sablon[j] === "{") derinlik++;
        else if (sablon[j] === "}") derinlik--;
        j++;
      }

      ifadeler.push(sonuc.length);
      sonuc += "\u0000"; // yer tutucu işareti
      i = j - 1;
      continue;
    }

    sonuc += sablon[i];
  }

  return { metin: sonuc, ifadeler };
}

/*
 * `apiClient("...")`, `apiClient<T>("...")`, şablon hâlleri ve
 * doğrudan `fetch("/api/backend/...")` çağrıları.
 */
const KALIPLAR = [
  /apiClient\s*(?:<[^>]*>)?\s*\(\s*"([^"]+)"/g,
  /apiClient\s*(?:<[^>]*>)?\s*\(\s*`([^`]+)`/g,
  /fetch\(\s*"(\/api\/backend\/[^"]+)"/g,
  /fetch\(\s*`(\/api\/backend\/[^`]+)`/g,
];

export function onyuzDosyaSayisi(): number {
  return ["app", "components", "services", "lib"]
    .flatMap((d) => dosyalar(join(ONYUZ_KOK, d), /\.(ts|tsx)$/))
    .length;
}

export function cagrilar(): Cagri[] {
  const bulunan: Cagri[] = [];

  for (const d of ["app", "components", "services", "lib"]) {
    for (const yol of dosyalar(join(ONYUZ_KOK, d), /\.(ts|tsx)$/)) {
      const kod = readFileSync(yol, "utf8");

      for (const kalip of KALIPLAR) {
        kalip.lastIndex = 0;

        let m: RegExpExecArray | null;
        while ((m = kalip.exec(kod)) !== null) {
          let ham = m[1];

          // Doğrudan fetch: proxy önekini at.
          ham = ham.replace(/^\/api\/backend\//, "");

          const { metin } = ifadeleriBol(ham);

          /*
           * YER TUTUCU İKİ FARKLI ŞEY OLABİLİR:
           *
           *   `/${id}/`      -> SEGMENT (yola girer, `X` olur)
           *   `tasks${q}`    -> SORGU EKİ (yola girmez, atılır)
           *
           * Ayrım basit: yer tutucudan hemen önceki karakter `/`
           * ise segmenttir; değilse bir öncekine yapışıktır ve
           * pratikte `buildQuery(...)` gibi bir sorgu üreticisidir.
           * İkisini ayırmamak, ilk ölçümde `accounting-accountsX`
           * gibi var olmayan yollar üretmişti.
           */
          let temiz = "";

          for (let i = 0; i < metin.length; i++) {
            const ch = metin[i];

            if (ch !== "\u0000") {
              temiz += ch;
              continue;
            }

            const oncekiSlash = i === 0 || metin[i - 1] === "/";
            if (oncekiSlash) temiz += "X";
            // değilse: sorgu eki, yola katılmaz
          }

          temiz = temiz.split("?")[0].split("#")[0];

          if (temiz.length === 0) continue;

          bulunan.push({
            ham,
            normal: `api/${segmentleriNormalize(temiz)}`,
            dosya: relative(ONYUZ_KOK, yol),
            // Yol bir ifadeyle BAŞLIYORSA önek bilinmiyor.
            hesaplanmis: metin.startsWith("\u0000"),
          });
        }
      }
    }
  }

  return bulunan;
}

/**
 * Çağrı bir uca çözülüyor mu.
 *
 * Yer tutucu `X` her iki tarafta da tek segment; doğrudan küme
 * araması yeterli. Şablon çağrının değişkeni sabit bir segmente
 * denk geliyorsa (`tasks/dashboard`) o da ayrıca aranıyor.
 */
export function cozuluyorMu(cagri: Cagri, envanter: Set<string>): boolean {
  if (envanter.has(cagri.normal)) return true;

  // `X` taşıyan çağrı, sabit karşılığı olan bir uca da denk gelebilir.
  const sabit = cagri.normal.replace(/(^|\/)X(?=\/|$)/g, "$1");
  return envanter.has(sabit);
}
