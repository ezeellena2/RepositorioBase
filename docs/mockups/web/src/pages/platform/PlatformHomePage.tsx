import { Link } from "react-router-dom";
import { Badge, PageHeader } from "../../components";
import { usePlatform } from "./PlatformShell";

export function PlatformHomePage() {
  const { platform } = usePlatform();

  return (
    <>
      <PageHeader
        title="Resumen de Platform"
        description="Contexto operativo interno. No es una organización más ni aparece en el menú del cliente."
      />

      <section className="panel">
        <h2 className="panel__title">Tu acceso</h2>
        <ul className="minilist">
          <li className="minilist__item">
            <span>Rol</span>
            <Badge tone="info">{platform.role === "owner" ? "Titular" : "Administradora"}</Badge>
          </li>
          <li className="minilist__item">
            <span>Membresía activada</span>
            <Badge tone={platform.activated ? "ok" : "warn"}>{platform.activated ? "Sí" : "Pendiente"}</Badge>
          </li>
          <li className="minilist__item">
            <span>MFA simulada</span>
            <Badge tone={platform.mfa.enrolled ? "ok" : "warn"}>
              {platform.mfa.enrolled ? "Enrolada" : "Sin enrolar"}
            </Badge>
          </li>
          <li className="minilist__item">
            <span>Permisos</span>
            <Badge tone="neutral">{platform.permissions.length}</Badge>
          </li>
          <li className="minilist__item">
            <span>Step-up reciente</span>
            <Badge tone={platform.stepUpFresh ? "ok" : "warn"}>{platform.stepUpFresh ? "Vigente" : "Vencido"}</Badge>
          </li>
        </ul>
      </section>

      <section className="panel">
        <h2 className="panel__title">Directorios</h2>
        <ul className="minilist">
          <li className="minilist__item">
            <span>Organizaciones</span>
            <Link className="btn btn--text" to="/platform/organizaciones">
              Abrir
            </Link>
          </li>
          <li className="minilist__item">
            <span>Identidades</span>
            <Link className="btn btn--text" to="/platform/identidades">
              Abrir
            </Link>
          </li>
          <li className="minilist__item">
            <span>Retención</span>
            <Link className="btn btn--text" to="/platform/retencion">
              Abrir
            </Link>
          </li>
          <li className="minilist__item">
            <span>Administradores</span>
            <Link className="btn btn--text" to="/platform/administradores">
              Abrir
            </Link>
          </li>
          <li className="minilist__item">
            <span>Auditoría</span>
            <Link className="btn btn--text" to="/platform/auditoria">
              Abrir
            </Link>
          </li>
        </ul>
      </section>

      <section className="panel panel--limits">
        <h2 className="panel__title">Lo que Platform no hace</h2>
        <ul className="limits">
          <li>No hay suplantación de personas usuarias: para operar como alguien hay que ser esa identidad.</li>
          <li>No hay borrado destructivo: las organizaciones se suspenden y se reactivan, no se eliminan.</li>
          <li>No se puede pedir un borrado: se lee la política de retención y se lo detiene con una retención legal.</li>
          <li>No se puede elegir arbitrariamente un tenant ni operar dentro de una organización por bypass.</li>
          <li>No se exponen CUIT, secretos, tokens, credenciales ni datos privados de negocio.</li>
          <li>Las acciones de Platform no aparecen en el menú del cliente, ni las del cliente acá.</li>
        </ul>
      </section>
    </>
  );
}
