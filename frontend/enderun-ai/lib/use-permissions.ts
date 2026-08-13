"use client";

import { useCallback, useMemo } from "react";

import { useCurrentUser } from "@/lib/use-current-user";

/**
 * Oturumdaki kullanıcının izin kontrolü.
 *
 * SÜPER KULLANICI ROL ADINDAN DEĞİL, backend'in hasAllPermissions
 * bayrağından anlaşılır. Önce "Admin" / "Genel Müdür" adına
 * bakılıyordu; rol yeniden adlandırılsa ya da başka bir role tüm
 * izinler verilse arayüz yanlış davranırdı. Menü filtresi ve sayfa
 * kapısı da aynı bayrağı kullanıyor.
 *
 * Bu yalnızca ARAYÜZ kolaylığı: gerçek yetki kontrolü uçlarda
 * RequirePermission ile yapılır. Buradaki bir hata veriyi açığa
 * çıkarmaz, yalnızca kullanıcıya işe yaramayan bir buton gösterir.
 */
export function usePermissions() {
  const { user, loading } = useCurrentUser();

  const granted = useMemo(() => new Set(user?.permissions ?? []), [user]);

  const isSuperUser = user?.hasAllPermissions === true;

  const has = useCallback(
    (permission: string) => isSuperUser || granted.has(permission),
    [granted, isSuperUser]
  );

  return { has, loading, user };
}
