import { type ButtonHTMLAttributes, type ReactNode } from "react";
import { Link } from "react-router-dom";

interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: "primary" | "secondary" | "ghost";
  block?: boolean;
  size?: "md" | "sm";
  loading?: boolean;
  children: ReactNode;
}

function classes(variant: string, block?: boolean, size?: string, extra?: string) {
  return [
    "btn",
    `btn--${variant}`,
    block ? "btn--block" : "",
    size === "sm" ? "btn--sm" : "",
    extra ?? "",
  ]
    .filter(Boolean)
    .join(" ");
}

export function Button({ variant = "primary", block, size, loading, children, className, disabled, ...rest }: ButtonProps) {
  return (
    <button className={classes(variant, block, size, className)} disabled={disabled || loading} {...rest}>
      {loading && <span className="btn__spinner" aria-hidden="true" />}
      {children}
    </button>
  );
}

interface LinkButtonProps {
  to: string;
  variant?: "primary" | "secondary" | "ghost";
  block?: boolean;
  size?: "md" | "sm";
  children: ReactNode;
}

export function LinkButton({ to, variant = "primary", block, size, children }: LinkButtonProps) {
  return (
    <Link to={to} className={classes(variant, block, size)}>
      {children}
    </Link>
  );
}
