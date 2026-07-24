// Navigation by query params: the ONLY external entry point is the
// screen/txId pair. It's read ONCE at startup (getContext().app.queryParams or, in dev, the URL);
// from then on navigation is internal React state (history is untouched — the app runs
// embedded in the host's iframe).

export type Screen = 'dashboard' | 'sign' | 'verify' | 'detail' | 'create' | 'onboarding' | 'documents';

const SCREENS: ReadonlySet<string> = new Set([
  'dashboard', 'sign', 'verify', 'detail', 'create', 'onboarding', 'documents',
]);

export interface Route {
  screen: Screen;
  txId?: string;
  // Documents screen: optional pre-filter by "documents signed with my signature version N"
  // (opened from the signature history). Internal state only — maps to the version filter.
  signatureVersion?: number;
}

export function parseRoute(params: Record<string, string>): Route {
  const screen = params['screen'];
  const resolved: Screen = screen && SCREENS.has(screen) ? (screen as Screen) : 'dashboard';
  const txId = params['txId'];
  const route: Route = { screen: resolved };
  if (txId && /^[0-9a-fA-F-]{36}$/.test(txId)) route.txId = txId;
  return route;
}

/** In dev (no getContext) the query params come from the browser URL. */
export function queryParamsFromUrl(): Record<string, string> {
  if (typeof window === 'undefined') return {};
  const out: Record<string, string> = {};
  new URLSearchParams(window.location.search).forEach((v, k) => (out[k] = v));
  return out;
}
