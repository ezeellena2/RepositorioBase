import { describe, expect, it } from "vitest";
import { canSignIn, isSuspensionReason, restoredStatus, transitionFor } from "./lifecycle.js";
import { isAcceptedReference } from "./retention.js";

describe("ciclo de vida de una cuenta", () => {
  it("sólo una cuenta activa puede ingresar", () => {
    expect(canSignIn("active")).toBe(true);
    for (const status of ["pending_confirmation", "self_deactivated", "administratively_suspended", "closed"] as const) {
      expect(canSignIn(status)).toBe(false);
    }
  });

  it("ofrece reactivar sólo sobre una suspensión operativa", () => {
    expect(transitionFor("administratively_suspended")).toBe("reactivate");
    expect(transitionFor("active")).toBe("suspend");
    expect(transitionFor("self_deactivated")).toBe("suspend");
    expect(transitionFor("pending_confirmation")).toBe("suspend");
  });

  it("no ofrece nada sobre una cuenta cerrada: es terminal", () => {
    expect(transitionFor("closed")).toBeNull();
  });

  it("devuelve la cuenta a donde estaba, no a activa por definición", () => {
    expect(restoredStatus("self_deactivated")).toBe("self_deactivated");
    expect(restoredStatus("active")).toBe("active");
    expect(restoredStatus(null)).toBe("active");
  });

  it("acepta solamente razones del conjunto cerrado", () => {
    expect(isSuspensionReason("PolicyViolation")).toBe(true);
    expect(isSuspensionReason("BillingHold")).toBe(true);
    expect(isSuspensionReason("PorqueSí")).toBe(false);
    expect(isSuspensionReason("")).toBe(false);
  });
});

describe("referencias de retención", () => {
  it("acepta la forma que acepta el servidor", () => {
    expect(isAcceptedReference("CASO-2026-014")).toBe(true);
    expect(isAcceptedReference("litigio.expediente:14")).toBe(true);
  });

  it("rechaza vacío, espacios y más de 64 caracteres", () => {
    expect(isAcceptedReference("")).toBe(false);
    expect(isAcceptedReference("caso 2026")).toBe(false);
    expect(isAcceptedReference("a".repeat(65))).toBe(false);
    expect(isAcceptedReference("a".repeat(64))).toBe(true);
  });
});
