import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

import languages from '../src/i18n/languages.json' with { type: 'json' };
import { staticUnusedNamespaces } from '../src/i18n/staticUnusedNamespaces.js';

const clientRoot = fileURLToPath(new URL('../', import.meta.url));
const cliPath = fileURLToPath(new URL('../node_modules/i18next-cli/dist/esm/cli.js', import.meta.url));

for (const namespace of staticUnusedNamespaces) {
  const result = spawnSync(
    process.execPath,
    [cliPath, 'status', languages.source, '--unused', '--namespace', namespace],
    {
      cwd: clientRoot,
      env: { ...process.env, CI: 'true' },
      stdio: 'inherit',
    },
  );

  if (result.error) throw result.error;
  if (result.status !== 0) process.exit(result.status ?? 1);
}
