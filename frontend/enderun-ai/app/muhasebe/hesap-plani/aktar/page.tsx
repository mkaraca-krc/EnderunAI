"use client";

import { ChangeEvent, useEffect, useState } from "react";
import ErpShell from "@/components/erp/erp-shell";

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
  const [updateExisting, setUpdateExisting] = useState(false);

  const [result, setResult] = useState<ImportResult | null>(null);
  const [loadingCompanies, setLoadingCompanies] = useState(true);
  const [processing, setProcessing] = useState(false);

  const [error, setError] = useState("");
  const [success, setSuccess] = useState("");

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

    if (
      !preview &&
      !window.confirm(
        "Hesap planı seçilen şirkete aktarılacak. Devam edilsin mi?"
      )
    ) {
      return;
    }

    const formData = new FormData();

    formData.append("companyId", companyId);
    formData.append("preview", String(preview));
    formData.append(
      "updateExisting",
      String(updateExisting)
    );
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
          <div
            style={{
              padding: 14,
              borderRadius: 8,
              background: "#fee2e2",
              color: "#991b1b",
              border: "1px solid #fecaca",
            }}
          >
            {error}
          </div>
        )}

        {success && (
          <div
            style={{
              padding: 14,
              borderRadius: 8,
              background: "#dcfce7",
              color: "#166534",
              border: "1px solid #bbf7d0",
            }}
          >
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

            <label className="span-2 erp-check">
              <input
                type="checkbox"
                checked={updateExisting}
                disabled={processing}
                onChange={(event) => {
                  setUpdateExisting(
                    event.target.checked
                  );
                  setResult(null);
                }}
              />

              Mevcut hesapların adını, üst hesabını ve
              özelliklerini güncelle
            </label>
          </div>

          {file && (
            <div
              style={{
                marginTop: 16,
                padding: 12,
                borderRadius: 8,
                background: "#f8fafc",
                border: "1px solid #e2e8f0",
              }}
            >
              <strong>Seçilen dosya:</strong>{" "}
              {file.name}
              <br />
              <span>
                Boyut:{" "}
                {(file.size / 1024).toFixed(1)} KB
              </span>
            </div>
          )}

          <div className="erp-actions">
            <button
              type="button"
              disabled={!canImport}
              onClick={() => void runImport(true)}
            >
              {processing
                ? "İşleniyor..."
                : "Ön İzleme"}
            </button>

            <button
              type="button"
              disabled={
                !canImport ||
                !previewSuccessful
              }
              onClick={() => void runImport(false)}
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
                  <div
                    key={String(label)}
                    style={{
                      padding: 16,
                      borderRadius: 8,
                      background: "#f8fafc",
                      border: "1px solid #e2e8f0",
                    }}
                  >
                    <div
                      style={{
                        fontSize: 13,
                        color: "#64748b",
                      }}
                    >
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
                  <table
                    style={{
                      width: "100%",
                      borderCollapse: "collapse",
                    }}
                  >
                    <thead>
                      <tr>
                        <th
                          style={{
                            textAlign: "left",
                            padding: 10,
                            borderBottom:
                              "1px solid #e2e8f0",
                          }}
                        >
                          Satır
                        </th>

                        <th
                          style={{
                            textAlign: "left",
                            padding: 10,
                            borderBottom:
                              "1px solid #e2e8f0",
                          }}
                        >
                          Hesap Kodu
                        </th>

                        <th
                          style={{
                            textAlign: "left",
                            padding: 10,
                            borderBottom:
                              "1px solid #e2e8f0",
                          }}
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
                              style={{
                                padding: 10,
                                borderBottom:
                                  "1px solid #f1f5f9",
                              }}
                            >
                              {item.rowNumber}
                            </td>

                            <td
                              style={{
                                padding: 10,
                                borderBottom:
                                  "1px solid #f1f5f9",
                              }}
                            >
                              {item.accountCode ?? "-"}
                            </td>

                            <td
                              style={{
                                padding: 10,
                                borderBottom:
                                  "1px solid #f1f5f9",
                              }}
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
    </ErpShell>
  );
}
