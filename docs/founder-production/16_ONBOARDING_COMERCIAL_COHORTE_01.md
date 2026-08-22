# 16 — Onboarding comercial, cohorte 01

Registro de los dos primeros negocios reales incorporados al piloto. Se hizo el 22 de agosto de
2026 sobre el ambiente **Demo** (`nichosuraba-production.up.railway.app`), que es donde vive el
piloto mientras Production no exista. No se creó ningún recurso nuevo ni se tocó DNS.

Este documento no lleva contraseñas. Las credenciales de la propietaria se entregan por canal
privado, igual que las cuentas Demo (ver `03_INVITACIONES_Y_ACCESOS.md`).

## Qué quedó creado

| Negocio | Categoría | Municipio | Capacidades | Estado |
|---|---|---|---|---|
| Delicadas | Spa y belleza | Carepa | Pedidos + Productos | Publicado |
| Victoria | Veterinarias | Carepa | Pedidos + Productos | Borrador |

Citas, fila virtual, servicios y personal quedan **apagados a mano** en los dos. No se derivaron:
se guardaron como decisión explícita, que es lo que permite encenderlos después sin rehacer nada.

### Delicadas

Marca de cuidado facial que hoy vende en línea. Su dirección pública dice eso mismo —no tiene
local a la calle— y la entrega se coordina por WhatsApp.

Catálogo: 12 productos en 5 categorías (Sérums y tratamientos, Limpieza, Protección solar,
Cremas, Combos y rutinas), todos con nombre, descripción breve, precio e imagen.

El contenido —nombres, precios y descripciones— sale del portafolio que entregó la propietaria.
Las imágenes de producto son las fotografías de ese mismo portafolio: para los productos sueltos
se extrajo el bitmap del packshot; para los combos, que no tienen foto propia, se recortó la banda
de las tres fotos de la página, **sin el precio**, porque el precio vive en el catálogo y una
imagen con el precio impreso miente en cuanto cambie.

### Victoria

Tienda veterinaria y pet shop. Queda en borrador a propósito: de las fotos del local se leen la
marca y el surtido, pero no un precio ni una presentación exacta, y el catálogo es lo único que no
se puede aproximar sin inventar. Tampoco hay teléfono, dirección ni cuenta de propietaria.

Lo que sí quedó: categoría, municipio, capacidades, descripción pública, portada tomada del mural
del local, logo derivado del mismo mural y dos fotografías de galería.

Para publicarla faltan seis casillas del checklist: contacto, ubicación, categoría del menú,
producto activo, propietario y permisos del propietario.

## Recorrido de la propietaria

Comprobado pantalla por pantalla el 22 de agosto de 2026. La guía general está en
`07_GUIA_PROPIETARIO.md`; esto es el recorrido corto para un negocio de pedidos.

1. **Entrar** — `/Account/Login`, con el correo y la contraseña entregados. Cae en `/panel`.
2. **Ver pedidos** — `/panel/{negocio}/pedidos`, con las bandejas Por atender, Nuevos y Preparando.
3. **Cambiar estados** — desde esa misma pantalla: aceptar o rechazar, marcar en preparación,
   listo para recoger y entregado. Cada cambio le avisa a quien pidió.
4. **Revisar avisos** — `/panel/{negocio}/avisos`. Ahí queda escrito todo lo que pasa aunque el
   aviso del teléfono no llegue; los avisos por dispositivo se activan desde *Mis negocios*.
5. **Editar datos básicos** — `/panel/{negocio}/configuracion/perfil`: descripción, contacto,
   redes e instrucciones. Nombre, dirección web, municipio y categoría los cambia la
   administración, no el propietario.
6. **Cambiar contraseña** — `/Account/Manage/ChangePassword`.

El catálogo y las franjas se administran en `/panel/{negocio}/configuracion/pedidos`.

## Dos cosas que el alta destapó, ya corregidas

**Un negocio de sólo pedidos nacía sin horario.** El alta creaba el horario únicamente cuando el
negocio abría citas, pero las franjas para recoger se calculan cruzando el horario del negocio con
la ventana de pedidos: Delicadas quedó publicada al 100 % del checklist ofreciendo cero franjas.
Ahora el alta crea el horario también con pedidos, y el checklist lo exige cuando hay citas **o**
pedidos. La fila sigue sin pedirlo: se atiende por orden de llegada. El horario de Delicadas —el
que se registró a mano el mismo día— no se tocó.

**El pie de página decía que los negocios eran ficticios.** Ahora dice «UrabáConecta · Piloto
controlado», y la marca que enciende esa línea dejó de llevar el nombre del ambiente en el HTML
público: era `data-ambiente="Demo"` en el `body` de cualquier ficha y ahora es una bandera sin
nombre que en la operación real ni se escribe.

## Lo que no se hizo, y por qué

- **No se creó Production.** Sigue sin existir; nada de lo de aquí la toca.
- **No se envió un pedido de prueba.** Un pedido ficticio entra a la bandeja y a los avisos de la
  propietaria, y lo primero que ella vea al entrar no debería ser basura de prueba. En su lugar se
  comprobó que las franjas se calculan (20 para el siguiente día hábil) y que la pantalla de
  pedido carga el catálogo completo.
- **No se tocaron los negocios que ya estaban.** Studio Laura usuga y los dos de muestra quedan
  como estaban.
