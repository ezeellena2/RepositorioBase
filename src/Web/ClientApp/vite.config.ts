import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import child_process from 'child_process';
import fs from 'fs';
import path from 'path';
import { env } from 'process';

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

// The dev server has to speak HTTPS for the same reason. The API compares the browser's Origin against the
// scheme the request arrived on, and it is reached over TLS; serving this side over HTTP made the two differ
// on scheme alone and every mutation was refused. It is also what __Host- cookies require: an insecure origin
// cannot store one, so a plain-HTTP dev server cannot exercise the session or antiforgery cookies at all.
// The ASP.NET development certificate is used rather than a generated one, so both halves present the
// identity the machine already trusts.
function devServerCertificate() {
  const folder =
    env.APPDATA !== undefined && env.APPDATA !== ''
      ? `${env.APPDATA}/ASP.NET/https`
      : `${env.HOME}/.aspnet/https`;
  const certificateFile = path.join(folder, 'clientapp.pem');
  const keyFile = path.join(folder, 'clientapp.key');

  if (!fs.existsSync(folder)) {
    fs.mkdirSync(folder, { recursive: true });
  }

  if (!fs.existsSync(certificateFile) || !fs.existsSync(keyFile)) {
    const exported = child_process.spawnSync(
      'dotnet',
      ['dev-certs', 'https', '--export-path', certificateFile, '--format', 'Pem', '--no-password'],
      { stdio: 'inherit', shell: process.platform === 'win32' },
    );
    if (exported.status !== 0) {
      throw new Error('Could not export the ASP.NET development certificate for the dev server.');
    }
  }

  return { key: fs.readFileSync(keyFile), cert: fs.readFileSync(certificateFile) };
}

// PORT is set only when the dev server is being hosted. Unit tests load this same config, and they have no
// server to secure — preparing a certificate for them would shell out to the .NET SDK to run `npm test`.
const serving = process.env.PORT !== undefined;

// https://vitejs.dev/config/
export default defineConfig({
  plugins: [react()],
  server: {
    port: serving ? parseInt(process.env.PORT!) : undefined,
    https: serving ? devServerCertificate() : undefined,
    proxy: proxyOptions
      ? {
          '/api': proxyOptions,
          '/openapi': proxyOptions,
          '/scalar': proxyOptions,
        }
      : undefined,
  },
  build: {
    outDir: 'build',
  },
});
