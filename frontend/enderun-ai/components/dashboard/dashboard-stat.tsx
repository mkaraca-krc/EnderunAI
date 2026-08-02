import Link from "next/link";

type DashboardStatProps = {
  icon: string;
  label: string;
  value: string;
  note: string;
  href: string;
};

export default function DashboardStat({
  icon,
  label,
  value,
  note,
  href,
}: DashboardStatProps) {
  return (
    <Link
      href={href}
      className="enderun-dashboard-stat"
    >
      <span className="enderun-dashboard-stat-icon">
        {icon}
      </span>

      <div>
        <span>{label}</span>
        <strong>{value}</strong>
        <small>{note}</small>
      </div>
    </Link>
  );
}
