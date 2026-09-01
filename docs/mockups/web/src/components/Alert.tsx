import { type ReactNode } from "react";
import { AlertIcon, CheckIcon } from "./Icons";

export function Alert({ tone, children }: { tone: "danger" | "success" | "info"; children: ReactNode }) {
  return (
    <div className={`alert alert--${tone}`} role={tone === "danger" ? "alert" : "status"}>
      <span className="alert__icon">{tone === "success" ? <CheckIcon size={16} /> : <AlertIcon size={16} />}</span>
      <div>{children}</div>
    </div>
  );
}
