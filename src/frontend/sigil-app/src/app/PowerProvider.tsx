// PowerProvider: resolves the Power Apps context ONCE at startup
// (getContext is async), exposes identity + query params via React context. In dev (without the
// runtime) it falls back to the browser URL and a mock identity. Identity is NEVER
// authoritative — the real authorization is the backend's; the UI only hides what
// the backend would reject anyway.

import { createContext, useContext, useEffect, useState, type ReactNode } from 'react';
import { queryParamsFromUrl } from '../lib/navigation';

export interface AppContext {
  user: { objectId?: string | undefined; fullName?: string | undefined; userPrincipalName?: string | undefined };
  queryParams: Record<string, string>;
  ready: boolean;
}

const Ctx = createContext<AppContext | undefined>(undefined);

// The context hook is co-located with its provider (standard pattern). Fast refresh doesn't apply
// to this app (it runs embedded in the Power Apps host), so the react-refresh rule is moot here.
// eslint-disable-next-line react-refresh/only-export-components
export function useAppContext(): AppContext {
  const c = useContext(Ctx);
  if (!c) throw new Error('useAppContext must be used within <PowerProvider>');
  return c;
}

export function PowerProvider({ children }: { children: ReactNode }): JSX.Element {
  const [ctx, setCtx] = useState<AppContext>({
    user: {},
    queryParams: queryParamsFromUrl(),
    ready: false,
  });

  useEffect(() => {
    let alive = true;
    void (async () => {
      try {
        // Deferred import: the SDK module may not be initialized in dev/test.
        const { getContext } = await import('@microsoft/power-apps/app');
        const c = await getContext();
        if (!alive) return;
        setCtx({
          user: {
            objectId: c.user.objectId,
            fullName: c.user.fullName,
            userPrincipalName: c.user.userPrincipalName,
          },
          queryParams: c.app.queryParams ?? {},
          ready: true,
        });
      } catch {
        // Dev/local: no Power Apps runtime. Mock identity + query params from the URL.
        if (!alive) return;
        setCtx({
          user: { fullName: 'Test User', userPrincipalName: 'test@sigil.local' },
          queryParams: queryParamsFromUrl(),
          ready: true,
        });
      }
    })();
    return () => {
      alive = false;
    };
  }, []);

  return <Ctx.Provider value={ctx}>{children}</Ctx.Provider>;
}
