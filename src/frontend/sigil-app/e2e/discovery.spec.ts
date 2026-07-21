import { test } from '@playwright/test';

// TEMPORAL — discovery: engancha el iframe del player, loguea la estructura de frames
// y saca un screenshot del dashboard para escribir los flujos contra la realidad.
// Se borra una vez validados los specs de crear/firmar/verificar.
test('discovery: frames + screenshot del dashboard', async ({ page }, testInfo) => {
  await page.goto(process.env.SIGIL_APP_URL!);
  await page.waitForTimeout(12_000); // dejar bootear el player + la app

  const info: Array<{ url: string; name: string; hasRoot: number; text: string }> = [];
  for (const f of page.frames()) {
    let hasRoot = 0;
    let text = '';
    try { hasRoot = await f.locator('#root').count(); } catch { /* cross-origin */ }
    try { text = (await f.locator('body').first().innerText({ timeout: 2000 })).replace(/\s+/g, ' ').slice(0, 300); } catch { /* noop */ }
    info.push({ url: f.url(), name: f.name(), hasRoot, text });
  }
  console.log('FRAMES_JSON=' + JSON.stringify(info));

  await page.screenshot({ path: 'e2e/__shots__/dashboard.png', fullPage: true });
  await testInfo.attach('dashboard', { body: await page.screenshot({ fullPage: true }), contentType: 'image/png' });
});
