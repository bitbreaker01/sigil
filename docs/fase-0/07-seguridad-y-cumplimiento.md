# Sigil — Seguridad y Cumplimiento

**Documento:** 07 — Fase 0
**Estado:** **Aprobado** (2026-07-10 — visto bueno del equipo)
**Última actualización:** 2026-07-10
**Depende de:** todos los anteriores; consolida las decisiones de seguridad dispersas en ADRs 005/006/007, doc 03 (§5/§6), doc 04 (§3.2/§3.3/§6) y doc 05 (§9) en un único modelo de amenazas.

Este documento no inventa controles nuevos: **mapea amenazas → controles ya decididos**, declara los límites honestos de cada control (todos verificados contra documentación oficial), y registra las recomendaciones organizacionales que exceden a Sigil.

---

## 1. Qué protege Sigil (activos)

1. **La integridad de los documentos firmados** — que nadie pueda alterar un PDF sellado sin que sea detectable (RNF-02).
2. **La atribución de las firmas** — que "X firmó esto a las HH:MM" sea respaldable (no repudio).
3. **La confidencialidad razonable** — que solo creador y participantes vean una transacción; que la actividad de firma de la organización no sea minable.
4. **La disponibilidad del flujo de firma** — sin bloqueos por payloads maliciosos o abuso de APIs.

## 2. Cadena de confianza (capas, de afuera hacia adentro)

| Capa | Control | Qué garantiza | Límite honesto (verificado) |
|------|---------|----------------|------------------------------|
| Identidad | **SSO Entra ID** (ADR-001); identidad autoritativa tomada del **contexto del plugin**, jamás del cliente; snapshots congelados al firmar (doc 03 §4.2) | Quién es cada actor; la evidencia no cambia si el usuario se renombra después | La robustez del login (MFA, Conditional Access) es del tenant, no de Sigil — §6 |
| Autorización | **Execute Privileges por Custom API** + **autorización de negocio por API contra `InitiatingUserId`** (doc 04 §3.2/§3.3); usuarios **sin ningún privilegio de escritura directo** (doc 03 §6) | Nadie ejecuta acciones ajenas ni escribe datos por fuera del motor | `IsPrivate` NO es seguridad (verificado); cada validación de negocio omitida = escalada — por eso los tests negativos por regla son criterio de done (doc 11) |
| Integridad | **`hash_contenido` anclado al enviar y verificado al sellar** (doc 04 §7 paso 2); **`hash_final`** sobre el archivo durable; **ledger append-only** (roles solo-lectura-vía-API + alternate key + Delete Restrict) | El contenido aprobado no cambió; el archivo distribuido es detectablemente íntegro byte a byte | La verificación exige recalcular el hash (ADR-007) — el QR solo lleva a la constancia |
| Evidencia protegida | **Column security** sobre hashes/token/fecha de sellado: solo el Service Principal (perfil Sigil \| FLS \| Evidence Writer) | Ni usuarios ni admins delegados leen/escriben la evidencia cruda | **No aplica a System Administrators** (verificado — ADR-006): un sysadmin del tenant puede leer y modificar columnas aseguradas |
| No repudio externo | **Token TSA RFC 3161** sobre `hash_final`, `CertReq=true`, doble validación, persistido en el ledger (ADR-005, doc 04 §6.4) | Un tercero imparcial certifica que ese hash existía en ese instante — fuera del alcance de cualquier admin del tenant | Es **feature flag** (RF-29): apagada, esta capa no existe y el nivel de evidencia lo declara por transacción |
| Auditabilidad | **Eventos de negocio** (toda transición — doc 06 R1, incluida cada verificación, evento 11) + **verificación cruzada del historial** (2026-07-13: hash del documento en cada evento de firma + chequeo de columnas de sistema sin modificación — doc 03 §4.6) + **auditoría nativa** en 4 tablas | Reconstrucción completa de quién hizo qué, cuándo y **sobre qué bytes exactos**, con detección de ediciones posteriores del historial | La auditoría registra operaciones **efectuadas**, no intentos; y el chequeo de columnas de sistema delata a todo actor que opere como sí mismo — NO a quien toma la identidad del motor (A13) ni al sysadmin (A4): para esos, la capa TSA |

