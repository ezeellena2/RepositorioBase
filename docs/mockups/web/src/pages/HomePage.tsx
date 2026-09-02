import { Link } from "react-router-dom";
import { Button } from "../components";
import { Sigil } from "../components/shell/Sigil";
import type { ModuleStatus, PlatformResumen, TenantResumen } from "../lib/api";
import { firstName, longDate, ROLE_LABEL, sealLine, shortDate, shortTime, typeLabel } from "../lib/labels";
import { useShell } from "../lib/shell-context";

const STATUS_LABEL: Record<ModuleStatus, string> = {
  vigente: "Vigente",
  sin_configurar: "Sin configurar",
  vence: "Vence",
};

export function HomePage() {
  const { me, resumen, loading, openTenantPop } = useShell();
  const tenant = me?.activeTenant ?? null;

  return (
    <div className="home">
      <div className="home-greeting">
        <h1>{me ? `Hola, ${firstName(me.user.name)}` : "Hola"}</h1>
        <p>{longDate()}</p>
      </div>

      {loading || !me || !tenant ? (
        <div className="home-seal is-skeleton" aria-busy="true" />
      ) : (
        <section className="home-seal" aria-label="Contexto activo">
          <Sigil name={tenant.name} scope={tenant.scope} type={tenant.type} size={44} />
          <div className="home-seal__body">
            <h2 className="home-seal__name">{tenant.name}</h2>
            <p className="home-seal__line">
              {sealLine(tenant).cuit && (
                <>
                  <strong className="tabular">CUIT {sealLine(tenant).cuit}</strong>
                  <span className="home-seal__sep">·</span>
                </>
              )}
              {sealLine(tenant).rest}
            </p>
          </div>
          {me.tenants.length > 1 && (
            <Button variant="ghost" inline onClick={openTenantPop}>
              Cambiar de contexto
            </Button>
          )}
        </section>
      )}

      {me && me.tenants.length > 1 && (
        <section className="home-contexts" aria-label="Tus contextos">
          <div className="home-card__label">Tus contextos</div>
          <ul className="home-contexts__list">
            {me.tenants.map((t) => {
              const isActive = t.id === tenant?.id;
              return (
                <li key={t.id} className={`home-contexts__row${isActive ? " is-active" : ""}`}>
                  <Sigil name={t.name} scope={t.scope} type={t.type} size={30} dot={!isActive && t.hasDegraded} />
                  <span className="home-contexts__name">{t.name}</span>
                  <span className="home-contexts__type">{typeLabel(t)}</span>
                  <span className="home-contexts__cuit tabular">{t.cuit ?? "Sin CUIT"}</span>
                  <span className="home-contexts__state">{isActive ? "Activo" : ROLE_LABEL[t.role]}</span>
                </li>
              );
            })}
          </ul>
        </section>
      )}

      {tenant && resumen && (resumen.scope === "platform" ? <PlatformGrid r={resumen} /> : <TenantGrid r={resumen} permissions={tenant.permissions} />)}
    </div>
  );
}

