// SigilApi implementation selector. In DEV/local (without the Power Apps runtime) it uses the mock;
// in the real app, PowerAppsSigilApi. The decision is made ONCE here — screens import `sigilApi`
// and don't know which one it is.
//
// USE_REAL_BACKEND is driven by the BUILD MODE, not hand-toggled: production builds (`vite build`,
// i.e. the deployed Code App via `pac code push`) → real backend; dev/test (`npm run dev`, Vitest)
// → mock. This kills the class of bug where a hand-set `true` gets committed and breaks CI: Vitest
// runs under Node, where @microsoft/power-apps's ESM entry doesn't resolve, so the real client is
// loaded via a DYNAMIC import gated on the flag — non-PROD never pulls in the SDK. To exercise the
// real backend locally (`power-apps run`), set this to true temporarily — do NOT commit that.

import { MockSigilApi } from './mock';
import type { SigilApi } from './SigilApi';

const USE_REAL_BACKEND = import.meta.env.PROD;

async function create(): Promise<SigilApi> {
  if (!USE_REAL_BACKEND) return new MockSigilApi();
  const { PowerAppsSigilApi } = await import('./powerApps');
  return new PowerAppsSigilApi();
}

export const sigilApi: SigilApi = await create();
export type { SigilApi } from './SigilApi';
