// SigilApi implementation selector. In DEV/local (without the Power Apps runtime) it uses the mock;
// in the real app, PowerAppsSigilApi. The decision is made ONCE here — screens import `sigilApi`
// and don't know which one it is.
//
// Flip USE_REAL_BACKEND to true to run against the real Dataverse backend (needs the Power Apps
// runtime + the generated clients — use `power-apps run` or the deployed Code App). The real client
// is loaded via a DYNAMIC import gated on the flag, so `npm run dev`/Vitest never pull in
// @microsoft/power-apps (its ESM entry doesn't resolve under Node) when the mock is active.

import { MockSigilApi } from './mock';
import type { SigilApi } from './SigilApi';

const USE_REAL_BACKEND = true;

async function create(): Promise<SigilApi> {
  if (!USE_REAL_BACKEND) return new MockSigilApi();
  const { PowerAppsSigilApi } = await import('./powerApps');
  return new PowerAppsSigilApi();
}

export const sigilApi: SigilApi = await create();
export type { SigilApi } from './SigilApi';