**Lectura de la cadena:** cada capa cubre el límite de la anterior. La pregunta "¿y si lo hace un sysadmin **después** del sellado?" tiene respuesta en la capa TSA (A4). La pregunta "¿y **antes** del sellado?" tiene una respuesta más incómoda que este documento declara sin maquillar (A2b). Y "¿si la TSA está apagada?": tamper-evidence interna solamente, y el registro lo dice (RNF-02 precisado en doc 01).

**Nota de ventana de despliegue:** el índice del alternate key del ledger (control de unicidad 1:1) se activa **asíncronamente** al importar la solución — hasta su activación, ese control de integridad nace apagado. El runbook (doc 09) exige verificarlo antes de habilitar tráfico (gate obligatorio, doc 04 §9).

## 3. Modelo de amenazas → controles

| # | Amenaza | Actor | Controles (referencia) | Riesgo residual |
|---|---------|-------|------------------------|-----------------|
| A1 | Alterar un PDF descargado y hacerlo pasar por original | Cualquiera con el archivo | Verificación por hash full-file (ADR-007); estampa + QR llevan a la constancia | Ninguno significativo: el hash delata todo cambio |
| A2 | Alterar el PDF de contenido **entre el envío y el sellado** | Actor con acceso de escritura anómalo (no sysadmin) | `hash_contenido` anclado en Send y verificado en el paso 2 del pipeline → Error de Sellado + evento crítico (doc 04 §7) | Detectado siempre; investigación manual del evento |
| A2b | Ídem A2, por un **sysadmin** que reemplaza `contentfile` Y reescribe `contenthash` coherentemente en la ventana pre-sellado (la columna no está asegurada — doc 03 §4.1 — y la column security igualmente no le aplicaría) | Sysadmin malicioso | Auditoría nativa sobre la transacción registra el Update (desactivable por el mismo actor — límite conocido); los firmantes que ya visualizaron el documento son testigos de la discrepancia | **Riesgo residual declarado y no mitigable técnicamente dentro del tenant**: la TSA NO cubre esta ventana (sella lo que se le presenta al sellar). La reducción real es organizacional: §6 (restricción de sysadmins + PIM). Este es el límite más honesto de la cadena |
| A3 | Reescribir el hash del ledger para legitimar un documento adulterado | Admin delegado | Column security (sin Update) + auditoría + roles sin escritura | Bloqueado para todos salvo sysadmin (→ A4) |
| A4 | Ídem A3, por un **System Administrator del tenant** | Sysadmin malicioso | **TSA**: el token en el ledger (y en poder de cualquier verificador que lo haya descargado) sella el hash original ante un tercero — la reescritura es demostrable. Auditoría nativa como segunda señal (puede ser desactivada por el mismo actor — límite declarado) | Con TSA apagada (RF-29): **riesgo aceptado y visible** en el nivel de evidencia. Mitigación opcional futura: ancla en Blob immutable (ADR-005 §6, requiere excepción a RNF-07) |
| A5 | Repudio: "yo no firmé eso" | Firmante | SSO Entra + snapshots de identidad y de imagen de firma al momento exacto + evento con actor + timestamps UTC + (con TSA) tiempo certificado | No se captura IP/dispositivo (no disponible confiablemente en el contexto del plugin) — la atribución es a la **cuenta**; ver §6 (MFA) |
| A6 | Invocar Custom APIs ajenas (firmar por otro, cancelar lo ajeno, correr jobs) | Usuario interno malicioso | Execute Privileges (jobs solo Service Principal) + autorización de negocio por API + lock/idempotencia (doc 04 §3.2/§3.3/§5) | Bajo; cubierto por tests negativos obligatorios (doc 11) |
| A7 | Minar la actividad de firma de la organización | Usuario curioso | Ledger sin acceso directo de usuarios; `signersummary` solo vía `capi_VerifyDocument` con txId (GUID no enumerable, impreso solo en documentos legítimos); eventos accesibles solo a participantes (GrantAccess) | **Tradeoff declarado (C-08):** quien posee un documento legítimo (y por ende su QR/txId) obtiene la constancia — es el propósito del sistema. Cada verificación queda registrada (evento 11) |
| A8 | DoS / agotamiento de recursos con payloads gigantes | Cualquiera | Validación de longitud **antes** de decodificar base64 (doc 04 §3.4), `env_MaxPdfSizeKB`, `env_MaxParticipants`, kill por recursos del sandbox como última línea | Bajo |
| A9 | XSS / inyección en el frontend | Contenido malicioso en nombres/motivos | React escapa por defecto; **regla: `dangerouslySetInnerHTML` prohibido**; CSP estricta de plataforma (nuestra personalización solo abre `worker-src/connect-src 'self'` — sigue sin permitir orígenes externos); mensajes de rechazo/nombres tratados siempre como texto plano | Bajo |
| A10 | Residuos de documentos en dispositivos | Usuario en equipo compartido | Sin persistencia local (doc 05 §5.2): binarios solo en memoria de pantalla, liberados al desmontar; descarga solo explícita | El SO/navegador puede cachear descargas explícitas — responsabilidad del usuario/política de dispositivos (§6) |
| A11 | Phishing con deep links falsos hacia una app impostora | Atacante interno/externo | Los links legítimos apuntan siempre a `apps.powerapps.com` (dominio de plataforma con SSO corporativo); las notificaciones salen de flows corporativos identificables | Educación de usuarios (§6); Sigil no puede impedir que alguien clickee un dominio ajeno |
| A12 | Manipulación del pipeline: doble sellado, carrera de firmas, replay de Submit, reintentos zombis del worker | Concurrencia accidental o deliberada | Lock de fila + revalidación post-lock + idempotencia por participante + alternate key + orden durable-primero + **revalidación de estado actual y pre-check de ledger en el worker** (doc 04 §5/§7, doc 06 R7/T14) | Diseñado y con tests de concurrencia obligatorios (doc 11) |
| A13 | **Compromiso de la identidad del motor**: robo o abuso de la credencial del Service Principal (vive en la conexión Dataverse de los TRES flows — triggers y jobs — y en el registro de la app) — el actor más potente después del sysadmin: rol Servicio + perfil FLS Evidence Writer | Insider con acceso a la conexión / atacante con la credencial | Dueño de la conexión restringido y conexión **jamás compartida**; credencial por **certificado** con rotación (no client secret longevo); monitoreo de sign-ins del SP en Entra (alertas por origen anómalo); la TSA sigue delatando reescrituras post-sellado (como A4) | Medio: atraviesa todas las capas internas. La gobernanza de la credencial es EL control — entra al runbook (doc 09) |
| A14 | **TSA comprometida o canal interceptado**: tokens "válidos" firmados por un certificado arbitrario | TSA maliciosa / MITM | **HTTPS obligatorio** (la validación de `env_TsaEndpoints` rechaza `http://` — doc 04 §6.4); `CertReq=true` + doble validación. **Límite declarado**: no se valida la cadena a raíz confiable en el plugin — la verificación independiente (`openssl ts`) lo detectaría; endurecimiento futuro: pin de CAs por endpoint | Bajo (dos TSAs públicas de primera línea) y detectable a posteriori |
| A15 | **Backup/restore del ambiente** retrocede o elimina ledger y auditoría de transacciones ya distribuidas | Admin de tenant (operación legítima o vector) | Los tokens TSA descargados por verificadores externos **sobreviven al restore** y delatan la discrepancia (mismo principio que A4); el evento de restore queda en los logs de administración del tenant | Declarado: primo de A4 a nivel plataforma; sin mitigación técnica interna adicional |
| A16 | **Destrucción del `finalfile` post-sellado** (única copia sistémica — RNF-07): la evidencia descrita por el ledger deja de existir | Sysadmin / operación errónea | Usuarios sin Delete (roles); auditoría registra la operación; verificación de copias descargadas sigue funcionando (Rojo ante reemplazo) | **Amenaza a la disponibilidad de la evidencia**: si nadie descargó el original, el ledger describe un archivo que ya no existe. Mitigación organizacional: backups del ambiente (§6) — tensión declarada con A15 |
| A17 | **Indisponibilidad de las TSAs** (caída total, throttling, bloqueo de red — el spike encontró DigiCert inaccesible desde una red real, doc 04 §10) | Externo | Degradación elegante: sellado completa con `Re-sellado pendiente` + job de reintento (ADR-005); ≥2 endpoints con rate limits respetados; el sellado NUNCA se bloquea por la TSA | Bajo: pérdida temporal del nivel de evidencia Completo, visible por transacción |

