import { test, expect } from '@playwright/test';
import { openApp } from './helpers';

// Flujo FIRMAR (RF-11): abrir la transacción pendiente del dashboard y firmarla.
// NOTA: firmar CONSUME el seed (la tx deja de estar pendiente). Es un test de una pasada
// contra el seed actual; para re-correr hay que re-sembrar una tx en turno para la cuenta.
test('firmar la transacción pendiente del dashboard', async ({ page }) => {
  const app = await openApp(page);

  // Dashboard → pestaña Pending → abrir la card con "Review & sign"
  const review = app.getByRole('button', { name: 'Review & sign' }).first();
  await expect(review).toBeVisible({ timeout: 30_000 });
  await review.click();

  // Pantalla Firmar: esperar a que el visor cargue y "Approve & sign" quede habilitado
  const approve = app.getByRole('button', { name: 'Approve & sign' });
  await expect(approve).toBeEnabled({ timeout: 90_000 });
  await approve.click();

  // Éxito: toast de firma registrada, o de sellado si fue el último firmante
  await expect(
    app.getByText(/Your signature has been registered|we are sealing the document/i),
  ).toBeVisible({ timeout: 30_000 });
});
