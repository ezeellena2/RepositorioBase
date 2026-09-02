import { AuthLayout, Card, Spinner } from "../components";
import { Sigil } from "../components/shell/Sigil";
import { ROLE_LABEL, typeLabel } from "../lib/labels";
import { useShell } from "../lib/shell-context";

/** Sin contexto activo y con más de uno: se elige antes de entrar. */
export function ChooseContextPage() {
  const { me, switching, selectTenant } = useShell();
  if (!me) return null;

  return (
    <AuthLayout>
      <Card title="¿Con quién querés operar?" subtitle="Cada contexto factura con su propio CUIT.">
        <ul className="tenant-list">
          {me.tenants.map((t) => (
            <li key={t.id}>
              <button type="button" className="tenant-row" onClick={() => void selectTenant(t.id)} disabled={switching !== null}>
                <Sigil name={t.name} scope={t.scope} type={t.type} size={30} dot={t.hasDegraded} />
                <span className="tenant-row__lines">
                  <span className="tenant-row__name">{t.name}</span>
                  <span className="tenant-row__cuit tabular">{t.cuit ?? "Sin CUIT"}</span>
                </span>
                <span className="tenant-row__meta">
                  <span className="badge">{typeLabel(t)}</span>
                  <span className="tenant-row__role">{ROLE_LABEL[t.role]}</span>
                </span>
                {switching === t.id && <Spinner />}
              </button>
            </li>
          ))}
        </ul>
      </Card>
    </AuthLayout>
  );
}
