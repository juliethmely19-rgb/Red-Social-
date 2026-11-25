FROM nginx:1.28-alpine

# Página principal (sustituye el index.html predeterminado de Nginx)
COPY index.html /usr/share/nginx/html/index.html

# Callback de OpenIddict
COPY callback.html /usr/share/nginx/html/callback.html

# Archivos JavaScript (config.js se regenera al arrancar con API_URL)
COPY js/ /usr/share/nginx/html/js/

# Hoja de estilos de la página principal
COPY index.css /usr/share/nginx/html/index.css

# Página de Login y sus recursos
COPY login/ /usr/share/nginx/html/login/

# Página de Bienvenida y sus recursos
COPY Bienvenida/ /usr/share/nginx/html/Bienvenida/

# Script que escribe js/config.js desde la variable API_URL (nginx ejecuta
# /docker-entrypoint.d/*.sh antes de arrancar). El sed quita finales de línea CRLF.
COPY docker-entrypoint.d/40-config-js.sh /docker-entrypoint.d/40-config-js.sh
RUN sed -i 's/\r$//' /docker-entrypoint.d/40-config-js.sh \
    && chmod +x /docker-entrypoint.d/40-config-js.sh

ENV API_URL=http://localhost:5290

EXPOSE 80

CMD ["nginx", "-g", "daemon off;"]
