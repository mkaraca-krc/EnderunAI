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
};

/**
 * Oturumdaki gerçek kullanıcı. Karşılama metinleri ve kişiselleştirme
 * bu kaynaktan beslenir — hiçbir ekranda isim sabit yazılmaz.
 */
export function useCurrentUser() {
  const [user, setUser] = useState<CurrentUser | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    let active = true;

    apiClient<CurrentUser>("auth/me")
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
