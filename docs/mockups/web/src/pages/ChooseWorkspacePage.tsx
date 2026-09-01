import { useState } from "react";
import { useNavigate } from "react-router-dom";
import { AuthLayout } from "../components/AuthLayout";
import { TenantAvatar } from "../components/Avatar";
import { ChevronRightIcon } from "../components/Icons";
import { useIdentity } from "../lib/identity";
import type { TenantDto } from "../api/client";

export function describeTenant(t: TenantDto): string {
  return t.type === "Personal" ? "Espacio personal" : `Organización · ${t.role}`;
}

export function ChooseWorkspacePage() {
  const navigate = useNavigate();
  const { context, selectTenant, signOut } = useIdentity();
  const [busyId, setBusyId] = useState<string | null>(null);

  if (!context) return null;

  async function choose(tenantId: string) {
    setBusyId(tenantId);
    try {
      await selectTenant(tenantId);
      navigate("/inicio", { replace: true });
    } finally {
      setBusyId(null);
    }
  }

  async function leave() {
    await signOut();
    navigate("/iniciar-sesion?salida=1", { replace: true });
  }

  const firstName = context.user.displayName.split(" ")[0];

  return (
    <AuthLayout
      title={`Hola, ${firstName}`}
      subtitle="Elegí con qué espacio querés operar. Podés cambiarlo cuando quieras desde el menú superior."
      wide
      aux={
        <>
          ¿No sos {firstName}?{" "}
          <button type="button" className="btn btn--ghost btn--sm" style={{ height: "auto", padding: 0, color: "var(--color-primary)" }} onClick={leave}>
            Cerrá sesión
          </button>
        </>
      }
    >
      <div className="tenant-list">
        {context.availableTenants.map((t) => (
          <button
            key={t.id}
            type="button"
            className="tenant-item"
            onClick={() => choose(t.id)}
            disabled={busyId !== null}
          >
            <TenantAvatar name={t.name} type={t.type} />
            <span className="tenant-item__body">
              <span className="tenant-item__name">{t.name}</span>
              <span className="tenant-item__meta">
                {describeTenant(t)} · <span className="nowrap">CUIT {t.cuit}</span>
              </span>
            </span>
            <span className="tenant-item__chevron">
              <ChevronRightIcon />
            </span>
          </button>
        ))}
      </div>
    </AuthLayout>
  );
}
