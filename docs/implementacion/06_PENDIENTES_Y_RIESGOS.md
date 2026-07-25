# Pendientes y riesgos

No hay bloqueos técnicos conocidos para ejecutar la vertical en Development.

## Antes de usar datos reales

1. Revisión jurídica del aviso, política, retención y atención de derechos. El texto actual es demostrativo.
2. Configurar almacén persistente y protegido para llaves de Data Protection y rotación de la clave HMAC.
3. Sustituir credenciales, cadena PostgreSQL y secretos de Development por un gestor de secretos.
4. Configurar correo real para recuperación de cuenta o deshabilitar esas pantallas hasta tener proveedor.
5. Definir respaldo, restauración, monitoreo, alertas, dominio, TLS y despliegue productivo.
6. Ejecutar prueba de carga y ajustar rate limits con tráfico representativo.
7. Diseñar una interfaz privada para administrar servicios, personal, horarios y excepciones; la V1 expone esas operaciones por API y usa datos demo sembrados.

## Riesgos residuales

- La pérdida de llaves de Data Protection impediría leer datos protegidos existentes.
- La rotación de HMAC requiere estrategia de versiones antes de producción.
- El teléfono se guarda protegido y sus últimos cuatro dígitos quedan en claro por necesidad de seguimiento; debe validarse en la revisión de privacidad.
- La disponibilidad depende de la calidad de la configuración de horario, personal y excepciones.
- Las pruebas E2E requieren Chromium de Playwright y Docker operativos.
