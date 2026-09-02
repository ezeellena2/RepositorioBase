import { randomBytes, randomUUID } from "node:crypto";
import type { CuitKind } from "./cuit.js";

export type Scope = "platform" | "tenant";
export type Role = "owner" | "admin" | "member" | "operator";
export type Severity = "warn" | "error";
export type ModuleStatus = "vigente" | "sin_configurar" | "vence";

export interface User {
  id: string;
  email: string;
  password: string;
  name: string;
  confirmed: boolean;
  mfa: boolean;
  createdAt: string;
}

export interface Tenant {
  id: string;
  scope: Scope;
  type: CuitKind | null; // tipo fiscal derivado del CUIT; null si scope es platform
  name: string;
  cuit: string | null; // 11 dígitos normalizados; null si scope es platform
}

export interface Membership {
  userId: string;
  tenantId: string;
  role: Role;
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

export interface Degraded {
  code: string;
  severity: Severity;
  text: string;
}

export interface ModuleRow {
  code: string;
  name: string;
  status: ModuleStatus;
  daysLeft?: number;
}

export interface WhatsappLink {
  phone: string;
  linkedAt: string;
}

export interface TenantResumen {
  modules: ModuleRow[];
  capabilities: string[];
}

export interface PlatformResumen {
  channel: { status: "verificado" | "sin_verificar"; lastCheckAt: string };
  onboarding: { done: number; total: number; items: { code: string; label: string; done: boolean }[]; appSubscriptionDone: boolean };
  last24h: { conversations: number; delivered: number; failed: number };
}

export interface Db {
  users: User[];
  tenants: Tenant[];
  memberships: Membership[];
  sessions: Session[];
  mails: Mail[];
  confirmTokens: ConfirmToken[];
  degraded: Map<string, Degraded[]>; // por tenantId
  tenantResumen: Map<string, TenantResumen>; // por tenantId (scope tenant)
  platformResumen: Map<string, PlatformResumen>; // por tenantId (scope platform)
  whatsappLinks: Map<string, WhatsappLink>; // por `${userId}:${tenantId}`
}

export const PERMISSIONS_BY_ROLE: Record<Role, string[]> = {
  operator: ["platform.whatsapp.read", "platform.bot.capabilities.read"],
  owner: ["bot.modules.manage", "bot.capabilities.manage", "whatsapp.links.read"],
  admin: ["bot.modules.manage", "bot.capabilities.manage", "whatsapp.links.read"],
  member: [],
};

export const db: Db = {
  users: [],
  tenants: [],
  memberships: [],
  sessions: [],
  mails: [],
  confirmTokens: [],
  degraded: new Map(),
  tenantResumen: new Map(),
  platformResumen: new Map(),
  whatsappLinks: new Map(),
};

export const now = () => new Date().toISOString();
export const newId = () => randomUUID();
export const newToken = () => randomBytes(24).toString("base64url");
const hoursAgo = (h: number) => new Date(Date.now() - h * 3600_000).toISOString();
const daysAgo = (d: number) => hoursAgo(d * 24);

export function seed(): void {
  db.users.length = 0;
  db.tenants.length = 0;
  db.memberships.length = 0;
  db.sessions.length = 0;
  db.mails.length = 0;
  db.confirmTokens.length = 0;
  db.degraded.clear();
  db.tenantResumen.clear();
  db.platformResumen.clear();
  db.whatsappLinks.clear();

  const juan: User = { id: newId(), email: "juan@acme.com", password: "1234", name: "Juan Pérez", confirmed: true, mfa: true, createdAt: now() };
  const maria: User = { id: newId(), email: "maria@acme.com", password: "1234", name: "María López", confirmed: true, mfa: false, createdAt: now() };
  const nuevo: User = { id: newId(), email: "nuevo@sur.com", password: "1234", name: "Distribuidora Sur SRL", confirmed: false, mfa: false, createdAt: now() };
  const operador: User = { id: newId(), email: "operador@plataforma.com", password: "1234", name: "Carla Ruiz", confirmed: true, mfa: true, createdAt: now() };

  const acme: Tenant = { id: newId(), scope: "tenant", type: "empresa", name: "Acme S.A.", cuit: "30712345671" };
  const juanPersona: Tenant = { id: newId(), scope: "tenant", type: "persona", name: "Juan Pérez", cuit: "20334445551" };
  const sur: Tenant = { id: newId(), scope: "tenant", type: "empresa", name: "Distribuidora Sur SRL", cuit: "30709988774" };
  const operaciones: Tenant = { id: newId(), scope: "platform", type: null, name: "Operaciones", cuit: null };

  db.users.push(juan, maria, nuevo, operador);
  db.tenants.push(acme, juanPersona, sur, operaciones);
  db.memberships.push(
    { userId: juan.id, tenantId: acme.id, role: "owner" },
    { userId: juan.id, tenantId: juanPersona.id, role: "owner" },
    { userId: maria.id, tenantId: acme.id, role: "member" },
    { userId: nuevo.id, tenantId: sur.id, role: "owner" },
    { userId: operador.id, tenantId: operaciones.id, role: "operator" },
  );

  // Condiciones abiertas por contexto (estado Degraded del inventario).
  db.degraded.set(acme.id, [{ code: "arca_cert_expiring", severity: "warn", text: "ARCA vence en 4 días" }]);
  db.degraded.set(operaciones.id, [{ code: "channel_unverified", severity: "warn", text: "Canal sin verificar" }]);

  // Resumen por tenant: módulos y capacidades habilitadas.
  db.tenantResumen.set(acme.id, {
    modules: [
      { code: "arca", name: "Facturación ARCA", status: "vence", daysLeft: 4 },
      { code: "turnos", name: "Turnos", status: "vigente" },
      { code: "avisos", name: "Avisos", status: "sin_configurar" },
    ],
    capabilities: ["emitir_factura", "sacar_turno", "consultar_saldo"],
  });
  db.tenantResumen.set(juanPersona.id, {
    modules: [{ code: "arca", name: "Facturación ARCA", status: "vigente" }],
    capabilities: ["emitir_factura"],
  });
  db.tenantResumen.set(sur.id, {
    modules: [{ code: "arca", name: "Facturación ARCA", status: "sin_configurar" }],
    capabilities: [],
  });

  // Resumen de plataforma.
  db.platformResumen.set(operaciones.id, {
    channel: { status: "sin_verificar", lastCheckAt: hoursAgo(0.25) },
    onboarding: {
      done: 5,
      total: 7,
      items: [
        { code: "meta_app", label: "Aplicación en Meta", done: true },
        { code: "business_verified", label: "Cuenta de negocio verificada", done: true },
        { code: "phone_registered", label: "Número registrado", done: true },
        { code: "webhook", label: "Webhook configurado", done: true },
        { code: "token", label: "Token de acceso permanente", done: true },
        { code: "app_subscription", label: "Suscripción de la aplicación al número", done: false },
        { code: "test_message", label: "Mensaje de prueba recibido", done: false },
      ],
      appSubscriptionDone: false,
    },
    last24h: { conversations: 38, delivered: 112, failed: 3 },
  });

  // Vínculos de WhatsApp propios, por persona y contexto.
  db.whatsappLinks.set(`${juan.id}:${acme.id}`, { phone: "+54 9 11 5555-0101", linkedAt: daysAgo(40) });
  db.whatsappLinks.set(`${juan.id}:${juanPersona.id}`, { phone: "+54 9 11 5555-0101", linkedAt: daysAgo(12) });
}

export const findUserByEmail = (email: string) => db.users.find((u) => u.email === email.trim().toLowerCase());
export const findTenantByCuit = (cuit: string) => db.tenants.find((t) => t.cuit === cuit);
export const membershipsOf = (userId: string) => db.memberships.filter((m) => m.userId === userId);
export const tenantById = (id: string) => db.tenants.find((t) => t.id === id);
export const findSession = (id: string) => db.sessions.find((s) => s.id === id);
export const degradedOf = (tenantId: string) => db.degraded.get(tenantId) ?? [];

export function pushMail(mail: Omit<Mail, "sentAt">): void {
  db.mails.unshift({ ...mail, sentAt: now() });
}
