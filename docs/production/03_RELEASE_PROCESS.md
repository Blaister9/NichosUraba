# 03 — Proceso de publicación

## Regla

**Production se despliega únicamente desde `release/founder-production`.** Ninguna rama `feat/*`
autodespliega Production. Esa rama existe para que lo que corre frente a negocios reales sea un
punto conocido y revisado, no la última idea en curso.

## Flujo

```
feat/<algo>
   │  desarrollo y pruebas locales
   ▼
dotnet test -c Release            ← toda la suite en verde
   │
   ▼
Demo (release/pilot-demo)         ← se prueba con datos ficticios
   │
   ▼
validación funcional              ← recorrido real de la función
   │
   ▼
merge o cherry-pick a release/founder-production
   │
   ▼
Production                        ← despliegue automático de Railway desde esa rama
   │
   ▼
ops/smoke-production.ps1          ← comprobación de humo, sólo lectura
```

## Configuración en Railway

- Servicio Production: rama observada `release/founder-production`.
- Servicio Demo: rama observada `release/pilot-demo`.
- Health check: `/health/ready`.
- Reinicio: `ON_FAILURE`, máximo 3 reintentos. **No usar `ALWAYS`**: oculta fallos de
  configuración detrás de un ciclo de reinicios.
- Una réplica. Sin autoescalado.

## Antes de promover un commit

```bash
dotnet tool restore --tool-manifest dotnet-tools.json
```

```bash
dotnet tool run dotnet-ef migrations has-pending-model-changes --project src/UrabaConecta.Infrastructure --startup-project src/UrabaConecta.Web/UrabaConecta.Web --context AppDbContext
```

Lista de verificación:

1. Suite completa en verde en Release.
2. Sin cambios de modelo pendientes de migración.
3. Si hay migraciones nuevas: **respaldo antes de desplegar** (`ops/backup-postgres.ps1`).
4. `Deployment__Commit` actualizado al SHA que se despliega.
5. Tras el despliegue: `ops/smoke-production.ps1`.

## Prohibiciones

- No hacer push directo a `release/founder-production` sin haber validado en Demo.
- No desplegar Production un viernes por la tarde ni sin respaldo reciente.
- No promover una migración que no se haya aplicado antes sobre una copia restaurada.

## Reversión

Ver `11_ROLLBACK.md`.
