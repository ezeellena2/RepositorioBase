import { useId, useState, type InputHTMLAttributes, type ReactNode } from "react";

interface FieldProps extends Omit<InputHTMLAttributes<HTMLInputElement>, "id"> {
  label: ReactNode;
  hint?: ReactNode;
  error?: string;
  trailing?: ReactNode;
}

export function Field({ label, hint, error, trailing, className, ...input }: FieldProps) {
  const id = useId();
  const describedBy = error ? `${id}-error` : hint ? `${id}-hint` : undefined;
  return (
    <div className="field">
      <div className="field__head">
        <label className="field__label" htmlFor={id}>
          {label}
        </label>
        {trailing}
      </div>
      <div className="field__control">
        <input
          id={id}
          className={`input${error ? " input--invalid" : ""}${className ? ` ${className}` : ""}`}
          aria-invalid={error ? true : undefined}
          aria-describedby={describedBy}
          {...input}
        />
      </div>
      {error ? (
        <span id={`${id}-error`} className="field__error">
          {error}
        </span>
      ) : hint !== undefined ? (
        <span id={`${id}-hint`} className="field__hint">
          {hint}
        </span>
      ) : null}
    </div>
  );
}

interface PasswordFieldProps extends Omit<FieldProps, "type"> {}

export function PasswordField({ trailing, ...props }: PasswordFieldProps) {
  const [visible, setVisible] = useState(false);
  const id = useId();
  const { label, hint, error, className, ...input } = props;
  return (
    <div className="field">
      <div className="field__head">
        <label className="field__label" htmlFor={id}>
          {label}
        </label>
        {trailing}
      </div>
      <div className="field__control">
        <input
          id={id}
          type={visible ? "text" : "password"}
          className={`input input--with-action${error ? " input--invalid" : ""}${className ? ` ${className}` : ""}`}
          aria-invalid={error ? true : undefined}
          {...input}
        />
        <button
          type="button"
          className="input__action"
          onClick={() => setVisible((v) => !v)}
          aria-pressed={visible}
        >
          {visible ? "Ocultar" : "Mostrar"}
        </button>
      </div>
      {error ? (
        <span className="field__error">{error}</span>
      ) : hint !== undefined ? (
        <span className="field__hint">{hint}</span>
      ) : null}
    </div>
  );
}
