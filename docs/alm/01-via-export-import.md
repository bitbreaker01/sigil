# Sigil — Vía 1: Exportar / Importar soluciones (manual · "packages")

**Documento:** ALM/01
**Estado:** Borrador (pendiente de verificación antagonista)
**Última actualización:** 2026-07-20
**Ver también:** [`00-vias-de-despliegue.md`](00-vias-de-despliegue.md) · [`04-env-vars-y-secretos.md`](04-env-vars-y-secretos.md) · [`../fase-0/09-alm-entornos-y-despliegue.md`](../fase-0/09-alm-entornos-y-despliegue.md)

> Cada afirmación de plataforma lleva cita `[n]`; **fuentes al final del archivo**. Lo no confirmado se marca **NO VERIFICADO**.

---

## 1. Qué es

La vía más básica: **exportar** la solución de Dev como **managed** e **importarla** a Test y Prod. Se hace desde el portal (maker / admin) o con la CLI `pac`. No requiere infraestructura extra: es la vía de arranque de cualquier tenant y el **plan B** natural cuando una vía automatizada falla (doc 09 §11).

## 2. Principios (el invariante del doc 00 §2)

- Unmanaged solo en Dev; se despliega **managed** a todo ambiente no-dev [1].
- La managed se **genera exportando la unmanaged como managed** y se trata como **artefacto de build** [1].
- No se importa una managed en el ambiente que contiene su unmanaged de origen — por eso Test/Prod son ambientes aparte [1].

## 3. Configuración (una sola vez por tenant/solución)

Esto ya está resuelto para Sigil (doc 12 + Runbook A); se resume para el caso reutilizable:

1. **Publisher con prefijo.** En Sigil: publisher `sanic` (prefijo `sanic_`). El prefijo del publisher marca todos los componentes.
2. **Solución unmanaged en Dev.** En Sigil: `sigil_core_sigil` — contiene tablas, choices, roles, perfil FLS, Custom APIs, plugin package, flows, connection references, env vars y la code app (doc 09 §2).
3. **Identidades y conexiones en Test/Prod ANTES del primer import** (Runbook A): application user + rol + perfil FLS (el perfil FLS **no viaja**, se repite por ambiente — doc 09 §7).
4. **Higiene de env vars antes de exportar** (crítico — doc 04 §3): remover los *current values* para que no viajen [3].

## 4. Cómo se usa — Portal (maker)

### 4.1 Exportar como managed (desde Dev)
1. En el maker portal de Dev, abrí la solución `sigil_core_sigil`.
2. **Publicá todos los cambios**: al exportar unmanaged solo se exportan los componentes publicados; Microsoft recomienda *Publish all changes* para incluir todo [2].
3. **Export as → Managed** [2].
4. Descargá el zip. **Este zip es el artefacto**: versionarlo en git (`solutions/snapshots/` — doc 09 §3).

### 4.2 Importar (en Test, luego en Prod)
1. En el ambiente destino, **Import solution** → subí el zip managed.
2. Si la solución trae **connection references**, el portal te pide elegir/crear las conexiones [4].
3. Si trae **environment variables**, el portal te pide **valores** — no muestra esta pantalla si los valores ya vienen en la solución o ya existen en el destino [5]. (Por eso la higiene del §3.4: querés que te pregunte, no que herede el valor de Dev.)
4. Al importar **managed**, los cambios entran **ya publicados** — no hace falta publicar después [6][7]. (En cambio, importar *unmanaged* entra en estado borrador y hay que publicar [6].)

## 5. Cómo se usa — CLI `pac` (scriptable)

Todos los comandos y flags están citados en §Fuentes. Flujo típico Dev → Test:

```bash
# 1) Exportar la unmanaged de Dev COMO managed  [8]
pac solution export --name sigil_core_sigil --managed --path ./out/sigil_core_sigil_managed.zip

# 2) (Opcional) Generar el archivo de deployment settings (conn refs + env vars)  [12]
pac solution create-settings \
  --solution-zip ./out/sigil_core_sigil_managed.zip \
  --settings-file ./config/settings.test.json
#   → editar settings.test.json con los valores del ambiente Test (ver doc 04 §4)

# 3) Importar en Test, aplicando settings y activando plugins  [9][10][11]
pac solution import \
  --path ./out/sigil_core_sigil_managed.zip \
  --settings-file ./config/settings.test.json \
  --activate-plugins \
  --publish-changes
```

