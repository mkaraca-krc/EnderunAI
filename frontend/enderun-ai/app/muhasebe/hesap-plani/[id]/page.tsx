"use client";

import Link from "next/link";
import {
  FormEvent,
  useEffect,
  useMemo,
  useState,
} from "react";
import { useParams, useRouter } from "next/navigation";

import ErpShell from "@/components/erp/erp-shell";
import { ConfirmDialog } from "@/components/ui";
import { useModuleActions } from "@/lib/auth/module-actions";

import {
  accountingAccountService,
  type AccountingAccountDetail,
  type AccountingAccountListItem,
  type AccountingAccountNature,
  type UpdateAccountingAccountRequest,
} from "@/services/accounting-account.service";

type FormState = {
  parentAccountId: string;
  code: string;
  name: string;
  description: string;
  nature: AccountingAccountNature;
  isPostingAllowed: boolean;
  requiresProject: boolean;
  requiresCostCenter: boolean;
  currencyCode: string;
  isActive: boolean;
};

const blankForm: FormState = {
  parentAccountId: "",
  code: "",
  name: "",
  description: "",
  nature: 0,
  isPostingAllowed: true,
  requiresProject: false,
  requiresCostCenter: false,
  currencyCode: "TRY",
  isActive: true,
};

const natureLabels: Record<
  AccountingAccountNature,
  string
> = {
  0: "Borç",
  1: "Alacak",
  2: "Borç / Alacak",
};

