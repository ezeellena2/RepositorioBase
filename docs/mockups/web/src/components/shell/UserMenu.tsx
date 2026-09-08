import { useEffect, useRef, useState } from "react";
import { initials } from "../../lib/format";

interface UserMenuProps {
  name: string;
  email: string;
  onLogout: () => void;
}

/**
 * Identidad autenticada. Está separado del selector de organización a propósito:
 * cambiar de persona exige cerrar sesión, nunca se suplanta a nadie.
 */
export function UserMenu({ name, email, onLogout }: UserMenuProps) {
  const [open, setOpen] = useState(false);
  const wrapRef = useRef<HTMLDivElement>(null);
  const triggerRef = useRef<HTMLButtonElement>(null);

  useEffect(() => {
    if (!open) return;
    function onPointerDown(event: MouseEvent) {
      if (!wrapRef.current?.contains(event.target as Node)) setOpen(false);
    }
    function onKeyDown(event: globalThis.KeyboardEvent) {
      if (event.key !== "Escape") return;
      setOpen(false);
      triggerRef.current?.focus();
    }
    document.addEventListener("mousedown", onPointerDown);
    document.addEventListener("keydown", onKeyDown);
    return () => {
      document.removeEventListener("mousedown", onPointerDown);
      document.removeEventListener("keydown", onKeyDown);
    };
  }, [open]);

  return (
    <div className="user-menu" ref={wrapRef}>
      <button
        ref={triggerRef}
        type="button"
        className="user-menu__trigger"
        onClick={() => setOpen((value) => !value)}
        aria-haspopup="menu"
        aria-expanded={open}
        aria-label={`Menú de ${name}`}
        title={name}
      >
        <span aria-hidden="true">{initials(name)}</span>
      </button>

      {open && (
        <div className="user-menu__pop" role="menu" aria-label={name}>
          <p className="user-menu__name">{name}</p>
          <p className="user-menu__email">{email}</p>
          <button type="button" role="menuitem" className="user-menu__item" onClick={onLogout}>
            Cerrar sesión
          </button>
          <p className="user-menu__note">Para operar como otra persona hay que cerrar sesión e ingresar con esa identidad.</p>
        </div>
      )}
    </div>
  );
}
