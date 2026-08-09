import { apiClient } from "@/lib/api/api-client";

const root = "hr/personel-belgeleri";

export type PersonnelDocumentListItem = {
  id: string;
  personnelId: string;
  personnelName: string;
  employeeNumber: string;

  documentType: number;
  documentTypeName: string;

  title: string;
  documentNumber?: string | null;

  issueDate?: string | null;
  expiryDate?: string | null;
  issuingInstitution?: string | null;

  isMandatory: boolean;

  /** Aslı görüldü işareti — yüklenmiş olmak doğrulanmış olmak değildir. */
  isVerified: boolean;
  verifiedAtUtc?: string | null;

  /** Geçerlilik durumu (İSG geçerlilik ölçeğiyle aynı). */
  status: number;
  statusName: string;
  statusColor: string;
  daysRemaining?: number | null;

  originalName?: string | null;
  contentType?: string | null;
  fileSize?: number | null;

  notes?: string | null;
  createdAtUtc: string;
};

export type PersonnelDocumentType = {
  value: number;
  name: string;
};

export type UploadPersonnelDocumentInput = {
  personnelId: string;
  documentType: number;
  title: string;
  file: File;
  documentNumber?: string;
  issueDate?: string;
  expiryDate?: string;
  issuingInstitution?: string;
  isMandatory?: boolean;
  notes?: string;
};

/**
 * Özlük belge arşivi.
 *
 * Uçlar H8'de yazıldı ama ekranı yoktu; belgeler yalnızca API'den
 * erişilebiliyordu. Bu servis o boşluğu kapatır.
 *
 * Gizlilik: kimlik fotokopisi ve adli sicil gibi belgeler taşıdığı
 * için uçlar personnel_document.* dar anahtarıyla korunuyor —
 * personnel.view sahada da var ve bu belgeler oradan görünmemeli.
 */
export const personnelDocumentService = {
  list(personnelId: string) {
    return apiClient<PersonnelDocumentListItem[]>(
      `${root}?personnelId=${personnelId}`
    );
  },

  types() {
    return apiClient<PersonnelDocumentType[]>(`${root}/turler`);
  },

  /** "Aslı görüldü" işaretini koyar ya da kaldırır. */
  verify(id: string, isVerified: boolean, notes?: string) {
    return apiClient<{ message: string; isVerified: boolean }>(
      `${root}/${id}/dogrula`,
      {
        method: "POST",
        body: { isVerified, notes: notes ?? null },
      }
    );
  },

  remove(id: string) {
    return apiClient<{ message: string }>(`${root}/${id}`, {
      method: "DELETE",
    });
  },

  /**
   * Belgeyi indirir. Dosya adı sunucudaki özgün addır; yoksa belge
   * başlığına düşülür.
   */
  async download(id: string, filename: string) {
    const response = await fetch(`/api/backend/${root}/${id}/indir`, {
      credentials: "include",
    });

    if (!response.ok) {
      if (response.status === 401 && typeof window !== "undefined") {
        window.location.href = "/login";
        return;
      }

      throw new Error("Belge indirilemedi.");
    }

    const blob = await response.blob();
    const objectUrl = window.URL.createObjectURL(blob);

    const link = document.createElement("a");
    link.href = objectUrl;
    link.download = filename;

    document.body.appendChild(link);
    link.click();
    link.remove();

    window.URL.revokeObjectURL(objectUrl);
  },

  /**
   * Belge yükler. apiClient JSON gönderdiği için çok parçalı istek
   * doğrudan fetch ile yapılıyor; Content-Type'ı tarayıcı kendisi
   * yazar (sınır değerini elle vermek bozar).
   */
  async upload(input: UploadPersonnelDocumentInput) {
    const form = new FormData();

    form.append("personnelId", input.personnelId);
    form.append("documentType", String(input.documentType));
    form.append("title", input.title);
    form.append("file", input.file);
    form.append("isMandatory", String(input.isMandatory ?? false));

    if (input.documentNumber) form.append("documentNumber", input.documentNumber);
    if (input.issueDate) form.append("issueDate", input.issueDate);
    if (input.expiryDate) form.append("expiryDate", input.expiryDate);
    if (input.issuingInstitution) {
      form.append("issuingInstitution", input.issuingInstitution);
    }
    if (input.notes) form.append("notes", input.notes);

    const response = await fetch(`/api/backend/${root}`, {
      method: "POST",
      credentials: "include",
      body: form,
    });

    let payload: { message?: string; id?: string } | null = null;

    try {
      payload = await response.json();
    } catch {
      // yanıt JSON değilse yoksay
    }

    if (!response.ok) {
      if (response.status === 401 && typeof window !== "undefined") {
        window.location.href = "/login";
      }

      throw new Error(payload?.message ?? "Belge yüklenemedi.");
    }

    return payload;
  },
};
