# Roles de Seguridad y Column Security

Referencia de los **roles de seguridad** (security roles) y del **column security profile** (field-level security) de Sigil, extraída de la metadata real de la solución.

Fuentes:

- `solutions/unpacked/Roles/Sigil - SR - User.xml`
- `solutions/unpacked/Roles/Sigil - SR - Service.xml`
- `solutions/unpacked/Roles/Sigil - SR - Auditor.xml`
- `solutions/unpacked/Other/FieldSecurityProfiles.xml`

---

## Cómo leer los niveles de privilegio

Cada privilegio (`RolePrivilege`) tiene un atributo `level` que define el **alcance** del acceso: hasta qué punto de la jerarquía de la organización el usuario puede ejercer ese privilegio. En la metadata de Sigil aparecen dos niveles:

| Valor en el XML | Nivel de acceso | Significado |
| --- | --- | --- |
| `Basic` | **User** (usuario) | Solo sobre los registros que le pertenecen al propio usuario (o compartidos con él). |
| `Global` | **Org** (organización) | Sobre todos los registros de la organización, sin importar la unidad de negocio ni el propietario. |

> Dataverse define además dos niveles intermedios — **BU** (Business Unit) y **Parent-Child BU** (unidad de negocio y sus descendientes) —, pero **ninguno de los tres roles de Sigil los usa**. Todos los privilegios son o bien `Basic` (User) o bien `Global` (Org).

Los privilegios se nombran con el prefijo `prv` seguido del verbo (Create/Read/Write/Delete/Append/AppendTo/Assign/Share) y el nombre lógico de la tabla. Para la nomenclatura de tablas y columnas ver [Convenciones de Nomenclatura](convenciones-nomenclatura.md).

### Verbos de privilegio

| Verbo | Qué habilita |
| --- | --- |
| **Create** | Crear registros nuevos. |
| **Read** | Leer / consultar registros. |
| **Write** | Modificar registros existentes. |
| **Delete** | Eliminar registros. |
| **Append** | Adjuntar este registro *a* otro (ser el hijo de una relación). |
| **AppendTo** | Permitir que otros registros se adjunten *a este* (ser el padre de una relación). |
| **Assign** | Cambiar el propietario de un registro. |
| **Share** | Compartir un registro con otro usuario o equipo. |

---

## Las tablas de negocio de Sigil

Los tres roles otorgan (en distinta medida) acceso a las tablas de negocio de la solución. Sus nombres lógicos:

| Nombre lógico | Rol en el dominio |
| --- | --- |
| `sanic_sigil_tbl_event` | Evento firmable / a sellar. |
| `sanic_sigil_tbl_ledgerentry` | Entrada del ledger — el registro de evidencia sellada (hashes, token TSA). |
| `sanic_sigil_tbl_mastersignature` | Firma maestra. |
| `sanic_sigil_tbl_participant` | Participante de un evento. |
| `sanic_sigil_tbl_signaturezone` | Zona de firma. |
| `sanic_sigil_tbl_transaction` | Transacción. |

Todos los privilegios que estos roles otorgan sobre estas tablas de negocio están a nivel **Org** (`Global`).

---

## Rol: `Sigil | SR | User`

- **Para quién:** el usuario final estándar de la aplicación.
- **Qué otorga:** acceso de **solo lectura** a las tablas de negocio de Sigil, además de los privilegios base de plataforma (leer metadata, formas, vistas, ejecutar Flows y workflows). No puede crear, modificar ni eliminar datos de negocio.

### Privilegios sobre tablas de negocio

| Tabla | Create | Read | Write | Delete | Append | AppendTo | Assign | Share |
| --- | :---: | :---: | :---: | :---: | :---: | :---: | :---: | :---: |
| `event` | — | Org | — | — | — | — | — | — |
| `ledgerentry` | — | — | — | — | — | — | — | — |
| `mastersignature` | — | Org | — | — | — | — | — | — |
| `participant` | — | Org | — | — | — | — | — | — |
| `signaturezone` | — | Org | — | — | — | — | — | — |
| `transaction` | — | Org | — | — | — | — | — | — |

> El rol **User no otorga ningún privilegio** sobre `ledgerentry` (ni siquiera Read). El acceso al ledger de evidencia está reservado a los roles Service y Auditor.

Fuera de las tablas de negocio, este rol incluye los privilegios estándar de plataforma: lectura de metadata y personalizaciones (Entity, Attribute, OptionSet, SystemForm, WebResource, etc.), ejecución de Flows (`prvFlow`) y workflows, y gestión de sus propios ajustes de UI y consultas de usuario.

---

## Rol: `Sigil | SR | Service`

- **Para quién:** la **identidad de servicio** (el proceso automatizado / application user que escribe la evidencia).
- **Qué otorga:** control **total** (Create/Read/Write/Delete/Append/AppendTo/Assign/Share) a nivel Org sobre todas las tablas de negocio, incluida `ledgerentry`. Es el único rol que puede **escribir** el ledger de evidencia.

### Privilegios sobre tablas de negocio

| Tabla | Create | Read | Write | Delete | Append | AppendTo | Assign | Share |
| --- | :---: | :---: | :---: | :---: | :---: | :---: | :---: | :---: |
| `event` | Org | Org | Org | Org | Org | Org | Org | Org |
| `ledgerentry` | Org | Org | Org | Org | Org | Org | — | — |
| `mastersignature` | Org | Org | Org | Org | Org | Org | Org | Org |
| `participant` | Org | Org | Org | Org | Org | Org | Org | Org |
| `signaturezone` | Org | Org | Org | Org | Org | Org | Org | Org |
| `transaction` | Org | Org | Org | Org | Org | Org | Org | Org |

