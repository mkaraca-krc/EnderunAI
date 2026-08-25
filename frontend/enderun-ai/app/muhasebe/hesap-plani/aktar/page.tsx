"use client";

import { ChangeEvent, useEffect, useState } from "react";
import ErpShell from "@/components/erp/erp-shell";
import { decimal } from "@/lib/format/turkish";
import { ConfirmDialog } from "@/components/ui";

type Company = {
  id: string;
  code: string;
  name: string;
};

type ImportError = {
  rowNumber: number;
  accountCode?: string | null;
  message: string;
};

type ImportResult = {
  preview: boolean;
  totalRowCount: number;
  validRowCount: number;
  createdCount: number;
  updatedCount: number;
  unchangedCount: number;
  skippedCount: number;
  errorCount: number;
  errors: ImportError[];
  message: string;
};

async function readResponse<T>(response: Response): Promise<T> {
  const contentType = response.headers.get("content-type") ?? "";

  let payload: unknown;

  if (contentType.includes("application/json")) {
    payload = await response.json();
  } else {
    payload = await response.text();
  }

  if (!response.ok) {
    if (
      typeof payload === "object" &&
      payload !== null &&
      "message" in payload
    ) {
      throw new Error(String(payload.message));
    }

    throw new Error(
      typeof payload === "string" && payload
        ? payload
        : `İşlem başarısız. HTTP ${response.status}`
    );
  }

  return payload as T;
}

