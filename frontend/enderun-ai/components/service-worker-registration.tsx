"use client";

import { useEffect } from "react";

export function ServiceWorkerRegistration() {
  useEffect(() => {
    if (typeof window === "undefined" || !("serviceWorker" in navigator)) {
      return;
    }

    navigator.serviceWorker.register("/sw.js").catch(() => {
      // PWA kurulabilirliği bu olmadan çalışmaz ama uygulamanın kendisi
      // service worker'sız da normal şekilde işlev görür — sessizce geç.
    });
  }, []);

  return null;
}
