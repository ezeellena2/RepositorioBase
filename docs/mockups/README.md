# Mockups

Sistema de ejemplo en React y Node que muestra cómo se ve y cómo funciona el producto. No es el proyecto real y no comparte código con `src/`.

```bash
cd docs/mockups
npm run install:all
npm run dev
```

Levanta la API en `http://localhost:3001` y la web en `http://localhost:5173`. La web proxea `/api` hacia la API. Los datos viven en memoria y se pierden al reiniciar.
Cuentas: `juan@acme.com` (dos contextos), `maria@acme.com` (miembro), `operador@plataforma.com` (plataforma), `nuevo@sur.com` (sin confirmar). Clave `1234`. El correo simulado se lee en `http://localhost:3001/api/dev/mails`.
Recorrido guiado con Playwright: `npm run demo` (con ventana) o `FAST=1 node demo.mjs` (verificación sin ventana). La primera vez: `npx playwright install chromium`.
