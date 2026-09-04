"use client";

import { FormEvent, useState } from "react";

import ErpShell from "@/components/erp/erp-shell";

/**
 * PAROLA DEĞİŞTİRME EKRANI.
 *
 * Sistemde bir kullanıcının kendi parolasını değiştirmesi ilk kez
 * mümkün: önceden tek yol yönetici sıfırlamasıydı.
 *
 * ── SUNUCU DOĞRULAMASI ESAS, BURADAKİ KOLAYLIK ──
 *
 * Uzunluk ve eşleşme kontrolü burada da var ama garanti sunucuda.
 * Buradaki kontrol yalnız kullanıcıyı bir gidiş-dönüşten kurtarıyor.
 */
const ASGARI_UZUNLUK = 12;

export default function ParolaSayfasi() {
  const [mevcut, setMevcut] = useState("");
  const [yeni, setYeni] = useState("");
  const [tekrar, setTekrar] = useState("");
  const [kaydediliyor, setKaydediliyor] = useState(false);
  const [hata, setHata] = useState("");
  const [basari, setBasari] = useState("");

  async function gonder(event: FormEvent) {
    event.preventDefault();
    setHata("");
    setBasari("");

    if (yeni.length < ASGARI_UZUNLUK) {
      setHata(`Yeni parola en az ${ASGARI_UZUNLUK} karakter olmalıdır.`);
      return;
    }

    if (yeni !== tekrar) {
      setHata("Yeni parolalar birbiriyle eşleşmiyor.");
      return;
    }

    setKaydediliyor(true);

    try {
      const yanit = await fetch("/api/auth/change-password", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        cache: "no-store",
        credentials: "same-origin",
        body: JSON.stringify({
          currentPassword: mevcut,
          newPassword: yeni,
          newPasswordConfirm: tekrar,
        }),
      });

      const sonuc = await yanit.json().catch(() => null);

      if (!yanit.ok) {
        setHata(sonuc?.message ?? "Parola değiştirilemedi.");
        return;
      }

      setBasari(
        sonuc?.message ??
          "Parola değiştirildi. Diğer oturumlarınız sonlandırıldı."
      );

      // ALANLAR TEMİZLENİYOR: parola metin kutusunda kalmasın.
      setMevcut("");
      setYeni("");
      setTekrar("");
    } catch {
      setHata("Parola servisine ulaşılamadı.");
    } finally {
      setKaydediliyor(false);
    }
  }

  return (
    <ErpShell title="Parola Değiştir" design="redwood">
      <div className="mx-auto max-w-lg">
        <h1 className="text-xl font-semibold text-slate-900">
          Parola Değiştir
        </h1>
        <p className="mt-1 text-sm text-slate-500">
          Parolanızı değiştirdiğinizde{" "}
          <strong>diğer cihazlardaki oturumlarınız sonlandırılır</strong>.
          Bu cihazdaki oturumunuz açık kalır.
        </p>

        {hata && (
          <div className="mt-4 rounded-lg bg-rose-50 px-4 py-3 text-sm text-rose-700">
            {hata}
          </div>
        )}

        {basari && (
          <div className="mt-4 rounded-lg bg-emerald-50 px-4 py-3 text-sm text-emerald-700">
            {basari}
          </div>
        )}

        <form onSubmit={gonder} className="mt-6 space-y-4">
          <div>
            <label
              htmlFor="mevcut-parola"
              className="block text-sm font-medium text-slate-700"
            >
              Mevcut parola
            </label>
            <input
              id="mevcut-parola"
              type="password"
              autoComplete="current-password"
              required
              value={mevcut}
              onChange={(e) => setMevcut(e.target.value)}
              className="mt-1 w-full rounded-lg border border-slate-300 px-3 py-2 text-sm"
            />
          </div>

          <div>
            <label
              htmlFor="yeni-parola"
              className="block text-sm font-medium text-slate-700"
            >
              Yeni parola
            </label>
            <input
              id="yeni-parola"
              type="password"
              autoComplete="new-password"
              required
              minLength={ASGARI_UZUNLUK}
              value={yeni}
              onChange={(e) => setYeni(e.target.value)}
              className="mt-1 w-full rounded-lg border border-slate-300 px-3 py-2 text-sm"
            />
            <p className="mt-1 text-xs text-slate-500">
              En az {ASGARI_UZUNLUK} karakter.
            </p>
          </div>

          <div>
            <label
              htmlFor="yeni-parola-tekrar"
              className="block text-sm font-medium text-slate-700"
            >
              Yeni parola (tekrar)
            </label>
            <input
              id="yeni-parola-tekrar"
              type="password"
              autoComplete="new-password"
              required
              minLength={ASGARI_UZUNLUK}
              value={tekrar}
              onChange={(e) => setTekrar(e.target.value)}
              className="mt-1 w-full rounded-lg border border-slate-300 px-3 py-2 text-sm"
            />
          </div>

          <button
            type="submit"
            disabled={kaydediliyor}
            className="rounded-lg bg-brand-700 px-4 py-2 text-sm font-medium text-white hover:bg-brand-600 disabled:opacity-50"
          >
            {kaydediliyor ? "Kaydediliyor…" : "Parolayı Değiştir"}
          </button>
        </form>
      </div>
    </ErpShell>
  );
}
