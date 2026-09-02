// Recorrido guiado y verificación del sistema de ejemplo.
//   npm run demo              → con ventana, despacio, para mirar
//   FAST=1 node demo.mjs      → sin ventana, rápido, para verificar
//   SHOTS=./capturas ...      → además guarda capturas en esa carpeta
// Requiere api (:3001) y web (:5173) levantadas con `npm run dev`.

import { chromium } from "playwright";
import { mkdirSync } from "node:fs";
import { execFileSync } from "node:child_process";

const FAST = process.env.FAST === "1";
const SHOTS = process.env.SHOTS ?? null;
const BASE = process.env.BASE ?? "http://localhost:5173";
const T = FAST ? 0 : 60;
if (SHOTS) mkdirSync(SHOTS, { recursive: true });

const browser = await chromium.launch({
  headless: FAST,
  slowMo: FAST ? 0 : 200,
  executablePath: process.env.CHROME_PATH || undefined,
});
const ctx = await browser.newContext({ viewport: { width: 1280, height: 800 } });
// Entornos donde el navegador no sale a internet pero curl sí (proxies con CA propia):
// FONTS_VIA_CURL=1 sirve Google Fonts a través de curl para que las capturas sean fieles.
if (process.env.FONTS_VIA_CURL === "1") {
  await ctx.route(/https:\/\/fonts\.(googleapis|gstatic)\.com\/.*/, async (route) => {
    try {
      const req = route.request();
      const body = execFileSync("curl", ["-sS", "-A", req.headers()["user-agent"] ?? "Mozilla/5.0", req.url()], { maxBuffer: 50e6 });
      const type = req.url().includes("googleapis") ? "text/css" : "font/woff2";
      await route.fulfill({ status: 200, body, headers: { "content-type": type, "access-control-allow-origin": "*" } });
    } catch {
      await route.abort();
    }
  });
}
const page = await ctx.newPage();
page.on("pageerror", (e) => console.log("  ✗ error en la página:", e.message));

let failures = 0;
function ok(cond, label) {
  console.log(`  ${cond ? "✓" : "✗"} ${label}`);
  if (!cond) failures++;
}
async function shot(name) {
  if (!SHOTS) return;
  await page.evaluate(() => document.fonts.ready);
  await page.waitForTimeout(200);
  await page.screenshot({ path: `${SHOTS}/${name}.png` });
}
async function login(email, password = "1234") {
  await ctx.clearCookies();
  await page.goto(`${BASE}/login`);
  await page.locator('input[name="email"]').pressSequentially(email, { delay: T });
  await page.locator('input[name="password"]').pressSequentially(password, { delay: T });
  await page.keyboard.press("Enter");
}
const navLabels = () => page.locator(".nav-item").evaluateAll((els) => els.map((e) => e.getAttribute("aria-label")));
const headerCuit = () => page.locator(".shell-header .tenant-cuit").innerText();

// ---------------------------------------------------------------- Juan
console.log("\nJuan: dos sombreros");
await login("juan@acme.com");
await page.waitForSelector("text=¿Con quién querés operar?");
ok((await page.locator(".tenant-row").count()) === 2, "elige entre dos contextos antes de entrar");
await shot("01-elegir-contexto");
await page.click('.tenant-row:has-text("Acme S.A.")');
await page.waitForSelector(".home-seal:not(.is-skeleton)");
ok((await headerCuit()) === "30-71234567-1", "header muestra el CUIT de Acme");
ok((await page.locator(".shell").getAttribute("data-context")) === "empresa", "data-context = empresa");
ok((await page.locator(".shell-status").innerText()).includes("ARCA vence en 4 días"), "chip de estado con la condición abierta");
let labels = await navLabels();
ok(labels.join("|") === "Inicio|Integraciones|Capacidades del asistente|Miembros y WhatsApp|Mi WhatsApp", `navegación de dueño: ${labels.join(", ")}`);
ok((await page.locator(".home-contexts__cuit").allInnerTexts()).join("|") === "30-71234567-1|20-33444555-1", "los dos CUIT se ven juntos en el Inicio");
ok((await page.locator(".home-card").count()) === 3, "tres tarjetas de resumen sembradas");
await shot("02-inicio-acme");

// cambio de contexto por el selector
await page.click(".tenant-switch");
await page.waitForSelector(".tenant-pop");
ok((await page.locator('.tenant-pop [role="menuitemradio"]').count()) === 2, "popover con dos opciones (role menuitemradio)");
await shot("03-selector-abierto");
await page.click('.tenant-opt:has-text("Juan Pérez")');
await page.waitForSelector('.context-flash.is-visible:has-text("20-33444555-1")');
ok((await page.locator(".context-flash").innerText()).includes("Ahora operás con Juan Pérez · CUIT 20-33444555-1"), "acuse aria-live con el CUIT nuevo");
ok((await headerCuit()) === "20-33444555-1", "header cambió al CUIT del monotributo");
ok((await page.locator(".shell").getAttribute("data-context")) === "persona", "data-context = persona (sigilo índigo)");
ok((await page.locator(".shell-status").count()) === 0, "sin chip: el monotributo no tiene condiciones abiertas");
ok(await page.locator('.tenant-switch').evaluate((el) => document.activeElement === el), "el foco volvió al selector");
await shot("04-inicio-persona");

