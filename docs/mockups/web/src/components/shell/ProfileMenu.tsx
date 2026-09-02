import { useState } from "react";
import { useNavigate } from "react-router-dom";
import { useMenu } from "../../hooks/useMenu";
import { initials } from "../../lib/labels";
import { useShell } from "../../lib/shell-context";
import { Icon } from "./Icons";

const MENU_ID = "profile-menu";

export function ProfileMenu() {
  const navigate = useNavigate();
  const { me, openTenantPop, logout } = useShell();
  const [open, setOpen] = useState(false);
  const { triggerRef, panelRef, toggle, close, onPanelKeyDown } = useMenu({ open, setOpen });

  if (!me) return null;
  const { user } = me;

  function go(to: string) {
    close(false);
    navigate(to);
  }

  return (
    <div className="profile">
      <button
        ref={triggerRef}
        type="button"
        className="profile__trigger"
        aria-haspopup="menu"
        aria-expanded={open}
        aria-controls={MENU_ID}
        aria-label={`Cuenta de ${user.name}`}
        onClick={toggle}
      >
        <span className="avatar avatar--30" aria-hidden="true">
          {initials(user.name)}
        </span>
        <span className="profile__name">{user.name}</span>
        <Icon name="chevron" size={12} className={`profile__chevron${open ? " is-open" : ""}`} />
      </button>

      {open && (
        <div id={MENU_ID} ref={panelRef} className="profile-pop" role="menu" aria-label="Cuenta" onKeyDown={onPanelKeyDown}>
          <div className="profile-pop__identity">
            <span className="avatar avatar--36" aria-hidden="true">
              {initials(user.name)}
            </span>
            <span className="profile-pop__lines">
              <span className="profile-pop__name">{user.name}</span>
              <span className="profile-pop__email">{user.email}</span>
            </span>
          </div>

          <button type="button" role="menuitem" className="profile-item" onClick={() => go("/perfil")}>
            <Icon name="user" />
            <span>Mi perfil</span>
          </button>
          <button type="button" role="menuitem" className="profile-item" onClick={() => go("/seguridad")}>
            <Icon name="shield" />
            <span>Seguridad y acceso</span>
            <span className={`profile-item__suffix ${user.mfa ? "is-ok" : "is-warn"}`}>{user.mfa ? "MFA activo" : "MFA pendiente"}</span>
          </button>
          <button
            type="button"
            role="menuitem"
            className="profile-item"
            onClick={() => {
              close(false);
              openTenantPop();
            }}
          >
            <Icon name="grid" />
            <span>Mis contextos ({me.tenants.length})</span>
          </button>

          <div className="profile-pop__sep" />

          <button type="button" role="menuitem" className="profile-item danger" onClick={() => void logout()}>
            <Icon name="logout" />
            <span>Cerrar sesión</span>
          </button>
        </div>
      )}
    </div>
  );
}
