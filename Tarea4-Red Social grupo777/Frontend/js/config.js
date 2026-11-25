// Configuración del frontend (dirección de la API).
//  - Live Server / abrir el HTML directamente: usa este valor por defecto.
//  - Contenedor Docker del frontend: se regenera al arrancar con la variable API_URL
//    (docker-entrypoint.d/40-config-js.sh).
//  - Páginas servidas por el backend (:5290): Program.cs responde /js/config.js
//    con el origen real desde el que se cargó la página.
window.APP_CONFIG = window.APP_CONFIG || { API_URL: "http://localhost:5290" };
