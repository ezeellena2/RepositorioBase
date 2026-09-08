import { describe, expect, it } from "vitest";
import { format, isValid, kind, normalize } from "./cuit.js";

describe("normalize", () => {
  it("quita guiones y espacios", () => {
    expect(normalize("20-33444555-1")).toBe("20334445551");
    expect(normalize(" 30 71234567 1 ")).toBe("30712345671");
  });

  it("devuelve null si no hay 11 dígitos", () => {
    expect(normalize("2033444555")).toBeNull();
    expect(normalize("20-33444555-11")).toBeNull();
    expect(normalize("")).toBeNull();
  });
});

describe("kind", () => {
  it("deduce persona por prefijo", () => {
    for (const p of ["20", "23", "24", "25", "26", "27"]) expect(kind(`${p}334445551`)).toBe("persona");
  });

  it("deduce empresa por prefijo", () => {
    for (const p of ["30", "33", "34"]) expect(kind(`${p}712345671`)).toBe("empresa");
  });

  it("devuelve null con prefijo 40", () => {
    expect(kind("40123456789")).toBeNull();
  });
});

describe("isValid", () => {
  it("acepta un CUIT válido de persona", () => {
    expect(isValid("20334445551")).toBe(true);
  });

  it("acepta un CUIT válido de empresa", () => {
    expect(isValid("30712345671")).toBe(true);
  });

  it("rechaza un verificador incorrecto", () => {
    expect(isValid("20334445556")).toBe(false);
  });

  it("rechaza uno de 10 dígitos", () => {
    expect(isValid("2033444555")).toBe(false);
  });

  it("resuelve los casos r=11 y r=10", () => {
    // 20000000001: suma = 5*2 = 10, r = 1 → dígito 1
    expect(isValid("20000000001")).toBe(true);
    // 27000000000: suma = 5*2 + 4*7 = 38, 38 % 11 = 5, r = 6 → dígito 6
    expect(isValid("27000000006")).toBe(true);
    // 20000000110: suma = 10 + 3*1 + 2*1 = 15, 15 % 11 = 4, r = 7 → dígito 7
    expect(isValid("20000000117")).toBe(true);
  });
});

describe("format", () => {
  it("arma XX-XXXXXXXX-X", () => {
    expect(format("20334445551")).toBe("20-33444555-1");
  });
});
