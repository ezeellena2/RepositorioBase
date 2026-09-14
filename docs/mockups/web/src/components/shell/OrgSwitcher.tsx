import { useEffect, useRef, useState } from "react";
import { useNavigate } from "react-router-dom";
import { Badge } from "../Badge";
import { Spinner } from "../Spinner";
import { STATUS_LABEL, TYPE_LABEL, initials } from "../../lib/format";
import { useSession } from "../../lib/session";

/**
 * Selector de organización activa. Cambiar de organización mantiene la misma
 * identidad autenticada: sólo recalcula contexto, permisos y menú.
 */
export function OrgSwitcher() {
  const navigate = useNavigate();
  const { me, switchOrg, switching } = useSession();
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

  if (!me) return null;
  const active = me.activeOrg;

  async function choose(orgId: string) {
    if (orgId !== active?.id) await switchOrg(orgId);
    setOpen(false);
  }

  return (
    <div className="orgswitch" ref={wrapRef}>
      <p className="orgswitch__label" id="orgswitch-label">
        Operando con
      </p>
      <button
        ref={triggerRef}
        type="button"
        className="orgswitch__trigger"
        onClick={() => setOpen((value) => !value)}
        aria-haspopup="menu"
        aria-expanded={open}
        aria-describedby="orgswitch-label"
        disabled={switching}
      >
        <span className="orgswitch__sigil" aria-hidden="true">
          {active ? initials(active.name) : "—"}
        </span>
        <span className="orgswitch__lines">
          <span className="orgswitch__name">{active ? active.name : "Sin organización activa"}</span>
          <span className="orgswitch__meta">
            {active ? `${TYPE_LABEL[active.type]} · ${active.roleLabel}` : "Elegí una organización"}
          </span>
        </span>
        {switching ? (
          <Spinner />
        ) : (
          <svg className={`orgswitch__chevron${open ? " is-open" : ""}`} width="12" height="12" viewBox="0 0 12 12" aria-hidden="true">
            <path d="M2.5 4.5 6 8l3.5-3.5" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round" />
          </svg>
        )}
      </button>

      {active?.status === "suspended" && (
        <p className="orgswitch__flag">
          <Badge tone="danger">Suspendida</Badge>
        </p>
      )}

      {open && (
        <div className="orgswitch__pop" role="menu" aria-label="Cambiar de organización">
          <p className="orgswitch__pophead">Tus organizaciones</p>
          <div className="orgswitch__list">
            {me.orgs.map((org) => (
              <button
                key={org.id}
                type="button"
                role="menuitemradio"
                aria-checked={org.id === active?.id}
                className={`orgopt${org.id === active?.id ? " is-active" : ""}`}
                onClick={() => void choose(org.id)}
                disabled={switching}
              >
                <span className="orgopt__sigil" aria-hidden="true">
                  {initials(org.name)}
                </span>
                <span className="orgopt__lines">
                  <span className="orgopt__name">{org.name}</span>
                  <span className="orgopt__meta">
                    {TYPE_LABEL[org.type]} · {org.roleLabel}
                  </span>
                </span>
                <span className="orgopt__state">
                  {org.status === "suspended" ? (
                    <Badge tone="danger">{STATUS_LABEL.suspended}</Badge>
                  ) : org.id === active?.id ? (
                    <span className="orgopt__check" aria-hidden="true">
                      ✓
                    </span>
                  ) : null}
                </span>
              </button>
            ))}
          </div>
          <div className="orgswitch__popfoot">
            <button
              type="button"
              className="btn btn--text"
              onClick={() => {
                setOpen(false);
                navigate("/app/organizaciones/nueva");
              }}
            >
              Crear otra organización
            </button>
            {!me.orgs.some((org) => org.type === "persona") && (
              <button
                type="button"
                className="btn btn--text"
                onClick={() => {
                  setOpen(false);
                  navigate("/app/personal/nueva");
                }}
              >
                Agregar cuenta personal
              </button>
            )}
            <p className="orgswitch__note">Cambiar de organización no cambia tu identidad ni tu sesión.</p>
          </div>
        </div>
      )}
    </div>
  );
}