## 4. Manejo de secretos y configuración

- **El código de Fase 1 no maneja secretos** (ADR-009 eliminó AI Vision; la TSA no requiere autenticación). **Precisión honesta:** el sistema SÍ tiene un secreto operativo — la **credencial del Service Principal** que autentica la conexión de los flows de jobs (A13). No vive en el código, pero existe y su gobernanza (certificado, rotación, dueño único, sin compartir) es parte del runbook (doc 09). Regla permanente: jamás secretos hardcodeados ni en variables de entorno de texto; si el futuro trae secretos de aplicación, la vía verificada es **managed identity para plugins** (GA ago-2025; los secrets de Key Vault NO son legibles desde plugins — verificado).
- Variables de entorno: configuración operativa, no secretos. Cambios en Prod = cambio controlado (doc 09).
- `.snk` de firma del assembly fuera de git; certificado de firma (si se adopta managed identity a futuro) gestionado por el proceso de la organización.

## 5. Privacidad y datos personales

- Datos personales manejados: nombre, correo, objectId de Entra (snapshots probatorios), imagen de firma manuscrita, contenido de los documentos.
- **La imagen de firma es dato sensible**: solo legible por su dueño (rol — doc 03 §6) y usada por el sistema para incrustación; el snapshot congelado por participante es parte de la evidencia de ESA transacción y hereda su acceso.
- **Tracing sin PII** (regla doc 04 §2): los trace logs son visibles para admins del ambiente — jamás nombres, correos, base64 ni hashes completos.
- **Retención:** los documentos completados y su evidencia son permanentes por diseño (R6, doc 06). No hay flujo de borrado post-completado en esta fase; un pedido de eliminación de datos personales contra un documento sellado es un **conflicto legal a resolver por la organización** (la evidencia pierde valor si se borra) — registrado como límite explícito, decisión fuera del alcance técnico.
- **Borradores confidenciales hasta el envío:** un participante NO puede leer el documento de un borrador no enviado (`GetDocumentContent` lo restringe por estado — doc 04 §3.3): el registro de participante existe desde la creación, pero el documento se le presenta recién al enviarse.
- **Higiene de sharing (POA, consolidado de doc 03 §6):** los shares son solo Read, otorgados al enviar (+ GrantAccess por evento); **jamás se revoca el acceso de un firmante a lo que firmó** — es su derecho probatorio — y los shares persisten tras estados terminales (el participante de una transacción cancelada conserva la constancia de que existió y su historial).
- Idioma/preferencias en `localStorage`: no sensible.

