import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

const target =
  process.env['services__webapi__https__0'] ||
  process.env['services__webapi__http__0'];

// changeOrigin is deliberately off. It rewrites the Host header to the upstream's, while the browser still
// sends the frontend's Origin, and the API compares the two: every mutation from this client would be
// refused as antiforgery_validation_failed (SPEC section 8 requires exact-origin validation). Rewriting the
// host is for virtual-hosted upstreams; a same-origin BFF proxy must leave it alone.
const proxyOptions = target
  ? { target, secure: false, changeOrigin: false }
  : undefined;

// https://vitejs.dev/config/
export default defineConfig({
  plugins: [react()],
  server: {
    port: parseInt(process.env.PORT!),
    proxy: proxyOptions
      ? {
          '/api': proxyOptions,
          '/openapi': proxyOptions,
          '/scalar': proxyOptions,
          '/weatherforecast': proxyOptions,
          '/WeatherForecast': proxyOptions,
        }
      : undefined,
  },
  build: {
    outDir: 'build',
  },
});
