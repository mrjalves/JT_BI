#!/usr/bin/env bash
set -e

# ==============================================================================
# Script de publicação do JT_BI (.NET 10)
# ==============================================================================

CONFIGURATION="Release"
OUTPUT_DIR="./publish"

echo "=========================================="
echo "Iniciando compilação e publicação do JT_BI"
echo "Configuração: $CONFIGURATION"
echo "Destino: $OUTPUT_DIR"
echo "=========================================="

# Limpar build anterior
if [ -d "$OUTPUT_DIR" ]; then
    echo "Limpando pasta de publicação antiga..."
    rm -rf "$OUTPUT_DIR"
fi

# Publicar
dotnet publish JT_BI.csproj -c "$CONFIGURATION" -o "$OUTPUT_DIR"

echo "=========================================="
echo "Publicação finalizada com sucesso!"
echo "Arquivos disponíveis em $OUTPUT_DIR"
echo "=========================================="
