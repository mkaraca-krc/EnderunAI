/**
 * DÜZEN RIG'İNİN VEKİLİ — ÜRETİMDEKİ nginx'İN KARŞILIĞI.
 *
 * ═══ NEDEN VAR ═══
 *
 * Üretimde `/api/hubs/` nginx tarafından DOĞRUDAN arka uca
 * yönlendiriliyor ve WebSocket yükseltmesi orada geçiyor. Rig'de
 * nginx yok: Next sunucusuna vuran SignalR isteği hiçbir yere
 * ulaşmıyordu ve canlı akış HİÇ KURULMUYORDU.
 *
 * Sonuç sessizdi: ses testi "ses çalmadı" diyordu, sebep ses
 * kararı değil BAĞLANTININ HİÇ OLMAMASIYDI. Rig üretime
 * benzemiyorsa, kırmızısı da yeşili de yalan söyler (Kural 81).
 *
 * ═══ NE YAPIYOR ═══
 *
 *   /api/hubs/*  → arka uç (WebSocket yükseltmesi dahil)
 *   diğer her şey → Next
 *
 * nginx'teki `location ^~ /api/hubs/` bloğunun aynı ayrımı.
 */
import http from "node:http";
import net from "node:net";

const DINLE = Number(process.env.VEKIL_PORT ?? 3002);
const NEXT = Number(process.env.VEKIL_NEXT_PORT ?? 3001);
const ARKA = Number(process.env.VEKIL_ARKA_PORT ?? 5156);

const HUB_ONEK = "/api/hubs/";

const hedefPort = (url) => (url.startsWith(HUB_ONEK) ? ARKA : NEXT);

const sunucu = http.createServer((istek, cevap) => {
  const port = hedefPort(istek.url ?? "/");

  const ileri = http.request(
    {
      host: "127.0.0.1",
      port,
      method: istek.method,
      path: istek.url,
      headers: istek.headers,
    },
    (yanit) => {
      cevap.writeHead(yanit.statusCode ?? 502, yanit.headers);
      yanit.pipe(cevap);
    }
  );

  ileri.on("error", () => {
    if (!cevap.headersSent) cevap.writeHead(502);
    cevap.end("vekil: hedefe ulasilamadi");
  });

  istek.pipe(ileri);
});

/*
 * WEBSOCKET YÜKSELTMESİ — ASIL SEBEP.
 *
 * `upgrade` olayı yakalanmazsa Node bağlantıyı düşürür ve SignalR
 * sessizce LongPolling'e iner; test "çalışıyor" görünürken ölçtüğü
 * şey değişmiş olurdu.
 */
sunucu.on("upgrade", (istek, soket, bas) => {
  const port = hedefPort(istek.url ?? "/");

  const yukari = net.connect(port, "127.0.0.1", () => {
    const satirlar = [
      `${istek.method} ${istek.url} HTTP/1.1`,
      ...Object.entries(istek.headers).map(([k, v]) => `${k}: ${v}`),
      "",
      "",
    ].join("\r\n");

    yukari.write(satirlar);
    if (bas && bas.length) yukari.write(bas);
    yukari.pipe(soket);
    soket.pipe(yukari);
  });

  yukari.on("error", () => soket.destroy());
  soket.on("error", () => yukari.destroy());
});

sunucu.listen(DINLE, "127.0.0.1", () => {
  console.log(`[duzen-vekil] ${DINLE} → next ${NEXT}, hub ${ARKA}`);
});