Comandos de la CLI relevantes a esta vía (cada uno citado en §Fuentes):

| Comando | Para qué | Cita |
|---------|----------|------|
| `pac solution export --managed` | Exportar; `--managed` es un switch sin valor | [8] |
| `pac solution import` | Importar el zip en Dataverse | [9] |
| `pac solution import --settings-file <json>` | Aplicar valores de conn refs + env vars | [10] |
| `pac solution import --publish-changes` | Publicar tras un import exitoso | [11] |
| `pac solution import --activate-plugins` | Activar plugins/workflows al importar | [11] |
| `pac solution import --stage-and-upgrade` | Importar y **upgrade** (ver §6) | [11] |
| `pac solution upgrade --solution-name <n>` | Aplicar un upgrade **staged** pendiente | [11] |
| `pac solution create-settings` | Generar el deployment settings file | [12] |
| `pac solution version` | Actualizar build/revision de la solución | [11] |
| `pac solution clone` | Crear un proyecto de solución (cdsproj) desde una solución existente | [11] |

> **Sobre `pac solution pack` / `unpack`:** existen y toman `--packagetype Unmanaged|Managed|Both` [11], **pero** son para el patrón "solución desempaquetada en git" que **las code apps no soportan** (doc 00 §5) [13]. Para Sigil el artefacto es el **zip snapshot**, no el árbol unpackeado (doc 09 §3).

## 6. Import: update vs upgrade vs stage-for-upgrade

Al re-importar una versión nueva de una managed, hay tres comportamientos (documentados para la UI de update) [14]:

| Modo | Qué hace | Cuándo |
|------|----------|--------|
| **Upgrade** (default) | Sube a la última versión, hace rollup de patches y **elimina** los componentes que ya no están en la versión nueva [14] | El caso normal: querés que el destino refleje exactamente la solución nueva |
| **Stage for Upgrade** | Sube de versión pero **difiere el borrado** de la versión previa/patches hasta que apliques el upgrade después [14] | Cuando necesitás coexistencia temporal para migrar datos |
| **Update** | Reemplaza la solución **sin borrar** los componentes removidos (quedan en el sistema) [14] | Rara vez; deja huérfanos — usar con criterio |

En CLI, `--stage-and-upgrade` importa y aplica el upgrade; `pac solution upgrade` completa un upgrade staged [11].

## 7. Ejemplo Sigil (referencia traducible)

- Solución: `sigil_core_sigil` · publisher `sanic_`.
- Artefacto: `solutions/snapshots/sigil_core_sigil_vX.Y.Z_managed.zip`, tag git `sigil/vX.Y.Z` (doc 09 §9).
- Settings por ambiente: `config/settings.test.json`, `config/settings.prod.json` (valores del doc 09 §6; **nunca** el `env_TsaEndpoints` de FreeTSA de Dev viaja a Prod — doc 09 §5).
- Tras importar: correr los **10 gates** del Runbook B antes de habilitar tráfico.

## 8. Code Apps y esta vía — VERIFICADO

Las code apps tienen **exactamente dos** limitaciones de ALM: no soportan **Solution Packager** (pack/unpack) [13] ni **integración de código fuente / git** [15]. **Eso es todo.** No hay limitación sobre el **export/import del zip de la solución**.

Y esto es lo clave: **un pipeline, por dentro, es un export/import de zip.** La doc oficial lo dice — *"Both managed and unmanaged solutions are automatically exported and stored in the pipelines host for every deployment"* y *"the same solution artifact will be deployed"* [17]. Es más: Microsoft documenta, como workaround de rollback, **descargar el artefacto del host del pipeline e importarlo manualmente en el destino** [18].

**Conclusión:** exportar `sigil_core_sigil` como managed (con la code app adentro) e importarla a mano **es una alternativa válida** a Pipelines — es el **mismo mecanismo**. Lo único que NO se puede es el loop **unpack→commit→pack** (Solution Packager) ni el versionado desempaquetado en git (doc 09 §3). El "plan B" del doc 09 §11 queda **confirmado**, no hipotético.

