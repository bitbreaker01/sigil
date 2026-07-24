# Solución de Power Platform — ALM

La solución `sigil_core_sigil` (publisher `sanic`) se versiona en **dos planos**, porque no todo
es igual:

## 1. Los `.zip` (managed + unmanaged) → **GitHub Releases**

Los paquetes exportados son **artefactos desplegables**, no fuente. No viven en el árbol de git
(inflarían el repo para siempre): se publican como **assets de un Release**, atados a un tag `vX.Y.Z.B`.

- **unmanaged** — fuente de verdad de Dev (se re-importa a Dev).
- **managed** — release sellado para Test/Prod.

Descargar un release:

```bash
gh release download vX.Y.Z.B --dir ./_dl
```

## 2. La metadata NO-código → `unpacked/` (versionada y diffeable)

Un `.zip` en un Release es un blob opaco: no ves *qué* cambió entre versiones. Por eso, la metadata
que **solo vive en la solución** se descomprime (`pac solution unpack` / unzip) y se commitea en
[`unpacked/`](unpacked/), donde el diff de git es tu changelog real:

| ✅ Se versiona en `unpacked/` (solo vive en la solución) | 🗑️ NO se versiona acá (ya es código) |
|---|---|
| Tablas / schema (`Entities/`) | Code App → fuente en `src/frontend` |
| Cloud flows (`Workflows/`) | Plugin package → fuente en `src/backend` |
| Connection references | Custom APIs / steps → los registra `tools/Sigil.Deploy` |
| Roles de seguridad, env var definitions | Web resources |

## Automatización

El unpack + limpieza + commit de la metadata se automatiza con GitHub Actions (ver
`.github/workflows/`), para no hacerlo a mano en cada cambio. El flujo:

1. Se exporta la solución de Power Platform.
2. El workflow la descomprime, **bota lo que ya es código**, y commitea solo la metadata en `unpacked/`.
3. Los `.zip` se publican como assets del Release.

> ⚠️ La solución contiene un **Code App**, que no soporta Solution Packager para round-trip. Por eso
> el code app **no** se reconstruye desde acá — su fuente es `src/frontend`. `unpacked/` es para
> *historia/diff* de la metadata, no para re-empaquetar el code app.
