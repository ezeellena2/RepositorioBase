import type { ReactNode } from "react";

export function AuthLayout({ children }: { children: ReactNode }) {
  return (
    <div className="auth">
      <div className="auth__stack">
        <div className="brand">Plataforma</div>
        {children}
      </div>
    </div>
  );
}
