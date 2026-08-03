"use client";

import {
  FormEvent,
  useCallback,
  useEffect,
  useState,
} from "react";
import Link from "next/link";
import ErpShell from "@/components/erp/erp-shell";

type Company = {
  id: string;
  code: string;
  name: string;
};

type Account = {
  id: string;
  companyId: string;
  companyName: string;
  code: string;
  title: string;
  shortName?: string | null;
  roles: number;
  status: number;
  taxOffice?: string | null;
  taxNumber?: string | null;
  authorizedPerson?: string | null;
  phone?: string | null;
  email?: string | null;
  address?: string | null;
  paymentTerm?: string | null;
  creditLimit?: number | null;
  receivableAccountingAccountId?: string | null;
  payableAccountingAccountId?: string | null;
  isActive: boolean;
};

type FormState = {
  companyId: string;
  code: string;
  title: string;
  shortName: string;
  roles: number;
  taxOffice: string;
  taxNumber: string;
  authorizedPerson: string;
  phone: string;
  email: string;
  address: string;
  paymentTerm: string;
  creditLimit: string;
};

const roleOptions = [
  [1, "Müşteri"],
  [2, "Tedarikçi"],
  [4, "Alt Yüklenici"],
  [8, "Resmî Kurum"],
  [16, "Banka"],
  [32, "Servis"],
  [64, "Kiralama"],
  [128, "Diğer"],
] as const;

const blank: FormState = {
  companyId: "",
  code: "",
  title: "",
  shortName: "",
  roles: 1,
  taxOffice: "",
  taxNumber: "",
  authorizedPerson: "",
  phone: "",
  email: "",
  address: "",
  paymentTerm: "",
  creditLimit: "",
};

async function api(path: string, options?: RequestInit) {
  const response = await fetch(`/api/backend/${path}`, {
    cache: "no-store",
    ...options,
  });

  if (response.status === 401) {
    location.href = "/login";
    throw new Error("Oturum süresi doldu.");
  }

  const body = await response.json().catch(() => null);

  if (!response.ok) {
    throw new Error(body?.message ?? `Hata ${response.status}`);
  }

  return body;
}

function toForm(account: Account): FormState {
  return {
    companyId: account.companyId,
    code: account.code,
    title: account.title,
    shortName: account.shortName ?? "",
    roles: account.roles || 1,
    taxOffice: account.taxOffice ?? "",
    taxNumber: account.taxNumber ?? "",
    authorizedPerson: account.authorizedPerson ?? "",
    phone: account.phone ?? "",
    email: account.email ?? "",
    address: account.address ?? "",
    paymentTerm: account.paymentTerm ?? "",
    creditLimit:
      account.creditLimit === null ||
      account.creditLimit === undefined
        ? ""
        : String(account.creditLimit),
  };
}

