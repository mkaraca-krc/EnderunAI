"use client";

import { useState } from "react";

import { Button, Modal } from "@/components/ui";
import { CHEQUE_VOID_REASONS } from "@/services/cheque.service";

/**
 * ÇEK İPTAL DİYALOĞU.
 *
 * Ortak `ConfirmDialog` kullanılmıyor: orada gerekçe yalnızca serbest
 * metin. İptal nedeninin SAYILABİLİR olması gerekiyor — "kaç çek
 * karşılıksız çıktı" sorusu serbest metinle hiç cevaplanamıyor, çünkü
 * aynı sebep on ayrı yazımla giriyor.
 *
 * KAPANMIŞ ÇEKTE "YANLIŞ GİRİŞ" LİSTEDE YOK: tahsil edilmiş ya da
 * ödenmiş bir çek gerçekten o hâle gelmiştir; yazım hatası varsa yol
 * DÜZENLEME'dir. Uç da aynı kuralı kendi başına uyguluyor — burada
 * gizlemek yalnızca kullanıcıyı sunucu hatasıyla karşılaştırmamak için.
 */
export function ChequeVoidDialog({
  open,
  /** Çek açık durumdan mı iptal ediliyor (portföy / yeni verilen). */
  fromClosedState,
  statusName,
  busy = false,
  error,
  onCancel,
  onConfirm,
}: {
  open: boolean;
  fromClosedState: boolean;
  statusName: string;
  busy?: boolean;
  error?: string;
  onCancel: () => void;
  onConfirm: (input: { reasonKind: number; reason: string }) => void;
}) {
  const [reasonKind, setReasonKind] = useState<number | "">("");
  const [reason, setReason] = useState("");

  const options = CHEQUE_VOID_REASONS.filter(
    (option) => !(fromClosedState && option.onlyOpen)
  );

  // "Diğer" sayılabilir bir neden değil; açıklaması olmazsa kayıt
  // "iptal edildi, sebebi yazılmadı" olarak kalır.
  const otherSelected = reasonKind === 90;
  const blocked =
    reasonKind === "" || (otherSelected && reason.trim().length === 0);

  return (
    <Modal
      open={open}
      title="Çeki iptal et"
      description={
        "Çekin ürettiği bütün mali etkiler ters kayıtla geri alınır ve çek " +
        "iptal durumuna geçer. Kayıt denetim izi için listede kalır."
      }
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
            onClick={() =>
              onConfirm({ reasonKind: Number(reasonKind), reason: reason.trim() })
            }
            disabled={busy || blocked}
          >
            {busy ? "İşleniyor…" : "İptal Et"}
          </Button>
        </>
      }
    >
      {fromClosedState && (
        <p className="mb-3 rounded-lg border border-amber-200 bg-amber-50 px-3 py-2 text-sm text-amber-800">
          Bu çek <strong>{statusName}</strong> durumunda. Gerçekleşmiş bir
          hareket storno ile geri alınacak ve çek numarası yeniden kullanıma
          açılacak. Bu işlem ayrı bir yetki gerektirir.
        </p>
      )}

      <label className="block text-sm font-medium text-slate-700">
        İptal nedeni (zorunlu)
        <select
          value={reasonKind === "" ? "" : String(reasonKind)}
          onChange={(event) =>
            setReasonKind(event.target.value === "" ? "" : Number(event.target.value))
          }
          disabled={busy}
          className="mt-1.5 w-full rounded-lg border border-slate-300 p-2.5 text-sm text-slate-900 outline-none transition focus:border-brand-500 focus:ring-2 focus:ring-brand-100"
        >
          <option value="">Seçiniz…</option>
          {options.map((option) => (
            <option key={option.value} value={option.value}>
              {option.label}
            </option>
          ))}
        </select>
      </label>

      <label className="mt-3 block text-sm font-medium text-slate-700">
        {otherSelected ? "Açıklama (zorunlu)" : "Açıklama"}
        <textarea
          value={reason}
          onChange={(event) => setReason(event.target.value)}
          rows={3}
          disabled={busy}
          className="mt-1.5 w-full rounded-lg border border-slate-300 p-3 text-sm text-slate-900 outline-none transition focus:border-brand-500 focus:ring-2 focus:ring-brand-100"
        />
      </label>

      {error && (
        <p className="mt-3 rounded-lg border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-700">
          {error}
        </p>
      )}
    </Modal>
  );
}
