
const API_URL = (window.APP_CONFIG && window.APP_CONFIG.API_URL) || "http://localhost:5290";
const alerta = document.getElementById("alerta");
const opcionesLogin = document.getElementById("opcionesLogin");
const perfil = document.getElementById("perfil");

// Conservamos la solicitud original de OpenIddict.
const parametros = new URLSearchParams(window.location.search);
const returnUrl = parametros.get("returnUrl");

// Construye las direcciones de los proveedores.
function crearUrlLogin(endpoint) {
    const url = new URL(endpoint, API_URL);

    if (returnUrl) {
        url.searchParams.set("returnUrl", returnUrl);
    }

    return url.toString();
}

function mostrarError(mensaje) {
    if (!alerta) return;

    alerta.textContent = mensaje;
    alerta.hidden = false;
}

function ponerCargando(boton, texto) {
    if (!boton) return;

    boton.disabled = true;

    const icono = boton.querySelector(".btn-icon");

    if (icono) {
        icono.innerHTML = '<span class="spinner"></span>';
    }

    const etiqueta = boton.querySelector(".btn-text");

    if (etiqueta) {
        etiqueta.textContent = texto;
    }
}

// Redirige al proveedor de autenticación.
function iniciarLogin(boton, endpoint, proveedor) {
    ponerCargando(boton, `Conectando con ${proveedor}...`);
    window.location.href = crearUrlLogin(endpoint);
}

// Muestra la sesión ya iniciada cuando la página la sirve el backend.
async function cargarPerfil() {
    try {
        const respuesta = await fetch(`${API_URL}/api/Auth/me`, {
            credentials: "include"
        });

        if (!respuesta.ok) return;

        const datos = await respuesta.json();

        if (!datos.autenticado) return;

        if (opcionesLogin) opcionesLogin.hidden = true;
        if (perfil) perfil.hidden = false;

        const nombre = datos.nombre || "Usuario";
        const email = datos.email || "Sin correo";
        const proveedor = datos.proveedor || "Sesión";

        const elementoNombre = document.getElementById("perfilNombre");
        const elementoEmail = document.getElementById("perfilEmail");
        const elementoProveedor = document.getElementById("perfilProveedor");
        const elementoAvatar = document.getElementById("perfilAvatar");

        if (elementoNombre) elementoNombre.textContent = nombre;
        if (elementoEmail) elementoEmail.textContent = email;
        if (elementoProveedor) elementoProveedor.textContent = proveedor;
        if (elementoAvatar) elementoAvatar.textContent = nombre.charAt(0).toUpperCase();
    } catch {
        // Si la API no responde, se dejan visibles los botones de login.
    }
}

document.addEventListener("DOMContentLoaded", () => {
    // Si la página se abrió sin una solicitud de OpenIddict (sin returnUrl) no habrá
    // access token al terminar. Si estamos en el frontend (Live Server / Docker),
    // volvemos a index.html, que inicia el flujo correcto.
    if (!returnUrl && window.location.origin !== new URL(API_URL).origin) {
        window.location.replace(new URL("../index.html", window.location.href).href);
        return;
    }

    if (!returnUrl) {
        mostrarError(
            "Abre index.html (Live Server) y pulsa «Iniciar sesión» para obtener un access token."
        );
    }

    const error = parametros.get("error");

    if (error) {
        mostrarError(error);
        window.history.replaceState({}, "", window.location.pathname);
    }

    const dropbox = document.getElementById("dropboxLogin");
    const figma = document.getElementById("figmaLogin");
    const twitter = document.getElementById("twitterLogin");
    const tiktok = document.getElementById("tiktokLogin");
    const discord = document.getElementById("discordLogin");
    const slack = document.getElementById("slackLogin");

    const google = document.getElementById("googleLogin");
    const github = document.getElementById("githubLogin");

    // Configuramos los enlaces de los proveedores.
    if (dropbox) {
        dropbox.href = crearUrlLogin("/api/Auth/dropbox/login");

        dropbox.addEventListener("click", () => {
            ponerCargando(dropbox, "Conectando con Dropbox...");
        });
    }

    if (figma) {
        figma.href = crearUrlLogin("/api/Auth/figma/login");

        figma.addEventListener("click", () => {
            ponerCargando(figma, "Conectando con Figma...");
        });
    }

    if (twitter) {
        twitter.href = crearUrlLogin("/api/Auth/twitter/login");

        twitter.addEventListener("click", () => {
            ponerCargando(twitter, "Conectando con X...");
        });
    }

    if (tiktok) {
        tiktok.href = crearUrlLogin("/api/Auth/tiktok/login");

        tiktok.addEventListener("click", () => {
            ponerCargando(tiktok, "Conectando con TikTok...");
        });
    }

    // Discord y Slack se conservan desde la rama del grupo.
    if (discord) {
        discord.href = crearUrlLogin("/api/Auth/discord/login");

        discord.addEventListener("click", () => {
            ponerCargando(discord, "Conectando con Discord...");
        });
    }

    if (slack) {
        slack.href = crearUrlLogin("/api/Auth/slack/login");

        slack.addEventListener("click", () => {
            ponerCargando(slack, "Conectando con Slack...");
        });
    }

    // Google.
    google?.addEventListener("click", (evento) => {
        evento.preventDefault();

        iniciarLogin(
            google,
            "/api/Auth/GoogleLogin",
            "Google"
        );
    });

    // GitHub.
    github?.addEventListener("click", (evento) => {
        evento.preventDefault();

        iniciarLogin(
            github,
            "/api/Auth/GithubLogin",
            "GitHub"
        );
    });

    document.getElementById("continuar")?.addEventListener("click", () => {
        window.location.href = new URL("../Bienvenida/bienvenida.html", window.location.href).href;
    });

    // Cerrar sesión local.
    document.getElementById("logout")?.addEventListener("click", async (evento) => {
        const boton = evento.currentTarget;

        ponerCargando(boton, "Cerrando sesión...");

        try {
            await fetch(`${API_URL}/api/Auth/logout`, {
                method: "POST",
                credentials: "include"
            });
        } catch (error) {
            console.error("Error al cerrar sesión:", error);
        } finally {
            window.location.href = `${API_URL}/login.html`;
        }
    });

    cargarPerfil();
});
