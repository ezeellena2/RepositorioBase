/**
 * Recorrido guiado del sistema de ejemplo, en una ventana de Chrome visible.
 *
 *   npm run demo
 *
 * Requiere que la app esté corriendo (npm run dev, en otra terminal).
 */
import { chromium } from 'playwright';

const WEB = 'http://localhost:5173';
const API = 'http://localhost:3001';

const FAST = process.env.FAST === '1';
const t = (ms) => new Promise((r) => setTimeout(r, FAST ? 0 : ms));

async function main() {
  const browser = await chromium.launch({
    headless: FAST,
    channel: 'chrome',
    slowMo: FAST ? 0 : 260,
    args: ['--window-size=1280,900', '--window-position=60,40']
  });
  const page = await browser.newPage({ viewport: { width: 1280, height: 830 } });

  // cartel narrador, fijo arriba
  async function say(text, ms = 2200) {
    await page.evaluate((msg) => {
      let el = document.getElementById('__narrator');
      if (!el) {
        el = document.createElement('div');
        el.id = '__narrator';
        el.style.cssText = [
          'position:fixed', 'top:0', 'left:0', 'right:0', 'z-index:99999',
          'background:#0e5c66', 'color:#fff', 'padding:12px 20px',
          'font:600 15px/1.4 system-ui,sans-serif', 'letter-spacing:.01em',
          'box-shadow:0 4px 18px rgba(0,0,0,.18)', 'pointer-events:none'
        ].join(';');
        document.body.appendChild(el);
      }
      el.textContent = msg;
    }, text);
    await t(FAST ? 0 : ms);
  }

  const reset = () => fetch(`${API}/api/dev/reset`, { method: 'POST' });

  try {
    await reset();

    /* ── 1. Login ────────────────────────────── */
    await page.goto(`${WEB}/login`);
    await say('1 · La pantalla de entrada. Una tarjeta, una acción, nada más.', 2600);

    await say('2 · Si mando el formulario vacío, valida los dos campos.', 1600);
    await page.getByRole('button', { name: 'Entrar' }).click();
    await t(1800);

    await say('3 · Con la contraseña equivocada: un error genérico, a propósito.', 1800);
    await page.getByLabel('Correo electrónico').fill('juan@acme.com');
    await page.getByLabel('Contraseña').fill('clave-que-no-es');
    await page.getByRole('button', { name: 'Entrar' }).click();
    await t(2400);
    await say('No dice si el correo existe o si falló la contraseña. Nunca revela cuál de los dos.', 3000);

    /* ── 2. Registro ─────────────────────────── */
    await page.getByRole('link', { name: 'Registrate' }).click();
    await say('4 · El registro. Acá está la diferencia: no se pregunta si sos persona o empresa.', 3000);

    const cuit = page.getByLabel('CUIT');
    await say('5 · Escribo un CUIT que empieza con 20…', 1400);
    await cuit.fill('20357511845');
    await page.getByLabel('Nombre y apellido').click().catch(() => {});
    await t(1200);
    await say('Dice "Persona física" y el campo de abajo pasó a "Nombre y apellido". Se dedujo del CUIT.', 3400);

    await say('6 · Ahora uno que empieza con 30…', 1400);
    await cuit.fill('30712345671');
    await page.getByLabel('Razón social').click().catch(() => {});
    await t(1200);
    await say('"Empresa", y el campo cambió a "Razón social". Mismo formulario, sin preguntar nada.', 3400);

    await say('7 · Y si el dígito verificador está mal, avisa al salir del campo.', 1600);
    await cuit.fill('30712345679');
    await page.getByLabel('Nombre o razón social').click().catch(() => {});
    await t(2600);

    await say('8 · Completo un alta de verdad.', 1400);
    await cuit.fill('30715554441');
    await page.getByLabel('Razón social').fill('Distribuidora Norte SRL');
    await page.getByLabel('Correo electrónico').fill('hola@norte.com.ar');
    await page.getByLabel('Contraseña').fill('una-clave-larga-123');
    await t(800);
    await page.getByRole('button', { name: 'Crear cuenta' }).click();
    await t(2000);
    await say('9 · "Si el correo no estaba registrado…". Ambiguo a propósito: no revela qué correos existen.', 3800);

    /* ── 3. Confirmación ─────────────────────── */
    await say('10 · El correo simulado. Acá está el enlace de confirmación.', 2000);
    const mails = await (await fetch(`${API}/api/dev/mails`)).json();
    const link = mails.find((m) => m.link.includes('/confirmar'))?.link;
    if (!link) throw new Error('No se generó el correo de confirmación: el alta no llegó a crear la cuenta.');
    await page.goto(link);
    await t(1400);
    await say('11 · Confirmado. Si vuelvo a abrir el mismo enlace, no falla: sigue diciendo "Listo".', 2400);
    await page.goto(link);
    await t(2400);

    /* ── 4. Primer ingreso ───────────────────── */
    await say('12 · Entro con la cuenta recién creada.', 1600);
    await page.getByRole('button', { name: 'Iniciar sesión' }).click();
    await page.getByLabel('Correo electrónico').fill('hola@norte.com.ar');
    await page.getByLabel('Contraseña').fill('una-clave-larga-123');
    await page.getByRole('button', { name: 'Entrar' }).click();
    await t(2200);
    await say('13 · Tiene un solo contexto, así que entra derecho. Ve su razón social y su CUIT.', 3400);

    /* ── 5. Varios contextos ─────────────────── */
    await say('14 · Salgo y entro como alguien que tiene dos contextos.', 1800);
    await page.getByRole('button', { name: 'Salir' }).click();
    await t(1200);
    await page.getByLabel('Correo electrónico').fill('juan@acme.com');
    await page.getByLabel('Contraseña').fill('1234');
    await page.getByRole('button', { name: 'Entrar' }).click();
    await t(2000);
    await say('15 · Juan trabaja en Acme y además tiene su monotributo. El sistema le pregunta con cuál opera.', 3800);

    await page.getByText('Acme S.A.').click();
    await t(1800);
    await say('16 · Eligió Acme. Fijate el CUIT: es el de la empresa.', 3000);

    await say('17 · Y desde arriba cambia de contexto sin volver a entrar.', 1800);
    await page.locator('select').selectOption({ label: 'Juan Pérez' });
    await t(2000);
    await say('18 · Mismo usuario, otro CUIT. Es el caso de los dos sombreros del que veníamos hablando.', 4000);

    await say('Fin del recorrido. La ventana queda abierta para que la uses vos.', 3000);
    await page.evaluate(() => document.getElementById('__narrator')?.remove());

    console.log('\n✓ Recorrido terminado. La ventana queda abierta.');
    console.log('  Cerrala a mano, o Ctrl+C acá.\n');
    if (FAST) { await browser.close(); return; }
    await new Promise(() => {});
  } catch (err) {
    console.error('\n✗ Falló el recorrido:', err.message);
    console.error('  ¿Está corriendo la app? npm run dev\n');
    await browser.close();
    process.exit(1);
  }
}

main();
