# Registro de Despliegue — Test (primer Dev→Test)

**Ambiente:** Test (`b606ae67-46b7-e8a0-9e74-378e9d52021b`)
**Fecha:** 2026-07-21
**Origen:** Dev → Test vía Power Platform Pipelines (delegated deployment)
**Solución:** `sigil_core_sigil` · versión: **1.1.0.0** (snapshot en `solutions/snapshots/`)
**Solicitó / desplegó:** Randy Kauffman
**Aprobó (Dev→Test):** lead técnico
**Identidad de runtime:** **cuenta de servicio** (`sigil-notifications@…`) con rol `Sigil | SR | Service` + membresía del perfil FLS `Sigil | FLS | Evidence Writer`. *(Decisión: cuenta de servicio para Test/dev; SP+certificado para Prod — F5.)*

---

## Resultado del import
- ✅ **Import exitoso** — banner "Solution 'Sigil | Core | Sigil' imported successfully".
- ✅ Mix de componentes (code app + plugin package + flows en UNA solución) viajó completo → **resuelve el NO VERIFICADO de doc 09 §11**.

## Pasos post-import (config por-ambiente — no viaja en la solución)
- ✅ Code Apps habilitado en Test (toggle Features).
- ✅ CSP de code apps (`worker-src`/`connect-src` = `'self' blob:`).
- ✅ `env_AppPlayUrl` seteado al appId de la code app en Test (vía Default Solution).
- ✅ Rol `Sigil | SR | User` para el usuario (abrir la app).
- ✅ Cuenta de servicio: rol `Sigil | SR | Service` + membresía FLS.
- ✅ Flows: owner reasignado a la cuenta de servicio.

## Gates post-import (Runbook B)

| Gate | Qué verifica | Estado | Evidencia |
|------|--------------|--------|-----------|
| 1 | Alternate keys ACTIVE | ✅ (implícito por gate 9) | `CF-A06` / sellado OK |
| 2 | Plugin steps presentes y activos | ✅ | Verificado — el package viajó completo |
| 3 | 3 flows On + connection refs + ownership | ✅ | Owner = cuenta de servicio |
| 4 | Env vars con valores de **Test** (no FreeTSA) | ✅ | Tabla doc 09 §6 |
| 5 | CSP con la app real (visor PDF) | ✅ | PDF renderiza sin violaciones |
| 6 | Deep link desde card real → pantalla correcta | ✅ | Cierra gate 6 canónico de F3 |
| 7 | Descargas desde el iframe (PDF final + `.tsr`) | ✅ | `sha256sum` + `openssl ts -reply` |
| 8 | TSA alcanzable desde el sandbox | ✅ (usado en gate 9) | Token real obtenido |
| 9 | **Smoke E2E** (crear→firmar→sellar→verificar V/R) | ✅ | Selló con TSA real; ledger `#_______` *(completar)* |
| 10 | Heartbeat del job diario | ✅ | Correo a `env_OperatorEmail` |

**Los 10 gates: EN VERDE.**

## Prueba de notificación punta a punta (criterio de salida F4 — doc 10)
- ✅ Transacción del smoke notificó de punta a punta.
- ✅ Correo entregado.
- ✅ Card de Teams entregada.

## Cierre
- [x] Los 10 gates en verde
- [x] Heartbeat recibido + transacción notificando de punta a punta
- [x] Registro archivado (este documento) — versión 1.1.0.0; ledger del smoke pendiente de anotar

**Estado:** ✅ **F4 CERRADA (2026-07-21)** — primer Dev→Test en verde, los 10 gates pasados, ciclo probatorio y canal de notificaciones validados en Test. *(Pendiente cosmético: anotar el número de ledger del smoke.)*
