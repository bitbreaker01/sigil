import { test, expect } from '@playwright/test';
import { openApp } from './helpers';

// Flujo VERIFICAR: subir un PDF que NO es un documento sellado → veredicto determinista
// "no sealing record" (found=false). Prueba upload → hash SHA-256 → VerifyDocument → veredicto,
// sin depender de un archivo sellado específico (eso se prueba con el gate 9 / un fixture sellado).
test('verificar un PDF desconocido devuelve "sin registro de sellado"', async ({ page }) => {
  const app = await openApp(page);

  await app.getByRole('button', { name: 'Verify' }).click();
  await expect(app.getByText('Verify a document')).toBeVisible({ timeout: 20_000 });

  // Subir el fixture (relativo al cwd = src/frontend/sigil-app)
  await app.locator('input[type="file"]').setInputFiles('e2e/fixtures/sample.pdf');

  await expect(app.getByText(/We found no sealing record/i)).toBeVisible({ timeout: 30_000 });
});
