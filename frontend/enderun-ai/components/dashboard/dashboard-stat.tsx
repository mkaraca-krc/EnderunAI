import Link from "next/link";

type DashboardStatProps = {
  icon: string;
  label: string;
  value: string;
  note: string;
  href: string;
  unavailable?: boolean;
};

export default function DashboardStat({
  icon,
  label,
  value,
  note,
  href,
  unavailable = false,
}: DashboardStatProps) {
  return (
    <Link
      href={href}
      className={`enderun-dashboard-stat${
        unavailable ? " is-pending" : ""
      }`}
    >
      <span className="enderun-dashboard-stat-icon">
        {icon}
      </span>

      <div>
        <span>{label}</span>
        <strong>{unavailable ? "—" : value}</strong>
        <small>{note}</small>
        {unavailable && (
          <span className="erp-pending-badge">
            Veri henüz yok
          </span>
        )}
      </div>
    </Link>
  );
}
