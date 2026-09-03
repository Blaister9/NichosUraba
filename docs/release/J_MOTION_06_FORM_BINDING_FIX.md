# J-MOTION-06 — corrección del formulario

HEAD heredado: `946735ff46e833466c0f4f1b0d39c301a45e86c4`.
Alcance: binding/validación del nombre, celular y nota del pedido. Motion, geometría,
API, contratos, dominio y reglas de publicación conservan su implementación.

## Causa y evidencia

Se reprodujo en la copia local equivalente de Lúmina, usando el mismo navegador
que había fallado en DEV. Antes de enviar se veía `Human J06` / `3000000006`,
pero `request.CustomerAlias` y `request.Phone` seguían vacíos. También se perdía
la nota. `InputText.Value` estaba vacío; `EditContext.IsModified` era false para
ambos campos. La identidad del modelo y `EditContext.Model` coincidía (14051783)
y permaneció estable al cambiar cantidad/recomponer: no había instancia vieja,
clonación del formulario ni sustitución del request.

Al validar aparecieron los dos Required; `Submit` no llegó a ejecutar la creación.
La traza temporal se guardó localmente y se retiró del código antes del commit.
No se registran datos de formularios en la aplicación final.

El punto de divergencia era el evento: los `InputText`/`InputTextArea` originales
actualizaban `CurrentValueAsString` con `change`. La entrada que sólo notificaba
`input` cambiaba el DOM, pero no el modelo ni EditContext. El E2E específico
reproduce esa secuencia con un evento `input` de reemplazo; no modifica clases ni
simula una creación de pedido. La entrada normal por teclado y FillAsync podía
actualizar campos al hacer blur, pero dejaba el último campo pendiente mientras
seguía enfocado. No se atribuye el fallo a una normalización del teléfono.

Preexistente: **sí**, por comparación con `d7aa2f8`: mismos componentes y targets
`request.CustomerAlias`, `request.Phone`, `request.Notes` en el mismo EditForm.
J-MOTION-06 no introdujo ese contrato de eventos. No se afirma haber redeployado
la versión anterior para comprobarlo.

## Corrección mínima

`PickupOrderTextInput` hereda de InputText y utiliza `CurrentValueAsString` con
`oninput`, conservando el mecanismo de InputBase: ValueChanged, FieldIdentifier,
NotifyFieldChanged, clases de validación, nombre del control y atributos ARIA.
Conserva además `change` como fallback para clientes que sólo emiten ese evento.
La igualdad de valor de InputBase evita notificaciones duplicadas cuando llegan
ambos. El modo Multiline aplica el mismo arreglo a la nota, que mostraba la misma
desincronización. Se sustituyen sólo los tres controles del formulario existente.

No se relajan Required, longitud, formato de celular ni consentimiento; tampoco
se añaden defaults, snapshots de submit, lectura manual del DOM para forzar el
envío, observers, cambios por frame o endpoints de diagnóstico.

## Pruebas

El nuevo E2E cubre teclado real de Chromium, FillAsync, reemplazo que emite sólo
input y reemplazo que emite sólo change. Comprueba estado modified de EditContext,
valores visibles, ausencia de mensajes tras corregirlos, persistencia durante
cantidad 2→3→2, cerrar selección, A→B→A y volver al resumen. El caso input-only
verifica primero Required y formato inválido, antes de completar los valores.

Cada caso crea por UI un pedido real local y verifica producto, cantidad 2, total,
estado Pending, código y confirmación. Lee la fila en PostgreSQL y usa la API
operativa existente para verificar nombre, celular y nota descifrados, iguales a
lo escrito. El GET público del código devuelve HTTP 200.

La pantalla es InteractiveServer: el submit viaja por el circuito Blazor y
ServerUrabaConectaApi invoca el caso de uso en el servidor. No hay un POST HTTP
/orders emitido por el navegador; no se inventa ese payload como evidencia.
La creación persistida y las lecturas API verifican la frontera real del flujo.

La sustitución programática probada emite los eventos de entrada del navegador;
no se afirma haber probado un perfil real con datos personales de autofill guardados.

Evidencia local: `artifacts/j-motion-06/form-fix/`, TRX en `artifacts/test-results/`.
La entrega final local registra resultados, commit/deployment y el único pedido
DEV. El contacto reservado autorizado ya estaba correcto y no se vuelve a modificar.

## Revisión humana

Abrir Lúmina en DEV, agregar dos Labiales, ir al resumen y escribir nombre/celular.
Volver al catálogo, cambiar cantidad y selección, regresar: los campos deben
conservar exactamente su valor. El pedido DEV registrado en la entrega permite
revisar confirmación, producto, cantidad, total y código sin crear otro pedido.
No se implementa tracking timeline ni se inicia J-MOTION-07.

## Cierre

Compilación Release y Debug: 0 errores, 0 advertencias.
Formulario: 4/4 con base nueva (teclado, FillAsync, sólo `input`, sólo `change`).
En los cuatro casos el valor visible, el valor enlazado, el estado `modified` del
EditContext, la fila persistida y la lectura por la API operativa coinciden.
Ordering API: 10/10. Dominio: 224/224.
Home y coreografía de escenas: sin cambios, en verde.
Cola: verde al correr cada prueba con su propia base. Las seis fallas al meter toda
la clase en una sola base son colisión de datos entre escenarios —tickets «Delta 1»
repetidos y conteos acumulados—, no del código.

Hallazgo preexistente, fuera del alcance de esta corrección: el medidor de contraste
de `.pickup-journey` marca `push-prompt-icon` con 4,28:1 frente al mínimo de 4,5.
Es el par de tokens `--uc-green` #0F8A62 sobre `--uc-paper` #FFFDF9 en
`PushPrompt.razor`, componente que esta tarea no toca. Se reprodujo el mismo fallo
compilando y ejecutando el commit heredado `946735f` sin esta corrección, así que no
lo introduce el arreglo del formulario. No se cambia el color: sería rediseño y
corresponde decidirlo aparte.
