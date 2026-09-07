"use client";

import {
  createContext,
  useCallback,
  useContext,
  useMemo,
  useState,
  type ReactNode,
} from "react";

/**
 * TASLAK DEPOSU — İKİ YÜZEY, TEK KAYNAK.
 *
 * ═══ DOĞURAN KUSUR (2026-09-07) ═══
 *
 * Mesajlaşmanın iki yazma kutusu var: yüzen panel ve `/mesajlar` tam
 * sayfası. Taslak panelde (`MesajBaloncugu`) tutuluyordu; tam sayfa
 * onu görmüyor, kendi yerel durumuna düşüyordu.
 *
 * Sonuç: panelde yazıp `/mesajlar`'a giden **yazdığını kaybediyordu.**
 *
 * **VE BU AZINLIĞIN YOLU DEĞİL:** dar pencerede ✉ düğmesi paneli
 * açmıyor, doğrudan `/mesajlar`'a götürüyor. Yani telefondan giren
 * herkes bu yoldan geçiyor — çoğunluğun yaşayacağı kayıp.
 *
 * ═══ NEDEN BAĞLAM (CONTEXT) ═══
 *
 * Depo kök layout'ta duruyor: hem baloncuk hem `/mesajlar` sayfası
 * onun altında. Panelde tutulsaydı panel kapanınca sökülürdü; sayfada
 * tutulsaydı rota değişiminde giderdi. Kök, ikisinin de üstünde olan
 * tek yer.
 *
 * ═══ KONUŞMA BAŞINA ═══
 *
 * `Record<konusmaId, metin>`. Kullanıcı A'ya yazıp B'ye geçip
 * dönünce A'daki durmalı; B'de A'nın metni GÖRÜNMEMELİ. Tek genel
 * alan ikinci şartı bozardı.
 */

interface TaslakDeposu {
  taslaklar: Record<string, string>;
  taslakYaz: (konusmaId: string, metin: string) => void;
}

const Baglam = createContext<TaslakDeposu | null>(null);

export function TaslakDeposuSaglayici({ children }: { children: ReactNode }) {
  const [taslaklar, setTaslaklar] = useState<Record<string, string>>({});

  const taslakYaz = useCallback((konusmaId: string, metin: string) => {
    setTaslaklar((mevcut) => ({ ...mevcut, [konusmaId]: metin }));
  }, []);

  const deger = useMemo(
    () => ({ taslaklar, taslakYaz }),
    [taslaklar, taslakYaz]
  );

  return <Baglam.Provider value={deger}>{children}</Baglam.Provider>;
}

/**
 * Depoyu okur.
 *
 * SAĞLAYICI YOKSA PATLAMAZ, YEREL DAVRANIR: bu bileşenler testlerde ve
 * ileride başka bağlamlarda sağlayıcısız da render edilebiliyor.
 * Patlamak, kullanıcıya mesajlaşmayı tamamen kaybettirirdi; yerel
 * davranmak yalnız paylaşımı kaybettirir.
 */
export function useTaslakDeposu(): TaslakDeposu | null {
  return useContext(Baglam);
}
