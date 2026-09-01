import { randomBytes, randomUUID } from "node:crypto";
import type { CuitKind } from "./cuit.js";

export interface User {
  id: string;
  email: string;
  password: string;
  name: string;
  confirmed: boolean;
  createdAt: string;
}

export interface Tenant {
  id: string;
  type: CuitKind;
  name: string;
  cuit: string; // 11 dígitos normalizados
}

export interface Membership {
  userId: string;
  tenantId: string;
  role: "owner" | "member";
}

export interface Session {
  id: string;
  userId: string;
  activeTenantId: string | null;
  createdAt: string;
}

export interface Mail {
  to: string;
  subject: string;
  body: string;
  link: string | null;
  sentAt: string;
}

export interface ConfirmToken {
  token: string;
  userId: string;
  expiresAt: string;
  usedAt: string | null;
}

export interface Db {
  users: User[];
  tenants: Tenant[];
  memberships: Membership[];
  sessions: Session[];
  mails: Mail[];
  confirmTokens: ConfirmToken[];
}

export const db: Db = {
  users: [],
  tenants: [],
  memberships: [],
  sessions: [],
  mails: [],
  confirmTokens: [],
};

export const now = () => new Date().toISOString();
export const newId = () => randomUUID();
export const newToken = () => randomBytes(24).toString("base64url");

export function seed(): void {
  db.users.length = 0;
  db.tenants.length = 0;
  db.memberships.length = 0;
  db.sessions.length = 0;
  db.mails.length = 0;
  db.confirmTokens.length = 0;

  const juan: User = { id: newId(), email: "juan@acme.com", password: "1234", name: "Juan Pérez", confirmed: true, createdAt: now() };
  const maria: User = { id: newId(), email: "maria@acme.com", password: "1234", name: "María López", confirmed: true, createdAt: now() };
  const nuevo: User = { id: newId(), email: "nuevo@sur.com", password: "1234", name: "Distribuidora Sur SRL", confirmed: false, createdAt: now() };

  const acme: Tenant = { id: newId(), type: "empresa", name: "Acme S.A.", cuit: "30712345671" };
  const juanPersona: Tenant = { id: newId(), type: "persona", name: "Juan Pérez", cuit: "20334445551" };
  const sur: Tenant = { id: newId(), type: "empresa", name: "Distribuidora Sur SRL", cuit: "30709988774" };

  db.users.push(juan, maria, nuevo);
  db.tenants.push(acme, juanPersona, sur);
  db.memberships.push(
    { userId: juan.id, tenantId: acme.id, role: "owner" },
    { userId: juan.id, tenantId: juanPersona.id, role: "owner" },
    { userId: maria.id, tenantId: acme.id, role: "member" },
    { userId: nuevo.id, tenantId: sur.id, role: "owner" },
  );
}

export const findUserByEmail = (email: string) => db.users.find((u) => u.email === email.trim().toLowerCase());
export const findTenantByCuit = (cuit: string) => db.tenants.find((t) => t.cuit === cuit);
export const membershipsOf = (userId: string) => db.memberships.filter((m) => m.userId === userId);
export const tenantById = (id: string) => db.tenants.find((t) => t.id === id);
export const findSession = (id: string) => db.sessions.find((s) => s.id === id);

export function pushMail(mail: Omit<Mail, "sentAt">): void {
  db.mails.unshift({ ...mail, sentAt: now() });
}
