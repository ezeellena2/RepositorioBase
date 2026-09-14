import { describe, expect, it } from "vitest";
import {
  CODE_MAX_ATTEMPTS,
  evaluateCode,
  generateCode,
  maskDni,
  normalizeDni,
  remainingAttempts,
  resendWaitSeconds,
  type CodeState,
} from "./signup.js";

const NOW = Date.parse("2026-09-14T12:00:00Z");
const inMinutes = (minutes: number) => new Date(NOW + minutes * 60_000).toISOString();
const state = (overrides: Partial<CodeState> = {}): CodeState => ({
  code: "482913",
  expiresAt: inMinutes(10),
  attempts: 0,
  spentAt: null,
  ...overrides,
});

describe("código por correo", () => {
  it("genera siempre seis dígitos, también cuando el azar da un número chico", () => {
    expect(generateCode(() => 0)).toBe("000000");
    expect(generateCode(() => 0.0042)).toBe("004200");
    expect(generateCode(() => 0.999999)).toMatch(/^\d{6}$/);
  });

  it("acepta el código correcto, aunque venga con espacios", () => {
    expect(evaluateCode(state(), "482913", NOW)).toBe("ok");
    expect(evaluateCode(state(), "482 913", NOW)).toBe("ok");
  });

  it("rechaza un código distinto", () => {
    expect(evaluateCode(state(), "000000", NOW)).toBe("wrong");
    expect(evaluateCode(state(), "", NOW)).toBe("wrong");
  });

  it("un código vencido no se acepta ni siquiera siendo el correcto", () => {
    expect(evaluateCode(state({ expiresAt: inMinutes(-1) }), "482913", NOW)).toBe("expired");
  });

  it("agotados los intentos, el código correcto tampoco sirve", () => {
    expect(evaluateCode(state({ attempts: CODE_MAX_ATTEMPTS }), "482913", NOW)).toBe("locked");
  });

  it("un desafío ya usado no vuelve a aceptar nada", () => {
    expect(evaluateCode(state({ spentAt: inMinutes(-2) }), "482913", NOW)).toBe("closed");
  });

  it("cuenta los intentos que quedan sin bajar de cero", () => {
    expect(remainingAttempts(0)).toBe(CODE_MAX_ATTEMPTS);
    expect(remainingAttempts(CODE_MAX_ATTEMPTS + 3)).toBe(0);
  });

  it("dice cuánto falta para reenviar, redondeando hacia arriba", () => {
    expect(resendWaitSeconds(new Date(NOW + 12_300).toISOString(), NOW)).toBe(13);
    expect(resendWaitSeconds(new Date(NOW - 1_000).toISOString(), NOW)).toBe(0);
  });
});

describe("DNI", () => {
  it("normaliza puntos y espacios y acepta siete u ocho dígitos", () => {
    expect(normalizeDni("30.111.222")).toBe("30111222");
    expect(normalizeDni(" 8 123 456 ")).toBe("8123456");
  });

  it("rechaza lo que no es un DNI", () => {
    expect(normalizeDni("123456")).toBeNull();
    expect(normalizeDni("123456789")).toBeNull();
    expect(normalizeDni("30.111.22a")).toBeNull();
  });

  it("muestra sólo los últimos tres dígitos", () => {
    expect(maskDni("30111222")).toBe("••••222");
  });
});
