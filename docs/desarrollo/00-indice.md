# Documentación de desarrollo de Sigil — Índice

**Qué es esta carpeta:** la referencia técnica **profunda** de Sigil — los *internals* del código,
la estructura del repositorio y los mecanismos de cada pieza. Va dirigida a quien lee, extiende o
mantiene el código (backend C# de Dataverse y frontend Code App en React/TypeScript).

**Relación con las guías.** La [Guía del Desarrollador](../guias/04-guia-desarrollador.md) es la
lectura de **alto nivel**: arranca acá si sos nuevo, te da el panorama, las convenciones y el porqué
de las decisiones. Esta carpeta (`docs/desarrollo/`) es el **detalle**: no repite el panorama, lo
profundiza — layout exacto, targets de compilación, comandos, contratos internos, el ciclo de vida
de una firma con sus estados numéricos.

## Panorama de las piezas

Sigil es una aplicación de **Microsoft Power Platform** en una única solución de Dataverse
(`sigil_core_sigil`, publisher `sanic`). Tres piezas de código y una de metadata:

| Pieza | Stack | Ubicación | Rol |
|-------|-------|-----------|-----|
| **Backend** | C# — plugins de Dataverse | `src/backend/` | 17 Custom APIs de negocio + worker de sellado asíncrono. El único que decide, valida, hashea y sella. Partido en núcleo puro (`netstandard2.0`) y cáscara (`net462`) |
| **Frontend** | React + TS + Vite + Fluent UI v9 | `src/frontend/sigil-app/` | Code App embebida en el player de Power Apps (iframe). Orquesta pantallas y llama Custom APIs. **Sin lógica de negocio ni criptografía** |
| **Herramienta de despliegue** | C# — Dataverse `ServiceClient` | `tools/Sigil.Deploy/` | Registra el plugin package y las Custom APIs por SDK, idempotente. Para Dev y diagnóstico |
| **Solución** | metadata de Power Platform | `solutions/` + GitHub Releases | Los `.zip` managed/unmanaged en Releases; la metadata no-código versionada en `solutions/unpacked/` |

Flujo de datos: el frontend llama Custom APIs vía el SDK `@microsoft/power-apps` (unas **bound** a
la tabla de transacción, otras **unbound** globales); los binarios (PDF, imágenes de firma) viajan
como **base64 por Custom API**. Toda escritura ocurre en los plugins, bajo contexto de sistema.

## Los documentos de esta carpeta

| # | Documento | Contenido | Estado |
|---|-----------|-----------|--------|
| 00 | **[Índice](00-indice.md)** | Esta página: qué hay acá y panorama de las piezas | ✅ |
| 01 | **[Arquitectura](01-arquitectura.md)** | Diagrama de piezas, ciclo de vida de una firma (estados + transiciones), modelo de confianza | ✅ |
| 02 | **[Estructura y build](02-estructura-y-build.md)** | Árbol del repo, tabla de proyectos .NET, cómo buildear/correr/testear cada pieza, prerequisitos | ✅ |
| 03 | **[Backend / plugins](03-backend.md)** | El framework `SigilApiPlugin`, anatomía de un handler, el lock de fila, las 17 Custom APIs | ✅ |
| 04 | **[Sellado y criptografía](04-sellado-y-cripto.md)** | El worker asíncrono, doble hash, TSA RFC 3161, incrustación de PNG, hoja de cierre | ✅ |
| 05 | **[Firma maestra](05-firma-maestra.md)** | Validación/normalización del PNG, versionado, contrato de coordenadas | ✅ |
| 06 | **[Frontend](06-frontend.md)** | El seam de datos (`SigilApi`), mock vs real, `PowerProvider`, navegación, pdf.js, i18n | ✅ |
| 07 | **[Deploy y ALM](07-deploy-y-alm.md)** | La herramienta de despliegue, el pipeline de solución (sync/release), Releases y metadata | ✅ |
| 08 | **[Testing y CI](08-testing-y-ci.md)** | La pirámide de tests, el stub de `IOrganizationService`, conformidad, carrera de locks, los jobs de CI | ✅ |
| 09 | **[Cómo extender](09-como-extender.md)** | Agregar una Custom API, agregar una pantalla, la regla de oro (backend decide, frontend orquesta) | ✅ |

> Los diez documentos están completos (✅). Esta carpeta es el detalle; para el panorama de alto nivel,
> la [Guía del Desarrollador](../guias/04-guia-desarrollador.md) sigue siendo la puerta de entrada.