// teclado: selector y perfil cierran con Escape y devuelven el foco
await page.focus(".tenant-switch");
await page.keyboard.press("Enter");
await page.waitForSelector(".tenant-pop");
ok(await page.evaluate(() => document.activeElement?.getAttribute("role") === "menuitemradio"), "Enter abre el selector y el foco entra al panel");
await page.keyboard.press("Escape");
ok(await page.locator(".tenant-switch").evaluate((el) => document.activeElement === el), "Escape cierra el selector y devuelve el foco");
await page.focus(".profile__trigger");
await page.keyboard.press("Enter");
await page.waitForSelector(".profile-pop");
await page.keyboard.press("ArrowDown");
await page.keyboard.press("Escape");
ok(await page.locator(".profile__trigger").evaluate((el) => document.activeElement === el), "perfil: Escape devuelve el foco al disparador");

// "Mis contextos" abre el mismo popover del header
await page.click(".profile__trigger");
await page.click('.profile-item:has-text("Mis contextos")');
await page.waitForSelector(".tenant-pop");
ok(true, "Mis contextos abre el único popover de contexto");
await page.keyboard.press("Escape");

// colapsar el sidebar y persistir
await page.click(".shell-header .icon-btn");
await page.waitForTimeout(250);
ok(await page.locator(".shell").evaluate((el) => el.classList.contains("is-min")), "hamburguesa colapsa el sidebar a 68px");
ok((await page.evaluate(() => localStorage.getItem("shell.nav"))) === "min", "la preferencia persiste en localStorage['shell.nav']");
await shot("05-sidebar-colapsado");
await page.click(".shell-header .icon-btn");
await page.click(".profile__trigger");
await page.click('.profile-item:has-text("Cerrar sesión")');
await page.waitForURL(`${BASE}/login`);

// ---------------------------------------------------------------- María
console.log("\nMaría: el rail flaco");
await login("maria@acme.com");
await page.waitForSelector(".home-seal:not(.is-skeleton)");
labels = await navLabels();
ok(labels.join("|") === "Inicio|Mi WhatsApp", `dos ítems y nada más: ${labels.join(", ")}`);
ok((await page.locator(".nav-item[aria-disabled], .nav-item.is-disabled").count()) === 0, "ningún ítem gris");
ok((await page.locator(".tenant-badge").count()) === 1 && (await page.locator(".tenant-switch").count()) === 0, "un solo contexto: badge, sin botón");
ok((await page.locator('.home-card__foot:has-text("Ajustar capacidades")').count()) === 0, "miembro: ve las capacidades, no el pie para ajustarlas");
await shot("06-maria");
await page.goto(`${BASE}/organizacion/integraciones`);
await page.waitForURL(`${BASE}/inicio`);
ok(true, "/organizacion/integraciones redirige a /inicio (no hay 403)");
await page.request.post(`${BASE}/api/auth/logout`);

// ---------------------------------------------------------------- Operadora
console.log("\nOperadora: plataforma");
await login("operador@plataforma.com");
await page.waitForSelector(".home-seal:not(.is-skeleton)");
labels = await navLabels();
ok(labels.join("|") === "Inicio|Canal de WhatsApp|Puesta en marcha|Números vinculados|Catálogo de capacidades|Monitor", `grupo Plataforma completo: ${labels.length} ítems`);
ok((await page.locator('.nav-group__text:has-text("Lo mío")').count()) === 0, "desaparece Lo mío");
ok((await page.locator(".shell").getAttribute("data-context")) === "platform", "data-context = platform (tinta)");
ok((await page.locator('.home-card__label:has-text("Puesta en marcha")').count()) === 1, "grilla de plataforma");
await shot("07-operadora");
await page.goto(`${BASE}/mi/whatsapp`);
await page.waitForURL(`${BASE}/inicio`);
ok(true, "/mi/whatsapp redirige en contexto plataforma");
await page.request.post(`${BASE}/api/auth/logout`);

// ---------------------------------------------------------------- 375px
console.log("\n375px: el CUIT no se cae");
await page.setViewportSize({ width: 375, height: 740 });
await login("juan@acme.com");
await page.waitForSelector("text=¿Con quién querés operar?");
await page.click('.tenant-row:has-text("Acme S.A.")');
await page.waitForSelector(".home-seal:not(.is-skeleton)");
const cuitFits = await page.locator(".shell-header .tenant-cuit").evaluate((el) => {
  const r = el.getBoundingClientRect();
  return el.scrollWidth <= el.clientWidth + 1 && r.right <= window.innerWidth;
});
ok(cuitFits, `CUIT entero en el header a 375px: "${await headerCuit()}"`);
ok(await page.locator(".tenant-switch").evaluate((el) => el.classList.contains("is-compact")), "selector en modo compacto: CUIT arriba");
await shot("08-375-cerrado");
await page.click(".shell-header .icon-btn");
await page.waitForTimeout(250);
ok(await page.locator(".shell").evaluate((el) => el.classList.contains("is-drawer-open")), "hamburguesa abre el drawer");
ok(await page.evaluate(() => document.activeElement?.classList.contains("nav-item")), "el foco entra al drawer");
await shot("09-375-drawer");
await page.keyboard.press("Escape");
ok(await page.locator(".shell-header .icon-btn").evaluate((el) => document.activeElement === el), "Escape cierra el drawer y devuelve el foco a la hamburguesa");
await page.click(".tenant-switch");
await page.waitForSelector(".tenant-pop");
const popFixed = await page.locator(".tenant-pop").evaluate((el) => getComputedStyle(el).position === "fixed");
ok(popFixed, "el popover pasa a hoja inferior fija (mismo DOM)");
await shot("10-375-popover");
await page.keyboard.press("Escape");

await browser.close();
console.log(failures === 0 ? "\nTodo en orden." : `\n${failures} verificaciones fallaron.`);
process.exit(failures === 0 ? 0 : 1);
