import { test, expect } from '@playwright/test';

// Smoke mínimo (ronda 1): confirma que la sesión guardada permite abrir la app
// SIN re-loguear. Si esto pasa, el login automatizado funcionó sin MFA.
// Los flujos reales (crear/firmar/verificar) se agregan una vez validada la auth.
test('la app abre autenticada, sin re-login', async ({ page }) => {
  await page.goto(process.env.SIGIL_APP_URL!);
  await expect(page).not.toHaveURL(/login\.microsoftonline\.com/);
  await expect(page).toHaveURL(/apps\.powerapps\.com/);
});
