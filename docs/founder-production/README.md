# Producción controlada para Negocios Fundadores (V5)

Documentación de la rama `feat/v5-founder-production`: lo necesario para que una socia configure
un negocio desde la interfaz y para que la administración lo publique y lo opere sin tocar código,
el proveedor de despliegue ni la base de datos.

## Por dónde empezar

| Si usted es… | Empiece por |
| --- | --- |
| Socia que prepara negocios | [06_GUIA_SOCIAS.md](06_GUIA_SOCIAS.md) |
| Persona propietaria de un negocio | [07_GUIA_PROPIETARIO.md](07_GUIA_PROPIETARIO.md) |
| Administración de plataforma | [08_GUIA_ADMINISTRADOR.md](08_GUIA_ADMINISTRADOR.md) |
| Quien va a desplegar | [11_RUNBOOK_PRODUCCION.md](11_RUNBOOK_PRODUCCION.md) y [12_GO_LIVE_CHECKLIST.md](12_GO_LIVE_CHECKLIST.md) |
| Quien decide si se sale a producción | [15_DATOS_MANUALES_PENDIENTES.md](15_DATOS_MANUALES_PENDIENTES.md) |

## Índice

| Documento | Contenido |
| --- | --- |
| [00_AUDITORIA_INICIAL.md](00_AUDITORIA_INICIAL.md) | Estado real función por función antes de tocar código |
| [01_ARQUITECTURA_PRODUCCION.md](01_ARQUITECTURA_PRODUCCION.md) | Capas, autorización, estados y persistencia |
| [02_ROLES_Y_PERMISOS.md](02_ROLES_Y_PERMISOS.md) | Los cuatro perfiles y qué no puede hacer cada uno |
| [03_INVITACIONES_Y_ACCESOS.md](03_INVITACIONES_Y_ACCESOS.md) | Enlaces de un solo uso y reinicio de accesos |
| [04_CONFIGURACION_NEGOCIO.md](04_CONFIGURACION_NEGOCIO.md) | Checklist, campos, validaciones y flujo de revisión |
| [05_IMAGENES_Y_R2.md](05_IMAGENES_Y_R2.md) | Reglas de imágenes y configuración de Cloudflare R2 |
| [06_GUIA_SOCIAS.md](06_GUIA_SOCIAS.md) | Guía operativa para socias |
| [07_GUIA_PROPIETARIO.md](07_GUIA_PROPIETARIO.md) | Guía operativa para propietarios |
| [08_GUIA_ADMINISTRADOR.md](08_GUIA_ADMINISTRADOR.md) | Guía operativa para la administración |
| [09_BACKUP_Y_RESTORE.md](09_BACKUP_Y_RESTORE.md) | Qué respaldar, con qué frecuencia y cómo restaurar |
| [10_PRIVACIDAD_Y_CONSENTIMIENTO.md](10_PRIVACIDAD_Y_CONSENTIMIENTO.md) | Textos legales, consentimiento y evidencia |
| [11_RUNBOOK_PRODUCCION.md](11_RUNBOOK_PRODUCCION.md) | Ambientes, variables, despliegue y smoke tests |
| [12_GO_LIVE_CHECKLIST.md](12_GO_LIVE_CHECKLIST.md) | Checklist de salida a producción |
| [13_PLAN_DE_ROLLBACK.md](13_PLAN_DE_ROLLBACK.md) | Cómo revertir código, base de datos y volúmenes |
| [14_RESULTADOS_PRUEBAS.md](14_RESULTADOS_PRUEBAS.md) | Qué se ejecutó, con qué resultado y qué no se ejecutó |
| [15_DATOS_MANUALES_PENDIENTES.md](15_DATOS_MANUALES_PENDIENTES.md) | Datos y decisiones que sólo puede aportar el usuario |

## Estado

**NOT-READY** para producción real. El código está listo y probado; faltan datos y decisiones
del usuario —datos jurídicos, revisión legal, cuenta de Cloudflare R2 y recursos productivos
separados— detallados en [15_DATOS_MANUALES_PENDIENTES.md](15_DATOS_MANUALES_PENDIENTES.md).
