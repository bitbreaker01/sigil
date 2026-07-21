import { test, expect } from '@playwright/test';
import { openApp } from './helpers';

// Cadena AUTÓNOMA crear→firmar: la misma cuenta crea una transacción (agregándose como
// firmante), dibuja la zona en el canvas, la envía, y luego la firma. Es self-seeding →
// repetible sin depender de datos sembrados a mano. (searchUsers no excluye al usuario actual
// y el creador puede ser participante — verificado en el código.)
test('crear una transacción y firmarla (cadena autónoma)', async ({ page }) => {
  test.setTimeout(180_000);
  const app = await openApp(page);
  const title = `E2E ${Date.now()}`;

  // 1) Abrir el wizard
  await app.getByRole('button', { name: 'New request' }).first().click();

  // 2) Documento: subir PDF + título
  await app.locator('input[type="file"]').first().setInputFiles('e2e/fixtures/sample.pdf');
  const titleBox = app.getByRole('textbox', { name: 'Request title' });
  await expect(titleBox).toBeVisible({ timeout: 30_000 });
  await titleBox.fill(title);
  // El "Next" del wizard tiene texto visible; el "Next" (página siguiente) del visor de PDF es
  // solo ícono (aria-label) → filtrar por texto los desambigua en el step de zonas.
  const next = app.getByRole('button', { name: 'Next' }).filter({ hasText: 'Next' });
  await expect(next).toBeEnabled({ timeout: 30_000 });
  await next.click();

  // 3) Firmantes: buscarme y agregarme
  const search = app.getByRole('textbox', { name: 'Add signer' });
  await expect(search).toBeVisible({ timeout: 20_000 });
  await search.fill('Playwright');
  const addBtn = app.getByRole('button', { name: 'Add signer' }).first();
  await expect(addBtn).toBeVisible({ timeout: 20_000 });
  await addBtn.click();
  await expect(next).toBeEnabled();
  await next.click();

  // 4) Zonas: armar el firmante (chip aria-pressed) y DIBUJAR sobre el canvas
  const chip = app.locator('button[aria-pressed]').first();
  await expect(chip).toBeVisible({ timeout: 30_000 });
  await chip.click();
  const canvas = app.locator('canvas').first();
  await expect(canvas).toBeVisible({ timeout: 30_000 });
  const box = (await canvas.boundingBox())!;
  // Rectángulo ~30% del ancho (alto se auto-bloquea 3:1). Movimiento >4px y >0.5% → zona válida.
  await page.mouse.move(box.x + box.width * 0.25, box.y + box.height * 0.30);
  await page.mouse.down();
  await page.mouse.move(box.x + box.width * 0.55, box.y + box.height * 0.45, { steps: 12 });
  await page.mouse.up();
  await expect(next).toBeEnabled({ timeout: 15_000 });
  await next.click();

  // 5) Revisar y enviar
  await app.getByRole('button', { name: 'Send for signature' }).click();
  await expect(app.getByText(/Request sent for signature/i)).toBeVisible({ timeout: 30_000 });

  // 6) Firmar la tx recién creada
  await app.getByRole('button', { name: 'Home' }).click();
  const review = app.getByRole('button', { name: 'Review & sign' }).first();
  await expect(review).toBeVisible({ timeout: 30_000 });
  await review.click();
  const approve = app.getByRole('button', { name: 'Approve & sign' });
  await expect(approve).toBeEnabled({ timeout: 90_000 });
  await approve.click();
  await expect(
    app.getByText(/Your signature has been registered|we are sealing the document/i).first(),
  ).toBeVisible({ timeout: 30_000 });
});
