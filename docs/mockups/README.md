# Mockups

Sistema de ejemplo en React y Node que muestra cómo se ve y cómo funciona el producto. No es el proyecto real y no comparte código con `src/`.

```bash
cd docs/mockups
npm install
npm run dev
```

Levanta la API en `http://localhost:3001` y la web en `http://localhost:5173`. La web proxea `/api` hacia la API.
Los datos viven en memoria y se pierden al reiniciar. El sistema visual está en `web/src/styles/` y los componentes base en `web/src/components/`.
