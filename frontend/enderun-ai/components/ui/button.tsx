import type { ButtonHTMLAttributes, ReactNode } from "react";

type ButtonVariant = "primary" | "secondary" | "danger" | "ghost";
type ButtonSize = "sm" | "md" | "lg";

interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  children: ReactNode;
  variant?: ButtonVariant;
  size?: ButtonSize;
  loading?: boolean;
  fullWidth?: boolean;
}

/*
 * Renkler kurumsal paletten geliyor: brand-700 (#18797c) erp-primary ile,
 * brand-600 (#1f9498) erp-primary-hover ile birebir aynı. Birincil buton
 * daha önce bg-slate-900 idi — slate bu projede turkuaz tonlu NÖTR gri
 * olarak yeniden tanımlı olduğu için buton koyu gri çıkıyor, marka
 * turkuazı hiç görünmüyordu.
 */
const variantClasses: Record<ButtonVariant, string> = {
  primary:
    "bg-brand-700 text-white hover:bg-brand-600 focus-visible:ring-brand-500",
  secondary:
    "border border-brand-200 bg-white text-brand-800 hover:bg-brand-50 focus-visible:ring-brand-400",
  danger:
    "bg-red-600 text-white hover:bg-red-700 focus-visible:ring-red-500",
  ghost:
    "bg-transparent text-brand-800 hover:bg-brand-50 focus-visible:ring-brand-400",
};

const sizeClasses: Record<ButtonSize, string> = {
  sm: "h-8 px-3 text-sm",
  md: "h-10 px-4 text-sm",
  lg: "h-12 px-5 text-base",
};

export function Button({
  children,
  variant = "primary",
  size = "md",
  loading = false,
  fullWidth = false,
  disabled,
  className = "",
  type = "button",
  ...props
}: ButtonProps) {
  return (
    <button
      type={type}
      disabled={disabled || loading}
      className={[
        "inline-flex items-center justify-center rounded-lg font-medium transition",
        "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-offset-2",
        "disabled:cursor-not-allowed disabled:opacity-50",
        variantClasses[variant],
        sizeClasses[size],
        fullWidth ? "w-full" : "",
        className,
      ].join(" ")}
      {...props}
    >
      {loading ? "İşleniyor..." : children}
    </button>
  );
}
