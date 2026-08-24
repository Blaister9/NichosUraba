/* Tema claro / oscuro.

   Tres estados y no dos: "sistema" es el valor por omisión y no escribe nada, así que el equipo
   sigue mandando; "claro" y "oscuro" escriben el atributo y mandan sobre el sistema en los dos
   sentidos. Guardar sólo un booleano habría hecho imposible volver a "lo que diga el equipo".

   Este archivo se ejecuta en el <head> y de forma síncrona a propósito: si el atributo se pusiera
   después de la primera pintura, quien navega en oscuro vería un fogonazo blanco en cada carga. */
(function () {
    var CLAVE = "urabaTema";
    var raiz = document.documentElement;

    function guardado() {
        try { return localStorage.getItem(CLAVE); } catch (e) { return null; }
    }

    function esOscuro(preferencia) {
        if (preferencia === "dark") return true;
        if (preferencia === "light") return false;
        return window.matchMedia && window.matchMedia("(prefers-color-scheme: dark)").matches;
    }

    /* La barra del navegador en móvil se pinta con este color. Sin actualizarla, el teléfono
       dibujaba una franja verde de una marca anterior encima de una aplicación oscura. */
    function pintarBarra(oscuro) {
        var meta = document.querySelector('meta[name="theme-color"]');
        if (meta) meta.setAttribute("content", oscuro ? "#0C1613" : "#FFFDF9");
    }

    function aplicar(preferencia) {
        if (preferencia === "dark" || preferencia === "light") raiz.setAttribute("data-theme", preferencia);
        else raiz.removeAttribute("data-theme");
        pintarBarra(esOscuro(preferencia));
    }

    aplicar(guardado());

    /* Con la preferencia en "sistema", cambiar el tema del equipo tiene que verse sin recargar. */
    if (window.matchMedia) {
        var consulta = window.matchMedia("(prefers-color-scheme: dark)");
        var alCambiar = function () { if (!guardado()) aplicar(null); };
        if (consulta.addEventListener) consulta.addEventListener("change", alCambiar);
        else if (consulta.addListener) consulta.addListener(alCambiar);
    }

    window.urabaTema = {
        leer: function () { return guardado() || "system"; },
        /* Devuelve lo que quedó aplicado, para que el componente no tenga que suponerlo. */
        fijar: function (preferencia) {
            try {
                if (preferencia === "system") localStorage.removeItem(CLAVE);
                else localStorage.setItem(CLAVE, preferencia);
            } catch (e) { /* modo privado: el tema vale para esta pantalla y no se recuerda */ }
            aplicar(preferencia === "system" ? null : preferencia);
            return preferencia;
        }
    };
})();
