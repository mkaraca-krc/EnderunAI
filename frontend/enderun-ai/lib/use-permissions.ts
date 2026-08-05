"use client";

import { useCallback, useMemo } from "react";

import { useCurrentUser } from "@/lib/use-current-user";

/**
 * Oturumdaki kullanıcının izin kontrolü.
 *
 * Admin ve Genel Müdür her izne sahip sayılır — menü filtresi
 * (erp-shell.tsx) da aynı kuralı uyguluyor; iki yerde farklı davranırsa
 * menüde görünen ekranda buton kaybolur.
 *
 * Bu yalnızca ARAYÜZ kolaylığı: gerçek yetki kontrolü uçlarda
 * RequirePermission ile yapılır. Buradaki bir hata veriyi açığa
 * çıkarmaz, yalnızca kullanıcıya işe yaramayan bir buton gösterir.
 */
export function usePermissions() {
  const { user, loading } = useCurrentUser();

  const granted = useMemo(() => new Set(user?.permissions ?? []), [user]);

  const isSuperUser = useMemo(
    () =>
      Boolean(
        user?.roles?.includes("Admin") || user?.roles?.includes("Genel Müdür")
      ),
    [user]
  );

  const has = useCallback(
    (permission: string) => isSuperUser || granted.has(permission),
    [granted, isSuperUser]
  );

  return { has, loading, user };
}
