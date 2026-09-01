import { useCallback, useEffect, useState, type ChangeEvent } from "react";
import { useNavigate } from "react-router-dom";
import { AuthLayout, Button, Card } from "../components";
import { api, ApiError, type Me, type Tenant } from "../lib/api";

const TYPE_LABEL: Record<Tenant["type"], string> = {
  persona: "Personal",
  empresa: "Empresa",
};

// Placeholder de la aplicación. El producto real va acá.
export function AppPage() {
  const navigate = useNavigate();
  const [me, setMe] = useState<Me | null>(null);
  const [switching, setSwitching] = useState(false);

  const load = useCallback(async () => {
    try {
      setMe(await api.me());
    } catch (error) {
      if (error instanceof ApiError && error.status === 401) navigate("/login", { replace: true });
    }
  }, [navigate]);

  useEffect(() => {
    void load();
  }, [load]);

  async function selectTenant(tenantId: string) {
    setSwitching(true);
    try {
      await api.selectTenant(tenantId);
      await load();
    } finally {
      setSwitching(false);
    }
  }

  async function logout() {
    await api.logout();
    navigate("/login", { replace: true });
  }

  if (!me) return null;

  if (!me.activeTenant && me.tenants.length > 1) {
    return (
      <AuthLayout>
        <Card title="¿Con quién querés operar?">
          <ul className="tenant-list">
            {me.tenants.map((t) => (
              <li key={t.id}>
                <button type="button" className="tenant-row" onClick={() => selectTenant(t.id)} disabled={switching}>
                  <span className="tenant-row__name">{t.name}</span>
                  <span className="tag">{TYPE_LABEL[t.type]}</span>
                </button>
              </li>
            ))}
          </ul>
        </Card>
      </AuthLayout>
    );
  }

  if (!me.activeTenant) {
    // Sin membresías: no debería pasar con la semilla, pero no dejamos la pantalla en blanco.
    return (
      <AuthLayout>
        <Card title="No tenés ningún espacio">
          <Button variant="ghost" onClick={logout}>
            Salir
          </Button>
        </Card>
      </AuthLayout>
    );
  }

  const tenant = me.activeTenant;
  const firstName = me.user.name.split(" ")[0];

  function handleSelect(event: ChangeEvent<HTMLSelectElement>) {
    void selectTenant(event.target.value);
  }

  return (
    <div className="app">
      <header className="topbar">
        <div className="topbar__side">
          <span className="brand brand--sm">Plataforma</span>
        </div>
        <div className="topbar__center">
          {me.tenants.length > 1 && (
            <select className="select" value={tenant.id} onChange={handleSelect} disabled={switching} aria-label="Cambiar de contexto">
              {me.tenants.map((t) => (
                <option key={t.id} value={t.id}>
                  {t.name}
                </option>
              ))}
            </select>
          )}
        </div>
        <div className="topbar__side topbar__side--end">
          <span className="topbar__user">{me.user.name}</span>
          <Button variant="text" onClick={logout}>
            Salir
          </Button>
        </div>
      </header>

      <main className="app__main">
        <h1 className="app__title">Hola, {firstName}</h1>
        <Card title={`Estás operando como ${tenant.name}`} subtitle={`${TYPE_LABEL[tenant.type]} · CUIT ${tenant.cuit}`}>
          {null}
        </Card>
      </main>
    </div>
  );
}
