import type { ReactElement } from "react";
import type { OrgType } from "../lib/api";

const ICON: Record<OrgType, ReactElement> = {
  persona: (
    <svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round">
      <circle cx="12" cy="8" r="4" />
      <path d="M4 21c0-4 3.6-7 8-7s8 3 8 7" />
    </svg>
  ),
  empresa: (
    <svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round">
      <rect x="4" y="3" width="16" height="18" rx="1.5" />
      <path d="M9 7h.01M15 7h.01M9 11h.01M15 11h.01M9 15h.01M15 15h.01M10 21v-3h4v3" />
    </svg>
  ),
};

interface ChoiceCardProps {
  kind: OrgType;
  title: string;
  detail: string;
  onChoose: () => void;
}

/** Una opción entera que se elige con un clic: lo que se lee y lo que se aprieta son lo mismo. */
export function ChoiceCard({ kind, title, detail, onChoose }: ChoiceCardProps) {
  return (
    <button type="button" className="choice" onClick={onChoose}>
      <span className="choice__icon" aria-hidden="true">
        {ICON[kind]}
      </span>
      <span className="choice__lines">
        <span className="choice__title">{title}</span>
        <span className="choice__detail">{detail}</span>
      </span>
      <svg className="choice__chevron" width="16" height="16" viewBox="0 0 16 16" aria-hidden="true">
        <path d="M6 3.5 10.5 8 6 12.5" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" />
      </svg>
    </button>
  );
}
