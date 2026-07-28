"use client";

import Link from "next/link";
import { useParams } from "next/navigation";
import {
  FormEvent,
  useCallback,
  useEffect,
  useMemo,
  useState,
} from "react";
import ErpShell from "@/components/erp/erp-shell";
import {
  ProjectHierarchyNode,
  ProjectHierarchyTree,
  ProjectModuleType,
  projectHierarchyService,
} from "@/services/project-hierarchy.service";

const moduleLabels: Record<ProjectModuleType, string> = {
  [ProjectModuleType.Hakedis]: "Hakediş",
  [ProjectModuleType.Personnel]: "Personel",
  [ProjectModuleType.Warehouse]: "Depo",
  [ProjectModuleType.Purchasing]: "Satın Alma",
  [ProjectModuleType.Finance]: "Finans",
};

type FlatNode = ProjectHierarchyNode & { depth: number };

function flattenNodes(
  nodes: ProjectHierarchyNode[],
  depth = 0
): FlatNode[] {
  return nodes.flatMap((node) => [
    { ...node, depth },
    ...flattenNodes(node.children, depth + 1),
  ]);
}

function HierarchyNodeCard({
  node,
  onDelete,
}: {
  node: ProjectHierarchyNode;
  onDelete: (node: ProjectHierarchyNode) => void;
}) {
  return (
    <li className="project-hierarchy-node">
      <div className="project-hierarchy-node-card">
        <div className="project-hierarchy-node-main">
          <span className="project-hierarchy-level-badge">
            {node.levelName}
          </span>
          <div>
            <strong>{node.name}</strong>
            <small>
              {node.code} · {node.path}
            </small>
          </div>
        </div>

        <div className="project-hierarchy-node-actions">
          {node.moduleScopes.map((scope) => (
            <span
              className="project-hierarchy-scope-badge"
              key={scope.moduleType}
            >
              {moduleLabels[scope.moduleType]} {scope.count}
            </span>
          ))}
          <button
            type="button"
            className="erp-link-button danger"
            onClick={() => onDelete(node)}
          >
            Sil
          </button>
        </div>
      </div>

      {node.children.length > 0 && (
        <ul className="project-hierarchy-children">
          {node.children.map((child) => (
            <HierarchyNodeCard
              key={child.id}
              node={child}
              onDelete={onDelete}
            />
          ))}
        </ul>
      )}
    </li>
  );
}