function TenantGrid({ r, permissions }: { r: TenantResumen; permissions: string[] }) {
  const canModules = permissions.includes("bot.modules.manage");
  const canCapabilities = permissions.includes("bot.capabilities.manage");
  return (
    <div className="home-grid">
      <section className="home-card">
        <div className="home-card__label">Tu WhatsApp</div>
        <div className="home-card__body">
          {r.whatsapp ? (
            <>
              <div className="home-card__big tabular">{r.whatsapp.phone}</div>
              <div className="home-card__muted">Vinculado el {shortDate(r.whatsapp.linkedAt)}</div>
            </>
          ) : (
            <>
              <p className="home-card__muted">Todavía no vinculaste un número en este contexto.</p>
              <Link to="/mi/whatsapp/vincular" className="btn btn--primary inline">
                Vincular un número
              </Link>
            </>
          )}
        </div>
        <Link to="/mi/whatsapp" className="home-card__foot">
          Ir a Mi WhatsApp
        </Link>
      </section>

      {canModules && (
        <section className="home-card">
          <div className="home-card__label">Módulos</div>
          <ul className="home-card__rows">
            {r.modules.map((m) => (
              <li key={m.code} className="module-row">
                <span className={`status-dot status-dot--${m.status}`} aria-hidden="true" />
                <span className="module-row__name">{m.name}</span>
                <span className={`module-row__state${m.status === "vence" ? " is-warn" : ""}`}>
                  {m.status === "vence" && m.daysLeft !== undefined ? `Vence en ${m.daysLeft} días` : STATUS_LABEL[m.status]}
                </span>
              </li>
            ))}
          </ul>
          <Link to="/organizacion/integraciones" className="home-card__foot">
            Ir a Integraciones
          </Link>
        </section>
      )}

      <section className="home-card">
        <div className="home-card__label">Lo que el asistente puede hacer acá</div>
        <div className="home-card__body">
          {r.capabilities.length === 0 ? (
            <p className="home-card__muted">Todavía no hay capacidades habilitadas en este contexto.</p>
          ) : (
            <div className="chips">
              {r.capabilities.map((c) => (
                <span key={c} className="chip">
                  {c}
                </span>
              ))}
            </div>
          )}
        </div>
        {canCapabilities && (
          <Link to="/organizacion/capacidades" className="home-card__foot">
            Ajustar capacidades
          </Link>
        )}
      </section>
    </div>
  );
}

function PlatformGrid({ r }: { r: PlatformResumen }) {
  const subscription = r.onboarding.items.find((i) => i.code === "app_subscription");
  return (
    <div className="home-grid">
      <section className="home-card">
        <div className="home-card__label">Canal</div>
        <div className="home-card__body">
          <div className="home-card__big">
            <span className={`status-dot ${r.channel.status === "verificado" ? "status-dot--vigente" : "status-dot--vence"}`} aria-hidden="true" />
            {r.channel.status === "verificado" ? "Verificado" : "Sin verificar"}
          </div>
          <div className="home-card__muted">Último chequeo a las {shortTime(r.channel.lastCheckAt)}</div>
        </div>
        <Link to="/plataforma/canal" className="home-card__foot">
          Ir al canal
        </Link>
      </section>

      <section className="home-card">
        <div className="home-card__label">Puesta en marcha</div>
        <div className="home-card__body">
          <div className="home-card__big tabular">
            {r.onboarding.done} de {r.onboarding.total} ítems
          </div>
          {subscription && (
            <div className={`module-row${subscription.done ? "" : " is-highlight"}`}>
              <span className={`status-dot ${subscription.done ? "status-dot--vigente" : "status-dot--vence"}`} aria-hidden="true" />
              <span className="module-row__name">{subscription.label}</span>
              <span className={`module-row__state${subscription.done ? "" : " is-warn"}`}>{subscription.done ? "Hecho" : "Pendiente"}</span>
            </div>
          )}
        </div>
        <Link to="/plataforma/puesta-en-marcha" className="home-card__foot">
          Ver la lista
        </Link>
      </section>

      <section className="home-card">
        <div className="home-card__label">Últimas 24 horas</div>
        <dl className="home-card__stats">
          <div>
            <dt>Conversaciones</dt>
            <dd className="tabular">{r.last24h.conversations}</dd>
          </div>
          <div>
            <dt>Entregados</dt>
            <dd className="tabular">{r.last24h.delivered}</dd>
          </div>
          <div>
            <dt>Fallidos</dt>
            <dd className={`tabular${r.last24h.failed > 0 ? " is-warn" : ""}`}>{r.last24h.failed}</dd>
          </div>
        </dl>
        <Link to="/plataforma/monitor" className="home-card__foot">
          Ir al monitor
        </Link>
      </section>
    </div>
  );
}
