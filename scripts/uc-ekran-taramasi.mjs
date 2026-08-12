#!/usr/bin/env node
/**
 * "Uç var, ekran yok" taraması.
 *
 * NE YAPAR: backend controller'larındaki bütün HTTP uçlarını çıkarır,
 * frontend kaynağında çağrılan yollarla karşılaştırır, hiçbir yerden
 * çağrılmayanları listeler.
 *
 * NEDEN: bir uç ekrandan çağrılmıyorsa iş bitmiş GÖRÜNÜR ama
 * kullanıcı özelliğe ulaşamaz. Tek tek fark edilmesi şansa kalıyordu;
 * bu betik onu tekrarlanabilir hale getiriyor.
 *
 * KAPSAM: frontend'deki HER dizgi taranır — yalnız apiClient
 * çağrıları değil, sayfa içindeki çıplak fetch'ler ve yerel api()
 * yardımcıları da. Servislerin `const root = "..."` deseni ve kök
 * üreten fonksiyonlar çözülür, yoksa kullanılan uçlar listeye düşer.
 *
 * RAPOR ÜÇ KADEMELİ, çünkü tek bir eşleştirme kuralı yetmiyor:
 * 1. KESİN — gevşek eşleşme bile yok, uca hiçbir ekran dokunmuyor.
 * 2. ŞÜPHELİ — yalnız değişken yollu bir referansla eşleşiyor
 *    (`secretariat/${path}` gibi fabrika servisleri). Gözle bakılır.
 * 3. METOT — yol birebir çağrılıyor ama bu HTTP metodu çağrılmıyor
 *    (liste var, silme yok gibi).
 *
 * Metot, yolu içeren çağrının parantez aralığından okunur; yükleme
 * yardımcıları gibi metodu gövdesinde kuran fonksiyonlar da dikkate
 * alınır. Bulunamazsa GET varsayılır (istemcilerin varsayılanı).
 *
 * Kullanım: node scripts/uc-ekran-taramasi.mjs [--json]
 */

import { readdirSync, readFileSync, statSync } from "node:fs";
import { join, relative } from "node:path";

const ROOT = new URL("..", import.meta.url).pathname.replace(/\/$/, "");
const BACKEND = join(ROOT, "backend/EnderunAI.Api/Controllers");
const FRONTEND = join(ROOT, "frontend/enderun-ai");

const FRONTEND_DIRS = ["app", "components", "services", "lib", "hooks"];
const SKIP_DIRS = new Set(["node_modules", ".next", "dist", "build", "tests"]);

/**
 * Bir segmentten `${...}` yer tutucularını siler, geriye sabit
 * metni bırakır.
 *
 * SÜSLÜ PARANTEZLER SAYILIYOR: `${query({ companyId })}` içinde iç
 * içe parantez var. Basit bir `\$\{[^}]*\}` ilk kapanışta durup
 * geriye ")}" bırakıyor, segment bozuluyor ve `isg/dashboard` gibi
 * gerçekten çağrılan uçlar "ekranı yok" diye listeye düşüyordu.
 */
function stripPlaceholders(segment) {
  let result = "";
  let depth = 0;

  for (let index = 0; index < segment.length; index += 1) {
    if (segment[index] === "$" && segment[index + 1] === "{") {
      depth += 1;
      index += 1;
      continue;
    }

    if (depth > 0) {
      if (segment[index] === "{") depth += 1;
      else if (segment[index] === "}") depth -= 1;
      continue;
    }

    result += segment[index];
  }

  return result.trim();
}

