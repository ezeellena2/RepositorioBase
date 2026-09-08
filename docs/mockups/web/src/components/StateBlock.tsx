import type { ReactNode } from "react";
import { Spinner } from "./Spinner";

export type StateKind = "loading" | "empty" | "error" | "denied" | "notfound";

const ICON: Record<StateKind, string> = {
  loading: "",
  empty: "○",
  error: "!",
  denied: "🔒",
  notfound: "?",
};

interface StateBlockProps {
  kind: StateKind;
  title: string;
  /** Qué pasó y cuál es el siguiente paso. */
  description?: ReactNode;
  action?: ReactNode;
}

/** Estados transversales: carga, vacío, error, 403 y 404. */
export function StateBlock({ kind, title, description, action }: StateBlockProps) {
  return (
    <div className={`state state--${kind}`} role={kind === "error" || kind === "denied" ? "alert" : "status"}>
      <div className="state__mark" aria-hidden="true">
        {kind === "loading" ? <Spinner /> : ICON[kind]}
      </div>
      <p className="state__title">{title}</p>
      {description && <p className="state__text">{description}</p>}
      {action && <div className="state__action">{action}</div>}
    </div>
  );
}
