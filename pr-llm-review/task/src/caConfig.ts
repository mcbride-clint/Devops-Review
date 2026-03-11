import * as fs from 'fs';
import * as https from 'https';

/**
 * Reads a PEM/CRT CA bundle from disk and applies it globally so that all
 * outbound HTTPS connections (azure-devops-node-api via the Node https module,
 * and native fetch via undici) trust the custom CA without disabling validation.
 */
export function applyCaBundle(caPath: string): void {
  let ca: Buffer;
  try {
    ca = fs.readFileSync(caPath);
  } catch (err) {
    throw new Error(`Failed to read CA bundle at "${caPath}": ${String(err)}`);
  }

  // Patch the global https agent — covers azure-devops-node-api (typed-rest-client)
  https.globalAgent = new https.Agent({ ca });

  // Patch the undici global dispatcher — covers Node 18+ native fetch
  // undici is bundled with Node 18+; we use require() to avoid a compile-time
  // dependency and to gracefully skip when unavailable.
  try {
    /* eslint-disable @typescript-eslint/no-require-imports */
    const { setGlobalDispatcher, Agent } = require('undici') as {
      setGlobalDispatcher: (d: unknown) => void;
      Agent: new (opts: unknown) => unknown;
    };
    /* eslint-enable @typescript-eslint/no-require-imports */
    setGlobalDispatcher(new Agent({ connect: { ca } }));
  } catch {
    // undici not available — fetch calls will rely on the OS trust store
  }
}
