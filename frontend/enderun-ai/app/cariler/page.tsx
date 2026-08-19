"use client";

import {
  FormEvent,
  useCallback,
  useEffect,
  useMemo,
  useState,
} from "react";

import {
  DataTable,
  type DataTableColumn,
} from "@/components/ui/data-table";
import Link from "next/link";
import ErpShell from "@/components/erp/erp-shell";
import { Button, ConfirmDialog, Drawer } from "@/components/ui";
import { amount, money } from "@/lib/format/turkish";
import { matchesSearch } from "@/lib/search/fold";

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

/**
 * Cari bakiyesi. `balance` TL defter değeridir; `currencyBalances`
 * dövizin kendi tutarını taşır — dövizli caride TL toplam kur
 * değiştikçe oynadığı için tek başına borcu göstermez.
 */
type BalanceRow = {
  currentAccountId: string;
  balance: number;
  hasForeignCurrency?: boolean;
  currencyBalances?: {
    currencyCode: string;
    balance: number;
    balanceLocal: number;
  }[];
};

/**
 * Tutarı kendi para biriminde yazar.
 *
 * SİMGE/KOD SONDA: `Intl` para biçimi Türkçede bile bazı kodları başa
 * koyuyordu ("$1.250,00"). Sağa hizalı bir sütunda öne gelen simge
 * rakamları kaydırır ve iki satırın basamakları hizalanmaz. Sayı
 * biçimi paylaşılan `turkishFormat`'tan geliyor; bu ekran kendi
 * biçimleyicisini kurmuyor.
 */
function formatCurrency(value: number, code: string) {
  return code === "TRY" ? money(value) : `${amount(value)} ${code}`;
}

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

const STATUS_LABELS = [
  "Taslak",
  "Onay Bekliyor",
  "Onaylandı",
  "Askıda",
  "Pasif",
] as const;

/**
 * Durum rengi ANLAMA bağlı, sıraya değil: onaylı yeşil, bekleyen
 * sarı (bir işlem gerektiriyor), askıda kırmızı, kalanı nötr.
 */
function statusTone(status: number) {
  if (status === 2) return "green";
  if (status === 1) return "yellow";
  if (status === 3) return "red";
  return "gray";
}