## 6. Recomendaciones organizacionales (fuera del alcance de Sigil, condicionan su garantía)

| Recomendación | Por qué importa a Sigil |
|---------------|-------------------------|
| **MFA + Conditional Access** para todos los usuarios | A5: la atribución de firma es a la cuenta; la fuerza de esa atribución ES la fuerza del login corporativo. Complemento gratuito: los **sign-in logs de Entra ID** registran IP/dispositivo del login que precedió a cada firma — control compensatorio de la no-captura de IP en Sigil |
| Restringir el rol System Administrator del ambiente al mínimo de personas, con PIM/acceso just-in-time | A2b y A4: el sysadmin es el único actor que atraviesa la column security (verificado), y la ventana pre-sellado (A2b) solo se reduce organizacionalmente |
| **Gobernanza de la credencial del Service Principal**: certificado (no secret longevo), rotación, dueño único de la conexión del flow, sin compartir, alertas de sign-in anómalo | A13: es el actor técnico más potente después del sysadmin |
| **Backups del ambiente** con política definida | A16 (pérdida de evidencia) — en tensión con A15 (un restore también retrocede el ledger): la política debe contemplar ambos |
| **TSA encendida en producción** (RF-29) | Sin ella, la cadena anti-repudio termina dentro del tenant |
| Los assets del push de la code app quedan en un **endpoint público sin restricción de IP** (verificado — investigación Code Apps jul-2026, registrada junto a las fuentes de ADR-001) | Mitigación recomendada por Microsoft: Conditional Access por ubicación si la política lo exige — decisión del tenant, no de Sigil |
| Política de dispositivos (Intune/MAM) para equipos que descargan documentos | A10 |
| No otorgar roles de maker/customizer en el ambiente de producción de Sigil | Reduce la superficie de modificación de componentes (flows, variables) |