export default function AccountingAccountImportPage() {
  const [companies, setCompanies] = useState<Company[]>([]);
  const [companyId, setCompanyId] = useState("");
  const [file, setFile] = useState<File | null>(null);

  const [result, setResult] = useState<ImportResult | null>(null);
  const [loadingCompanies, setLoadingCompanies] = useState(true);
  const [processing, setProcessing] = useState(false);

  const [error, setError] = useState("");
  const [success, setSuccess] = useState("");
  const [confirmingImport, setConfirmingImport] = useState(false);

  useEffect(() => {
    void loadCompanies();
  }, []);

  async function loadCompanies() {
    setLoadingCompanies(true);
    setError("");

    try {
      const response = await fetch("/api/backend/companies", {
        method: "GET",
        credentials: "include",
        cache: "no-store",
      });

      const data = await readResponse<Company[]>(response);

      setCompanies(data);

      const enderun =
        data.find(
          (company) =>
            company.code?.toUpperCase() === "ENDERUN"
        ) ?? data[0];

      if (enderun) {
        setCompanyId(enderun.id);
      }
    } catch (requestError) {
      setError(
        requestError instanceof Error
          ? requestError.message
          : "Şirket bilgileri alınamadı."
      );
    } finally {
      setLoadingCompanies(false);
    }
  }

  function handleFileChange(
    event: ChangeEvent<HTMLInputElement>
  ) {
    const selectedFile =
      event.target.files?.[0] ?? null;

    setResult(null);
    setSuccess("");
    setError("");

    if (!selectedFile) {
      setFile(null);
      return;
    }

    const extension =
      selectedFile.name
        .split(".")
        .pop()
        ?.toLowerCase();

    if (extension !== "xlsx" && extension !== "xlsm") {
      setFile(null);
      event.target.value = "";
      setError(
        "Yalnızca .xlsx veya .xlsm Excel dosyası seçebilirsiniz."
      );
      return;
    }

    setFile(selectedFile);
  }

  async function runImport(preview: boolean) {
    setError("");
    setSuccess("");

    if (!companyId) {
      setError("Şirket seçimi zorunludur.");
      return;
    }

    if (!file) {
      setError("Excel dosyası seçmelisiniz.");
      return;
    }

    setConfirmingImport(false);

    const formData = new FormData();

    formData.append("companyId", companyId);
    formData.append("preview", String(preview));
    formData.append("file", file);

    setProcessing(true);

    try {
      const response = await fetch(
        "/api/backend/accounting-accounts/import",
        {
          method: "POST",
          credentials: "include",
          body: formData,
        }
      );

      const data =
        await readResponse<ImportResult>(response);

      setResult(data);

      if (preview) {
        setSuccess(
          "Ön izleme tamamlandı. Veritabanında değişiklik yapılmadı."
        );
      } else {
        setSuccess(
          `${data.createdCount} yeni hesap oluşturuldu, ` +
          `${data.updatedCount} hesap güncellendi.`
        );
      }
    } catch (requestError) {
      setError(
        requestError instanceof Error
          ? requestError.message
          : "Hesap planı aktarımı başarısız oldu."
      );
    } finally {
      setProcessing(false);
    }
  }

  const canImport =
    Boolean(companyId) &&
    Boolean(file) &&
    !processing;

  const previewSuccessful =
    result?.preview === true &&
    result.errorCount === 0;

  return (
    <ErpShell
      design="redwood"
      title="Hesap Planı Aktar"
      description="Excel hesap planını ön izleyin ve seçilen şirkete aktarın."
    >
      <div
        style={{
          display: "grid",
          gap: 20,
          maxWidth: 1100,
        }}
      >
        {error && (
          <div className="erp-alert error">
            {error}
          </div>
        )}

        {success && (
          <div className="erp-alert success">
            {success}
          </div>
        )}

        <section className="erp-form-card">
          <div className="erp-form-grid">
            <label className="span-2">
              <span>Şirket *</span>

              <select
                value={companyId}
                disabled={
                  loadingCompanies || processing
                }
                onChange={(event) => {
                  setCompanyId(event.target.value);
                  setResult(null);
                  setSuccess("");
                }}
              >
                <option value="">
                  Şirket seçin
                </option>

                {companies.map((company) => (
                  <option
                    key={company.id}
                    value={company.id}
                  >
                    {company.code} — {company.name}
                  </option>
                ))}
              </select>
            </label>

            <label className="span-2">
              <span>Excel hesap planı *</span>

              <input
                type="file"
                accept=".xlsx,.xlsm"
                disabled={processing}
                onChange={handleFileChange}
              />
            </label>

            {/*
              "MEVCUTLARI GÜNCELLE" KUTUSU KALDIRILDI.

              Aktarım mevcut hesabı GÜNCELLEMİYOR (karar: Mehmet
              Karacabey, 2026-08-25). Muhasebe hesabını bir dosyayla
              değiştirmek elle yapılacak bir iştir; dosyada yanlış
              yazılmış tek bir ad, hesabın anlamını sessizce
              değiştirir ve fark edilmez.

              Kutu duruyor olsaydı işaretlenebilir ama hiçbir şey
              yapmazdı — kullanıcıya var olmayan bir yetenek vaat
              eden bir düğme, olmayan düğmeden kötüdür.
            */}
            <p className="span-2 erp-status">
              Mevcut hesap kodları GÜNCELLENMEZ, atlanır ve raporda
              listelenir. Üst hesabı bulunmayan satırlar da atlanır;
              eksik üst hesap otomatik oluşturulmaz.
            </p>
          </div>

          {file && (
            <div
              className="rw-subtle-panel"
              style={{ marginTop: 16 }}
            >
              <strong>Seçilen dosya:</strong>{" "}
              {file.name}
              <br />
              <span>
                Boyut:{" "}
                {decimal(file.size / 1024, 1)}{" "}
                KB
              </span>
            </div>
          )}

          <div className="erp-actions">
            <button
              type="button"
              disabled={processing || !file || !companyId}
              onClick={() => void runImport(true)}
            >
              {processing ? "İşleniyor…" : "Ön İzleme"}
            </button>

            <button
              type="button"
              className="erp-primary"
              disabled={processing || !file || !companyId}
              onClick={() => setConfirmingImport(true)}
            >
              Gerçek Aktarımı Başlat
            </button>
          </div>

        </section>

        {result && (
          <>
            <section className="erp-form-card">
              <h2
                style={{
                  marginTop: 0,
                  marginBottom: 16,
                }}
              >
                {result.preview
                  ? "Ön İzleme Sonucu"
                  : "Aktarım Sonucu"}
              </h2>

              <div
                style={{
                  display: "grid",
                  gridTemplateColumns:
                    "repeat(auto-fit, minmax(150px, 1fr))",
                  gap: 12,
                }}
              >
                {[
                  [
                    "Toplam Satır",
                    result.totalRowCount,
                  ],
                  [
                    "Geçerli Hesap",
                    result.validRowCount,
                  ],
                  ["Yeni Hesap", result.createdCount],
                  [
                    "Güncellenecek",
                    result.updatedCount,
                  ],
                  [
                    "Değişmeyen",
                    result.unchangedCount,
                  ],
                  ["Atlanan", result.skippedCount],
                  ["Hatalı", result.errorCount],
                ].map(([label, value]) => (
                  <div key={String(label)} className="rw-subtle-panel">
                    <div className="rw-value-muted" style={{ fontSize: 13 }}>
                      {label}
                    </div>

                    <strong
                      style={{
                        display: "block",
                        marginTop: 6,
                        fontSize: 25,
                      }}
                    >
                      {value}
                    </strong>
                  </div>
                ))}
              </div>

              <p style={{ marginBottom: 0 }}>
                {result.message}
              </p>
            </section>

            {result.errors.length > 0 && (
              <section className="erp-form-card">
                <h2 style={{ marginTop: 0 }}>
                  Hatalı Satırlar
                </h2>

                <div
                  style={{
                    overflowX: "auto",
                  }}
                >
                  <table className="rw-plain-table">
                    <thead>
                      <tr>
                        <th
                        >
                          Satır
                        </th>

                        <th
                        >
                          Hesap Kodu
                        </th>

                        <th
                        >
                          Hata
                        </th>
                      </tr>
                    </thead>

                    <tbody>
                      {result.errors.map(
                        (item, index) => (
                          <tr
                            key={`${item.rowNumber}-${index}`}
                          >
                            <td
                            >
                              {item.rowNumber}
                            </td>

                            <td
                            >
                              {item.accountCode ?? "-"}
                            </td>

                            <td
                            >
                              {item.message}
                            </td>
                          </tr>
                        )
                      )}
                    </tbody>
                  </table>
                </div>
              </section>
            )}
          </>
        )}
      </div>
      {/*
        Önizleme onay istemez, GERÇEK AKTARIM ister: önizleme hiçbir
        şey yazmıyor, aktarım hesap planını değiştiriyor.
      */}
      <ConfirmDialog
        open={confirmingImport}
        title="Hesap planı aktarılsın mı?"
        description="Dosyadaki hesaplar seçilen şirkete yazılır. Önce önizleme almanız önerilir."
        confirmLabel="Aktar"
        busy={processing}
        onCancel={() => setConfirmingImport(false)}
        onConfirm={() => void runImport(false)}
      />

    </ErpShell>
  );
}
