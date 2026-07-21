import type { Page, FrameLocator } from '@playwright/test';

// La app corre DENTRO del iframe del player de Power Apps (name="fullscreen-app-host",
// confirmado por el discovery). Todas las interacciones con la UI de Sigil pasan por acá.
export function app(page: Page): FrameLocator {
  return page.frameLocator('iframe[name="fullscreen-app-host"]');
}

export async function openApp(page: Page): Promise<FrameLocator> {
  await page.goto(process.env.SIGIL_APP_URL!);
  return app(page);
}
