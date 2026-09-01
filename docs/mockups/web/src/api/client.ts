export interface ProblemDetails {
  type: string;
  title: string;
  status: number;
  code: string;
  detail?: string;
  errors?: Record<string, string[]>;
  traceId: string;
}

export class ApiError extends Error {
  readonly problem: ProblemDetails;
  constructor(problem: ProblemDetails) {
    super(problem.title);
    this.problem = problem;
  }
  get status() {
    return this.problem.status;
  }
  get code() {
    return this.problem.code;
  }
  get errors() {
    return this.problem.errors ?? {};
  }
}

export type TenantType = "Personal" | "Organization";

export interface TenantDto {
  id: string;
  type: TenantType;
  name: string;
  cuit: string;
  role: string;
}

export interface IdentityContext {
  user: {
    id: string;
    displayName: string;
    email: string;
    cuit: string;
    cuitKind: "persona" | "empresa";
    emailConfirmed: boolean;
  };
  activeTenant: TenantDto | null;
  availableTenants: TenantDto[];
  permissions: string[];
  session: { expiresAt: string; requiresTwoFactor: boolean };
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

async function request<T>(method: string, url: string, body?: unknown): Promise<T> {
  const response = await fetch(url, {
    method,
    headers: body === undefined ? undefined : { "Content-Type": "application/json" },
    body: body === undefined ? undefined : JSON.stringify(body),
    credentials: "same-origin",
  });
  if (response.status === 204 || response.status === 202) {
    return undefined as T;
  }
  const contentType = response.headers.get("content-type") ?? "";
  if (!response.ok) {
    if (contentType.includes("application/problem+json")) {
      throw new ApiError((await response.json()) as ProblemDetails);
    }
    throw new ApiError({
      type: "about:blank",
      title: "Algo salió mal",
      status: response.status,
      code: "unexpected",
      traceId: "",
    });
  }
  return (await response.json()) as T;
}

export const api = {
  register(input: { cuit: string; displayName: string; email: string; password: string }) {
    return request<void>("POST", "/api/identity/register", input);
  },
  resendConfirmation(email: string) {
    return request<void>("POST", "/api/identity/resend-confirmation", { email });
  },
  confirmEmail(token: string) {
    return request<void>("POST", "/api/identity/confirm-email", { token });
  },
  signIn(input: { email: string; password: string }) {
    return request<void>("POST", "/api/identity/sessions", input);
  },
  signOut() {
    return request<void>("DELETE", "/api/identity/sessions/current");
  },
  context() {
    return request<IdentityContext>("GET", "/api/identity/context");
  },
  selectTenant(tenantId: string) {
    return request<IdentityContext>("PUT", "/api/identity/context/tenant", { tenantId });
  },
  mailbox() {
    return request<{ messages: MailMessage[] }>("GET", "/api/demo/mailbox");
  },
  resetDemo() {
    return request<void>("POST", "/api/demo/reset");
  },
};
