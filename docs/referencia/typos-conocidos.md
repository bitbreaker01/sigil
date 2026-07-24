# Typos e Inconsistencias Conocidas

Errores de nomenclatura que **ya existen en la metadata desplegada** y que se conocen y se **dejan como
están** — corregirlos implica tocar varios lugares y re-desplegar/re-vincular. Se documentan para que
nadie los "arregle" a medias ni se sorprenda al encontrarlos.

## 1. Connection reference de Dataverse — `sanic_SigilConnDataverseSP`

**Qué:** el logical name de la connection reference de Dataverse es `sanic_SigilConnDataverseSP` —
PascalCase, y **rompe el patrón** `sanic_sigil_conn_*` que sí siguen Outlook (`sanic_sigil_conn_outlook`)
y Teams (`sanic_sigil_conn_teams`).

**Dónde vive:** en `Other/Customizations.xml` de la solución **y en los 3 cloud flows** (los tres JSON la
referencian con esa grafía exacta). Corregirlo implicaría tocar el XML de la solución + los 3 flows +
re-vincular la reference en cada ambiente.

**Por qué se deja:** funciona; el costo/riesgo de renombrar y re-atar en Dev/Test/Prod no compensa.

Ver [Conexiones y flujos](conexiones-y-flujos.md).

## 2. Alternate key del ledger — `sanic_sanic_sigil_ak_transaction`

**Qué:** el nombre de la alternate key de `sanic_sigil_tbl_ledgerentry` (la que garantiza **1 ledger por
transacción**) **duplica el prefijo del publisher**: `sanic_sanic_sigil_...` — dice `sanic_` dos veces.

**Impacto:** ninguno funcional; la key opera igual. Es solo cosmético en el schema name.

Ver [Modelo de datos](modelo-de-datos.md).

## 3. Display name de `signaturezone` — "Zon de firma"

**Qué:** el display name de `sanic_sigil_tbl_signaturezone` es **"Sigil | TBL | Zon de firma"** — le falta
la "a" (debería ser "Zona de firma"). El logical name está bien.

**Impacto:** solo visible en el portal de Dataverse; el código y los flows usan el logical name.

## 4. Tabla `event` presentada como "Historial"

**Qué:** la tabla de auditoría tiene logical name `sanic_sigil_tbl_event` pero display name **"Historial"**.

**Nota:** no es un typo sino una divergencia deliberada (el registro de eventos se le presenta al usuario
como "Historial"), pero conviene saberlo para no buscar una tabla "Historial" en el schema.

---

*Al corregir cualquiera de estos, actualizá este documento y verificá que no rompa referencias — sobre
todo la #1, que vive en los flows y en cada ambiente.*
