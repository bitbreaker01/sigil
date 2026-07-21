import { test as setup, expect } from '@playwright/test';

// Loguea UNA vez contra Microsoft Entra y guarda la sesión en storageState.
// Diseñado para DETECTAR MFA/registro y fallar con un mensaje claro — es justo la
// preocupación a validar: si la cuenta todavía exige un segundo factor, esto lo delata.
const authFile = 'e2e/.auth/user.json';

setup('authenticate', async ({ page }) => {
  const url = process.env.SIGIL_APP_URL;
  const user = process.env.SIGIL_E2E_USER;
  const pass = process.env.SIGIL_E2E_PASS;
  if (!url || !user || !pass) {
    throw new Error('Faltan variables: SIGIL_APP_URL / SIGIL_E2E_USER / SIGIL_E2E_PASS');
  }

  await page.goto(url);

  // Paso 1 — email
  await page.locator('input[name="loginfmt"]').waitFor({ state: 'visible', timeout: 30_000 });
  await page.locator('input[name="loginfmt"]').fill(user);
  await page.locator('input[type="submit"]').click();

  // Paso 2 — password
  await page.locator('input[name="passwd"]').waitFor({ state: 'visible', timeout: 30_000 });
  await page.locator('input[name="passwd"]').fill(pass);
  await page.locator('input[type="submit"]').click();

  // Después del password pueden pasar varias cosas: redirect a la app (éxito),
  // "¿Mantener sesión iniciada?" (KMSI → Sí), o un desafío/registro de MFA (→ fallar claro).
  const MFA_SIGNS = [
    'Approve sign in request',
    'Enter code',
    'We texted',
    'More information required',
    'Help us protect your account',
    'Keep your account secure',
    'Verify your identity',
    'Additional security verification',
  ];

  const deadline = Date.now() + 60_000;
  let landed = false;
  while (Date.now() < deadline) {
    if (/apps\.powerapps\.com/.test(page.url())) { landed = true; break; }

    // MFA / registro → abortar con diagnóstico
    for (const sign of MFA_SIGNS) {
      const hit = await page.getByText(sign, { exact: false }).first().isVisible().catch(() => false);
      if (hit) {
        throw new Error(
          `El login pidió MFA/registro (pantalla: "${sign}"). La cuenta de prueba todavía tiene ` +
          `un requerimiento de segundo factor activo — revisá per-user MFA y políticas de CA (doc fase-3/01 §2).`,
        );
      }
    }

    // KMSI "Stay signed in?" → aceptar para que la sesión persista
    const kmsi = await page.getByText('Stay signed in?', { exact: false }).first().isVisible().catch(() => false);
    if (kmsi) {
      await page.locator('#idSIButton9').click().catch(() => {});
    }

    await page.waitForTimeout(1000);
  }

  if (!landed) {
    throw new Error(`El login no volvió a la app tras autenticar. URL actual: ${page.url()}`);
  }

  await expect(page).toHaveURL(/apps\.powerapps\.com/);
  await page.context().storageState({ path: authFile });
});
