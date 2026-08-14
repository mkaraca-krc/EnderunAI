"use client";

import { useState } from "react";

import { Button } from "./button";
import { Modal } from "./modal";

interface ConfirmDialogProps {
  open: boolean;
  title: string;
  description?: string;
  /** Onay düğmesinin etiketi — ne olacağını söylesin ("Çeki İptal Et"). */
  confirmLabel: string;
  /**
   * Gerekçe ZORUNLU mu. Geri alınamaz işlemlerde açık olmalı: neden
   * yapıldığı yazılmayan bir iptal, aylar sonra kimsenin
   * açıklayamadığı bir kayıt bırakır.
   */
  requireReason?: boolean;

  /**
   * Gerekçe alanı GÖRÜNSÜN ama zorunlu olmasın.
   *
   * Verilmezse `requireReason` neyse odur. Ayrı bir seçenek olmasının
   * sebebi: satın alma iptali gibi işlemlerde gerekçe isteğe bağlı
   * ama kayda geçmesi değerli. Yalnızca `requireReason` olsaydı
   * seçenek "ya zorla ya da hiç sorma" olurdu ve o bilgi kaybolurdu.
   */
  showReason?: boolean;

  reasonLabel?: string;
  busy?: boolean;
  error?: string;
  onCancel: () => void;
  onConfirm: (reason: string) => void;
}

/**
 * Geri alınamaz işlemler için onay diyaloğu.
 *
 * window.confirm / window.prompt yerine: tarayıcı diyaloğu gerekçeyi
 * zorunlu tutamıyor, boş metni kabul ediyor, hata mesajını aynı yerde
 * gösteremiyor ve biçimlendirilemiyor. Burada onay düğmesi gerekçe
 * yazılmadan AÇILMIYOR.
 */
export function ConfirmDialog({
  open,
  title,
  description,
  confirmLabel,
  requireReason = false,
  showReason,
  reasonLabel,
  busy = false,
  error,
  onCancel,
  onConfirm,
}: ConfirmDialogProps) {
  // Gerekçe alanı BİLEŞENİN İÇİNDE: her açılışta temiz başlaması için
  // çağıran taraf key veriyor (bkz. çek ekranı) ve React bileşeni
  // yeniden kuruyor. Effect ile sıfırlamak, açılışta fazladan bir
  // render turu doğuruyordu.
  const [reason, setReason] = useState("");

  const blocked = requireReason && reason.trim().length === 0;
  const fieldVisible = showReason ?? requireReason;
  const label =
    reasonLabel ?? (requireReason ? "Gerekçe (zorunlu)" : "Gerekçe");

  return (
    <Modal
      open={open}
      title={title}
      description={description}
      onClose={onCancel}
      busy={busy}
      size="sm"
      footer={
        <>
          <Button
            type="button"
            variant="secondary"
            onClick={onCancel}
            disabled={busy}
          >
            Vazgeç
          </Button>

          <Button
            type="button"
            onClick={() => onConfirm(reason.trim())}
            disabled={busy || blocked}
          >
            {busy ? "İşleniyor…" : confirmLabel}
          </Button>
        </>
      }
    >
      {fieldVisible && (
        <label className="block text-sm font-medium text-slate-700">
          {label}
          <textarea
            value={reason}
            onChange={(event) => setReason(event.target.value)}
            rows={3}
            disabled={busy}
            className="mt-1.5 w-full rounded-lg border border-slate-300 p-3 text-sm text-slate-900 outline-none transition focus:border-brand-500 focus:ring-2 focus:ring-brand-100"
          />
        </label>
      )}

      {error && (
        <p className="mt-3 rounded-lg border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-700">
          {error}
        </p>
      )}
    </Modal>
  );
}
