import { useRef } from "react";
import { Outlet } from "react-router-dom";
import { useMediaQuery } from "../../hooks/useMediaQuery";
import { contextKey } from "../../lib/labels";
import { useShell } from "../../lib/shell-context";
import { ContextFlash } from "./ContextFlash";
import { Header } from "./Header";
import { Sidebar } from "./Sidebar";

export function Shell() {
  const { me, navMin, drawerOpen } = useShell();
  const isDrawer = useMediaQuery("(max-width: 1023px)");
  const hamburgerRef = useRef<HTMLButtonElement>(null);
  const tenant = me?.activeTenant ?? null;

  const classes = ["shell", !isDrawer && navMin ? "is-min" : "", isDrawer && drawerOpen ? "is-drawer-open" : ""].filter(Boolean).join(" ");

  return (
    <div className={classes} data-context={tenant ? contextKey(tenant) : undefined}>
      <Sidebar isDrawer={isDrawer} hamburgerRef={hamburgerRef} />
      <div className="shell-column">
        <Header isDrawer={isDrawer} hamburgerRef={hamburgerRef} />
        <ContextFlash />
        <main className="shell-content">
          <Outlet />
        </main>
      </div>
    </div>
  );
}
