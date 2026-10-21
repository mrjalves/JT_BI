#!/usr/bin/env bash
# ==============================================================================
# Script de Configuração Inicial do JT_BI na VPS Linux (Ubuntu / Debian)
# Execute na VPS como root ou com sudo:
#   chmod +x setup-vps.sh
#   sudo ./setup-vps.sh <DEPLOY_USER>
# Exemplo:
#   sudo ./setup-vps.sh ubuntu
#   ou: sudo ./setup-vps.sh deploy
# ==============================================================================

set -euo pipefail

DEPLOY_USER="${1:-$USER}"
APP_DIR="/var/www/jt_sistemas_bi"
SERVICE_NAME="jt_sistemas_bi"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

echo "=== [1/5] Configurando diretórios da aplicação em $APP_DIR ==="
mkdir -p "$APP_DIR"
mkdir -p "$APP_DIR/uploads"
mkdir -p "$APP_DIR/logs"

# O usuário do deploy precisa conseguir gravar via rsync; www-data precisa ler/executar
chown -R "$DEPLOY_USER":www-data "$APP_DIR"
chmod -R 775 "$APP_DIR"

echo "=== [2/5] Criando appsettings.Production.json se não existir ==="
if [ ! -f "$APP_DIR/appsettings.Production.json" ]; then
    echo "{}" > "$APP_DIR/appsettings.Production.json"
    chown "$DEPLOY_USER":www-data "$APP_DIR/appsettings.Production.json"
    chmod 660 "$APP_DIR/appsettings.Production.json"
    echo "Arquivo criado: $APP_DIR/appsettings.Production.json"
else
    echo "Arquivo $APP_DIR/appsettings.Production.json já existente. Preservado."
fi

echo "=== [3/5] Instalando serviço systemd ($SERVICE_NAME) ==="
SERVICE_SRC="$SCRIPT_DIR/jt_bi.service"
if [ ! -f "$SERVICE_SRC" ] && [ -f "$SCRIPT_DIR/jt_sistemas_bi.service" ]; then
    SERVICE_SRC="$SCRIPT_DIR/jt_sistemas_bi.service"
fi
if [ -f "$SERVICE_SRC" ]; then
    cp "$SERVICE_SRC" "/etc/systemd/system/${SERVICE_NAME}.service"
    chmod 644 "/etc/systemd/system/${SERVICE_NAME}.service"
    systemctl daemon-reload
    systemctl enable "${SERVICE_NAME}.service"
    echo "Serviço ${SERVICE_NAME} habilitado para inicialização automática."
fi

echo "=== [4/5] Configurando permissão sudo sem senha para o GitHub Actions ==="
# Permite que o usuário de deploy reinicie o serviço via SSH sem pedir senha interativa
SUDOERS_FILE="/etc/sudoers.d/${SERVICE_NAME}"
cat <<EOF > "$SUDOERS_FILE"
# Permissões para deploy automatizado do JT_BI via GitHub Actions
$DEPLOY_USER ALL=(ALL) NOPASSWD: /usr/bin/systemctl restart $SERVICE_NAME
$DEPLOY_USER ALL=(ALL) NOPASSWD: /usr/bin/systemctl is-active $SERVICE_NAME
$DEPLOY_USER ALL=(ALL) NOPASSWD: /usr/bin/systemctl status $SERVICE_NAME
EOF
chmod 440 "$SUDOERS_FILE"
visudo -cf "$SUDOERS_FILE"
echo "Regra sudoers criada com sucesso em $SUDOERS_FILE."

echo "=== [5/5] Configuração do Nginx (opcional) ==="
if [ -d "/etc/nginx" ] && [ -f "$SCRIPT_DIR/nginx-jt_bi.conf" ]; then
    mkdir -p /etc/nginx/sites-available /etc/nginx/sites-enabled
    cp "$SCRIPT_DIR/nginx-jt_bi.conf" "/etc/nginx/sites-available/jt_bi"
    if [ ! -e "/etc/nginx/sites-enabled/jt_bi" ]; then
        ln -s "/etc/nginx/sites-available/jt_bi" "/etc/nginx/sites-enabled/"
        echo "Link simbólico criado em /etc/nginx/sites-enabled/jt_bi"
    fi
    nginx -t && systemctl reload nginx || echo "Verifique as configurações do Nginx manualmente."
fi

echo "=== Configuração da VPS para o JT_BI concluída com sucesso! ==="
