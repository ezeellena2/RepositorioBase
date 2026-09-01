import { randomBytes, randomUUID, scryptSync, timingSafeEqual } from "node:crypto";
import { buildCuit, type CuitKind } from "./cuit.js";

export type TenantType = "Personal" | "Organization";

export interface User {
  id: string;
  cuit: string;
  cuitKind: CuitKind;
  displayName: string;
  email: string;
  passwordHash: string;
  emailConfirmed: boolean;
  createdAt: string;
}

export interface Tenant {
  id: string;
  type: TenantType;
  name: string;
  cuit: string;
  state: "Pending" | "Active";
}

export interface Membership {
  userId: string;
  tenantId: string;
  roleName: string;
  permissions: string[];
}

export interface Session {
  id: string;
  userId: string;
  activeTenantId: string | null;
  createdAt: string;
  expiresAt: string;
}

export interface ConfirmationToken {
  token: string;
  userId: string;
  expiresAt: string;
  usedAt: string | null;
}

export interface MailMessage {
  id: string;
  to: string;
  subject: string;
  preview: string;
  kind: "confirm" | "already_registered" | "welcome";
  actionUrl: string | null;
  sentAt: string;
}

export interface Store {
  users: Map<string, User>;
  tenants: Map<string, Tenant>;
  memberships: Membership[];
  sessions: Map<string, Session>;
  confirmations: Map<string, ConfirmationToken>;
  mailbox: MailMessage[];
}

const OWNER_PERMISSIONS = [
  "members.view",
  "members.invite",
  "roles.manage",
  "settings.manage",
];
const MEMBER_PERMISSIONS = ["members.view"];
const PERSONAL_PERMISSIONS = ["settings.manage"];

export function hashPassword(password: string): string {
  const salt = randomBytes(16).toString("hex");
  const hash = scryptSync(password, salt, 32).toString("hex");
  return `${salt}:${hash}`;
}

export function verifyPassword(password: string, stored: string): boolean {
  const [salt, hash] = stored.split(":");
  const candidate = scryptSync(password, salt, 32);
  const expected = Buffer.from(hash, "hex");
  return candidate.length === expected.length && timingSafeEqual(candidate, expected);
}

export function normalizeEmail(email: string): string {
  return email.trim().toLowerCase();
}

export function newToken(): string {
  return randomBytes(24).toString("base64url");
}

export function nowIso(): string {
  return new Date().toISOString();
}

export function isoIn(ms: number): string {
  return new Date(Date.now() + ms).toISOString();
}

function empty(): Store {
  return {
    users: new Map(),
    tenants: new Map(),
    memberships: [],
    sessions: new Map(),
    confirmations: new Map(),
    mailbox: [],
  };
}

function seed(store: Store): void {
  // Ana: persona física, confirmada, con espacio personal y dos organizaciones.
  const ana: User = {
    id: randomUUID(),
    cuit: buildCuit("27", "30123456"),
    cuitKind: "persona",
    displayName: "Ana Pereyra",
    email: "ana@ejemplo.com",
    passwordHash: hashPassword("Demo1234"),
    emailConfirmed: true,
    createdAt: nowIso(),
  };
  store.users.set(ana.id, ana);

  const personal: Tenant = {
    id: randomUUID(),
    type: "Personal",
    name: "Ana Pereyra",
    cuit: ana.cuit,
    state: "Active",
  };
  const estudio: Tenant = {
    id: randomUUID(),
    type: "Organization",
    name: "Estudio Pereyra y Asociados",
    cuit: buildCuit("30", "71234567"),
    state: "Active",
  };
  const distribuidora: Tenant = {
    id: randomUUID(),
    type: "Organization",
    name: "Distribuidora del Litoral S.A.",
    cuit: buildCuit("30", "65432109"),
    state: "Active",
  };
  for (const t of [personal, estudio, distribuidora]) store.tenants.set(t.id, t);

  store.memberships.push(
    { userId: ana.id, tenantId: personal.id, roleName: "Titular", permissions: PERSONAL_PERMISSIONS },
    { userId: ana.id, tenantId: estudio.id, roleName: "Responsable", permissions: OWNER_PERMISSIONS },
    { userId: ana.id, tenantId: distribuidora.id, roleName: "Miembro", permissions: MEMBER_PERMISSIONS },
  );

  // Martín: solo un espacio, entra directo sin elegir.
  const martin: User = {
    id: randomUUID(),
    cuit: buildCuit("20", "28765432"),
    cuitKind: "persona",
    displayName: "Martín Sosa",
    email: "martin@ejemplo.com",
    passwordHash: hashPassword("Demo1234"),
    emailConfirmed: true,
    createdAt: nowIso(),
  };
  store.users.set(martin.id, martin);
  const martinPersonal: Tenant = {
    id: randomUUID(),
    type: "Personal",
    name: "Martín Sosa",
    cuit: martin.cuit,
    state: "Active",
  };
  store.tenants.set(martinPersonal.id, martinPersonal);
  store.memberships.push({
    userId: martin.id,
    tenantId: martinPersonal.id,
    roleName: "Titular",
    permissions: PERSONAL_PERMISSIONS,
  });
}

export let store: Store = empty();
seed(store);

export function resetStore(): void {
  store = empty();
  seed(store);
}

export function createAccount(input: {
  cuit: string;
  cuitKind: CuitKind;
  displayName: string;
  email: string;
  password: string;
}): User {
  const user: User = {
    id: randomUUID(),
    cuit: input.cuit,
    cuitKind: input.cuitKind,
    displayName: input.displayName,
    email: normalizeEmail(input.email),
    passwordHash: hashPassword(input.password),
    emailConfirmed: false,
    createdAt: nowIso(),
  };
  store.users.set(user.id, user);

  const tenant: Tenant = {
    id: randomUUID(),
    type: input.cuitKind === "empresa" ? "Organization" : "Personal",
    name: input.displayName,
    cuit: input.cuit,
    state: "Pending",
  };
  store.tenants.set(tenant.id, tenant);
  store.memberships.push({
    userId: user.id,
    tenantId: tenant.id,
    roleName: tenant.type === "Organization" ? "Responsable" : "Titular",
    permissions: tenant.type === "Organization" ? OWNER_PERMISSIONS : PERSONAL_PERMISSIONS,
  });
  return user;
}

export function issueConfirmation(user: User): ConfirmationToken {
  // Invalidate previous tokens for the same user.
  for (const c of store.confirmations.values()) {
    if (c.userId === user.id && !c.usedAt) c.usedAt = nowIso();
  }
  const record: ConfirmationToken = {
    token: newToken(),
    userId: user.id,
    expiresAt: isoIn(24 * 60 * 60 * 1000),
    usedAt: null,
  };
  store.confirmations.set(record.token, record);
  return record;
}

export function findUserByEmail(email: string): User | undefined {
  const normalized = normalizeEmail(email);
  for (const u of store.users.values()) if (u.email === normalized) return u;
  return undefined;
}

export function findUserByCuit(cuit: string): User | undefined {
  for (const u of store.users.values()) if (u.cuit === cuit) return u;
  return undefined;
}

export function membershipsOf(userId: string): Membership[] {
  return store.memberships.filter((m) => m.userId === userId);
}

export function sendMail(message: Omit<MailMessage, "id" | "sentAt">): MailMessage {
  const record: MailMessage = { id: randomUUID(), sentAt: nowIso(), ...message };
  store.mailbox.unshift(record);
  return record;
}