export default function AccountingAccountDetailPage() {
  const params = useParams<{ id: string }>();
  const router = useRouter();
  const id = params.id;

  /*
   * POST accounting-accounts/{id}/deactivate -> accounting.delete
   * Pasife almak silmeye denk yetki: hesap defterden düşüyor.
   */
  const actions = useModuleActions("accounting");

  const [item, setItem] =
    useState<AccountingAccountDetail | null>(null);

  const [accounts, setAccounts] = useState<
    AccountingAccountListItem[]
  >([]);

  const [form, setForm] = useState<FormState>(blankForm);

  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [deactivating, setDeactivating] =
    useState(false);

  const [message, setMessage] = useState("");
  const [error, setError] = useState("");
  const [deactivateOpen, setDeactivateOpen] = useState(false);

  useEffect(() => {
    async function load() {
      setLoading(true);
      setError("");

      try {
        const detail =
          await accountingAccountService.getById(id);

        setItem(detail);

        setForm({
          parentAccountId:
            detail.parentAccountId ?? "",
          code: detail.code,
          name: detail.name,
          description: detail.description ?? "",
          nature: detail.nature,
          isPostingAllowed:
            detail.isPostingAllowed,
          requiresProject:
            detail.requiresProject,
          requiresCostCenter:
            detail.requiresCostCenter,
          currencyCode:
            detail.currencyCode ?? "TRY",
          isActive: detail.isActive,
        });

        const accountList =
          await accountingAccountService.getAll({
            companyId: detail.companyId,
          });

        setAccounts(
          accountList.filter(
            (account) => account.id !== id
          )
        );
      } catch (err) {
        setError(
          err instanceof Error
            ? err.message
            : "Muhasebe hesabı alınamadı."
        );
      } finally {
        setLoading(false);
      }
    }

    void load();
  }, [id]);

  const selectedParent = useMemo(
    () =>
      accounts.find(
        (account) =>
          account.id === form.parentAccountId
      ) ?? null,
    [accounts, form.parentAccountId]
  );

  async function save(event: FormEvent) {
    event.preventDefault();

    /*
     * SÜRÜM YOKSA KAYDETMEYE HİÇ BAŞLANMAZ.
     *
     * `item` sunucudan gelen son yanıt; sürüm ondan alınıyor. Henüz
     * yüklenmediyse gönderilecek bir sürüm de yoktur ve sunucu
     * zaten reddederdi — kullanıcıyı ağ turu bekletmeden aynı şeyi
     * burada söylüyoruz. Sunucudaki kapı KALDIRILMADI; bu yalnız
     * daha erken bir uyarı.
     */
    if (!item) {
      setError(
        "Sayfanın eski bir sürümü açık. Sayfayı yenileyip tekrar deneyin."
      );
      return;
    }

    setSaving(true);
    setMessage("");
    setError("");

    const payload: UpdateAccountingAccountRequest = {
      parentAccountId:
        form.parentAccountId || null,
      name: form.name.trim(),

      /*
       * SÜRÜM SUNUCUDAN GELEN DEĞERLE GÖNDERİLİYOR — formdan DEĞİL.
       *
       * Formdan alınsaydı kullanıcı sayfada dururken sürüm eskimez,
       * hep "güncel" görünürdü ve kayıp güncelleme yakalanmazdı.
       * `item` son sunucu yanıtı; sürüm oradan geliyor.
       */
      surum: item.surum,
      description:
        form.description.trim() || null,
      nature: form.nature,
      isPostingAllowed:
        form.isPostingAllowed,
      requiresProject:
        form.requiresProject,
      requiresCostCenter:
        form.requiresCostCenter,
      currencyCode:
        form.currencyCode
          .trim()
          .toUpperCase() || null,
    };

    try {
      const updated =
        await accountingAccountService.update(
          id,
          payload
        );

      setItem(updated);
      setMessage(
        "Muhasebe hesabı güncellendi."
      );
    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : "Muhasebe hesabı güncellenemedi."
      );
    } finally {
      setSaving(false);
    }
  }

  async function deactivate() {
    if (!item) {
      return;
    }

    setDeactivating(true);
    setMessage("");
    setError("");

    try {
      const result =
        await accountingAccountService.deactivate(
          item.id
        );

      setDeactivateOpen(false);
      setMessage(result.message);

      setForm((current) => ({
        ...current,
        isActive: false,
      }));

      setItem((current) =>
        current
          ? {
              ...current,
              isActive: false,
            }
          : current
      );
    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : "Hesap pasife alınamadı."
      );
    } finally {
      setDeactivating(false);
    }
  }

  /*
   * GERİ AÇMA (HP/1 · K3).
   *
   * Servisteki `activate` ucunun ekrandaki karşılığı. Düğmesi
   * olmasaydı yetenek kullanıcı için YOK demekti (Kural 62'nin ters
   * yönü) — ve "Durum" seçicisi de kaldırıldığı için pasife alınan
   * bir hesabı geri açmanın HİÇBİR yolu kalmazdı.
   */
  async function activate() {
    if (!item) return;

    setDeactivating(true);
    setMessage("");
    setError("");

    try {
      const result =
        await accountingAccountService.activate(item.id);

      setMessage(result.message);

      setForm((current) => ({
        ...current,
        isActive: true,
      }));

      setItem((current) =>
        current
          ? {
              ...current,
              isActive: true,
            }
          : current
      );
    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : "Hesap geri açılamadı."
      );
    } finally {
      setDeactivating(false);
    }
  }

  if (loading) {
    return (
      <ErpShell
      design="redwood"
        title="Muhasebe Hesabı"
        description="Hesap bilgileri yükleniyor"
      >
        <div className="erp-form-card">
          Hesap bilgileri yükleniyor...
        </div>
      </ErpShell>
    );
  }

  if (!item) {
    return (
      <ErpShell
      design="redwood"
        title="Muhasebe Hesabı"
        description="Kayıt bulunamadı"
      >
        {error && (
          <div className="erp-alert error">
            {error}
          </div>
        )}

        <Link
          href="/muhasebe/hesap-plani"
          className="erp-secondary-button"
          style={{ textDecoration: "none" }}
        >
          Hesap Planına Dön
        </Link>
      </ErpShell>
    );
  }

  return (
    <ErpShell
      design="redwood"
      title={`${item.code} - ${item.name}`}
      description="Muhasebe hesabı detay ve düzenleme ekranı"
    >
      <div className="erp-toolbar">
        <div>
          <strong>{item.code}</strong>
          <small>
            Seviye {item.level} ·{" "}
            {natureLabels[item.nature]}
          </small>
        </div>

        <div className="erp-actions">
          <Link
            href="/muhasebe/hesap-plani"
            className="erp-secondary-button"
            style={{ textDecoration: "none" }}
          >
            Hesap Planına Dön
          </Link>

          {item.isActive && actions.can("delete") && (
            <button
              type="button"
              className="erp-secondary-button"
              disabled={deactivating}
              onClick={() =>
                setDeactivateOpen(true)
              }
            >
              {deactivating
                ? "İşleniyor..."
                : "Pasife Al"}
            </button>
          )}

          {!item.isActive && actions.can("delete") && (
            <button
              type="button"
              className="erp-secondary-button"
              disabled={deactivating}
              onClick={() => void activate()}
            >
              {deactivating ? "İşleniyor..." : "Geri Aç"}
            </button>
          )}
        </div>
      </div>

      {message && (
        <div className="erp-alert success">
          {message}
        </div>
      )}

      {error && (
        <div className="erp-alert error">
          {error}
        </div>
      )}

      <form
        className="erp-form-card"
        onSubmit={save}
      >
        <div className="erp-form-grid">
          <label>
            <span>Üst Hesap</span>

            <select
              value={form.parentAccountId}
              disabled={saving}
              onChange={(event) =>
                setForm((current) => ({
                  ...current,
                  parentAccountId:
                    event.target.value,
                }))
              }
            >
              <option value="">
                Ana hesap
              </option>

              {accounts.map((account) => (
                <option
                  key={account.id}
                  value={account.id}
                >
                  {account.code} - {account.name}
                </option>
              ))}
            </select>
          </label>

          {/*
            DURUM SEÇİCİSİ KALDIRILDI (HP/1 · K3).
            Aktiflik artık formdan değil, kendi düğmelerinden
            değişiyor: "Pasife Al" / "Geri Aç". Seçici bırakılsaydı
            hiçbir şey yapmayan bir kutu olurdu — kullanıcı "Pasif"
            seçer, "güncellendi" mesajı alır, hesap aktif kalırdı.
            Sessiz yalan, görünür hatadan kötüdür.
          */}
          <label>
            <span>Durum</span>
            <strong>{item.isActive ? "Aktif" : "Pasif"}</strong>
          </label>

          {/*
            HESAP KODU SALT OKUNUR (HP/1 · K1).
            Kod oluşturulduktan sonra değişmez; düzenlenebilir
            bırakılsaydı kullanıcı yazar, kaydeder, "güncellendi"
            görür ve kod değişmezdi. Yanlış kod için yol:
            hesabı pasife al, doğrusunu yeni hesap olarak aç.
          */}
          <label>
            <span>Hesap Kodu</span>

            <input
              value={form.code}
              readOnly
              disabled
            />
            <small>
              Hesap kodu değiştirilemez. Yanlışsa hesabı pasife alıp
              doğru kodla yeni hesap açın.
            </small>
          </label>

          <label>
            <span>Hesap Adı *</span>

            <input
              required
              maxLength={250}
              value={form.name}
              onChange={(event) =>
                setForm((current) => ({
                  ...current,
                  name: event.target.value,
                }))
              }
            />
          </label>

          <label>
            <span>Hesap Karakteri *</span>

            <select
              value={form.nature}
              onChange={(event) =>
                setForm((current) => ({
                  ...current,
                  nature: Number(
                    event.target.value
                  ) as AccountingAccountNature,
                }))
              }
            >
              <option value={0}>Borç</option>
              <option value={1}>Alacak</option>
              <option value={2}>
                Borç / Alacak
              </option>
            </select>
          </label>

          <label>
            <span>Para Birimi</span>

            <input
              maxLength={3}
              value={form.currencyCode}
              onChange={(event) =>
                setForm((current) => ({
                  ...current,
                  currencyCode:
                    event.target.value
                      .toUpperCase()
                      .replace(/[^A-Z]/g, ""),
                }))
              }
            />
          </label>

          <label className="span-2">
            <span>Açıklama</span>

            <textarea
              rows={4}
              maxLength={1000}
              value={form.description}
              onChange={(event) =>
                setForm((current) => ({
                  ...current,
                  description:
                    event.target.value,
                }))
              }
            />
          </label>
        </div>

        <section
          style={{
            display: "grid",
            gridTemplateColumns:
              "repeat(auto-fit, minmax(220px, 1fr))",
            gap: 12,
            marginTop: 16,
          }}
        >
          <OptionCard
            title="Kayıt Yapılabilir"
            description="Muhasebe fişlerinde bu hesaba kayıt girilebilir."
            checked={form.isPostingAllowed}
            onChange={(checked) =>
              setForm((current) => ({
                ...current,
                isPostingAllowed: checked,
              }))
            }
          />

          <OptionCard
            title="Proje Zorunlu"
            description="Fiş satırında proje seçimi zorunlu olur."
            checked={form.requiresProject}
            onChange={(checked) =>
              setForm((current) => ({
                ...current,
                requiresProject: checked,
              }))
            }
          />

          <OptionCard
            title="Masraf Merkezi Zorunlu"
            description="Fiş satırında masraf merkezi seçilmelidir."
            checked={form.requiresCostCenter}
            onChange={(checked) =>
              setForm((current) => ({
                ...current,
                requiresCostCenter: checked,
              }))
            }
          />
        </section>

        <section className="rw-subtle-panel" style={{ marginTop: 16 }}>
          <strong>Hesap Bilgileri</strong>

          <div
            style={{
              display: "grid",
              gridTemplateColumns:
                "repeat(auto-fit, minmax(180px, 1fr))",
              gap: 10,
              marginTop: 10,
            }}
          >
            <Info
              label="Mevcut Seviye"
              value={String(item.level)}
            />

            <Info
              label="Üst Hesap"
              value={
                selectedParent
                  ? `${selectedParent.code} - ${selectedParent.name}`
                  : "Ana hesap"
              }
            />

            <Info
              label="Oluşturulma"
              value={new Date(
                item.createdAtUtc
              ).toLocaleString("tr-TR")}
            />

            <Info
              label="Son Güncelleme"
              value={
                item.updatedAtUtc
                  ? new Date(
                      item.updatedAtUtc
                    ).toLocaleString("tr-TR")
                  : "Henüz güncellenmedi"
              }
            />
          </div>
        </section>

        <div
          className="erp-actions"
          style={{ marginTop: 18 }}
        >
          {actions.can("edit") && (
            <button
              type="submit"
              className="erp-primary-button"
              disabled={
                saving ||
                !form.code.trim() ||
                !form.name.trim()
              }
            >
              {saving
                ? "Kaydediliyor..."
                : "Değişiklikleri Kaydet"}
            </button>
          )}
        </div>
      </form>
      <ConfirmDialog
        open={deactivateOpen}
        title="Hesap pasife alınsın mı?"
        description={
          item
            ? `${item.code} — ${item.name} yeni fişlerde seçilemez. Geçmiş kayıtlar defterde kalır.`
            : ""
        }
        confirmLabel="Pasife Al"
        busy={deactivating}
        onCancel={() => setDeactivateOpen(false)}
        onConfirm={() => void deactivate()}
      />

    </ErpShell>
  );
}

function OptionCard({
  title,
  description,
  checked,
  onChange,
}: {
  title: string;
  description: string;
  checked: boolean;
  onChange: (checked: boolean) => void;
}) {
  return (
    <label
      style={{
        display: "flex",
        alignItems: "center",
        gap: 10,
        padding: 14,
        border: "1px solid var(--erp-border)",
        borderRadius: 10,
      }}
    >
      <input
        type="checkbox"
        checked={checked}
        onChange={(event) =>
          onChange(event.target.checked)
        }
      />

      <span>
        <strong>{title}</strong>
        <small>{description}</small>
      </span>
    </label>
  );
}

function Info({
  label,
  value,
}: {
  label: string;
  value: string;
}) {
  return (
    <div>
      <small>{label}</small>
      <strong
        style={{ display: "block" }}
      >
        {value}
      </strong>
    </div>
  );
}
