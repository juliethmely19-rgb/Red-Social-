#!/bin/sh
# Genera js/config.js con la URL pública del backend (variable API_URL).
set -e

API_URL="${API_URL:-http://localhost:5290}"

cat > /usr/share/nginx/html/js/config.js <<JS
window.APP_CONFIG = { API_URL: "${API_URL}" };
JS

echo "config.js generado: API_URL=${API_URL}"
