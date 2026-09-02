import type { ButtonHTMLAttributes, ReactNode } from "react";
import { Spinner } from "./Spinner";

interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: "primary" | "ghost" | "text";
  /** Ancho automático y 38px de alto: para pantallas internas. */
  inline?: boolean;
  loading?: boolean;
  children: ReactNode;
}

export function Button({ variant = "primary", inline = false, loading = false, disabled, className, children, ...rest }: ButtonProps) {
  const classes = ["btn", `btn--${variant}`, inline ? "inline" : "", loading ? "btn--loading" : "", className ?? ""].filter(Boolean).join(" ");
  return (
    <button className={classes} disabled={disabled || loading} aria-busy={loading || undefined} {...rest}>
      <span className="btn__label">{children}</span>
      {loading && (
        <span className="btn__spinner">
          <Spinner />
        </span>
      )}
    </button>
  );
}
