# Changelog

Todos los cambios notables de Sigil se documentan acá. El formato se basa en
[Keep a Changelog](https://keepachangelog.com/es-ES/1.1.0/); las versiones de la solución de Power
Platform se publican como [GitHub Releases](https://github.com/bitbreaker01/sigil/releases) (tags `sol-vX.Y.Z.B`).

## [Unreleased]

### Added
- **Documentación de referencia** (`docs/referencia/`): índice, catálogo de choices, modelo de datos,
  Custom APIs (contrato I/O completo de las 17), roles y seguridad, variables de entorno, conexiones y
  flujos, y typos conocidos — generada de la metadata real de la solución.
- **Documentación técnica del desarrollador** (`docs/desarrollo/`, 10 documentos): arquitectura, estructura
  y build, backend, sellado y cripto, firma maestra, frontend, deploy y ALM, testing y CI, cómo extender.
- **ALM de la solución de dos niveles**: workflow `solution-sync` (al publicar un Release: unpack + prune +
  commit de la metadata no-código en `solutions/unpacked/`) y `solution-release` (export managed+unmanaged
  con el Service Principal + creación del Release + reuso de `solution-sync`).
- **Test de contrato del frontend** (`states.contract.test.ts`): verifica los valores de choice de
  `states.ts` contra el catálogo — el espejo del `ChoicesTests` del backend.
- Este `CHANGELOG.md`.

### Changed
- **Code App — portabilidad de la conexión**: la conexión a Dataverse pasó de una conexión **directa**
  (`sharedConnectionId`, atada a un ambiente) a una **connection reference**
  (`xrmConnectionReferenceLogicalName`), haciéndola portable entre Dev/Test/Prod. Resuelve el error
  "no tenés permisos para usar esta conexión" en Test.
- **Frontend — backend por build mode**: `USE_REAL_BACKEND` ahora se deriva de `import.meta.env.PROD`
  (producción = backend real; dev/test = mock) en vez de estar hardcodeado.
- **Frontend — fuente única de choices**: `powerApps.ts` deriva sus constantes de choice de `states.ts`
  en vez de duplicarlas.
- **Reestructura del repositorio**: documentación viva (`docs/guias`, `docs/referencia`, `docs/desarrollo`)
  separada del andamio de construcción, que se apartó y luego se eliminó.
- **Catálogo de choices**: extraído del apéndice del documento de convenciones de nomenclatura a su propio
  documento (`docs/referencia/catalogo-de-choices.md`), como fuente única de verdad.

### Fixed
- **Deploy tool — login interactivo**: usa `RedirectUri=http://localhost` (loopback); MSAL.NetCore rechaza
  el redirect legacy `app://…`.
- **`solution-sync` — nombres ilegales en Windows**: sanea los archivos de roles con `|` (que el `pac unpack`
  genera de los display names) para no romper el checkout/pull en Windows.
- Se quitaron ~427 citas a documentos de diseño (ya eliminados) de los comentarios y mensajes del código, y
  se renombraron los tests de conformidad `Runbook*` → `Conformance_*`.

---

*El historial anterior a este changelog vive en el log de git.*
