import { createSession, db, membershipsOf, seed, type Session } from "./store.js";

// Escenarios de demostración. Cada uno reinicia los datos en memoria y deja el
// mockup en un punto de partida concreto de alguno de los recorridos.

export interface Scenario {
  id: string;
  journey: "A" | "B" | "C" | "D";
  name: string;
  description: string;
  /** Ruta del mockup donde arranca el recorrido. */
  start: string;
  /** Qué mirar cuando arranca. */
  hint: string;
}

interface ScenarioSetup {
  userId?: string;
  activeOrgId?: string | null;
  stepUpMinutesAgo?: number | null;
  revoked?: boolean;
}

interface ScenarioDef extends Scenario {
  setup?: () => ScenarioSetup;
}

const minutesAgo = (minutes: number) => new Date(Date.now() - minutes * 60_000).toISOString();

const DEFS: ScenarioDef[] = [
  // --- Recorrido A ---------------------------------------------------------
  {
    id: "a-registro",
    journey: "A",
    name: "Registro de organización",
    description: "Formulario vacío, sin sesión. Probá CUIT inválido, CUIT ya registrado y alta correcta.",
    start: "/registro",
    hint: "El CUIT 30-71234567-1 ya existe: sirve para ver el conflicto.",
  },
  {
    id: "a-confirmacion",
    journey: "A",
    name: "Confirmación de correo pendiente",
    description: "Identidad registrada sin confirmar, con el correo esperando en la bandeja simulada.",
    start: "/login",
    hint: "Abrí la bandeja en el panel de demostración y seguí el enlace de nuevo@sur.com.",
  },
  {
    id: "a-login-bloqueado",
    journey: "A",
    name: "Ingreso bloqueado (429)",
    description: "Intentos fallidos ya consumidos para juan@acme.com.",
    start: "/login",
    hint: "Un intento más devuelve 429 con el tiempo de espera.",
    setup() {
      db.attempts.push({ email: "juan@acme.com", failures: 3, blockedUntil: new Date(Date.now() + 30_000).toISOString() });
      return {};
    },
  },
  {
    id: "a-sesion-vencida",
    journey: "A",
    name: "Sesión vencida o revocada",
    description: "Sesión iniciada y luego revocada del lado del servidor.",
    start: "/app",
    hint: "Cualquier acción devuelve 401 y te lleva al ingreso con un aviso.",
    setup: () => ({ userId: "u-juan", activeOrgId: "org-acme", revoked: true }),
  },
  {
    id: "a-sin-organizacion",
    journey: "A",
    name: "Identidad sin organizaciones",
    description: "Sofía Aguirre ingresó pero no pertenece a ninguna organización.",
    start: "/app",
    hint: "Estado vacío explicativo con la acción para crear una organización.",
    setup: () => ({ userId: "u-sofia", activeOrgId: null }),
  },
  {
    id: "a-una-organizacion",
    journey: "A",
    name: "Una sola organización",
    description: "María López entra directo en contexto, con permisos limitados.",
    start: "/app",
    hint: "No hay selector inicial: entra directo y el contexto queda explícito.",
    setup: () => ({ userId: "u-maria", activeOrgId: "org-acme" }),
  },
  {
    id: "a-varias-organizaciones",
    journey: "A",
    name: "Varias organizaciones: elección inicial",
    description: "Juan Pérez ingresó y todavía no eligió con cuál operar.",
    start: "/app",
    hint: "Selector inicial; después queda el selector persistente en el shell.",
    setup: () => ({ userId: "u-juan", activeOrgId: null }),
  },

  // --- Recorrido B ---------------------------------------------------------
  {
    id: "b-contexto",
    journey: "B",
    name: "Contexto con permisos completos",
    description: "Juan Pérez operando en Acme S.A. como Titular.",
    start: "/app",
    hint: "Puede invitar. Cambiá a Distribuidora Sur y la acción desaparece.",
    setup: () => ({ userId: "u-juan", activeOrgId: "org-acme" }),
  },
  {
    id: "b-sin-permiso",
    journey: "B",
    name: "Sin permiso para invitar (403)",
    description: "Juan Pérez operando en Distribuidora Sur, donde es Integrante.",
    start: "/app/miembros",
    hint: "La acción no está en el menú y por URL directa se ve un 403 explicado.",
    setup: () => ({ userId: "u-juan", activeOrgId: "org-sur" }),
  },
  {
    id: "b-suspendida",
    journey: "B",
    name: "Organización suspendida",
    description: "Bruno Ortiz operando en Norte Servicios SRL, suspendida por Platform.",
    start: "/app",
    hint: "Contexto suspendido: las acciones operativas quedan bloqueadas con explicación.",
    setup: () => ({ userId: "u-bruno", activeOrgId: "org-norte" }),
  },
  {
    id: "b-error-red",
    journey: "B",
    name: "Error de red simulado",
    description: "El próximo pedido al servidor falla; después se puede reintentar.",
    start: "/app/miembros",
    hint: "Se ve el estado de error con acción de reintento.",
    setup() {
      db.failNext = "network";
      return { userId: "u-juan", activeOrgId: "org-acme" };
    },
  },

  // --- Recorrido C ---------------------------------------------------------
  {
    id: "c-emision",
    journey: "C",
    name: "Emitir una invitación",
    description: "Juan Pérez en Acme S.A., con permiso para invitar.",
    start: "/app/miembros",
    hint: "Al invitar se genera un correo utilizable en la bandeja simulada.",
    setup: () => ({ userId: "u-juan", activeOrgId: "org-acme" }),
  },
  {
    id: "c-invitacion-nueva",
    journey: "C",
    name: "Invitada nueva, sin cuenta",
    description: "Invitación pendiente para nadia@nueva.com, que todavía no tiene identidad.",
    start: "/invitacion?token=inv-nueva",
    hint: "Registrarse, confirmar el correo, ingresar y recién ahí aceptar.",
  },
  {
    id: "c-invitacion-sin-confirmar",
    journey: "C",
    name: "Invitada con cuenta sin confirmar",
    description: "Paula Giménez existe pero no confirmó el correo.",
    start: "/invitacion?token=inv-sin-confirmar",
    hint: "La membresía no se activa hasta confirmar e ingresar.",
  },
  {
    id: "c-invitacion-existente",
    journey: "C",
    name: "Identidad existente invitada",
    description: "María López, ya registrada, invitada a Distribuidora Sur SRL.",
    start: "/invitacion?token=inv-existente",
    hint: "Ingresa con su identidad y acepta, sin duplicar identidad ni membresía.",
  },
  {
    id: "c-invitacion-vencida",
    journey: "C",
    name: "Invitación vencida",
    description: "Enlace con fecha de vencimiento pasada.",
    start: "/invitacion?token=inv-vencida",
    hint: "Estado vencido, sin posibilidad de aceptar.",
  },
  {
    id: "c-invitacion-cancelada",
    journey: "C",
    name: "Invitación cancelada",
    description: "Invitación dada de baja por quien la emitió.",
    start: "/invitacion?token=inv-cancelada",
    hint: "Estado cancelado, con el siguiente paso explicado.",
  },
  {
    id: "c-invitacion-usada",
    journey: "C",
    name: "Aceptación repetida (idempotente)",
    description: "María López abre una invitación que ya había aceptado.",
    start: "/invitacion?token=inv-usada",
    hint: "Comunica que ya estaba aceptada y no duplica la membresía.",
    setup: () => ({ userId: "u-maria", activeOrgId: "org-acme" }),
  },

  // --- Recorrido D ---------------------------------------------------------
  {
    id: "d-bootstrap-vencido",
    journey: "D",
    name: "Invitación bootstrap vencida",
    description: "Enlace de alta de Platform vencido, con recuperación neutral.",
    start: "/platform/invitacion?token=inv-platform-vencida",
    hint: "La recuperación responde siempre igual: no revela identidades.",
  },
  {
    id: "d-mfa-pendiente",
    journey: "D",
    name: "Platform con MFA pendiente",
    description: "Elena Vidal aceptó la invitación pero no completó la secuencia de MFA simulada.",
    start: "/platform/mfa",
    hint: "La membresía Platform se activa recién al terminar la secuencia.",
    setup() {
      const invitation = db.invitations.find((i) => i.token === "inv-platform-vencida");
      if (invitation) {
        invitation.status = "accepted";
        invitation.expiresAt = new Date(Date.now() + 3600_000).toISOString();
      }
      db.platform.push({ userId: "u-elena", role: "admin", activatedAt: null });
      return { userId: "u-elena", activeOrgId: null };
    },
  },
  {
    id: "d-panel-completo",
    journey: "D",
    name: "Panel Platform con MFA vigente",
    description: "Carla Ruiz, titular de Platform, con step-up reciente.",
    start: "/platform",
    hint: "Directorios separados, paginación simulada y datos allowlisted.",
    setup: () => ({ userId: "u-carla", activeOrgId: null, stepUpMinutesAgo: 1 }),
  },
  {
    id: "d-sin-stepup",
    journey: "D",
    name: "Platform sin step-up reciente",
    description: "Diego Sosa puede mirar, pero no mutar sin revalidar MFA.",
    start: "/platform/organizaciones",
    hint: "Suspender pide revalidación simulada antes de continuar.",
    setup: () => ({ userId: "u-diego", activeOrgId: null, stepUpMinutesAgo: 240 }),
  },
  {
    id: "d-conflicto",
    journey: "D",
    name: "Conflicto de concurrencia (409)",
    description: "Otra persona modificó las organizaciones mientras esta pantalla estaba abierta.",
    start: "/platform/organizaciones",
    hint: "Suspender o reactivar devuelve 409 y ofrece recargar.",
    setup() {
      db.failNext = "conflict";
      return { userId: "u-carla", activeOrgId: null, stepUpMinutesAgo: 1 };
    },
  },
  {
    id: "d-ultimo-owner",
    journey: "D",
    name: "Protección del último titular",
    description: "Carla Ruiz es la única titular de Platform.",
    start: "/platform/administradores",
    hint: "Revocarla se impide y se explica por qué.",
    setup: () => ({ userId: "u-carla", activeOrgId: null, stepUpMinutesAgo: 1 }),
  },
];

export const scenarios: Scenario[] = DEFS.map(({ setup: _setup, ...rest }) => rest);

export function findScenario(id: string): ScenarioDef | undefined {
  return DEFS.find((s) => s.id === id);
}

/** Reinicia los datos en memoria y aplica el escenario. Devuelve la sesión si el escenario arranca autenticado. */
export function applyScenario(def: ScenarioDef): Session | null {
  seed();
  const setup = def.setup?.() ?? {};
  if (!setup.userId) return null;

  const activeOrgId =
    setup.activeOrgId === undefined
      ? membershipsOf(setup.userId).length === 1
        ? membershipsOf(setup.userId)[0].orgId
        : null
      : setup.activeOrgId;

  const stepUpAt =
    setup.stepUpMinutesAgo === undefined || setup.stepUpMinutesAgo === null
      ? null
      : minutesAgo(setup.stepUpMinutesAgo);

  const session = createSession(setup.userId, activeOrgId, stepUpAt);
  if (setup.revoked) session.revoked = true;
  return session;
}

/** Reinicio simple: vuelve a los datos originales sin sesión. */
export function resetAll(): void {
  seed();
}