> Sobre `ledgerentry` el rol otorga Create/Read/Write/Delete/Append/AppendTo pero **no** Assign ni Share — el ledger no se reasigna ni se comparte, solo se crea y se lee.

Además de las tablas de negocio, este rol incluye privilegios que los otros dos no tienen, propios de una identidad de servicio / integración:

- **Callback Registration** — Create / Read / Write / Delete (`prvCreateCallbackRegistration`, etc.), a nivel User (`Basic`). Necesario para registrar webhooks / callbacks de integración.
- **Column Permission (portal FLS)** — Read sobre `mspp_columnpermission` y `mspp_columnpermissionprofile`, a nivel Org.

El resto son los mismos privilegios base de plataforma (metadata, Flows, workflows, ajustes de usuario) que los otros roles.

---

## Rol: `Sigil | SR | Auditor`

- **Para quién:** el rol de **auditoría / lectura ampliada**.
- **Qué otorga:** acceso de **solo lectura** a las tablas de negocio, pero — a diferencia de User — **sí puede leer `ledgerentry`** (la evidencia sellada). No puede crear, modificar ni eliminar ningún dato de negocio.

### Privilegios sobre tablas de negocio

| Tabla | Create | Read | Write | Delete | Append | AppendTo | Assign | Share |
| --- | :---: | :---: | :---: | :---: | :---: | :---: | :---: | :---: |
| `event` | — | Org | — | — | — | — | — | — |
| `ledgerentry` | — | Org | — | — | — | — | — | — |
| `mastersignature` | — | — | — | — | — | — | — | — |
| `participant` | — | Org | — | — | — | — | — | — |
| `signaturezone` | — | — | — | — | — | — | — | — |
| `transaction` | — | Org | — | — | — | — | — | — |

> El rol Auditor lee `event`, `ledgerentry`, `participant` y `transaction`, pero **no** otorga Read sobre `mastersignature` ni `signaturezone`. Su foco es la evidencia auditable (el ledger y sus registros asociados), no las estructuras de firma.

El resto son los privilegios base de plataforma (metadata, formas, vistas, Flows, workflows, ajustes de usuario), idénticos al rol User.

---

## Comparación rápida entre roles

Read (R) / Create-Write-Delete completo (CWD) sobre cada tabla de negocio, todo a nivel **Org**:

| Tabla | User | Service | Auditor |
| --- | :---: | :---: | :---: |
| `event` | R | CWD | R |
| `ledgerentry` | — | CWD | R |
| `mastersignature` | R | CWD | — |
| `participant` | R | CWD | R |
| `signaturezone` | R | CWD | — |
| `transaction` | R | CWD | R |

- **User** → solo lectura, **sin acceso al ledger**.
- **Service** → control total, **único que escribe** el ledger.
- **Auditor** → solo lectura, **incluye el ledger** (auditoría de evidencia).

---

## Column Security Profile: `Sigil | FLS | Evidence Writer`

`fieldsecurityprofileid = {7cb3ad1e-0580-f111-ab0e-70a8a59a720a}`

El **field-level security (FLS)** de Dataverse actúa *por encima* de los privilegios de tabla: aunque un rol tenga Read/Write sobre `ledgerentry`, las columnas marcadas como **secured field** quedan ocultas / no editables salvo que el usuario esté incluido en un column security profile que le otorgue permiso explícito sobre esas columnas.

### Columnas protegidas

Todas las columnas protegidas pertenecen a la tabla **`sanic_sigil_tbl_ledgerentry`** — son las columnas de **evidencia** del ledger:

| Columna | Contenido |
| --- | --- |
| `sanic_sigil_sealedon` | Marca temporal del sellado (cuándo se selló la evidencia). |
| `sanic_sigil_tsatoken` | Token de la Time Stamp Authority (TSA). |
| `sanic_sigil_finalhash` | Hash final de la evidencia sellada. |
| `sanic_sigil_contenthash` | Hash del contenido. |

### Permisos que otorga el profile

Los cuatro `FieldPermission` son idénticos:

| Permiso | Valor | Significado |
| --- | :---: | --- |
| `CanRead` | 4 | **Allowed** — puede leer la columna. |
| `CanUpdate` | 4 | **Allowed** — puede modificar la columna. |
| `CanCreate` | 4 | **Allowed** — puede escribir la columna al crear el registro. |
| `CanReadUnmasked` | 0 | **Not allowed** — no se define lectura sin máscara (no aplica; estas columnas no usan masking). |

> En field-level security el valor `4` significa **Allowed** y `0` significa **Not allowed**. Las cuatro columnas de evidencia otorgan Read + Update + Create a quien esté asignado a este profile.

### Propósito

Estas cuatro columnas (`sealedon`, `tsatoken`, `finalhash`, `contenthash`) son el **corazón de la evidencia criptográfica** del ledger: los hashes y el token de la autoridad de tiempo que prueban que un registro fue sellado y no fue alterado. Marcarlas como secured fields y otorgar escritura **únicamente a través del profile `Sigil | FLS | Evidence Writer`** garantiza que:

- **Solo la identidad de servicio** (el proceso que ejecuta el sellado, asociado a este profile) puede **escribir** esos hashes y tokens.
- Ningún otro usuario — aunque tuviera Write sobre `ledgerentry` por su rol — puede **fabricar ni alterar** los valores de evidencia. La escritura de la prueba criptográfica queda centralizada y a prueba de manipulación.

En otras palabras: los security roles definen *quién puede tocar la fila* del ledger, y el FLS profile define *quién puede tocar las columnas de evidencia dentro de esa fila*. Solo el Evidence Writer sella.
