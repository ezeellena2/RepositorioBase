import type { ReactNode } from "react";

interface CardProps {
  title: string;
  subtitle?: ReactNode;
  footer?: ReactNode;
  children: ReactNode;
}

export function Card({ title, subtitle, footer, children }: CardProps) {
  return (
    <section className="card">
      <h1 className="card__title">{title}</h1>
      {subtitle && <p className="card__subtitle">{subtitle}</p>}
      <div className="card__body">{children}</div>
      {footer && <div className="card__footer">{footer}</div>}
    </section>
  );
}
