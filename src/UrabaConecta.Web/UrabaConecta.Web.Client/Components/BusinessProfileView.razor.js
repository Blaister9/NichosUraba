// Compartir la ficha. Es la razón por la que la ficha existe: que el negocio la mande por WhatsApp
// o la ponga en su Instagram.
//
// En móvil abre la hoja nativa, que es donde ya están WhatsApp y las demás. Donde no existe —casi
// todo escritorio— se copia la dirección al portapapeles, porque un botón que no hace nada es peor
// que no tenerlo. El valor de retorno le dice a la interfaz qué mensaje mostrar.
export async function compartir(titulo, texto, url) {
    const datos = { title: titulo, text: texto, url };
    if (navigator.share) {
        try {
            await navigator.share(datos);
            return "compartido";
        } catch (error) {
            // Cerrar la hoja sin elegir nada llega como AbortError. No es un fallo: es una decisión,
            // y anunciarla como error dejaría un mensaje rojo tras un gesto deliberado.
            if (error && error.name === "AbortError") return "cancelado";
        }
    }
    try {
        await navigator.clipboard.writeText(url);
        return "copiado";
    } catch {
        return "sin-soporte";
    }
}
