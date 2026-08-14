"use client";

import Link from "next/link";
import {
  useCallback,
  useEffect,
  useMemo,
  useState,
} from "react";

import ErpShell from "@/components/erp/erp-shell";
import { ConfirmDialog } from "@/components/ui";

import {
  accountingAccountService,
  type AccountingAccountListItem,
  type AccountingAccountNature,
} from "@/services/accounting-account.service";

import {
  companyService,
  type CompanyListItem,
} from "@/services/company.service";

const natureLabels: Record<
  AccountingAccountNature,
  string
> = {
  0: "Borç",
  1: "Alacak",
  2: "Borç / Alacak",
};

const natureClasses: Record<
  AccountingAccountNature,
  string
> = {
  0: "blue",
  1: "green",
  2: "yellow",
};

type ActiveFilter = "all" | "active" | "passive";

type TreeNode = AccountingAccountListItem & {
  children: TreeNode[];
};

type VisibleRow = {
  item: TreeNode;
  depth: number;
};

function normalizeSearch(value: string) {
  return value
    .trim()
    .toLocaleLowerCase("tr-TR");
}

function compareCodes(left: string, right: string) {
  return left.localeCompare(
    right,
    "tr",
    {
      numeric: true,
      sensitivity: "base",
    }
  );
}

export default function AccountingAccountsPage() {
  const [companies, setCompanies] = useState<
    CompanyListItem[]
  >([]);

  const [items, setItems] = useState<
    AccountingAccountListItem[]
  >([]);

  const [companyId, setCompanyId] = useState("");
  const [search, setSearch] = useState("");

  const [activeFilter, setActiveFilter] =
    useState<ActiveFilter>("active");

  const [expandedIds, setExpandedIds] =
    useState<Set<string>>(new Set());

  const [loading, setLoading] = useState(true);
  const [seeding, setSeeding] = useState(false);

  const [message, setMessage] = useState("");
  const [error, setError] = useState("");
  const [seedOpen, setSeedOpen] = useState(false);
  const [deactivating, setDeactivating] =
    useState<AccountingAccountListItem | null>(null);

  const loadCompanies = useCallback(async () => {
    try {
      const result = await companyService.getAll();

      setCompanies(result);

      setCompanyId((current) => {
        if (current) {
          return current;
        }

        return (
          result.find(
            (company) =>
              company.isActive !== false
          )?.id ??
          result[0]?.id ??
          ""
        );
      });
    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : "Şirketler alınamadı."
      );
    }
  }, []);

  const loadAccounts = useCallback(async () => {
    if (!companyId) {
      setItems([]);
      setLoading(false);
      return;
    }

    setLoading(true);
    setError("");

    try {
      const isActive =
        activeFilter === "all"
          ? undefined
          : activeFilter === "active";

      const result =
        await accountingAccountService.getAll({
          companyId,
          isActive,
        });

      setItems(result);
    } catch (err) {
      setItems([]);

      setError(
        err instanceof Error
          ? err.message
          : "Hesap planı alınamadı."
      );
    } finally {
      setLoading(false);
    }
  }, [activeFilter, companyId]);

  useEffect(() => {
    void loadCompanies();
  }, [loadCompanies]);

  useEffect(() => {
    void loadAccounts();
  }, [loadAccounts]);

  const itemById = useMemo(() => {
    return new Map(
      items.map((item) => [item.id, item])
    );
  }, [items]);

  const tree = useMemo(() => {
    const nodeById = new Map<string, TreeNode>();

    for (const item of items) {
      nodeById.set(item.id, {
        ...item,
        children: [],
      });
    }

    const roots: TreeNode[] = [];

    for (const node of nodeById.values()) {
      const parentId = node.parentAccountId;

      if (
        parentId &&
        nodeById.has(parentId)
      ) {
        nodeById
          .get(parentId)!
          .children.push(node);
      } else {
        roots.push(node);
      }
    }

    const sortNodes = (nodes: TreeNode[]) => {
      nodes.sort((left, right) =>
        compareCodes(left.code, right.code)
      );

      for (const node of nodes) {
        sortNodes(node.children);
      }
    };

    sortNodes(roots);

    return roots;
  }, [items]);

  useEffect(() => {
    if (items.length === 0) {
      setExpandedIds(new Set());
      return;
    }

    setExpandedIds((current) => {
      if (current.size > 0) {
        return current;
      }

      return new Set(
        items
          .filter((item) => item.level <= 2)
          .map((item) => item.id)
      );
    });
  }, [items]);

  const summary = useMemo(() => {
    return {
      total: items.length,

      posting: items.filter(
        (item) => item.isPostingAllowed
      ).length,

      group: items.filter(
        (item) => !item.isPostingAllowed
      ).length,

      projectRequired: items.filter(
        (item) => item.requiresProject
      ).length,
    };
  }, [items]);

  const searchState = useMemo(() => {
    const normalized = normalizeSearch(search);

    if (!normalized) {
      return {
        matchingIds: new Set<string>(),
        visibleIds: null as Set<string> | null,
        forcedExpandedIds: new Set<string>(),
      };
    }

    const matchingIds = new Set<string>();
    const visibleIds = new Set<string>();
    const forcedExpandedIds = new Set<string>();

    for (const item of items) {
      const haystack =
        `${item.code} ${item.name}`
          .toLocaleLowerCase("tr-TR");

      if (!haystack.includes(normalized)) {
        continue;
      }

      matchingIds.add(item.id);
      visibleIds.add(item.id);

      let parentId = item.parentAccountId;

      while (parentId) {
        visibleIds.add(parentId);
        forcedExpandedIds.add(parentId);

        parentId =
          itemById.get(parentId)
            ?.parentAccountId ?? null;
      }
    }

    return {
      matchingIds,
      visibleIds,
      forcedExpandedIds,
    };
  }, [itemById, items, search]);

  const visibleRows = useMemo(() => {
    const result: VisibleRow[] = [];

    const isSearching =
      searchState.visibleIds !== null;

    const walk = (
      nodes: TreeNode[],
      depth: number
    ) => {
      for (const node of nodes) {
        if (
          isSearching &&
          !searchState.visibleIds?.has(node.id)
        ) {
          continue;
        }

        result.push({
          item: node,
          depth,
        });

        const isExpanded =
          expandedIds.has(node.id) ||
          searchState.forcedExpandedIds.has(
            node.id
          );

        if (isExpanded) {
          walk(node.children, depth + 1);
        }
      }
    };

    walk(tree, 0);

    return result;
  }, [
    expandedIds,
    searchState,
    tree,
  ]);

  function toggleExpanded(id: string) {
    setExpandedIds((current) => {
      const next = new Set(current);

      if (next.has(id)) {
        next.delete(id);
      } else {
        next.add(id);
      }

      return next;
    });
  }

  function expandAll() {
    setExpandedIds(
      new Set(
        items
          .filter((item) => item.childCount > 0)
          .map((item) => item.id)
      )
    );
  }

  function collapseAll() {
    setExpandedIds(new Set());
  }

  async function seedStandardPlan() {
    if (!companyId) {
      setError("Önce şirket seçmelisiniz.");
      return;
    }

    setSeedOpen(false);

    setSeeding(true);
    setMessage("");
    setError("");

    try {
      const result =
        await accountingAccountService
          .seedStandardPlan(companyId);

      setMessage(result.message);
      await loadAccounts();
    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : "Enderun hesap planı kurulamadı."
      );
    } finally {
      setSeeding(false);
    }
  }

  async function deactivateAccount(
    account: AccountingAccountListItem
  ) {
    setDeactivating(null);

    setMessage("");
    setError("");

    try {
      const result =
        await accountingAccountService
          .deactivate(account.id);

      setMessage(result.message);
      await loadAccounts();
    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : "Hesap pasife alınamadı."
      );
    }
  }

  return (
    <ErpShell
      design="redwood"
      title="Hesap Planı"
      description="Enderun hesap planını ağaç yapısında yönetin."
    >
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

      <section className="erp-form-card">
        <div
          style={{
            display: "flex",
            gap: 10,
            flexWrap: "wrap",
            justifyContent: "space-between",
            alignItems: "center",
            marginBottom: 16,
          }}
        >
          <div
            style={{
              display: "flex",
              gap: 8,
              flexWrap: "wrap",
            }}
          >
            <Link
              href="/muhasebe/hesap-plani/yeni"
              className="erp-secondary-button"
              style={{ textDecoration: "none" }}
            >
              + Yeni Hesap
            </Link>

            <button
              type="button"
              className="erp-secondary-button"
              disabled={seeding || !companyId}
              onClick={() =>
                setSeedOpen(true)
              }
            >
              {seeding
                ? "Kuruluyor..."
                : "Enderun Hesap Planını Kur"}
            </button>
          </div>

          <div
            style={{
              display: "flex",
              gap: 8,
              flexWrap: "wrap",
            }}
          >
            <button
              type="button"
              className="erp-secondary-button"
              onClick={expandAll}
            >
              Tümünü Aç
            </button>

            <button
              type="button"
              className="erp-secondary-button"
              onClick={collapseAll}
            >
              Tümünü Kapat
            </button>
          </div>
        </div>

        <div className="erp-form-grid">
          <label>
            <span>Şirket</span>

            <select
              value={companyId}
              onChange={(event) => {
                setCompanyId(event.target.value);
                setExpandedIds(new Set());
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
                  {company.code} - {company.name}
                </option>
              ))}
            </select>
          </label>

          <label>
            <span>Hesap Ara</span>

            <input
              value={search}
              placeholder="Kod veya hesap adı"
              onChange={(event) =>
                setSearch(event.target.value)
              }
            />
          </label>

          <label>
            <span>Durum</span>

            <select
              value={activeFilter}
              onChange={(event) => {
                setActiveFilter(
                  event.target.value as ActiveFilter
                );

                setExpandedIds(new Set());
              }}
            >
              <option value="active">
                Aktif
              </option>

              <option value="passive">
                Pasif
              </option>

              <option value="all">
                Tümü
              </option>
            </select>
          </label>
        </div>
      </section>

      <section
        style={{
          display: "grid",
          gridTemplateColumns:
            "repeat(auto-fit, minmax(190px, 1fr))",
          gap: 12,
          margin: "16px 0",
        }}
      >
        <SummaryCard
          label="Toplam Hesap"
          value={summary.total}
        />

        <SummaryCard
          label="Kayıt Hesabı"
          value={summary.posting}
        />

        <SummaryCard
          label="Grup Hesabı"
          value={summary.group}
        />

        <SummaryCard
          label="Proje Zorunlu"
          value={summary.projectRequired}
        />
      </section>

      <div className="erp-table-card">
        <div
          style={{
            display: "flex",
            justifyContent: "space-between",
            gap: 12,
            padding: "14px 16px",
            borderBottom:
              "1px solid var(--erp-border)",
          }}
        >
          <strong>
            Hesap Ağacı
          </strong>

          <small>
            Görüntülenen: {visibleRows.length}
          </small>
        </div>

        <table className="erp-table">
          <thead>
            <tr>
              <th>Hesap</th>
              <th>Karakter</th>
              <th>Kullanım</th>
              <th>Kontroller</th>
              <th>Durum</th>
              <th>İşlem</th>
            </tr>
          </thead>

          <tbody>
            {loading && (
              <tr>
                <td colSpan={6}>
                  Hesap planı yükleniyor...
                </td>
              </tr>
            )}

            {!loading &&
              visibleRows.length === 0 && (
                <tr>
                  <td colSpan={6}>
                    Hesap planında kayıt bulunmuyor.
                  </td>
                </tr>
              )}

            {!loading &&
              visibleRows.map(
                ({ item, depth }) => {
                  const hasChildren =
                    item.children.length > 0;

                  const isExpanded =
                    expandedIds.has(item.id) ||
                    searchState.forcedExpandedIds.has(
                      item.id
                    );

                  const isMatch =
                    searchState.matchingIds.has(
                      item.id
                    );

                  return (
                    <tr
                      key={item.id}
                      className={
                        isMatch
                          ? "rw-row-match"
                          : item.isPostingAllowed
                            ? ""
                            : "rw-row-passive"
                      }
                    >
                      <td>
                        <div
                          style={{
                            display: "flex",
                            alignItems: "flex-start",
                            gap: 8,
                            paddingLeft: depth * 20,
                          }}
                        >
                          <button
                            type="button"
                            onClick={() =>
                              hasChildren &&
                              toggleExpanded(item.id)
                            }
                            disabled={!hasChildren}
                            aria-label={
                              isExpanded
                                ? "Alt hesapları kapat"
                                : "Alt hesapları aç"
                            }
                            className="rw-icon-button"
                          >
                            {hasChildren
                              ? isExpanded
                                ? "▾"
                                : "▸"
                              : "•"}
                          </button>

                          <div>
                            <div
                              style={{
                                display: "flex",
                                gap: 8,
                                flexWrap: "wrap",
                                alignItems: "center",
                              }}
                            >
                              <strong>
                                {item.code}
                              </strong>

                              <span>
                                {item.name}
                              </span>
                            </div>

                            <small>
                              Seviye {item.level}

                              {hasChildren
                                ? ` · ${item.children.length} alt hesap`
                                : ` · ${item.currencyCode ?? "TRY"}`}
                            </small>
                          </div>
                        </div>
                      </td>

                      <td>
                        <span
                          className={`erp-status ${
                            natureClasses[
                              item.nature
                            ]
                          }`}
                        >
                          {
                            natureLabels[
                              item.nature
                            ]
                          }
                        </span>
                      </td>

                      <td>
                        <span
                          className={`erp-status ${
                            item.isPostingAllowed
                              ? "green"
                              : "blue"
                          }`}
                        >
                          {item.isPostingAllowed
                            ? "Kayıt Hesabı"
                            : "Grup Hesabı"}
                        </span>
                      </td>

                      <td>
                        <small>
                          {item.requiresProject
                            ? "Proje zorunlu"
                            : "Proje serbest"}
                        </small>

                        <small>
                          {item.requiresCostCenter
                            ? "Masraf merkezi zorunlu"
                            : "Masraf merkezi serbest"}
                        </small>
                      </td>

                      <td>
                        <span
                          className={`erp-status ${
                            item.isActive
                              ? "green"
                              : "red"
                          }`}
                        >
                          {item.isActive
                            ? "Aktif"
                            : "Pasif"}
                        </span>
                      </td>

                      <td>
                        <div className="erp-actions">
                          <Link
                            href={`/muhasebe/hesap-plani/${item.id}`}
                            className="erp-secondary-button"
                            style={{
                              textDecoration: "none",
                            }}
                          >
                            Detay
                          </Link>

                          {item.isActive && (
                            <button
                              type="button"
                              className="erp-secondary-button"
                              onClick={() =>
setDeactivating(item)
                              }
                            >
                              Pasife Al
                            </button>
                          )}
                        </div>
                      </td>
                    </tr>
                  );
                }
              )}
          </tbody>
        </table>
      </div>
      <ConfirmDialog
        open={seedOpen}
        title="Enderun hesap planı kurulsun mu?"
        description={`${
          companies.find((item) => item.id === companyId)?.name ??
          "Seçili şirket"
        } için standart hesap planı oluşturulur. Var olan hesaplar korunur.`}
        confirmLabel="Kur"
        busy={seeding}
        onCancel={() => setSeedOpen(false)}
        onConfirm={() => void seedStandardPlan()}
      />

      <ConfirmDialog
        open={deactivating !== null}
        title="Hesap pasife alınsın mı?"
        description={
          deactivating
            ? `${deactivating.code} — ${deactivating.name} yeni fişlerde seçilemez. Geçmiş kayıtlar defterde kalır.`
            : ""
        }
        confirmLabel="Pasife Al"
        onCancel={() => setDeactivating(null)}
        onConfirm={() => {
          if (deactivating) void deactivateAccount(deactivating);
        }}
      />

    </ErpShell>
  );
}

function SummaryCard({
  label,
  value,
}: {
  label: string;
  value: number;
}) {
  return (
    <div className="erp-form-card">
      <small>{label}</small>

      <strong
        style={{
          display: "block",
          marginTop: 7,
          fontSize: 26,
        }}
      >
        {value}
      </strong>
    </div>
  );
}
