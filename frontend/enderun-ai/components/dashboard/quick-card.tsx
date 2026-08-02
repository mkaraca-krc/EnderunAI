import Link from "next/link";

type QuickCardProps = {
  label: string;
  value: number;
  href: string;
};

export default function QuickCard({
  label,
  value,
  href,
}: QuickCardProps) {
  return (
    <Link href={href}>
      <small>{label}</small>

      <div
        style={{
          marginTop: 8,
          fontSize: 24,
          fontWeight: 800,
        }}
      >
        {value}
      </div>
    </Link>
  );
}
