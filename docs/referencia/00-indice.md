# Referencia — Índice

Documentación de **referencia** de Sigil: los contratos que el código y los tests consumen, y toda la
metadata **no-código** de la solución de Power Platform (generada de `solutions/unpacked/`).

| Doc | Qué es |
|-----|--------|
| [Convenciones de nomenclatura](convenciones-nomenclatura.md) | Reglas de nombres de todos los componentes |
| [Catálogo de choices](catalogo-de-choices.md) | Los 5 choices + valores (fuente única, verificada por 3 tests) |
| [Modelo de datos](modelo-de-datos.md) | Las 6 tablas: campos, tipos, relaciones, alternate keys |
| [Custom APIs](custom-apis.md) | El contrato de entrada/salida completo de las 17 Custom APIs |
| [Roles y seguridad](roles-y-seguridad.md) | Los 3 roles de seguridad + el column security profile |
| [Variables de entorno](variables-de-entorno.md) | Las 10 env vars: tipo, valor, y quién las consume |
| [Conexiones y flujos](conexiones-y-flujos.md) | Connection references + los 3 cloud flows |
| [Typos e inconsistencias conocidas](typos-conocidos.md) | Errores de naming conocidos y por qué se dejan |

> La metadata no-código (tablas, roles, env vars, flows…) se **versiona** en `solutions/unpacked/` y se
> regenera con el workflow `solution-sync` al publicar un Release. Estos documentos son la **vista humana**
> de esa metadata. Para el *cómo funciona* (arquitectura, internals), ver [`docs/desarrollo/`](../desarrollo/00-indice.md).