/** Yolu karşılaştırılabilir hale getirir: parametreler yıldız olur. */
function normalizePath(raw) {
  return raw
    .replace(/^\/+/, "")
    .replace(/^api\/backend\//, "")
    .replace(/^api\//, "")
    .split("?")[0]
    .split("#")[0]
    .split("/")
    .filter((segment) => segment.length > 0)
    .map((segment) => {
      // {id}, {id:guid} → * (backend yol parametresi)
      if (/^\{.*\}$/.test(segment)) return "*";

      // ${id} gibi yer tutucular segmentten SİLİNİR, segmentin
      // tamamı yıldıza çevrilmez: `branches${query}` yalnızca
      // sorgu dizgisi ekliyor, uç hâlâ "branches". Tamamını yıldız
      // saymak bu tür kök uçları "çağrılmıyor" gösteriyordu.
      const literal = stripPlaceholders(segment);

      if (literal.length === 0) return "*";
      if (!/^[a-zA-Z0-9._-]+$/.test(literal)) return "*";

      return literal.toLowerCase();
    })
    .join("/");
}

function walk(dir, extensions, files = []) {
  let entries;
  try {
    entries = readdirSync(dir);
  } catch {
    return files;
  }

  for (const entry of entries) {
    if (SKIP_DIRS.has(entry)) continue;
    const full = join(dir, entry);
    const stat = statSync(full);
    if (stat.isDirectory()) walk(full, extensions, files);
    else if (extensions.some((ext) => entry.endsWith(ext))) files.push(full);
  }

  return files;
}

// ---------------------------------------------------------------- backend

/**
 * Controller dosyasından uçları çıkarır.
 *
 * Sınıf düzeyi [Route("api/...")] taban yol; metot düzeyi
 * [HttpGet("alt")] ona eklenir. Bütün controller'lar açık yol
 * yazıyor ([controller] jetonu kullanılmıyor), o yüzden tahmin yok.
 */
function readBackendEndpoints() {
  const endpoints = [];

  for (const file of walk(BACKEND, [".cs"])) {
    const lines = readFileSync(file, "utf8").split("\n");
    const relativeFile = relative(ROOT, file);

    let baseRoute = null;
    let pendingHttp = null;

    for (let index = 0; index < lines.length; index += 1) {
      const line = lines[index];

      const routeMatch = line.match(/^\s*\[Route\("([^"]+)"\)\]/);
      const httpMatch = line.match(
        /^\s*\[Http(Get|Post|Put|Delete|Patch)(?:\("([^"]*)"\))?\]/
      );

      if (httpMatch) {
        pendingHttp = {
          method: httpMatch[1].toUpperCase(),
          suffix: httpMatch[2] ?? "",
          line: index + 1,
        };
        continue;
      }

      if (routeMatch) {
        // Metot düzeyi [Route] bekleyen bir [Http*] varsa ona ait.
        if (pendingHttp) pendingHttp.suffix = routeMatch[1];
        else baseRoute = routeMatch[1];
        continue;
      }

      // Metot imzası: bekleyen [Http*] burada kapanır.
      if (pendingHttp && /\b(public|internal)\s/.test(line)) {
        // MUTLAK METOT ROTASI: [Route("/api/...")] eğik çizgiyle
        // başlıyorsa sınıfın taban yolunu EZER (ASP.NET kuralı).
        // Birleştirilseydi uç "api/x//api/y" gibi var olmayan bir
        // yola çıkar ve ekranı olduğu halde "çağrılmıyor" sayılırdı.
        const full = pendingHttp.suffix.startsWith("/")
          ? pendingHttp.suffix
          : [baseRoute ?? "", pendingHttp.suffix]
              .filter((part) => part.length > 0)
              .join("/");

        const name = line.match(/\s([A-Za-z_][A-Za-z0-9_]*)\s*\(/);

        endpoints.push({
          method: pendingHttp.method,
          route: full.replace(/^\/+/, ""),
          path: normalizePath(full),
          file: relativeFile,
          line: pendingHttp.line,
          action: name ? name[1] : "?",
        });

        pendingHttp = null;
      }
    }
  }

  return endpoints;
}

// --------------------------------------------------------------- frontend

/**
 * Dosyadaki dizgi sabitlerini toplar.
 *
 * Servislerin neredeyse tamamı `const root = "hr/recruitment"` yazıp
 * çağrılarda `${root}/postings` kullanıyor. Sabit çözülmezse yol
 * "*\/postings" olarak görünür, gerçek ucun segment sayısını tutmaz
 * ve kullanılan bir uç "çağrılmıyor" diye listeye düşerdi.
 */
function readStringConstants(source) {
  const constants = new Map();
  const pattern = /(?:const|let)\s+([A-Za-z_$][\w$]*)\s*(?::[^=]+)?=\s*(["'`])([^"'`]*)\2/g;

  let match;
  while ((match = pattern.exec(source)) !== null) {
    constants.set(match[1], match[3]);
  }

  // Kök yolu fonksiyonla üretenler de var:
  //   function root(projectId) { return `projects/${projectId}/documents`; }
  // Gövdesi tek bir dizgi dönüşünden ibaretse değeri sabit sayılır.
  const functionPattern =
    /function\s+([A-Za-z_$][\w$]*)\s*\([^)]*\)\s*\{\s*return\s+(["'`])([^"'`]*)\2\s*;?\s*\}/g;

  while ((match = functionPattern.exec(source)) !== null) {
    constants.set(match[1], match[3]);
  }

  return constants;
}

/** `${root}` gibi yer tutucuları bilinen sabitlerle doldurur. */
function resolveConstants(raw, constants) {
  let value = raw;

  // Sabitler birbirine dayanabiliyor; birkaç tur yeter, sonsuz döngü yok.
  for (let round = 0; round < 3; round += 1) {
    const next = value
      .replace(/\$\{([A-Za-z_$][\w$]*)\}/g, (whole, name) =>
        constants.has(name) ? constants.get(name) : whole
      )
      // `${root(projectId)}` — argüman önemsiz, dönen yol önemli.
      .replace(/\$\{([A-Za-z_$][\w$]*)\([^)]*\)\}/g, (whole, name) =>
        constants.has(name) ? constants.get(name) : whole
      );

    if (next === value) break;
    value = next;
  }

  return value;
}

/**
 * Bir çağrının HTTP metodunu belirler.
 *
 * Yolu içeren ÇAĞRININ kendi parantez aralığında `method: "X"`
 * aranır; yoksa GET (istemcilerin varsayılanı).
 *
 * NEDEN SATIR PENCERESİ DEĞİL: "sonraki birkaç satıra bak"
 * yaklaşımı bir sonraki çağrının metodunu bu yola yapıştırıyordu.
 * Kod tabanında `apiClient` dışında `request()` gibi başka
 * yardımcılar da var, o yüzden "yeni çağrı görünce kes" kuralı da
 * yetmedi. Parantez aralığı tek doğru sınır.
 */
function readHelperMethods(source) {
  const helpers = new Map();
  const pattern =
    /(?:async\s+)?function\s+([A-Za-z_$][\w$]*)|(?:const|let)\s+([A-Za-z_$][\w$]*)\s*=\s*(?:async\s*)?\(/g;

  let match;
  while ((match = pattern.exec(source)) !== null) {
    const name = match[1] ?? match[2];

    // Gövdenin başından kısa bir pencere: yardımcı fetch'i hemen
    // kuruyor. Bulunamazsa yardımcı kaydedilmiyor, çağrı yerindeki
    // kural geçerli kalıyor.
    const body = source.slice(match.index, match.index + 600);
    const methodMatch = body.match(
      /method:\s*["'](GET|POST|PUT|DELETE|PATCH)["']/i
    );

    if (methodMatch) helpers.set(name, methodMatch[1].toUpperCase());
  }

  return helpers;
}

function methodInCall(source, position, helpers) {
  let openIndex = -1;
  let depth = 0;

  for (let index = position - 1; index >= 0; index -= 1) {
    const character = source[index];
    if (character === ")") depth += 1;
    else if (character === "(") {
      if (depth === 0) {
        openIndex = index;
        break;
      }
      depth -= 1;
    }
  }

  if (openIndex === -1) return "GET";

  depth = 0;
  let closeIndex = source.length;

  for (let index = openIndex; index < source.length; index += 1) {
    const character = source[index];
    if (character === "(") depth += 1;
    else if (character === ")") {
      depth -= 1;
      if (depth === 0) {
        closeIndex = index;
        break;
      }
    }
  }

  const methodMatch = source
    .slice(openIndex, closeIndex)
    .match(/method:\s*["'](GET|POST|PUT|DELETE|PATCH)["']/i);

  if (methodMatch) return methodMatch[1].toUpperCase();

  // YARDIMCI FONKSİYONLAR: `upload(path, file)` çağrısında metot
  // görünmüyor, POST'u yardımcının GÖVDESİ kuruyor. Çağrıya bakıp
  // GET demek bütün dosya yükleme uçlarını "POST hiç çağrılmıyor"
  // diye yanlış işaretliyordu.
  const callee = source
    .slice(Math.max(0, openIndex - 80), openIndex)
    .match(/([A-Za-z_$][\w$]*)\s*(?:<[^<>]*>\s*)?$/);

  if (callee && helpers.has(callee[1])) return helpers.get(callee[1]);

  return "GET";
}

/**
 * Frontend kaynağındaki bütün yol benzeri dizgileri toplar.
 *
 * Çağrı biçimini ayrıştırmıyor: tırnaklı ve ters tırnaklı HER dizgi
 * alınıyor. Böylece apiClient dışındaki çağrı yolları (sayfa içi
 * fetch, yerel api() yardımcısı) da kapsama giriyor.
 */
function readFrontendReferences(rootSegments) {
  const references = new Map(); // normalized path -> [{file, line, method}]

  for (const dir of FRONTEND_DIRS) {
    for (const file of walk(join(FRONTEND, dir), [".ts", ".tsx"])) {
      const source = readFileSync(file, "utf8");
      const lines = source.split("\n");
      const relativeFile = relative(ROOT, file);
      const constants = readStringConstants(source);
      const helpers = readHelperMethods(source);

      // ÇIPLAK DEĞİŞKENLE ÇAĞRI: `apiClient(root)` biçiminde hiç
      // dizgi yok. Sabit bir argüman olarak geçiriliyorsa değerinin
      // kendisi de çağrılmış bir yoldur; sayılmazsa "branches",
      // "cheques" gibi kök uçlar ekranı olduğu halde listeye düşer.
      for (const [name, value] of constants) {
        if (!value.includes("/") && !/^[a-z][a-z0-9-]*$/i.test(value)) continue;

        const path = normalizePath(value);
        if (path.length === 0) continue;

        const passedAsArgument = new RegExp(
          `\\(\\s*${name.replace(/\$/g, "\\$")}\\s*[,)]`,
          "g"
        );

        let call;
        while ((call = passedAsArgument.exec(source)) !== null) {
          // Metot ÇAĞRI YERİNDEN okunuyor. Sabit çağrılarına
          // körlemesine GET atanınca `apiClient(root, {method:"POST"})`
          // biçimindeki oluşturma uçları "POST hiç çağrılmıyor" diye
          // yanlış işaretleniyordu.
          const line = source.slice(0, call.index).split("\n").length - 1;

          if (!references.has(path)) references.set(path, []);
          references.get(path).push({
            file: relativeFile,
            line: line + 1,
            method: methodInCall(source, call.index + 1, helpers),
          });
        }
      }

      // DİZGİLER SATIR SATIR DEĞİL, KAYNAK ÜZERİNDEN ÇIKARILIYOR:
      // uzun çağrılar şablon dizgisini satır ortasında bölüyor
      // (`accounting-reports/general-ledger${buildQuery(` gibi).
      // Satır bazlı tarama böyle bir dizgiyi hiç göremiyor ve
      // ekranı olan uç "çağrılmıyor" diye listeye düşüyordu.
      const literalPattern = /"[^"\n]*"|'[^'\n]*'|`[^`]*`/g;
      let literalMatch;

      while ((literalMatch = literalPattern.exec(source)) !== null) {
        const index = source.slice(0, literalMatch.index).split("\n").length - 1;

        {
          const raw = resolveConstants(literalMatch[0].slice(1, -1), constants);
          if (raw.length === 0) continue;
          // `$` de kabul: servislerin çoğu `${root}/alt-yol` yazıyor.
          if (!/^[$/a-zA-Z]/.test(raw)) continue;

          // TEK SEGMENTLİK YOLLAR: `branches${query}` gibi çağrılarda
          // eğik çizgi yok. Bunları büsbütün elemek kök uçları
          // ("api/branches") yanlışlıkla listeye düşürüyordu; hepsini
          // almak ise rastgele arayüz metinlerini yol sayardı. Orta
          // yol: yalnız GERÇEK bir backend kök segmentiyse sayılır.
          if (!raw.includes("/") && !raw.includes("?")) {
            const candidate = normalizePath(raw);
            if (!rootSegments.has(candidate)) continue;
          }

          const path = normalizePath(raw);
          if (path.length === 0) continue;

          // BİLGİ TAŞIMAYAN REFERANS ATILIR: çözülemeyen değişkenlerden
          // oluşan "*/*" gibi bir yol, aynı segment sayısındaki HER ucu
          // "çağrılıyor" gösterir ve gerçek boşlukları örter. En az bir
          // gerçek segment şart.
          if (!path.split("/").some((segment) => segment !== "*")) continue;

          if (!references.has(path)) references.set(path, []);
          references.get(path).push({
            file: relativeFile,
            line: index + 1,
            method: methodInCall(source, literalMatch.index, helpers),
          });
        }
      }
    }
  }

  return references;
}

/**
 * Yıldızın gerçek segmentleri de karşıladığı gevşek eşleşme.
 *
 * İKİ KURAL BİRDEN GEREKİYOR, çünkü ikisinin de kör noktası var:
 * - Katı (birebir) eşleşme `secretariat/${path}` gibi fabrika
 *   servislerini göremez; ekranı olan uçları listeye düşürür.
 * - Gevşek eşleşme ise `hakedis/${id}` referansının hiç çağrılmayan
 *   `hakedis/upload` ucunu örtmesine izin verir.
 * Bu yüzden rapor iki kademeli: gevşek eşleşme bile yoksa KESİN,
 * yalnız gevşek eşleşme varsa ŞÜPHELİ.
 */
function looselyMatches(routePath, referencePath) {
  const route = routePath.split("/");
  const reference = referencePath.split("/");
  if (route.length !== reference.length) return false;

  return route.every(
    (segment, index) =>
      segment === "*" || reference[index] === "*" || segment === reference[index]
  );
}

// ------------------------------------------------------------------ rapor

const endpoints = readBackendEndpoints();

// Tek segmentlik frontend dizgilerini süzmek için gerçek uç kökleri.
const rootSegments = new Set(
  endpoints.map((endpoint) => endpoint.path.split("/")[0])
);

const references = readFrontendReferences(rootSegments);
const referenceEntries = [...references.entries()];

const unused = [];
const suspect = [];
const methodMismatch = [];

for (const endpoint of endpoints) {
  const loose = referenceEntries.filter(([path]) =>
    looselyMatches(endpoint.path, path)
  );

  if (loose.length === 0) {
    unused.push(endpoint);
    continue;
  }

  const exact = referenceEntries.filter(([path]) => path === endpoint.path);

  if (exact.length === 0) {
    suspect.push(endpoint);
    continue;
  }

  // Metot yalnız BİREBİR eşleşen referanslardan okunuyor; yıldızla
  // gelen eşleşmenin metodu başka bir ucun olabilir.
  const methods = new Set(
    exact.flatMap(([, hits]) => hits.map((hit) => hit.method))
  );

  if (!methods.has(endpoint.method)) {
    methodMismatch.push({
      ...endpoint,
      seen: [...methods].sort().join(", "),
    });
  }
}

if (process.argv.includes("--json")) {
  console.log(
    JSON.stringify(
      { total: endpoints.length, unused, suspect, methodMismatch },
      null,
      2
    )
  );
  process.exit(0);
}

function printByFile(title, items) {
  if (items.length === 0) return;

  const byFile = new Map();
  for (const endpoint of items) {
    if (!byFile.has(endpoint.file)) byFile.set(endpoint.file, []);
    byFile.get(endpoint.file).push(endpoint);
  }

  console.log(`\n== ${title} ==`);
  for (const [file, entries] of [...byFile.entries()].sort()) {
    console.log(`\n${file}`);
    for (const entry of entries.sort((a, b) => a.line - b.line)) {
      console.log(
        `  ${entry.method.padEnd(6)} ${entry.route}` +
          `  (${entry.action}, satır ${entry.line})`
      );
    }
  }
}

console.log(`Toplam uç: ${endpoints.length}`);
console.log(`Hiçbir referansla eşleşmiyor (kesin): ${unused.length}`);
console.log(`Yalnız değişken yollu referansla eşleşiyor (şüpheli): ${suspect.length}`);
console.log(`Yol çağrılıyor ama bu metot çağrılmıyor: ${methodMismatch.length}`);

printByFile("EKRANDAN ÇAĞRILMAYAN UÇLAR (kesin)", unused);
printByFile(
  "ŞÜPHELİ — yalnız değişken yollu bir referansla eşleşiyor, gözle bakılmalı",
  suspect
);

if (methodMismatch.length > 0) {
  console.log("\n== YOL ÇAĞRILIYOR AMA BU METOT ÇAĞRILMIYOR ==");
  for (const item of methodMismatch.sort((a, b) =>
    a.file.localeCompare(b.file)
  )) {
    console.log(
      `  ${item.method.padEnd(6)} ${item.route}` +
        `  — frontend'de görülen: ${item.seen}` +
        `  (${item.file}:${item.line})`
    );
  }
}
