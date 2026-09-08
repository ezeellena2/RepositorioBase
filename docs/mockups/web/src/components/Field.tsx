import { useId, type ComponentProps, type ReactNode } from "react";

interface FieldProps extends Omit<ComponentProps<"input">, "id"> {
  label: string;
  error?: string;
  /** Línea de ayuda debajo del input. El error la reemplaza. */
  hint?: ReactNode;
  /** Ayuda en --muted en vez de --ink-2. */
  hintMuted?: boolean;
  action?: ReactNode;
}

export function Field({ label, error, hint, hintMuted, action, className, ...input }: FieldProps) {
  const id = useId();
  const errorId = `${id}-error`;
  const hintId = `${id}-hint`;
  const classes = ["field", error ? "field--error" : "", action ? "field--with-action" : "", className ?? ""]
    .filter(Boolean)
    .join(" ");
  return (
    <div className={classes}>
      <label className="field__label" htmlFor={id}>
        {label}
      </label>
      <div className="field__control">
        <input
          id={id}
          className="field__input"
          aria-invalid={error ? true : undefined}
          aria-describedby={error ? errorId : hint ? hintId : undefined}
          {...input}
        />
        {action && <span className="field__action">{action}</span>}
      </div>
      {error ? (
        <span id={errorId} className="field__error">
          {error}
        </span>
      ) : hint ? (
        <span id={hintId} className={`field__hint${hintMuted ? " field__hint--muted" : ""}`}>
          {hint}
        </span>
      ) : null}
    </div>
  );
}
