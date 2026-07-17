// QueryClient (doc 05 §5.1): cache + polling for states and lists. BINARIES never go through
// here (§5.2) — the default gcTime would retain tens of MB of base64. refetchOnWindowFocus
// enabled only makes sense for state queries (binary ones don't exist in Query).

import { QueryClient } from '@tanstack/react-query';

export function createQueryClient(): QueryClient {
  return new QueryClient({
    defaultOptions: {
      queries: {
        staleTime: 30_000,
        gcTime: 5 * 60_000,
        refetchOnWindowFocus: true, // only applies to state queries (doc 05 §5.1)
        retry: 1,
      },
    },
  });
}

// Polling interval while there are Sealing transactions visible (doc 05 §5.1):
// 5 s, cap 3 min → then a message + manual refresh.
export const POLLING_SEALING_MS = 5_000;
export const POLLING_CAP_MS = 3 * 60_000;
