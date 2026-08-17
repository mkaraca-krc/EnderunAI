"use client";

import { useEffect, useState } from "react";

import { apiClient } from "@/lib/api/api-client";

export type CurrentUser = {
  id?: string | null;
  username?: string | null;
  fullName?: string | null;
  /** Kullanıcının kendi seçtiği hitap: "Bey" | "Hanım" | null. */
  honorific?: string | null;
  roles: string[];
  permissions: string[];

  /**
   * Katalogdaki her izne sahip mi. Backend'den gelir (/auth/me);
   * arayüz süper kullanıcıyı ROL ADINDAN değil bu bayraktan tanır.
   */
  hasAllPermissions?: boolean;
};

/**
 * PAYLAŞILAN İSTEK.
 *
 * Bu kanca her örnekte kendi `auth/me` isteğini atıyordu. Eleman
 * seviyesi yetki (R2) yayıldıkça bir sayfada üç dört örnek olması
 * normal hale geldi — ekranın modülü dışında bir izin isteyen düğme
 * ikinci bir `useModuleActions` çağrısı doğuruyor. Sonuç, tek sayfa
 * açılışında aynı yanıtı getiren birkaç eşzamanlı istekti.
 *
 * Söz (promise) modül düzeyinde tutuluyor; ilk çağıran isteği başlatır,
 * diğerleri aynı sözü bekler.
 *
 * YALNIZCA BAŞARILI YANIT ÖNBELLEKLENİR. 401 önbelleklenirse giriş
 * sonrası kullanıcı hâlâ oturumsuz görünürdü: giriş `router.push` ile
 * yapılıyor, yani modül durumu sıfırlanmıyor.
 */
let sessionRequest: Promise<CurrentUser | null> | null = null;

function loadSession() {
  if (!sessionRequest) {
    sessionRequest = apiClient<CurrentUser>("auth/me").catch(() => {
      // Başarısız istek önbellekte kalmaz; sonraki çağıran yeniden dener.
      sessionRequest = null;
      return null;
    });
  }

  return sessionRequest;
}

/**
 * Oturum bilgisini bir sonraki okumada yeniden getirtir.
 *
 * Giriş ve çıkış AKIŞLARI ÇAĞIRMAK ZORUNDA: ikisi de `router.push` /
 * `router.replace` kullanıyor, tam sayfa yüklemesi olmadığı için
 * modül düzeyindeki önbellek kendiliğinden temizlenmiyor.
 */
export function clearCurrentUserCache() {
  sessionRequest = null;
}

/**
 * Oturumdaki gerçek kullanıcı. Karşılama metinleri ve kişiselleştirme
 * bu kaynaktan beslenir — hiçbir ekranda isim sabit yazılmaz.
 */
export function useCurrentUser() {
  const [user, setUser] = useState<CurrentUser | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    let active = true;

    loadSession()
      .then((session) => {
        if (active) setUser(session);
      })
      .catch(() => {
        if (active) setUser(null);
      })
      .finally(() => {
        if (active) setLoading(false);
      });

    return () => {
      active = false;
    };
  }, []);

  return { user, loading };
}
