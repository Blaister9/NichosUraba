# Resultados de pruebas — membresías

**Fecha:** 25 de julio de 2026

## Ejecución

| Verificación | Resultado |
|---|---|
| `docker compose up -d` | PostgreSQL local saludable en puerto 5433 |
| `dotnet restore UrabaConecta.slnx` | Correcto |
| `dotnet build UrabaConecta.slnx --configuration Release --no-restore` | 0 advertencias, 0 errores |
| `dotnet ef database update ...` | `AddMembershipAdministration` aplicada |
| Dominio | 34/34 |
| Integración con PostgreSQL real | 14/14 |
| E2E Chromium | 11/11 |
| **Total** | **59/59** |

Las 36 pruebas de línea base permanecen. Se agregaron 12 unitarias, 6 de integración y 5 E2E.

## Cobertura nueva

- permisos efectivos de propietario, autoelevación, permisos superiores, transiciones y versiones;
- vinculación, cuenta ficticia, duplicidad, persistencia, activación y desactivación;
- revocación inmediata sobre citas, configuración y equipo con cookie vigente;
- aislamiento de lectura, modificación, activación, vinculación e historial;
- cuenta presente en dos negocios con permisos independientes;
- auditoría sin contraseña;
- propietario único, transferencia y dos desactivaciones concurrentes;
- conservación de perfiles operativos;
- cinco recorridos Chromium: asignación, revocación, último propietario, transferencia y aislamiento;
- vista principal de equipo a `360 × 800`.

La prueba concurrente crea un establecimiento ficticio con dos propietarios y demuestra exactamente un éxito y un `409`, dejando un propietario activo. Integración y E2E usan PostgreSQL Testcontainers; no se usó EF InMemory ni reintento automático de suites.
