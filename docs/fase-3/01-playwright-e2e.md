# Sigil — F3 Cierre 01: Playwright E2E automatizado contra Dev

**Documento operativo** (cierre de F3 — qué tenés que hacer para dejar el E2E automatizado corriendo)
**Estado:** Pendiente de ejecución
**Depende de:** [doc 11 §2/§3](../fase-0/11-testing-y-observabilidad.md) (decisión de stack + exclusión de CA), [doc 05 §4/§8](../fase-0/05-frontend-code-app.md) (pantallas y flujos), Runbook A §A12 (datos semilla)
**Leyenda:** 🧑 **acción tuya (usuario)** · 🤖 acción de Claude/dev (implementación) · ⚠️ decisión/riesgo

> Cada afirmación de plataforma lleva cita `[n]`; **fuentes al final**. Lo no confirmable se marca **NO VERIFICADO**.

---

## 1. Objetivo

Dejar una suite **Playwright** que maneje la **app real hosteada en Dev** (un navegador de verdad) y recorra los flujos de punta a punta: **crear → firmar → verificar** (doc 11 §2) [P1]. Es la red de regresión automatizada del frontend: corre sola en cada cambio, a diferencia de la prueba manual.

**Qué NO es:** no reemplaza la matriz de dispositivos reales (los `devices[...]` de Playwright son **emulación** — viewport/UA, no hardware real) [P7]. Esa matriz es el doc `02-matriz-dispositivos.md`.

## 2. El bloqueante: cuentas de prueba + exclusión de Conditional Access

El login de la app es **Entra SSO**. Un login automatizado headless **choca con MFA/Conditional Access** (doc 07 §6 pide MFA+CA para todos). Sin resolver esto, Playwright **no puede loguearse**. La decisión ya está tomada (doc 11 §3): **excluir cuentas de prueba dedicadas de la CA, SOLO en Dev**.

⚠️ **Verdad incómoda que hay que entender (verificado):** la Conditional Access es un **constructo a nivel de tenant** [C6]. Una cuenta excluida de una política queda excluida **donde esa política aplique** — la CA **no** tiene una dimensión "ambiente de Power Platform". Si Dev/Test/Prod comparten el mismo tenant Entra, una exclusión **no se limita sola a Dev**. Para acotar el daño, los docs soportan dos palancas combinables:

1. **Scope por Target resource (app):** una política de CA se puede apuntar a **aplicaciones específicas** [C5]. El patrón limpio: una **política separada apuntada SOLO a la app de Dev**, y excluir de **esa** política al grupo de cuentas de prueba — mientras la política estricta que cubre Test/Prod **no** las excluye.
   - ⚠️ **NO VERIFICADO:** ninguna página de CA que leí confirma que las apps de Power Platform Dev/Test/Prod aparezcan como **recursos de CA distintos y apuntables**. **Verificalo en tu tenant** antes de confiar en el aislamiento por app.
2. **Scope por acceso de la cuenta:** que las cuentas de prueba tengan acceso **solo a Dev** (sin rol/licencia en Test/Prod). Aunque la exclusión sea tenant-wide, la cuenta **no puede llegar** a Test/Prod. (Inferencia sólida, no recomendación textual de Microsoft — declarada.)

### 🧑 Pasos (usuario — rol mínimo: Conditional Access Administrator [C1])
1. **Crear cuentas de prueba dedicadas** (2–3, ej. `sigil-e2e-1@…`), o reutilizar los usuarios semilla (Runbook A §A12) — con **Firma Maestra** configurada (necesaria para el flujo firmar).
2. **Crear un grupo de seguridad** en Entra, ej. `sigil-e2e-exclusions`, con esas cuentas [C3][C4]. Usá un **grupo**, no cuentas sueltas — Microsoft lo recomienda para gestionar la membresía sin tocar la política [C3], y habilita **access reviews** como control compensatorio [C1].
3. **Entra admin center → Entra ID → Conditional Access** [C6] → crear una política **apuntada al recurso/app de Dev** [C5] → en *Assignments → Users → Exclude* agregar el grupo [C1]. (El *exclude* siempre gana sobre el *include* [C2].)
4. **Empezá en report-only** al menos una semana antes de enforcement [C3], y **probá** que las cuentas excluidas efectivamente **no** reciben MFA (otras políticas podrían igual forzarla) [C2].
5. **Nunca en Test/Prod** — la matriz de dispositivos en Test es manual con cuentas reales (doc 11 §3). Registrá la exclusión y cubrila con **access reviews** recurrentes [C1].

