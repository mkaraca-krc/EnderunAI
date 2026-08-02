import Link from "next/link";

const actions = [
  { label: "Projeler", href: "/projeler" },
  { label: "Keşifler", href: "/kesifler" },
  { label: "Metrajlar", href: "/metrajlar" },
  { label: "Hakedişler", href: "/hakedis" },
  { label: "Fiyat Farkı", href: "/fiyat-farki" },
  { label: "Satın Alma", href: "/satin-alma" },
  { label: "Depo & Stok", href: "/depo-stok" },
  { label: "AI Merkezi", href: "/ai-asistan" },
];

export default function QuickActionsWidget() {
  return (
    <div className="erp-panel dashboard-quick-actions-widget">
      <div className="erp-panel-header">
        <div>
          <h2>Hızlı İşlemler</h2>
          <p>Sık kullanılan modüller</p>
        </div>
      </div>

      <div className="erp-quick-grid">
        {actions.map((action) => (
          <Link key={action.href} href={action.href}>
            {action.label}
          </Link>
        ))}
      </div>
    </div>
  );
}
