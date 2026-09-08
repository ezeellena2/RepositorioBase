import { useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { AuthLayout, Card, Spinner } from '../components/ui.jsx';
import { api } from '../lib/api.js';

export default function App() {
  const nav = useNavigate();
  const [ctx, setCtx] = useState(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    api
      .me()
      .then(setCtx)
      .catch(() => nav('/login', { replace: true }))
      .finally(() => setLoading(false));
  }, [nav]);

  async function choose(tenantId) {
    setCtx(await api.setTenant(tenantId));
  }

  async function logout() {
    await api.logout();
    nav('/login', { replace: true });
  }

  if (loading)
    return (
      <AuthLayout>
        <Card>
          <Spinner dark />
        </Card>
      </AuthLayout>
    );
  if (!ctx) return null;

  if (!ctx.activeTenant)
    return (
      <AuthLayout>
        <Card title="¿Con quién querés operar?">
          <div className="tenant-list">
            {ctx.tenants.map((t) => (
              <button key={t.id} className="tenant-row" onClick={() => choose(t.id)}>
                <span className="name">{t.name}</span>
                <span className="badge">{t.type === 'empresa' ? 'Empresa' : 'Personal'}</span>
              </button>
            ))}
          </div>
        </Card>
      </AuthLayout>
    );

  return (
    <>
      <header className="topbar">
        <div className="logo">Plataforma</div>
        {ctx.tenants.length > 1 && (
          <select value={ctx.activeTenant.id} onChange={(e) => choose(e.target.value)}>
            {ctx.tenants.map((t) => (
              <option key={t.id} value={t.id}>
                {t.name}
              </option>
            ))}
          </select>
        )}
        <span className="grow" />
        <span className="who">{ctx.user.name}</span>
        <button className="btn-text" onClick={logout}>
          Salir
        </button>
      </header>
      <main className="app-body">
        <h1>Hola, {ctx.user.name.split(' ')[0]}</h1>
        <div className="panel">
          <h2>Estás operando como {ctx.activeTenant.name}</h2>
          <p>
            {ctx.activeTenant.type === 'empresa' ? 'Empresa' : 'Persona física'} · CUIT {ctx.activeTenant.cuit}
          </p>
        </div>
      </main>
    </>
  );
}