> ⚠️ **Seguridad (verificado):** usar exclusiones **con moderación y solo para cuentas confiables**, y re-agregarlas a la política apenas se pueda [C3]; una exclusión mal hecha es un bypass silencioso [C1].

## 3. 🤖 Setup de Playwright en el repo

Playwright vive **separado** del unit test (Vitest). Ubicación sugerida: `src/frontend/sigil-app/e2e/` con su propio `playwright.config.ts`.

```bash
cd src/frontend/sigil-app
npm init playwright@latest        # scaffolding; o: npm i -D @playwright/test@latest  [P1]
npx playwright install --with-deps # navegadores (+ deps de OS en CI)  [P2]
```

**`playwright.config.ts`** (campos citados) [P3][P6][P9]:
```ts
import { defineConfig, devices } from '@playwright/test';
export default defineConfig({
  testDir: './e2e',                                  // [P3]
  use: { baseURL: process.env.SIGIL_APP_URL,         // la Play URL de Dev  [P3]
         trace: 'on-first-retry' },                  // traza en el 1er retry  [P9]
  projects: [
    { name: 'setup', testMatch: /.*\.setup\.ts/ },   // proyecto de auth  [P5]
    { name: 'chromium', use: { ...devices['Desktop Chrome'],
        storageState: 'e2e/.auth/user.json' }, dependencies: ['setup'] }, // [P5][P6]
    // Emulación (NO dispositivos reales — ver doc 02):
    { name: 'Mobile Safari', use: { ...devices['iPhone 12'],
        storageState: 'e2e/.auth/user.json' }, dependencies: ['setup'] },  // [P6][P7]
    { name: 'Mobile Chrome', use: { ...devices['Pixel 5'],
        storageState: 'e2e/.auth/user.json' }, dependencies: ['setup'] },
  ],
});
```
> **No hace falta `webServer`:** ese campo lanza un dev server local; acá probás una **URL ya desplegada** (la app de Dev), así que se omite [P4].
> **Nombres de dispositivo:** usá los confirmados `iPhone 12` / `Pixel 5` (los del ejemplo oficial) [P6]; `iPhone 14`/`Pixel 7` **NO VERIFICADO** que existan con ese string — chequealos contra el registro de `devices` antes de usarlos.

## 4. 🤖 Autenticación (loguearse UNA vez y reusar)

El patrón oficial: un proyecto `setup` (`auth.setup.ts`) que loguea y **guarda el `storageState`** a un archivo; los demás proyectos lo reusan vía `storageState` + `dependencies: ['setup']` [P5]. Esto **elimina el re-login por test** [P5] — y, con la CA excluida (§2), el login corre sin MFA.

```ts
// e2e/auth.setup.ts
import { test as setup } from '@playwright/test';
const authFile = 'e2e/.auth/user.json';
setup('authenticate', async ({ page }) => {
  await page.goto(process.env.SIGIL_APP_URL!);        // redirige a login.microsoftonline.com
  await page.getByLabel('Email').fill(process.env.SIGIL_E2E_USER!);
  await page.getByRole('button', { name: 'Next' }).click();
  await page.getByLabel('Password').fill(process.env.SIGIL_E2E_PASS!);
  await page.getByRole('button', { name: 'Sign in' }).click();
  // "¿Mantener sesión iniciada?" → Sí; esperar a que cargue la app…
  await page.context().storageState({ path: authFile }); // [P5]
});
```
- 🧑 **Credenciales por variable de entorno**, nunca en el repo: `SIGIL_APP_URL`, `SIGIL_E2E_USER`, `SIGIL_E2E_PASS` en un `.env` gitignoreado (local) y en **GitHub Secrets** (CI). (Práctica estándar de GitHub Actions — no citada aquí.)
- ⚠️ **NO VERIFICADO (declarado):** los docs de Playwright **no** afirman "MFA no se puede automatizar" — esa es guía del equipo (doc 11 §3), no un hecho documentado por Playwright. Los selectores exactos del formulario de Entra pueden variar y suelen requerir ajuste fino; iteralo con `--ui`/`--headed`.

## 5. 🤖 Los flujos a cubrir (specs)

Mapear a los flujos de doc 05 §4 (una spec por flujo, con asserts de estado):
1. **Crear** (`create.spec.ts`): wizard → subir PDF → definir zonas obligatorias (RF-28) → enviar → la tx aparece en el dashboard como *Pendiente de Firma*.
2. **Firmar** (`sign.spec.ts`): abrir una tx en turno → visor PDF renderiza → firmar en la zona → estado avanza (Firmado Parcialmente / Sellando).
3. **Verificar** (`verify.spec.ts`): pantalla Verify → subir el PDF final → **Verde**; alterar y subir → **Rojo** (espejo del gate 9 / CF-D11, pero por UI).

