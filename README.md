# Sigil

Plataforma de **firma digital con evidencia criptográfica** sobre Microsoft 365 / Power Platform:
enviar documentos PDF a firmar, firmarlos, sellarlos con una marca de tiempo confiable (RFC 3161)
y **verificar** después que un documento es auténtico e íntegro.

## Estructura del repositorio

```
sigil/
├── src/
│   ├── backend/            # Plugins Dataverse (C#): Custom APIs + motor de sellado
│   └── frontend/sigil-app/ # Code App (React + TypeScript + Vite + Fluent UI)
├── tools/Sigil.Deploy/     # Despliegue del backend por SDK (registra package + Custom APIs)
├── tests/                  # Conformance (contra Dev) + integración (carrera de locks)
├── solutions/              # Artefactos de la solución de Power Platform  → ver solutions/README.md
│   └── unpacked/           #   metadata NO-código, versionada y diffeable (tablas, flows, roles…)
├── docs/
│   ├── guias/              # 📖 Documentación viva (usuario, operador, cumplimiento, desarrollador)
│   ├── referencia/         #   Contratos vivos que los tests corroboran (ej. Apéndice A de choices)
│   └── desarrollo/         #   Referencia técnica del código (arquitectura, backend, frontend, ALM…)
└── .github/workflows/      # CI
```

## Por dónde empezar

- **Usar / operar / auditar / extender Sigil** → **[docs/guias/](docs/guias/00-indice.md)** (la documentación viva).
- **Artefactos de la solución (zips managed/unmanaged)** → publicados como **GitHub Releases**; la
  metadata no-código se versiona en `solutions/unpacked/`. Ver **[solutions/README.md](solutions/README.md)**.

## Componentes

| Pieza | Stack | Ubicación |
|-------|-------|-----------|
| Backend (Custom APIs + sellado) | C# / .NET, Dataverse plugins | `src/backend/` |
| Frontend (Code App) | React + TS + Vite + Fluent UI v9 | `src/frontend/sigil-app/` |
| Herramienta de despliegue | C# / Dataverse ServiceClient | `tools/Sigil.Deploy/` |
| Solución Power Platform | managed/unmanaged | Releases + `solutions/` |
