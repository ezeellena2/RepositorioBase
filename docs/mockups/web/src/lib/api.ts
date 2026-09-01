export interface Health {
  ok: true;
}

export interface Tenant {
  id: string;
  type: "persona" | "empresa";
  name: string;
}

export interface Me {
  user: { id: string; name: string; email: string };
  activeTenant: (Tenant & { cuit: string }) | null;
  tenants: Tenant[];
}

/** Error devuelto por la API con { error: { code, message } }. */
export class ApiError extends Error {
  readonly status: number;
  readonly code: string;
  constructor(status: number, code: string, message: string) {
    super(message);
    this.status = status;
    this.code = code;
  }
}

/** La API no respondió: red caída, servidor apagado, respuesta ilegible. */
export class NetworkError extends Error {
  constructor() {
    super("network");
  }
}

async function request<T>(method: string, url: string, body?: unknown): Promise<T> {
  let response: Response;
  try {
    response = await fetch(url, {
      method,
      headers: body === undefined ? undefined : { "Content-Type": "application/json" },
      body: body === undefined ? undefined : JSON.stringify(body),
      credentials: "same-origin",
    });
  } catch {
    throw new NetworkError();
  }

  if (response.status === 204 || response.status === 202) {
    return undefined as T;
  }

  let payload: unknown = null;
  try {
    payload = await response.json();
  } catch {
    if (response.ok) return undefined as T;
    throw new NetworkError();
  }

  if (!response.ok) {
    if (response.status >= 500) throw new NetworkError();
    const error = (payload as { error?: { code?: string; message?: string } } | null)?.error;
    throw new ApiError(response.status, error?.code ?? "unknown", error?.message ?? "Error");
  }
  return payload as T;
}

export const api = {
  health: () => request<Health>("GET", "/api/health"),
  register: (input: { cuit: string; name: string; email: string; password: string }) =>
    request<void>("POST", "/api/auth/register", input),
  login: (input: { email: string; password: string }) => request<void>("POST", "/api/auth/login", input),
  logout: () => request<void>("POST", "/api/auth/logout"),
  me: () => request<Me>("GET", "/api/me"),
};
