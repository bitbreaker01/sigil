# Catálogo de Choices

**Fuente única de verdad** de los valores numéricos de los *choices* (option sets globales) de Sigil.
Estos números se **copian del portal** — nunca se predicen —, porque el código y los cloud flows
comparan **por número**, y un valor equivocado es un bug silencioso.

Este catálogo es un **contrato vivo**, atado por tres tests que lo parsean y lo verifican:

| Lado | Test | Verifica |
|------|------|----------|
| Backend | `ChoicesTests` | los enums de `src/backend/Sigil.Plugins.Core/Domain/Choices.cs` |
| Backend | `Conformance_ModeloDatosTests` | los option sets del **ambiente Dataverse real** |
| Frontend | `states.contract.test.ts` | los mapas de `src/frontend/sigil-app/src/domain/states.ts` |

> ⚠️ El formato de la tabla de abajo (`| choice | etiqueta | valor |`) lo parsean esos tests con un
> regex. **No cambies la estructura de columnas** sin actualizar los parsers. Si cambia un valor en
> Dataverse, actualizá esta tabla y el código a la vez — si no, alguno de los tres tests se pone rojo.

**Option Value Prefix del publisher `sanic`: `15946`** → todo valor cae en el rango `159460000`–`159469999`.

## Valores canónicos

| Choice | Etiqueta lógica | Valor |
|--------|-----------------|-------|
| sanic_sigil_choice_transactionstatus | Borrador | 159460000 |
| sanic_sigil_choice_transactionstatus | Pendiente de Firma | 159460001 |
| sanic_sigil_choice_transactionstatus | Firmado Parcialmente | 159460002 |
| sanic_sigil_choice_transactionstatus | Sellando | 159460003 |
| sanic_sigil_choice_transactionstatus | Completado | 159460004 |
| sanic_sigil_choice_transactionstatus | Rechazado | 159460005 |
| sanic_sigil_choice_transactionstatus | Expirado | 159460006 |
| sanic_sigil_choice_transactionstatus | Error de Sellado | 159460007 |
| sanic_sigil_choice_transactionstatus | Cancelado | 159460008 |
| sanic_sigil_choice_participantstatus | Pendiente | 159460000 |
| sanic_sigil_choice_participantstatus | Turno Activo | 159460001 |
| sanic_sigil_choice_participantstatus | Firmado | 159460002 |
| sanic_sigil_choice_participantstatus | Rechazado | 159460003 |
| sanic_sigil_choice_routingtype | Secuencial | 159460000 |
| sanic_sigil_choice_routingtype | Paralelo | 159460001 |
| sanic_sigil_choice_tsastatus | Sellado con TSA | 159460000 |
| sanic_sigil_choice_tsastatus | Sin sello TSA | 159460001 |
| sanic_sigil_choice_tsastatus | Re-sellado pendiente | 159460002 |
| sanic_sigil_choice_eventtype | Transacción creada | 159460000 |
| sanic_sigil_choice_eventtype | Enviada a firma | 159460001 |
| sanic_sigil_choice_eventtype | Firma registrada | 159460002 |
| sanic_sigil_choice_eventtype | Rechazada | 159460003 |
| sanic_sigil_choice_eventtype | Recordatorio programado | 159460004 |
| sanic_sigil_choice_eventtype | Sellado iniciado | 159460005 |
| sanic_sigil_choice_eventtype | Sellado completado | 159460006 |
| sanic_sigil_choice_eventtype | Error de sellado | 159460007 |
| sanic_sigil_choice_eventtype | Re-sellado TSA obtenido | 159460008 |
| sanic_sigil_choice_eventtype | Expirada | 159460009 |
| sanic_sigil_choice_eventtype | Verificación realizada | 159460010 |
| sanic_sigil_choice_eventtype | Cancelada por el creador | 159460011 |
| sanic_sigil_choice_eventtype | TSA abandonada | 159460012 |

## Cardinalidad

`transactionstatus` (9) · `participantstatus` (4) · `routingtype` (2) · `tsastatus` (3) · `eventtype` (13) — **31 opciones** en 5 choices.

> Los valores se referencian en la documentación por **nombre lógico**, jamás por número. El número
> vive únicamente acá (fuente de verdad), en `Choices.cs` (backend) y en `states.ts` (frontend).