export default function Page() {
  const [companies, setCompanies] = useState<Company[]>([]);
  const [items, setItems] = useState<Account[]>([]);
  const [balances, setBalances] = useState<Record<string, number>>({});
  const [form, setForm] = useState<FormState>(blank);
  const [show, setShow] = useState(false);
  const [editingId, setEditingId] = useState<string | null>(null);
  const [syncing, setSyncing] = useState(false);
  const [saving, setSaving] = useState(false);
  const [msg, setMsg] = useState("");
  const [err, setErr] = useState("");

  const load = useCallback(async () => {
    try {
      const [companyList, accountList, balanceList] = await Promise.all([
        api("companies"),
        api("current-accounts"),
        // Bakiye muhasebe defterinden gelir; ayrı bir hareket defteri yok.
        api("current-accounts/balances").catch(() => []),
      ]);

      setCompanies(companyList);
      setItems(accountList);
      setBalances(
        Object.fromEntries(
          (balanceList as { currentAccountId: string; balance: number }[]).map(
            (row) => [row.currentAccountId, row.balance]
          )
        )
      );
      setForm((current) => ({
        ...current,
        companyId:
          current.companyId || companyList[0]?.id || "",
      }));
    } catch (error) {
      setErr(
        error instanceof Error
          ? error.message
          : "Liste alınamadı."
      );
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  function toggleRole(value: number) {
    setForm((current) => ({
      ...current,
      roles:
        ((current.roles & value) === value
          ? current.roles & ~value
          : current.roles | value) || 1,
    }));
  }

  function startCreate() {
    setEditingId(null);
    setForm({
      ...blank,
      companyId: form.companyId || companies[0]?.id || "",
    });
    setMsg("");
    setErr("");
    setShow(true);
  }

  function startEdit(account: Account) {
    setEditingId(account.id);
    setForm(toForm(account));
    setMsg("");
    setErr("");
    setShow(true);
    window.scrollTo({ top: 0, behavior: "smooth" });
  }

  function cancelForm() {
    setEditingId(null);
    setShow(false);
    setForm({
      ...blank,
      companyId: form.companyId || companies[0]?.id || "",
    });
    setErr("");
  }

  async function save(event: FormEvent) {
    event.preventDefault();

    try {
      setSaving(true);
      setMsg("");
      setErr("");

      const payload = {
        ...form,
        creditLimit: form.creditLimit
          ? Number(form.creditLimit)
          : null,
      };

      if (editingId) {
        await api(`current-accounts/${editingId}`, {
          method: "PUT",
          headers: {
            "Content-Type": "application/json",
          },
          body: JSON.stringify(payload),
        });

        setMsg("Cari kart güncellendi.");
      } else {
        await api("current-accounts", {
          method: "POST",
          headers: {
            "Content-Type": "application/json",
          },
          body: JSON.stringify(payload),
        });

        setMsg("Cari kart oluşturuldu.");
      }

      const preservedCompanyId = form.companyId;

      setEditingId(null);
      setShow(false);
      setForm({
        ...blank,
        companyId: preservedCompanyId,
      });

      await load();
    } catch (error) {
      setErr(
        error instanceof Error
          ? error.message
          : "Kayıt başarısız."
      );
    } finally {
      setSaving(false);
    }
  }

  async function act(
    id: string,
    action: "submit" | "approve"
  ) {
    try {
      setMsg("");
      setErr("");

      const result = await api(
        `current-accounts/${id}/${action}`,
        { method: "POST" }
      );

      setMsg(result.message);
      await load();
    } catch (error) {
      setErr(
        error instanceof Error
          ? error.message
          : "İşlem başarısız."
      );
    }
  }

  async function synchronizeAccounting() {
    const companyId =
      form.companyId || companies[0]?.id;

    if (!companyId) {
      setErr(
        "Muhasebe eşleştirmesi için şirket bulunamadı."
      );
      return;
    }

    const company = companies.find(
      (item) => item.id === companyId
    );

    if (
      !window.confirm(
        `${company?.name ?? "Seçili şirket"} için cari muhasebe hesapları eşleştirilsin mi?`
      )
    ) {
      return;
    }

    try {
      setSyncing(true);
      setMsg("");
      setErr("");

      const result = await api(
        `current-accounts/synchronize-accounting?companyId=${encodeURIComponent(companyId)}`,
        { method: "POST" }
      );

      setMsg(
        result.message ??
          "Muhasebe hesapları eşleştirildi."
      );

      await load();
    } catch (error) {
      setErr(
        error instanceof Error
          ? error.message
          : "Muhasebe eşleştirmesi başarısız."
      );
    } finally {
      setSyncing(false);
    }
  }

  return (
    <ErpShell title="Cari Kartlar">
      <div className="erp-toolbar">
        <strong>{items.length} cari kart</strong>

        <div className="erp-actions">
          <button
            type="button"
            disabled={syncing || !form.companyId}
            onClick={synchronizeAccounting}
          >
            {syncing
              ? "Eşleştiriliyor..."
              : "Muhasebe Hesaplarını Eşleştir"}
          </button>

          <button type="button" onClick={startCreate}>
            + Yeni Cari Kart
          </button>
        </div>
      </div>

      {msg && (
        <div className="erp-alert success">{msg}</div>
      )}

      {err && (
        <div className="erp-alert error">{err}</div>
      )}

      {show && (
        <form className="erp-form-card" onSubmit={save}>
          <h3>
            {editingId
              ? "Cari Kart Düzenle"
              : "Yeni Cari Kart"}
          </h3>

          <div className="erp-form-grid">
            <label className="span-2">
              <span>Şirket *</span>
              <select
                required
                disabled={Boolean(editingId)}
                value={form.companyId}
                onChange={(event) =>
                  setForm({
                    ...form,
                    companyId: event.target.value,
                  })
                }
              >
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

            <label>
              <span>Kod *</span>
              <input
                required
                disabled={Boolean(editingId)}
                value={form.code}
                onChange={(event) =>
                  setForm({
                    ...form,
                    code: event.target.value.toUpperCase(),
                  })
                }
              />
            </label>

            <label>
              <span>Kısa Ad</span>
              <input
                value={form.shortName}
                onChange={(event) =>
                  setForm({
                    ...form,
                    shortName: event.target.value,
                  })
                }
              />
            </label>

            <label className="span-2">
              <span>Ünvan *</span>
              <input
                required
                value={form.title}
                onChange={(event) =>
                  setForm({
                    ...form,
                    title: event.target.value,
                  })
                }
              />
            </label>

            <div className="span-2 erp-role-grid">
              {roleOptions.map(([value, label]) => (
                <label key={value}>
                  <input
                    type="checkbox"
                    checked={
                      (form.roles & value) === value
                    }
                    onChange={() => toggleRole(value)}
                  />
                  {label}
                </label>
              ))}
            </div>

            <label>
              <span>Vergi Dairesi</span>
              <input
                value={form.taxOffice}
                onChange={(event) =>
                  setForm({
                    ...form,
                    taxOffice: event.target.value,
                  })
                }
              />
            </label>

            <label>
              <span>Vergi No</span>
              <input
                value={form.taxNumber}
                onChange={(event) =>
                  setForm({
                    ...form,
                    taxNumber: event.target.value,
                  })
                }
              />
            </label>

            <label>
              <span>Yetkili Kişi</span>
              <input
                value={form.authorizedPerson}
                onChange={(event) =>
                  setForm({
                    ...form,
                    authorizedPerson: event.target.value,
                  })
                }
              />
            </label>

            <label>
              <span>Telefon</span>
              <input
                value={form.phone}
                onChange={(event) =>
                  setForm({
                    ...form,
                    phone: event.target.value,
                  })
                }
              />
            </label>

            <label>
              <span>E-posta</span>
              <input
                type="email"
                value={form.email}
                onChange={(event) =>
                  setForm({
                    ...form,
                    email: event.target.value,
                  })
                }
              />
            </label>

            <label>
              <span>Vade</span>
              <input
                value={form.paymentTerm}
                onChange={(event) =>
                  setForm({
                    ...form,
                    paymentTerm: event.target.value,
                  })
                }
              />
            </label>

            <label>
              <span>Kredi Limiti</span>
              <input
                type="number"
                min="0"
                step="0.01"
                value={form.creditLimit}
                onChange={(event) =>
                  setForm({
                    ...form,
                    creditLimit: event.target.value,
                  })
                }
              />
            </label>

            <label className="span-2">
              <span>Adres</span>
              <textarea
                value={form.address}
                onChange={(event) =>
                  setForm({
                    ...form,
                    address: event.target.value,
                  })
                }
              />
            </label>
          </div>

          <div className="erp-actions">
            <button
              type="button"
              onClick={cancelForm}
              disabled={saving}
            >
              İptal
            </button>

            <button type="submit" disabled={saving}>
              {saving
                ? "Kaydediliyor..."
                : editingId
                  ? "Değişiklikleri Kaydet"
                  : "Taslak Kaydet"}
            </button>
          </div>
        </form>
      )}

      <div className="erp-table-card">
        <table className="erp-table">
          <thead>
            <tr>
              <th>Kod</th>
              <th>Ünvan</th>
              <th>Şirket</th>
              <th>120 Alıcı</th>
              <th>320 Satıcı</th>
              <th>Bakiye</th>
              <th>Durum</th>
              <th>İşlemler</th>
            </tr>
          </thead>

          <tbody>
            {items.map((account) => (
              <tr key={account.id}>
                <td>{account.code}</td>
                <td>{account.title}</td>
                <td>{account.companyName}</td>

                <td>
                  <span
                    className={
                      account.receivableAccountingAccountId
                        ? "erp-status green"
                        : "erp-status gray"
                    }
                  >
                    {account.receivableAccountingAccountId
                      ? "Bağlı"
                      : "Bağlı Değil"}
                  </span>
                </td>

                <td>
                  <span
                    className={
                      account.payableAccountingAccountId
                        ? "erp-status green"
                        : "erp-status gray"
                    }
                  >
                    {account.payableAccountingAccountId
                      ? "Bağlı"
                      : "Bağlı Değil"}
                  </span>
                </td>

                <td>
                  {balances[account.id] === undefined ? (
                    <span className="erp-status gray">Hareket yok</span>
                  ) : (
                    <>
                      <strong>
                        {new Intl.NumberFormat("tr-TR", {
                          style: "currency",
                          currency: "TRY",
                        }).format(Math.abs(balances[account.id]))}
                      </strong>
                      <small>
                        {balances[account.id] === 0
                          ? "Kapalı"
                          : balances[account.id] > 0
                            ? "Borç"
                            : "Alacak"}
                      </small>
                    </>
                  )}
                </td>

                <td>
                  <span
                    className={
                      account.status === 2
                        ? "erp-status green"
                        : account.status === 1
                          ? "erp-status yellow"
                          : "erp-status gray"
                    }
                  >
                    {[
                      "Taslak",
                      "Onay Bekliyor",
                      "Onaylandı",
                      "Askıda",
                      "Pasif",
                    ][account.status] ?? "Bilinmiyor"}
                  </span>
                </td>

                <td>
                  <div className="erp-actions">
                    <button
                      type="button"
                      onClick={() => startEdit(account)}
                    >
                      Düzenle
                    </button>

                    {account.status === 0 && (
                      <button
                        type="button"
                        onClick={() =>
                          act(account.id, "submit")
                        }
                      >
                        Onaya Gönder
                      </button>
                    )}

                    {account.status === 1 && (
                      <button
                        type="button"
                        onClick={() =>
                          act(account.id, "approve")
                        }
                      >
                        Onayla
                      </button>
                    )}

                    {account.status === 2 && (
                      <span className="erp-status green">
                        Kullanılabilir
                      </span>
                    )}

                    <Link
                      className="erp-secondary-button"
                      href={`/cariler/${account.id}/ekstre`}
                    >
                      Ekstre
                    </Link>
                  </div>
                </td>
              </tr>
            ))}

            {items.length === 0 && (
              <tr>
                <td colSpan={7}>
                  Henüz cari kart bulunmuyor.
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>
    </ErpShell>
  );
}
