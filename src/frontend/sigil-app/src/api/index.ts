// SigilApi implementation selector. In DEV/local (without the Power Apps runtime or the
// generated clients) it uses the mock; in the real app, PowerAppsSigilApi. The decision is
// made ONCE here — screens import `sigilApi` and don't know which one it is.
//
// Rule: if the Power Apps context is available (getContext doesn't throw) AND the generated
// clients exist, use the real one. Until generated/ exists, we force the mock even if the
// runtime is present (allows `power-apps run` with the real UI before generating the clients).

import { MockSigilApi } from './mock';
import type { SigilApi } from './SigilApi';

// Compile-time flag: set to true once generated/ exists and powerApps.ts is wired.
const USE_REAL_BACKEND = false;

function create(): SigilApi {
  if (!USE_REAL_BACKEND) return new MockSigilApi();
  // Deferred import to avoid breaking the bundle if @microsoft/power-apps isn't initialized.
  throw new Error('PowerAppsSigilApi is enabled once the typed clients are generated (api/powerApps.ts).');
}

export const sigilApi: SigilApi = create();
export type { SigilApi } from './SigilApi';
