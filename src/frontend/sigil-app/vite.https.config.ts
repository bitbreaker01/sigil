// TEMPORARY dev-only config: serves the app over HTTPS (self-signed) so that Web Crypto
// (crypto.subtle) works from a phone over the LAN/Tailscale — plain HTTP on a non-localhost
// origin is not a "secure context" and disables SubtleCrypto. Not committed; delete after testing.
import { readFileSync } from 'node:fs';
import base from './vite.config';

export default {
  ...base,
  server: {
    host: '0.0.0.0',
    port: 5174,
    https: {
      key: readFileSync('/tmp/sigil-verify-test/key.pem'),
      cert: readFileSync('/tmp/sigil-verify-test/cert.pem'),
    },
  },
};
