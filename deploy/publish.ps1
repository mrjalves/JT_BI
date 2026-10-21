<#
.SYNOPSIS
    Script de publicação local do JT_BI para homologação ou deploy manual.
.DESCRIPTION
    Compila o JT_BI em modo Release e gera a pasta ./publish com todos os binários e assets estáticos prontos.
#>

param(
    [string]$Configuration = "Release",
    [string]$OutputDir = "./publish"
)

$ErrorActionPreference = "Stop"

Write-Host "==========================================" -ForegroundColor Cyan
Write-Host "Iniciando compilação e publicação do JT_BI" -ForegroundColor Cyan
Write-Host "Configuração: $Configuration" -ForegroundColor Gray
Write-Host "Destino: $OutputDir" -ForegroundColor Gray
Write-Host "==========================================" -ForegroundColor Cyan

# 1. Limpar saída anterior
if (Test-Path $OutputDir) {
    Write-Host "Limpando diretório de publicação anterior..." -ForegroundColor Yellow
    Remove-Item -Path $OutputDir -Recurse -Force
}

# 2. Executar dotnet publish
Write-Host "Executando dotnet publish..." -ForegroundColor Green
dotnet publish JT_BI.csproj -c $Configuration -o $OutputDir

if ($LASTEXITCODE -ne 0) {
    Write-Error "Falha na publicação do JT_BI."
    exit 1
}

Write-Host "Publicação concluída com sucesso em: $OutputDir" -ForegroundColor Green
Write-Host "Total de arquivos gerados: $((Get-ChildItem -Path $OutputDir -Recurse -File).Count)" -ForegroundColor Gray