export default function ProjectHierarchyPage() {
  const params = useParams<{ id: string }>();
  const projectId = params.id;
  const [hierarchy, setHierarchy] =
    useState<ProjectHierarchyTree | null>(null);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [message, setMessage] = useState("");
  const [error, setError] = useState("");
  const [levelForm, setLevelForm] = useState({
    code: "",
    name: "",
    sortOrder: 10,
    isRequired: true,
  });
  const [nodeForm, setNodeForm] = useState({
    levelId: "",
    parentNodeId: "",
    code: "",
    name: "",
    description: "",
    sortOrder: 10,
  });

  const loadHierarchy = useCallback(async () => {
    if (!projectId) return;

    setLoading(true);
    setError("");
    try {
      const result = await projectHierarchyService.getTree(projectId);
      setHierarchy(result);
      setNodeForm((current) => ({
        ...current,
        levelId: current.levelId || result.levels[0]?.id || "",
      }));
      setLevelForm((current) => ({
        ...current,
        sortOrder:
          result.levels.length === 0
            ? 10
            : Math.max(...result.levels.map((level) => level.sortOrder)) +
              10,
      }));
    } catch (err) {
      setError(
        err instanceof Error
          ? err.message
          : "Proje hiyerarşisi yüklenemedi."
      );
    } finally {
      setLoading(false);
    }
  }, [projectId]);

  useEffect(() => {
    const timerId = window.setTimeout(() => {
      void loadHierarchy();
    }, 0);

    return () => window.clearTimeout(timerId);
  }, [loadHierarchy]);

  const flatNodes = useMemo(
    () => flattenNodes(hierarchy?.nodes ?? []),
    [hierarchy]
  );

  const selectedLevel = hierarchy?.levels.find(
    (level) => level.id === nodeForm.levelId
  );

  const parentOptions = useMemo(() => {
    if (!selectedLevel) return [];
    return flatNodes.filter(
      (node) => node.levelSortOrder < selectedLevel.sortOrder
    );
  }, [flatNodes, selectedLevel]);

  async function createLevel(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setSaving(true);
    setMessage("");
    setError("");

    try {
      await projectHierarchyService.createLevel(projectId, {
        code: levelForm.code.trim().toUpperCase(),
        name: levelForm.name.trim(),
        sortOrder: Number(levelForm.sortOrder),
        isRequired: levelForm.isRequired,
      });
      setLevelForm((current) => ({
        ...current,
        code: "",
        name: "",
      }));
      setMessage("Yeni hiyerarşi seviyesi oluşturuldu.");
      await loadHierarchy();
    } catch (err) {
      setError(
        err instanceof Error ? err.message : "Seviye oluşturulamadı."
      );
    } finally {
      setSaving(false);
    }
  }

  async function createNode(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setSaving(true);
    setMessage("");
    setError("");

    try {
      await projectHierarchyService.createNode(projectId, {
        levelId: nodeForm.levelId,
        parentNodeId: nodeForm.parentNodeId || null,
        code: nodeForm.code.trim().toUpperCase(),
        name: nodeForm.name.trim(),
        description: nodeForm.description.trim() || null,
        sortOrder: Number(nodeForm.sortOrder),
      });
      setNodeForm((current) => ({
        ...current,
        parentNodeId: "",
        code: "",
        name: "",
        description: "",
      }));
      setMessage("Yeni proje kırılımı oluşturuldu.");
      await loadHierarchy();
    } catch (err) {
      setError(
        err instanceof Error ? err.message : "Proje kırılımı oluşturulamadı."
      );
    } finally {
      setSaving(false);
    }
  }

  async function applyMkeTemplate() {
    if (
      !window.confirm(
        "Kırıkkale, Ankara ve Çankırı kırılımlarını içeren MKE şablonu uygulansın mı?"
      )
    ) {
      return;
    }

    setSaving(true);
    setMessage("");
    setError("");
    try {
      const result =
        await projectHierarchyService.applyMkeTemplate(projectId);
      setHierarchy(result.hierarchy);
      setMessage(
        `${result.createdLevelCount} seviye ve ${result.createdNodeCount} kırılım oluşturuldu.`
      );
    } catch (err) {
      setError(
        err instanceof Error ? err.message : "MKE şablonu uygulanamadı."
      );
    } finally {
      setSaving(false);
    }
  }

  async function deleteLevel(levelId: string, levelName: string) {
    if (!window.confirm(`${levelName} seviyesi silinsin mi?`)) return;

    setSaving(true);
    setMessage("");
    setError("");
    try {
      await projectHierarchyService.deleteLevel(projectId, levelId);
      setMessage("Hiyerarşi seviyesi silindi.");
      await loadHierarchy();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Seviye silinemedi.");
    } finally {
      setSaving(false);
    }
  }

  async function deleteNode(node: ProjectHierarchyNode) {
    if (!window.confirm(`${node.path} kırılımı silinsin mi?`)) return;

    setSaving(true);
    setMessage("");
    setError("");
    try {
      await projectHierarchyService.deleteNode(projectId, node.id);
      setMessage("Proje kırılımı silindi.");
      await loadHierarchy();
    } catch (err) {
      setError(
        err instanceof Error ? err.message : "Proje kırılımı silinemedi."
      );
    } finally {
      setSaving(false);
    }
  }

  return (
    <ErpShell
      title="Proje Hiyerarşisi"
      description={
        hierarchy
          ? `${hierarchy.projectCode} · ${hierarchy.projectName}`
          : "Şehir, fabrika, iş paketi, blok, kat ve mahal kırılımları"
      }
    >
      <div className="erp-project-breadcrumb">
        <Link href="/projeler">Projeler</Link>
        <span>›</span>
        <Link href={`/projeler/${projectId}`}>
          {hierarchy?.projectName ?? "Proje Merkezi"}
        </Link>
        <span>›</span>
        <strong>Hiyerarşi</strong>
      </div>

      <div className="erp-page-toolbar">
        <div>
          <strong>{hierarchy?.levels.length ?? 0} seviye</strong>
          <span> · {flatNodes.length} kırılım</span>
        </div>
        <button
          type="button"
          className="erp-secondary-button"
          disabled={saving || (hierarchy?.levels.length ?? 0) > 0}
          onClick={applyMkeTemplate}
        >
          MKE Örneğini Uygula
        </button>
      </div>

      {message && <div className="erp-alert success">{message}</div>}
      {error && <div className="erp-alert error">{error}</div>}

      <section className="project-hierarchy-layout">
        <div className="project-hierarchy-settings">
          <form className="erp-form-card" onSubmit={createLevel}>
            <div className="erp-form-header">
              <h2>Kullanıcı Tanımlı Seviyeler</h2>
              <p>Şehir, lokasyon, fabrika, blok, kat veya mahal ekleyin.</p>
            </div>

            <div className="erp-form-grid">
              <label>
                <span>Seviye Kodu *</span>
                <input
                  required
                  maxLength={40}
                  value={levelForm.code}
                  onChange={(event) =>
                    setLevelForm({
                      ...levelForm,
                      code: event.target.value.toUpperCase(),
                    })
                  }
                />
              </label>
              <label>
                <span>Seviye Adı *</span>
                <input
                  required
                  maxLength={100}
                  value={levelForm.name}
                  onChange={(event) =>
                    setLevelForm({
                      ...levelForm,
                      name: event.target.value,
                    })
                  }
                />
              </label>
              <label>
                <span>Sıra *</span>
                <input
                  required
                  type="number"
                  min={0}
                  value={levelForm.sortOrder}
                  onChange={(event) =>
                    setLevelForm({
                      ...levelForm,
                      sortOrder: Number(event.target.value),
                    })
                  }
                />
              </label>
              <label className="erp-checkbox-field">
                <input
                  type="checkbox"
                  checked={levelForm.isRequired}
                  onChange={(event) =>
                    setLevelForm({
                      ...levelForm,
                      isRequired: event.target.checked,
                    })
                  }
                />
                <span>Zorunlu seviye</span>
              </label>
            </div>

            <div className="erp-form-actions">
              <button
                type="submit"
                className="erp-primary-button"
                disabled={saving}
              >
                Seviyeyi Ekle
              </button>
            </div>

            <div className="project-hierarchy-level-list">
              {hierarchy?.levels.map((level) => (
                <div key={level.id}>
                  <span>{level.sortOrder}</span>
                  <strong>{level.name}</strong>
                  <small>{level.nodeCount} kırılım</small>
                  <button
                    type="button"
                    className="erp-link-button danger"
                    disabled={saving || level.nodeCount > 0}
                    onClick={() => deleteLevel(level.id, level.name)}
                  >
                    Sil
                  </button>
                </div>
              ))}
            </div>
          </form>

          <form className="erp-form-card" onSubmit={createNode}>
            <div className="erp-form-header">
              <h2>Yeni Proje Kırılımı</h2>
              <p>Seviyeyi ve varsa üst kırılımı seçin.</p>
            </div>

            <div className="erp-form-grid">
              <label>
                <span>Seviye *</span>
                <select
                  required
                  value={nodeForm.levelId}
                  onChange={(event) =>
                    setNodeForm({
                      ...nodeForm,
                      levelId: event.target.value,
                      parentNodeId: "",
                    })
                  }
                >
                  <option value="">Seviye seçin</option>
                  {hierarchy?.levels.map((level) => (
                    <option key={level.id} value={level.id}>
                      {level.sortOrder} · {level.name}
                    </option>
                  ))}
                </select>
              </label>
              <label>
                <span>Üst Kırılım</span>
                <select
                  value={nodeForm.parentNodeId}
                  onChange={(event) =>
                    setNodeForm({
                      ...nodeForm,
                      parentNodeId: event.target.value,
                    })
                  }
                >
                  <option value="">Kök kırılım</option>
                  {parentOptions.map((node) => (
                    <option key={node.id} value={node.id}>
                      {"— ".repeat(node.depth)}
                      {node.path}
                    </option>
                  ))}
                </select>
              </label>
              <label>
                <span>Kırılım Kodu *</span>
                <input
                  required
                  maxLength={60}
                  value={nodeForm.code}
                  onChange={(event) =>
                    setNodeForm({
                      ...nodeForm,
                      code: event.target.value.toUpperCase(),
                    })
                  }
                />
              </label>
              <label>
                <span>Kırılım Adı *</span>
                <input
                  required
                  maxLength={200}
                  value={nodeForm.name}
                  onChange={(event) =>
                    setNodeForm({
                      ...nodeForm,
                      name: event.target.value,
                    })
                  }
                />
              </label>
              <label>
                <span>Sıra *</span>
                <input
                  required
                  type="number"
                  min={0}
                  value={nodeForm.sortOrder}
                  onChange={(event) =>
                    setNodeForm({
                      ...nodeForm,
                      sortOrder: Number(event.target.value),
                    })
                  }
                />
              </label>
              <label>
                <span>Açıklama</span>
                <input
                  maxLength={1000}
                  value={nodeForm.description}
                  onChange={(event) =>
                    setNodeForm({
                      ...nodeForm,
                      description: event.target.value,
                    })
                  }
                />
              </label>
            </div>

            <div className="erp-form-actions">
              <button
                type="submit"
                className="erp-primary-button"
                disabled={saving || !nodeForm.levelId}
              >
                Kırılımı Ekle
              </button>
            </div>
          </form>
        </div>

        <div className="erp-panel project-hierarchy-tree-panel">
          <div className="erp-panel-header">
            <div>
              <h2>Proje Kırılım Ağacı</h2>
              <p>
                Hakediş, personel, depo, satın alma ve finans kayıtları en
                alt uygun düğüme bağlanır.
              </p>
            </div>
          </div>

          {loading ? (
            <div className="erp-loading">Hiyerarşi yükleniyor...</div>
          ) : (hierarchy?.nodes.length ?? 0) === 0 ? (
            <div className="erp-empty-state">
              <strong>Henüz proje kırılımı yok</strong>
              <p>Seviyeleri tanımlayın veya MKE örneğini uygulayın.</p>
            </div>
          ) : (
            <ul className="project-hierarchy-tree">
              {hierarchy?.nodes.map((node) => (
                <HierarchyNodeCard
                  key={node.id}
                  node={node}
                  onDelete={deleteNode}
                />
              ))}
            </ul>
          )}
        </div>
      </section>
    </ErpShell>
  );
}