- **NO VERIFICADO menor (operacional, no bloqueante):** si la code app recibe un **appId nuevo** en el destino (doc 09 §11) — se absorbe con el paso post-import de `env_AppPlayUrl` (doc 09 §6). Aplica igual a Pipelines y a esta vía manual (mismo mecanismo).

## 9. Ventajas y desventajas

**Ventajas**
- ✅ Cero infraestructura; disponible en cualquier tenant desde el día cero.
- ✅ Transparente y auditable: ves y versionás el zip exacto.
- ✅ Scriptable con `pac` (repetible, plan B confiable).

**Desventajas**
- ❌ Manual → error humano; **no** fuerza el orden de stages (disciplina del operador).
- ❌ Aprobaciones y gates **externos** a la herramienta.
- ❌ La higiene de env vars es responsabilidad tuya: un *current value* olvidado **viaja** al destino [3].

---

## Fuentes

Verificadas contra Microsoft Learn el 2026-07-20.

1. Solution concepts (unmanaged solo dev; managed a no-dev; managed = build artifact; no importar managed en el ambiente de su unmanaged): https://learn.microsoft.com/en-us/power-platform/alm/solution-concepts-alm
2. Exportar soluciones (Publish all changes antes de exportar; Export as → Managed): https://learn.microsoft.com/en-us/power-apps/maker/data-platform/export-solutions
3. Variables de entorno (remover el valor antes de exportar para que no viaje): https://learn.microsoft.com/en-us/power-apps/maker/data-platform/environmentvariables
4. Importar soluciones (prompt de connection references): https://learn.microsoft.com/en-us/power-apps/maker/data-platform/import-update-export-solutions
5. Importar soluciones (prompt de environment variables; no aparece si el valor ya está): https://learn.microsoft.com/en-us/power-apps/maker/data-platform/import-update-export-solutions
6. Importar soluciones (managed entra publicado; unmanaged entra en borrador y hay que publicar): https://learn.microsoft.com/en-us/power-apps/maker/data-platform/import-update-export-solutions
7. Update solutions (managed se importa siempre publicado): https://learn.microsoft.com/en-us/power-apps/maker/data-platform/update-solutions
8. `pac solution export` (`--managed` es switch sin valor): https://learn.microsoft.com/en-us/power-platform/developer/cli/reference/solution
9. `pac solution import` (importa a Dataverse): https://learn.microsoft.com/en-us/power-platform/developer/cli/reference/solution
10. `pac solution import --settings-file` (deployment settings: conn refs + env vars): https://learn.microsoft.com/en-us/power-platform/developer/cli/reference/solution
11. `pac solution import` flags (`--publish-changes`, `--activate-plugins`, `--stage-and-upgrade`) y comandos `version`, `clone`, `upgrade`, `pack --packagetype`: https://learn.microsoft.com/en-us/power-platform/developer/cli/reference/solution
12. `pac solution create-settings` (genera el settings file desde el zip/carpeta): https://learn.microsoft.com/en-us/power-platform/developer/cli/reference/solution
13. Code apps ALM (no soportan solution packager): https://learn.microsoft.com/en-us/power-apps/developer/code-apps/how-to/alm
14. Update solutions (Upgrade / Stage for Upgrade / Update — comportamiento de cada uno): https://learn.microsoft.com/en-us/power-apps/maker/data-platform/update-solutions
15. Code apps ALM (no soportan integración de código fuente): https://learn.microsoft.com/en-us/power-apps/developer/code-apps/how-to/alm
16. Code apps ALM (`pac code push` + Pipelines como camino de deploy): https://learn.microsoft.com/en-us/power-apps/developer/code-apps/how-to/alm
17. Pipelines FAQ (managed y unmanaged se exportan y guardan en el host; "the same solution artifact will be deployed" — el pipeline es export/import de zip): https://learn.microsoft.com/en-us/power-platform/alm/pipelines
18. Pipelines FAQ (workaround: descargar el artefacto del host e importarlo manualmente en el destino): https://learn.microsoft.com/en-us/power-platform/alm/pipelines