function roleLabels(roles: number) {
  return roleOptions
    .filter(([value]) => (roles & value) === value)
    .map(([, label]) => label);
}

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
  const [balances, setBalances] = useState<Record<string, BalanceRow>>({});
  const [form, setForm] = useState<FormState>(blank);
  const [show, setShow] = useState(false);
  const [editingId, setEditingId] = useState<string | null>(null);
  const [dirty, setDirty] = useState(false);
  const [syncing, setSyncing] = useState(false);
  const [confirmingSync, setConfirmingSync] = useState(false);
  const [saving, setSaving] = useState(false);
  const [msg, setMsg] = useState("");
  const [err, setErr] = useState("");

  const [search, setSearch] = useState("");
  const [roleFilter, setRoleFilter] = useState("0");
  const [statusFilter, setStatusFilter] = useState("");

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
          (balanceList as BalanceRow[]).map((row) => [
            row.currentAccountId,
            row,
          ])
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

  /**
   * Süzgeç. ARAMA KUTUSU EKLENDİ: bu ekranda hiç yoktu ve liste
   * kayıt sayısı kadar uzuyordu — yüzlerce cari arasında bir tanesini
   * bulmanın tek yolu tarayıcının Ctrl+F'iydi, o da sayfalanmamış
   * listenin tamamı yüklüyse çalışıyordu.
   *
   * Kod, ünvan, kısa ad, vergi no ve yetkili birlikte aranır: kullanıcı
   * hangisini hatırlıyorsa onu yazar.
   */
  const visible = useMemo(() => {
    const role = Number(roleFilter);

    return items.filter((account) => {
      if (role && (account.roles & role) !== role) return false;

      if (statusFilter !== "" && account.status !== Number(statusFilter)) {
        return false;
      }

      return matchesSearch(
        search,
        account.code,
        account.title,
        account.shortName,
        account.taxNumber,
        account.authorizedPerson,
        account.companyName,
      );
    });
  }, [items, search, roleFilter, statusFilter]);

  const filtered = visible.length !== items.length;

  function toggleRole(value: number) {
    setDirty(true);
    setForm((current) => ({
      ...current,
      roles:
        ((current.roles & value) === value
          ? current.roles & ~value
          : current.roles | value) || 1,
    }));
  }

  function update(patch: Partial<FormState>) {
    setDirty(true);
    setForm((current) => ({ ...current, ...patch }));
  }

  function startCreate() {
    setEditingId(null);
    setForm({
      ...blank,
      companyId: form.companyId || companies[0]?.id || "",
    });
    setDirty(false);
    setMsg("");
    setErr("");
    setShow(true);
  }

  function startEdit(account: Account) {
    setEditingId(account.id);
    setForm(toForm(account));
    setDirty(false);
    setMsg("");
    setErr("");
    setShow(true);
  }

  function cancelForm() {
    setEditingId(null);
    setShow(false);
    setDirty(false);
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
      setDirty(false);
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

  const syncCompany = companies.find(
    (item) => item.id === (form.companyId || companies[0]?.id)
  );

  async function synchronizeAccounting() {
    const companyId = form.companyId || companies[0]?.id;

    if (!companyId) {
      setConfirmingSync(false);
      setErr("Muhasebe eşleştirmesi için şirket bulunamadı.");
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

      setConfirmingSync(false);
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


  /*
   * Bakiye sütunu `balances` üzerine kapanıyor ve eylem sütunu
   * duruma bağlı, o yüzden sütunlar bileşen içinde tanımlanıyor
   * (belleğe ALINMIYOR — bkz. F4b desen kararı: bayat kapanış riski).
   */
  const columns: DataTableColumn<Account>[] = [
    { key: "kod", header: "Kod", value: (account) => account.code },
    {
      key: "unvan",
      header: "Ünvan",
      value: (account) =>
        `${account.title} (${roleLabels(account.roles).join(" · ")})`,
      render: (account) => (
        <>
          <strong>{account.title}</strong>
          {/* Roller kartın en ayırt edici bilgisi: aynı ünvanlı bir
              cari hem müşteri hem tedarikçi olabiliyor. */}
          <small>{roleLabels(account.roles).join(" · ")}</small>
        </>
      ),
    },
    { key: "sirket", header: "Şirket", value: (account) => account.companyName },
    {
      key: "alici",
      header: "120 Alıcı",
      value: (account) =>
        account.receivableAccountingAccountId ? "Bağlı" : "Bağlı Değil",
      render: (account) => (
        <span
          className={
            account.receivableAccountingAccountId
              ? "erp-status green"
              : "erp-status gray"
          }
        >
          {account.receivableAccountingAccountId ? "Bağlı" : "Bağlı Değil"}
        </span>
      ),
    },
    {
      key: "satici",
      header: "320 Satıcı",
      value: (account) =>
        account.payableAccountingAccountId ? "Bağlı" : "Bağlı Değil",
      render: (account) => (
        <span
          className={
            account.payableAccountingAccountId
              ? "erp-status green"
              : "erp-status gray"
          }
        >
          {account.payableAccountingAccountId ? "Bağlı" : "Bağlı Değil"}
        </span>
      ),
    },
    {
      key: "bakiye",
      header: "Bakiye",
      numeric: true,
      value: (account) => {
        const row = balances[account.id];
        if (row === undefined) return "Hareket yok";

        const yon =
          row.balance === 0 ? "Kapalı" : row.balance > 0 ? "Borç" : "Alacak";

        return `${money(Math.abs(row.balance))} ${yon}`;
      },
      render: (account) =>
        balances[account.id] === undefined ? (
          <span className="erp-status gray">Hareket yok</span>
        ) : (
          <>
            <strong>{money(Math.abs(balances[account.id].balance))}</strong>
            <small>
              {balances[account.id].balance === 0
                ? "Kapalı"
                : balances[account.id].balance > 0
                  ? "Borç"
                  : "Alacak"}
            </small>
            {/* Dövizli caride TL toplam kurla oynadığı için tek başına
                yanıltıcı; dövizin kendi tutarı da yazılır. */}
            {balances[account.id].hasForeignCurrency &&
              (balances[account.id].currencyBalances ?? [])
                .filter((row) => row.currencyCode !== "TRY")
                .map((row) => (
                  <small key={row.currencyCode}>
                    {formatCurrency(Math.abs(row.balance), row.currencyCode)}{" "}
                    {row.balance >= 0 ? "borç" : "alacak"}
                  </small>
                ))}
          </>
        ),
    },
    {
      key: "durum",
      header: "Durum",
      value: (account) => STATUS_LABELS[account.status] ?? "Bilinmiyor",
      render: (account) => (
        <span className={`erp-status ${statusTone(account.status)}`}>
          {STATUS_LABELS[account.status] ?? "Bilinmiyor"}
        </span>
      ),
    },
    {
      key: "islemler",
      header: "İşlemler",
      value: () => "",
      render: (account) => (
        <div className="erp-actions">
          <button type="button" onClick={() => startEdit(account)}>
            Düzenle
          </button>

          {account.status === 0 && (
            <button type="button" onClick={() => act(account.id, "submit")}>
              Onaya Gönder
            </button>
          )}

          {account.status === 1 && (
            <button type="button" onClick={() => act(account.id, "approve")}>
              Onayla
            </button>
          )}

          <Link
            className="erp-secondary-button"
            href={`/cariler/${account.id}/ekstre`}
          >
            Ekstre
          </Link>
        </div>
      ),
    },
  ];


  return (
    <ErpShell
      title="Cari Kartlar"
      description="Müşteri, tedarikçi ve alt yüklenici kartları ile muhasebe bağlantıları."
      design="redwood"
    >
      <div className="erp-toolbar rw-toolbar-end">
        <div className="erp-actions">
          <button
            type="button"
            className="erp-secondary-button"
            disabled={syncing || companies.length === 0}
            onClick={() => setConfirmingSync(true)}
          >
            Muhasebe Hesaplarını Eşleştir
          </button>

          <button
            type="button"
            className="erp-primary-button"
            onClick={startCreate}
          >
            + Yeni Cari Kart
          </button>
        </div>
      
        <Button variant="secondary" onClick={() => void load()}>Yenile</Button>
      </div>

      {msg && (
        <div className="erp-alert success">{msg}</div>
      )}

      {err && (
        <div className="erp-alert error">{err}</div>
      )}

      <div className="erp-table-card">
        <div className="rw-filters">
          <label className="rw-filter-search">
            <span>Ara</span>
            <input
              type="search"
              value={search}
              onChange={(event) => setSearch(event.target.value)}
              placeholder="Kod, ünvan, kısa ad, vergi no veya yetkili"
              aria-label="Cari kart ara"
            />
          </label>

          <label>
            <span>Rol</span>
            <select
              value={roleFilter}
              onChange={(event) => setRoleFilter(event.target.value)}
              aria-label="Role göre süz"
            >
              <option value="0">Tümü</option>
              {roleOptions.map(([value, label]) => (
                <option key={value} value={value}>
                  {label}
                </option>
              ))}
            </select>
          </label>

          <label>
            <span>Durum</span>
            <select
              value={statusFilter}
              onChange={(event) => setStatusFilter(event.target.value)}
              aria-label="Duruma göre süz"
            >
              <option value="">Tümü</option>
              {STATUS_LABELS.map((label, index) => (
                <option key={label} value={index}>
                  {label}
                </option>
              ))}
            </select>
          </label>

          {/*
            Kullanıcı "hepsi bu kadar mı, yoksa süzgeç mi kesti"
            sorusunu ekrandan yanıtlayabilmeli.
          */}
          <span className="rw-filter-summary" data-testid="cari-sayisi">
            {filtered
              ? `${visible.length} / ${items.length} cari kart`
              : `${items.length} cari kart`}
          </span>
        </div>

        <div className="erp-table-wrap">
          <DataTable
              rows={visible}
              columns={columns}
              rowKey={(account) => account.id}
              title="Cari Hesaplar"
              emptyText="Cari hesap bulunamadı."
              resetKey={`${search}|${roleFilter}|${statusFilter}`}
            />
        </div>
      </div>

      {/*
        Form artık tablonun ÜSTÜNDE değil, sağdan kayan panelde.
        Eskiden form açılınca sayfa yukarı kaydırılıyordu (scrollTo) ve
        kullanıcı düzenlediği satırı gözden kaybediyordu; panelde liste
        arkada duruyor.
      */}
      <Drawer
        open={show}
        title={editingId ? "Cari Kart Düzenle" : "Yeni Cari Kart"}
        description={
          editingId
            ? "Şirket ve kod değiştirilemez; kart oluşturulduktan sonra muhasebe bağlantısı bunlara dayanır."
            : "Kart taslak olarak kaydedilir, onaya gönderildikten sonra kullanılabilir."
        }
        onClose={cancelForm}
        busy={saving}
        dirty={dirty}
        size="xl"
        footer={
          <div className="flex justify-end gap-3">
            <Button
              type="button"
              variant="secondary"
              onClick={cancelForm}
              disabled={saving}
            >
              Vazgeç
            </Button>

            {/* Düğme panelin altında, form ise içeriğinde: `form`
                özniteliği ikisini bağlar, ayrı bir gönderim yolu
                yazmaya gerek kalmaz. */}
            <Button type="submit" form="cari-form" loading={saving}>
              {editingId ? "Değişiklikleri Kaydet" : "Taslak Kaydet"}
            </Button>
          </div>
        }
      >
        <form id="cari-form" onSubmit={save}>
          <div className="erp-form-grid">
            <label className="span-2">
              <span>Şirket *</span>
              <select
                required
                disabled={Boolean(editingId)}
                value={form.companyId}
                onChange={(event) =>
                  update({ companyId: event.target.value })
                }
              >
                {companies.map((company) => (
                  <option key={company.id} value={company.id}>
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
                  update({ code: event.target.value.toUpperCase() })
                }
              />
            </label>

            <label>
              <span>Kısa Ad</span>
              <input
                value={form.shortName}
                onChange={(event) =>
                  update({ shortName: event.target.value })
                }
              />
            </label>

            <label className="span-2">
              <span>Ünvan *</span>
              <input
                required
                value={form.title}
                onChange={(event) =>
                  update({ title: event.target.value })
                }
              />
            </label>

            <div className="span-2 erp-role-grid">
              {roleOptions.map(([value, label]) => (
                <label key={value}>
                  <input
                    type="checkbox"
                    checked={(form.roles & value) === value}
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
                  update({ taxOffice: event.target.value })
                }
              />
            </label>

            <label>
              <span>Vergi No</span>
              <input
                value={form.taxNumber}
                onChange={(event) =>
                  update({ taxNumber: event.target.value })
                }
              />
            </label>

            <label>
              <span>Yetkili Kişi</span>
              <input
                value={form.authorizedPerson}
                onChange={(event) =>
                  update({ authorizedPerson: event.target.value })
                }
              />
            </label>

            <label>
              <span>Telefon</span>
              <input
                value={form.phone}
                onChange={(event) =>
                  update({ phone: event.target.value })
                }
              />
            </label>

            <label>
              <span>E-posta</span>
              <input
                type="email"
                value={form.email}
                onChange={(event) =>
                  update({ email: event.target.value })
                }
              />
            </label>

            <label>
              <span>Vade</span>
              <input
                value={form.paymentTerm}
                onChange={(event) =>
                  update({ paymentTerm: event.target.value })
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
                  update({ creditLimit: event.target.value })
                }
              />
            </label>

            <label className="span-2">
              <span>Adres</span>
              <textarea
                value={form.address}
                onChange={(event) =>
                  update({ address: event.target.value })
                }
              />
            </label>
          </div>
        </form>
      </Drawer>

      {/*
        window.confirm YERİNE: tarayıcı diyaloğu hangi şirket için
        çalışacağını biçimlendirilmiş şekilde gösteremiyor, işlem
        sürerken kilitleniyor ve hata mesajını aynı yerde veremiyordu.
      */}
      <ConfirmDialog
        open={confirmingSync}
        title="Muhasebe hesapları eşleştirilsin mi?"
        description={`${syncCompany?.name ?? "Seçili şirket"} için cari kartların 120/320 muhasebe hesapları oluşturulur ve eksik bağlantılar tamamlanır. Var olan bağlantılar korunur.`}
        confirmLabel="Eşleştir"
        busy={syncing}
        onCancel={() => setConfirmingSync(false)}
        onConfirm={() => void synchronizeAccounting()}
      />
    </ErpShell>
  );
}
