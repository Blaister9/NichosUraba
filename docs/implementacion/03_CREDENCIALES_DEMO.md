# Credenciales ficticias de Development

Estas cuentas se crean únicamente cuando `ASPNETCORE_ENVIRONMENT=Development`.

| Uso | Correo | Contraseña | Negocio |
|---|---|---|---|
| Propietaria | `propietaria@bella.demo` | Secreto externo | Salón Bella Urabá |
| Propietario de aislamiento | `propietario@otro.demo` | Secreto externo | Negocio Aislado Demo |
| Trabajadora | `trabajadora@bella.demo` | Secreto externo | Salón Bella Urabá |
| Propietario de barbería | `propietario@corte.demo` | Secreto externo | Barbería El Corte |
| Operador de turnos | `turnos@corte.demo` | Secreto externo | Barbería El Corte |
| Trabajador sin permiso de turnos | `sinasignacion@corte.demo` | Secreto externo | Barbería El Corte |

Son datos ficticios. En Railway/Demo las contraseñas se leen de `DemoSeed__AdminPassword` y
`DemoSeed__BusinessPassword`; deben ser distintas, generadas aleatoriamente y rotadas por canal privado.
No existe registro público. El valor fijo usado por pruebas locales no es una credencial del despliegue.
