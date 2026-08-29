"use client";

import { Component, type ErrorInfo, type ReactNode } from "react";

/**
 * HATA SINIRI — BEYAZ EKRANIN PANZEHİRİ.
 *
 * NEDEN VAR: uygulamada bugüne kadar HİÇBİR hata sınırı yoktu
 * (`app/error.tsx` de yok). React, render sırasında hata alan ağacı
 * KÖKÜNDEN söküyor; yakalayan kimse olmayınca geriye boş bir `<div>`
 * kalıyor. Kullanıcı ne olduğunu görmüyor, ne yapacağını bilmiyor ve
 * elinde yalnızca sayfayı yenilemek kalıyor.
 *
 * Somut örnek ÖP/1b sırasında çıktı: kabuk `currentUser?.roles[0]`
 * yazıyordu ve oturum beklenen şekilde gelmediğinde YAN MENÜDEKİ TEK
 * SATIR bütün uygulamayı düşürüyordu. Kabuk her ekranı sardığı için
 * çöktüğünde açık kalan tek bir sayfa bile olmuyor.
 *
 * SINIF BİLEŞENİ ZORUNLU: `getDerivedStateFromError` ve
 * `componentDidCatch` kancalarla yazılamıyor. Bu, React'in kendi
 * sınırı — tercih değil.
 *
 * NE YAKALAMAZ (bilerek yazılıyor, sonra "neden çalışmadı" denmesin):
 * olay işleyicilerindeki hatalar, `setTimeout` içindekiler, sunucu
 * tarafı render ve sınırın KENDİ render'ı. Olay işleyicileri zaten
 * `try/catch` ile ekranda hata gösteriyor.
 */

type Props = {
  children: ReactNode;
  /** Kayda ve ekrana giden yer adı: "kabuk" ya da "içerik". */
  nerede: string;
  /**
   * Hata ekranının biçimi. `tam` bütün sayfayı kaplar (kabuk çöktü,
   * yan menü de yok); `govde` kabuğun içeriğinin yerine geçer.
   */
  bicim: "tam" | "govde";
  /** Hata bildirimi. Vermeyen çağıran yalnızca ekranı alır. */
  onHata?: (bilgi: {
    nerede: string;
    hataAdi: string;
    mesaj: string;
    yol: string;
  }) => void;
};

type State = { hata: Error | null };

export class HataSiniri extends Component<Props, State> {
  state: State = { hata: null };

  static getDerivedStateFromError(hata: Error): State {
    return { hata };
  }

  componentDidCatch(hata: Error, bilgi: ErrorInfo) {
    /*
     * KAYDA NE GİDER: yalnız YAPISAL bilgi.
     *
     * Kullanıcı adı, e-posta, tutar, IBAN, cari unvanı GÖNDERİLMEZ.
     * Kullanıcının kim olduğunu sunucu zaten oturumdan biliyor;
     * istemcinin ayrıca göndermesi hem gereksiz hem de günlüğe
     * kişisel veri taşıma yolu açardı.
     *
     * Mesaj yine de kısaltılıyor: bir servis hatası iş metnini
     * mesajın içinde taşıyabilir ve o metin ekrandan günlüğe
     * sızabilir. Yapısal React hataları zaten kısadır; kısaltmanın
     * bedeli yok, kazancı var.
     *
     * BİLEŞEN YIĞINI GÖNDERİLMİYOR: `bilgi.componentStack` yalnız
     * tarayıcı konsoluna yazılıyor. Sunucuya gitseydi dosya yolları
     * ve bileşen adlarıyla birlikte uzun bir metin günlüğe düşerdi;
     * hata ayıklamada işe yarayan yeri konsol.
     */
    // eslint-disable-next-line no-console
    console.error(`[${this.props.nerede}] hata sınırı yakaladı`, hata, bilgi);

    this.props.onHata?.({
      nerede: this.props.nerede,
      hataAdi: hata.name || "Error",
      mesaj: (hata.message || "").slice(0, 200),
      yol: typeof window === "undefined" ? "" : window.location.pathname,
    });
  }

  private yenidenDene = () => {
    /*
     * DURUMU SIFIRLA, SAYFAYI YENİLEME.
     *
     * `window.location.reload()` açık paneli, filtreyi ve kaydırma
     * konumunu siler — kullanıcıyı yaptığı işin başına döndürür.
     * Hata geçiciyse (bir isteğin dönmemesi gibi) yeniden render
     * yetiyor; kalıcıysa ekran zaten tekrar hata ekranına düşer ve
     * kullanıcı bunu görür.
     */
    this.setState({ hata: null });
  };

  render() {
    const { hata } = this.state;

    if (!hata) return this.props.children;

    const tam = this.props.bicim === "tam";

    return (
      <div
        role="alert"
        style={{
          display: "flex",
          flexDirection: "column",
          alignItems: "center",
          justifyContent: "center",
          gap: "12px",
          padding: "40px 24px",
          textAlign: "center",
          minHeight: tam ? "100vh" : "320px",
        }}
      >
        <strong style={{ fontSize: "18px" }}>Bir şeyler ters gitti</strong>

        <p style={{ maxWidth: "460px", lineHeight: 1.5 }}>
          {tam
            ? "Ekran açılamadı. Yeniden deneyebilir ya da sayfayı yenileyebilirsiniz."
            : "Bu bölüm açılamadı. Menüden başka bir ekrana geçebilir ya da yeniden deneyebilirsiniz."}
        </p>

        {/*
          HATA METNİ KULLANICIYA GÖSTERİLİYOR ama küçük ve ikincil.
          Gizlenseydi kullanıcı telefonda "hata veriyor" demekten
          başka bir şey söyleyemezdi; öne çıkarılsaydı ekran teknik
          bir çöp yığınına dönerdi.
        */}
        <small style={{ opacity: 0.7, maxWidth: "460px", wordBreak: "break-word" }}>
          {hata.name}: {(hata.message || "").slice(0, 200)}
        </small>

        <button
          type="button"
          className="erp-secondary-button"
          onClick={this.yenidenDene}
        >
          Yeniden Dene
        </button>
      </div>
    );
  }
}
