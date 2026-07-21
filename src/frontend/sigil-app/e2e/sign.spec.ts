import { test, expect } from '@playwright/test';
import { openApp } from './helpers';

// Flujo FIRMAR (RF-11): abrir la transacción pendiente del dashboard y firmarla.
// Firmar CONSUME la tx pendiente (deja de estar en turno), así que el test:
//   - SKIPEA si no hay ninguna pendiente (seed ya firmado o sin sembrar), y
//   - cuando hay una, la firma y verifica el toast de éxito.
// Para una suite repetible sin re-sembrar a mano, el camino durable es crear→firmar (ronda 3).
test('firmar la transacción pendiente del dashboard', async ({ page }) => {
  const app = await openApp(page);

  const review = app.getByRole('button', { name: 'Review & sign' }).first();
  const hasPending = await review.isVisible({ timeout: 20_000 }).catch(() => false);
  test.skip(
    !hasPending,
    'No hay tx pendiente en el seed (ya firmada o no sembrada). Re-sembrar una tx en turno para la cuenta, o usar crear→firmar.',
  );

  await review.click();

  // Pantalla Firmar: esperar a que el visor cargue y "Approve & sign" quede habilitado
  const approve = app.getByRole('button', { name: 'Approve & sign' });
  await expect(approve).toBeEnabled({ timeout: 90_000 });
  await approve.click();

  // Éxito: toast de firma registrada, o de sellado si fue el último firmante (.first(): Fluent
  // renderiza el contenido del toast + un anuncio aria-live → el texto aparece 2 veces).
  await expect(
    app.getByText(/Your signature has been registered|we are sealing the document/i).first(),
  ).toBeVisible({ timeout: 30_000 });
});
