import { useState } from 'react';

export function AuthLayout({ children }) {
  return (
    <div className="auth">
      <div className="logo">Plataforma</div>
      {children}
    </div>
  );
}

export function Card({ title, lede, children }) {
  return (
    <div className="card">
      {title && <h1>{title}</h1>}
      {lede && <p className="lede">{lede}</p>}
      {children}
    </div>
  );
}

export function Field({ label, error, hint, derived, type = 'text', reveal, ...props }) {
  const [shown, setShown] = useState(false);
  const isPassword = type === 'password';
  const inputType = isPassword && shown ? 'text' : type;
  return (
    <div className={`field${error ? ' invalid' : ''}`}>
      <label htmlFor={props.id}>{label}</label>
      <div className="control">
        <input type={inputType} {...props} />
        {isPassword && reveal !== false && (
          <button type="button" className="reveal" onClick={() => setShown((s) => !s)} tabIndex={-1}>
            {shown ? 'Ocultar' : 'Mostrar'}
          </button>
        )}
      </div>
      {error ? (
        <div className="error">{error}</div>
      ) : derived ? (
        <div className="hint derived">{derived}</div>
      ) : hint ? (
        <div className="hint">{hint}</div>
      ) : null}
    </div>
  );
}

export function Button({ variant = 'primary', loading, children, ...props }) {
  return (
    <button className={`btn ${variant}`} disabled={loading || props.disabled} {...props}>
      {loading ? <span className="spinner" /> : children}
    </button>
  );
}

export function Alert({ variant = 'error', children }) {
  if (!children) return null;
  return <div className={`alert ${variant}`}>{children}</div>;
}

export function Spinner({ dark }) {
  return (
    <div className="centered">
      <span className={`spinner${dark ? ' dark' : ''}`} />
    </div>
  );
}
