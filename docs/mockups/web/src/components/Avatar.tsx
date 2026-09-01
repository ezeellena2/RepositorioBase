import type { TenantType } from "../api/client";

export function initialsOf(name: string): string {
  const parts = name.trim().split(/\s+/).filter(Boolean);
  const first = parts[0]?.[0] ?? "";
  const second = parts.length > 1 ? parts[parts.length - 1][0] : "";
  return (first + second).toUpperCase();
}

export function TenantAvatar({ name, type, size = "md" }: { name: string; type: TenantType; size?: "md" | "sm" }) {
  return (
    <span
      className={`avatar${size === "sm" ? " avatar--sm" : ""} ${type === "Personal" ? "avatar--personal" : "avatar--org"}`}
      aria-hidden="true"
    >
      {initialsOf(name)}
    </span>
  );
}

export function UserAvatar({ name }: { name: string }) {
  return (
    <span className="avatar avatar--sm avatar--round" aria-hidden="true">
      {initialsOf(name)}
    </span>
  );
}
