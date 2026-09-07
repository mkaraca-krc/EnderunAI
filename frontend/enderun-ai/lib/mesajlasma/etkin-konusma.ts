"use client";

/**
 * EKRANDA ETKİN KONUŞMA — SESİN ÇALIP ÇALMAYACAĞININ TEK KAYNAĞI.
 *
 * ═══ NEDEN AYRI BİR MODÜL ═══
 *
 * Karar veren yer baloncuk (`MesajBaloncugu`), bilen yer ise panel
 * (`MesajPaneli`). İkisi kardeş; biri diğerinin durumunu göremiyor.
 * Ve panel İKİ yerde yaşıyor: baloncuğun içinde ve `/mesajlar` tam
 * sayfasında. Prop zinciriyle taşımak, tam sayfa için ayrı bir yol
 * daha açmak demekti — AYRIŞAN HER NOKTA, BİRİNİN SINAMADIĞI BİR
 * NOKTADIR.
 *
 * ═══ NEDEN REACT DURUMU DEĞİL ═══
 *
 * Bu değer render'ı etkilemiyor; yalnız bir olay geldiğinde
 * OKUNUYOR. Durum olsaydı her konuşma seçiminde ağacın yeniden
 * çizilmesine sebep olurdu ve hiçbir görsel karşılığı olmazdı.
 *
 * ═══ GÖRÜNÜRLÜK DE BURADA ═══
 *
 * "Kullanıcı zaten bakıyor" iki şart: doğru konuşma AÇIK **ve**
 * sekme GÖRÜNÜR. Başka sekmedeyken ses çalmalı (B2) — konuşma açık
 * kalsa bile.
 */

let etkin: string | null = null;

export function etkinKonusmayiYaz(konusmaId: string | null): void {
  etkin = konusmaId;
}

export function etkinKonusma(): string | null {
  return etkin;
}

/**
 * Kullanıcı bu konuşmaya BAKIYOR mu.
 *
 * `document.visibilityState` sekme arkada olduğunda "hidden" döner.
 * Sunucu tarafında (`document` yok) `false` dönüyor: bilmiyorsak
 * "bakıyor" varsaymak sesi yanlışlıkla susturur.
 */
export function konusmayaBakiliyor(konusmaId: string): boolean {
  if (typeof document === "undefined") return false;
  if (document.visibilityState !== "visible") return false;
  return etkin === konusmaId;
}