## 7. Niveles de evidencia (resumen operativo)

| Nivel | Condición | Qué se puede demostrar |
|-------|-----------|------------------------|
| **Completo** | TSA activa, token válido en el ledger (`Sellado con TSA`) | Integridad + autoría + **tiempo certificado por tercero**; oponible fuera del tenant |
| **Completo diferido** | Token obtenido por re-sellado (`genTime` > `sealedon`) | Ídem, pero el tercero certifica existencia **a la fecha del token** — ambas fechas visibles (doc 04 §4) |
| **Interno transitorio** | `Re-sellado pendiente` (TSA activa pero inaccesible al sellar) | Integridad + autoría internas HOY; el token de tercero está en obtención (job de reintento) — la pantalla lo dice tal cual |
| **Interno** | TSA apagada (`Sin sello TSA`) | Integridad + autoría dentro del boundary del tenant (hash + ledger + FLS + auditoría); vulnerable solo a los actores A2b/A4 |

La pantalla de verificación muestra SIEMPRE el nivel — nadie asume evidencia que no existe (RF-29).

## 8. Qué NO es Sigil (límites de cumplimiento — recordatorio de doc 01)

- No emite **firma electrónica calificada** ni usa certificados X.509 personales por firmante: la validez es de **control interno corporativo** con evidencia criptográfica robusta.
- No cubre firmantes externos a la organización.
- La calificación legal de la evidencia ante un tribunal específico es materia de asesoría legal de la organización, no de este documento.

## 9. Trazabilidad

| RNF/RF | Sección |
|--------|---------|
| RNF-02 (no repudio, con precisión de RF-29) | §2, §3 (A4/A5), §7 |
| RF-17 (ledger inalterable) | §2 (capas integridad/evidencia) + A3/A4 |
| RF-18 (identidad Entra) | §2 (capa identidad) + A5 |
| RF-29 (TSA flag → nivel de evidencia) | §7 |
| RNF-04 | §2 (capa auditabilidad) |
| RNF-07 (solo Dataverse) | §4 (sin anclas externas salvo decisión futura documentada en A4) |

---

*Anterior: [06-maquina-de-estados-y-flujos.md](06-maquina-de-estados-y-flujos.md) · Siguiente: 08 — Notificaciones y recordatorios.*