## 6. 🤖 Correr y depurar

```bash
npx playwright test                 # todos los proyectos  [P8]
npx playwright test --project=chromium --headed   # ver la interacción  [P8]
npx playwright test --ui            # UI Mode (recomendado para desarrollo)  [P8]
npx playwright show-report          # reporte HTML (carpeta playwright-report)  [P10]
```
Ante un fallo, la **traza** (`trace: 'on-first-retry'`) se abre en el Trace Viewer para replay acción por acción [P9].

## 7. 🤖 CI (opcional, contra Dev)

`npm init playwright` ofrece generar `.github/workflows/playwright.yml` (corre en push/PR) [P11]; instala navegadores con `npx playwright install --with-deps` [P11] y sube el reporte HTML como artefacto [P11]. Las credenciales de prueba van como **GitHub Secrets** del environment `dev` (como el runner de conformidad — Runbook A §A4.4).

## 8. Salida (cuándo está terminado)

- Exclusión de CA aplicada y **probada** (§2.4), registrada + access review agendada.
- `playwright test` **verde** contra Dev en los 3 flujos (crear/firmar/verificar).
- (Opcional) job de CI corriendo contra Dev con secrets.

---

## Fuentes

Verificadas contra documentación oficial el 2026-07-20.

- P1. Playwright init / `@playwright/test`: https://playwright.dev/docs/intro
- P2. Instalar navegadores `--with-deps` (CI): https://playwright.dev/docs/browsers
- P3. Config: `testDir`, `baseURL`, `projects`: https://playwright.dev/docs/test-configuration
- P4. `webServer` lanza un server LOCAL; innecesario con una URL desplegada: https://playwright.dev/docs/test-webserver
- P5. Auth: reusar `storageState`; proyecto `setup`; `dependencies`: https://playwright.dev/docs/auth
- P6. Proyectos + `dependencies` + emulación de dispositivos (`iPhone 12`/`Pixel 5`): https://playwright.dev/docs/test-projects
- P7. Los `devices[...]` son **emulación** (userAgent/viewport/hasTouch), no hardware real: https://playwright.dev/docs/emulation
- P8. Correr: `--project`, `--headed`, `--ui`: https://playwright.dev/docs/running-tests
- P9. Trace Viewer / `trace: 'on-first-retry'`: https://playwright.dev/docs/trace-viewer-intro
- P10. Reporte HTML / `show-report`: https://playwright.dev/docs/test-reporters
- P11. CI en GitHub Actions (`--with-deps`, artefacto): https://playwright.dev/docs/ci-intro
- C1. Excluir con grupo; crear política; access reviews; riesgo de bypass: https://learn.microsoft.com/en-us/entra/id-governance/conditional-access-exclusion
- C2. El *exclude* gana sobre el *include*; probar en un set chico: https://learn.microsoft.com/en-us/entra/identity/conditional-access/concept-conditional-access-users-groups
- C3. Usar grupos; exclusiones con moderación/solo confiables; report-only; roles: https://learn.microsoft.com/en-us/entra/identity/conditional-access/plan-conditional-access
- C4. Grupo dedicado; patrón break-glass excluido de CA: https://learn.microsoft.com/en-us/entra/identity/role-based-access-control/security-emergency-access
- C5. Múltiples políticas combinan; Target resources por app: https://learn.microsoft.com/en-us/entra/identity/conditional-access/concept-conditional-access-policies
- C6. Ruta actual "Entra ID > Conditional Access"; per-tenant (el rol mínimo para crear/modificar políticas se cita en C1/C3): https://learn.microsoft.com/en-us/entra/identity/conditional-access/overview

**NO VERIFICADO (declarado):**
- Que las apps de Power Platform Dev/Test/Prod sean **recursos de CA distintos y apuntables** — verificar en el tenant (§2).
- Que Playwright documente "MFA no se puede automatizar" — es guía del equipo (doc 11 §3), no un hecho de Playwright (§4).
- Nombres de dispositivo `iPhone 14`/`Pixel 7` — usar los confirmados `iPhone 12`/`Pixel 5` o chequear el registro (§3).
- Guía de "guardar credenciales como GitHub Secrets" — práctica estándar de GitHub Actions, no citada aquí (§4/§7).
