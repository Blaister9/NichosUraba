# Cambios de modelo — configuración privada

## Migración

Se creó `20260725221914_AddPrivateBusinessConfiguration`.

La migración era necesaria porque el modelo implementado no contenía campos exigidos y ya descritos en parte por `05_DOMAIN_MODEL.md` y `06_DATA_MODEL.md`: descripción/orden, participación del personal, motivo/tipo de excepción y versiones de concurrencia.

## Tablas afectadas

| Tabla | Cambio |
|---|---|
| `services` | `Description`, `DisplayOrder`, `Version`; check de orden no negativo |
| `staff_members` | `ParticipatesInAvailability`, `Version` |
| `business_hours` | `Version` |
| `availability_exceptions` | `Type`, `Reason`, `Version`; check de intervalo |
| `business_memberships` | `CanManageConfiguration` |

No se crearon tablas duplicadas.

## Compatibilidad

- Servicios existentes reciben descripción vacía, orden `0` y versión `0`.
- Personal existente conserva participación en disponibilidad mediante valor inicial `true`; así no desaparecen franjas al migrar.
- Excepciones existentes se convierten: `IsUnavailable=true` a `ClosedAllDay`; las demás a `ExtraordinaryOpening`.
- Propietarios conservan acceso implícito. Trabajadores existentes permanecen sin permiso salvo asignación explícita.
- `IsUnavailable` se conserva para compatibilidad de datos; `Type` define el comportamiento nuevo.
- Citas y relaciones históricas no cambian.

La migración se probó al crear bases limpias en integración/E2E y al actualizar la base local que ya contenía la migración inicial.

## Reversión

`dotnet ef database update 20260725213558_InitialScheduling` elimina las columnas y checks nuevos. Antes de revertir debe respaldarse PostgreSQL: se perderían descripción, orden, permiso, motivos, tipos y versiones agregados. Las citas, servicios, perfiles y horarios base permanecen, pero un cierre parcial volvería a la representación anterior y ya no sería interpretable como tal.
