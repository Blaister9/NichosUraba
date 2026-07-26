# Riesgos del piloto

| Riesgo | Control actual | Pendiente operativo |
|---|---|---|
| Entrega insegura de clave temporal | clave aleatoria, una sola visualización, cambio obligatorio | acordar canal y responsable |
| Activación incompleta | lista calculada en servidor y bloqueo de activación | revisión humana antes de publicar |
| Escrituras simultáneas | versión y bloqueo de fila; respuesta `409` | capacitar para recargar |
| Cruce entre negocios | autorización por membresía y `BusinessId` | revisar roles antes de cada piloto |
| Suspensión tardía | despublicación atómica y bloqueo de altas | definir contacto de emergencia |
| Borrado accidental | solo borradores sin operaciones | preferir archivo |
| Configuración inicial genérica | valores conservadores, visibles en checklist | propietario debe ajustar antes de operar |
| Creación de cuentas en producción | deshabilitada fuera de Development/Demo | diseñar invitación y recuperación |

No incluye facturación, pagos, notificaciones, analítica avanzada, importación masiva ni integraciones externas.
