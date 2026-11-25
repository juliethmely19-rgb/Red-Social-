const API_URL = (window.APP_CONFIG && window.APP_CONFIG.API_URL) || "http://localhost:5290";

const nombre = document.getElementById("nombre");
const email = document.getElementById("email");
const biografia = document.getElementById("biografia");
const fecha = document.getElementById("fecha");
const mensaje = document.getElementById("mensaje");

let usuarioOriginal = {
    user: "",
    email: "",
    biografia: "",
    fecha: "",
    estado: "Activo"
};

function mostrarUsuario(datos) {
    nombre.value = datos.user || "";
    email.value = datos.email || "";
    biografia.value = datos.biografia || "";

    fecha.value = datos.fecha
        ? new Date(datos.fecha).toLocaleString("es-BO")
        : "—";

    document.getElementById("nombreTitulo").textContent =
        datos.user || "Mi perfil";

    document.getElementById("avatar").textContent =
        (datos.user || "U").charAt(0).toUpperCase();

    document.getElementById("estado").textContent =
        datos.estado || "Activo";
}


async function cargarPerfil() {
    try {
        const token = sessionStorage.getItem("access_token");

        if (!token) {
            console.error("No existe un access_token.");
            mensaje.textContent = "No se encontró una sesión válida.";
            return;
        }

        const respuesta = await fetch(`${API_URL}/api/Auth/me`, {
            headers: {
                "Authorization": `Bearer ${token}`
            }
        });

        if (respuesta.status === 401 || respuesta.status === 403) {
            mensaje.textContent = "Tu sesión no es válida o no tienes permisos.";
            return;
        }

        if (!respuesta.ok) {
            throw new Error(`Error HTTP: ${respuesta.status}`);
        }

        const sesion = await respuesta.json();

        mostrarToken(token, sesion.proveedor);

        let datos = {
            user: sesion.nombre || sesion.name || "Usuario",
            email: sesion.email || "",
            biografia: "",
            fecha: "",
            estado: "Activo"
        };

        try {
            const perfil = await fetch(
                `${API_URL}/api/Usuario/Buscar/${encodeURIComponent(datos.user)}`,
                {
                    headers: {
                        "Authorization": `Bearer ${token}`
                    }
                }
            );

            if (perfil.ok) {
                const usuario = await perfil.json();

                datos = {
                    user: usuario.user ?? datos.user,
                    email: usuario.email ?? datos.email,
                    biografia: usuario.biografia ?? datos.biografia,
                    fecha: usuario.fecha ?? datos.fecha,
                    estado: usuario.estado ?? datos.estado
                };
            }
        } catch (error) {
            console.error("No se pudo consultar el perfil:", error);
        }

        usuarioOriginal = { ...datos };
        mostrarUsuario(datos);

    } catch (error) {
        console.error("No se pudo cargar el perfil:", error);
        mensaje.textContent = "No se pudo cargar la información del perfil.";
    }
}

document.addEventListener("DOMContentLoaded", () => {
    cargarPerfil();

    document.getElementById("formPerfil")?.addEventListener("submit", (event) => {
        event.preventDefault();
        mensaje.textContent =
            "Vista previa: los cambios todavía no se guardaron en el servidor.";
    });

    document.getElementById("restablecer")?.addEventListener("click", () => {
        mostrarUsuario(usuarioOriginal);
        mensaje.textContent = "Se restauraron los datos originales.";
    });
});


// Muestra el access token para pegarlo en Scalar (botón Authorize / Auth → Bearer).
function mostrarToken(token, proveedor) {
    const caja = document.getElementById("tokenCaja");
    const campo = document.getElementById("tokenValor");
    const via = document.getElementById("tokenProveedor");

    if (!caja || !campo) return;

    campo.value = token;
    if (via) via.textContent = proveedor || "OpenIddict";
    caja.hidden = false;

    document.getElementById("copiarToken")?.addEventListener("click", async () => {
        try {
            await navigator.clipboard.writeText(token);
        } catch {
            campo.select();
            document.execCommand("copy");
        }
        mensaje.textContent = "Access token copiado. Pégalo en Scalar como Bearer.";
    });
}

async function cerrarVista() {
    const token = sessionStorage.getItem("access_token");

    try {
        await fetch(`${API_URL}/api/Auth/logout`, {
            method: "POST",
            // credentials: "include" para que el backend borre su cookie de sesión.
            credentials: "include",
            headers: token
                ? { "Authorization": `Bearer ${token}` }
                : {}
        });
    } catch (error) {
        console.error("Error al cerrar sesión:", error);
    } finally {
        sessionStorage.removeItem("access_token");
        sessionStorage.removeItem("id_token");
        sessionStorage.removeItem("oidc_state");
        sessionStorage.removeItem("oidc_code_verifier");

        window.location.href = `${API_URL}/login.html`;
    }
}
