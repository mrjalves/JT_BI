# Guia Completo de Deploy e Governança Git - JT_BI

Este documento descreve a governança de arquivos Git (`.gitignore`) e a arquitetura e procedimentos de implantação (deploy) do **JT_BI** (.NET 10 Blazor Server).

---

## 1. Governança e Regras do `.gitignore`

O arquivo [`.gitignore`](file:///c:/Users/sirja/source/repos/mrjalves/JT_BI/.gitignore) foi configurado com padrões corporativos para projetos .NET / ASP.NET Core e Blazor.

### Categorias Ignoradas:
- **Artefatos de Compilação:** Pastas `bin/`, `obj/`, `out/`, `publish/`, `bld/` e binários gerados.
- **Arquivos Pessoais de IDE:** `.vs/`, `.vscode/*` (mantendo configurações compartilháveis), `.idea/`, arquivos `*.user`, `*.suo`.
- **Configurações e Segredos de Produção:**
  - `appsettings.Production.json` e `appsettings.Staging.json` (mantendo no repositório apenas `appsettings.json` e `appsettings.Development.json` como referências limpas).
  - Certificados e chaves (`*.pfx`, `*.cer`, `*.key`, `*.pem`, `*.env`).
- **Arquivos Temporários e Logs:** Pastas `logs/`, arquivos `*.log`, bancos de dados de teste (`*.db`, `*.sqlite`).
- **Arquivos de Sistema Operacional:** `.DS_Store`, `Thumbs.db`, `desktop.ini`.

---

## 2. Estrutura dos Arquivos de Deploy

Os seguintes arquivos de infraestrutura e automação foram preparados no repositório:

| Arquivo | Descrição |
| :--- | :--- |
| [`.github/workflows/deploy.yml`](file:///c:/Users/sirja/source/repos/mrjalves/JT_BI/.github/workflows/deploy.yml) | Pipeline de CI/CD automatizado via GitHub Actions com envio SSH/SCP, backup e rollback automático. |
| [`deploy/jt_bi.service`](file:///c:/Users/sirja/source/repos/mrjalves/JT_BI/deploy/jt_bi.service) | Unit file do **Systemd** para gerenciamento do processo .NET como serviço de fundo no Linux. |
| [`deploy/nginx-jt_bi.conf`](file:///c:/Users/sirja/source/repos/mrjalves/JT_BI/deploy/nginx-jt_bi.conf) | Configuração de Proxy Reverso Nginx com suporte a WebSockets/SignalR (essencial para Blazor Server). |
| [`deploy/publish.ps1`](file:///c:/Users/sirja/source/repos/mrjalves/JT_BI/deploy/publish.ps1) | Script PowerShell para build e publicação rápida local (Release) no Windows. |
| [`deploy/publish.sh`](file:///c:/Users/sirja/source/repos/mrjalves/JT_BI/deploy/publish.sh) | Script Bash equivalente para Linux / macOS / WSL. |
| [`Dockerfile`](file:///c:/Users/sirja/source/repos/mrjalves/JT_BI/Dockerfile) | Arquivo Docker multi-estágio para containerização da aplicação em .NET 10. |
| [`.dockerignore`](file:///c:/Users/sirja/source/repos/mrjalves/JT_BI/.dockerignore) | Otimização do contexto de build do Docker. |
| [`docker-compose.yml`](file:///c:/Users/sirja/source/repos/mrjalves/JT_BI/docker-compose.yml) | Composição para subir o serviço isolado em container na porta 5010. |

---

## 3. Modelo Principal: Deploy na VPS Linux (Systemd + Nginx)

Este modelo segue o mesmo padrão de produção adotado pelo ecossistema **JT Sistemas**.

### 3.1. Pré-requisitos na VPS
1. **Instalar .NET 10 Runtime**:
   ```bash
   sudo apt-get update
   sudo apt-get install -y dotnet-runtime-10.0 aspnetcore-runtime-10.0
   ```
2. **Criar o diretório de destino e ajustar permissões**:
   ```bash
   sudo mkdir -p /var/www/jt_bi
   sudo chown -R www-data:www-data /var/www/jt_bi
   ```

### 3.2. Configurar o Serviço Systemd
1. Copie o arquivo [`deploy/jt_bi.service`](file:///c:/Users/sirja/source/repos/mrjalves/JT_BI/deploy/jt_bi.service) para `/etc/systemd/system/`:
   ```bash
   sudo cp deploy/jt_bi.service /etc/systemd/system/jt_bi.service
   ```
2. Recarregue os daemons e habilite o serviço:
   ```bash
   sudo systemctl daemon-reload
   sudo systemctl enable jt_bi
   sudo systemctl start jt_bi
   ```
3. Verifique o status:
   ```bash
   sudo systemctl status jt_bi
   ```

### 3.3. Configurar o Nginx com WebSockets
> [!IMPORTANT]
> O **Blazor Interactive Server** depende de SignalR e conexão WebSocket persistente. Se os cabeçalhos `Upgrade` e `Connection` não forem configurados no Nginx, a interface apresentará desconexões ou recarregamentos contínuos.

1. Copie o template [`deploy/nginx-jt_bi.conf`](file:///c:/Users/sirja/source/repos/mrjalves/JT_BI/deploy/nginx-jt_bi.conf) para `/etc/nginx/sites-available/jt_bi`:
   ```bash
   sudo cp deploy/nginx-jt_bi.conf /etc/nginx/sites-available/jt_bi
   sudo ln -s /etc/nginx/sites-available/jt_bi /etc/nginx/sites-enabled/
   ```
2. Teste e recarregue o Nginx:
   ```bash
   sudo nginx -t
   sudo systemctl reload nginx
   ```
3. (Opcional) Instale o Certificado SSL gratuito via Certbot:
   ```bash
   sudo certbot --nginx -d bi.jtsistemas.com.br
   ```

---

## 4. Pipeline de CI/CD (GitHub Actions)

O workflow [`.github/workflows/deploy.yml`](file:///c:/Users/sirja/source/repos/mrjalves/JT_BI/.github/workflows/deploy.yml) é disparado a cada `push` na branch `main` / `Master` ou via acionamento manual (`workflow_dispatch`).

### 4.1. Secrets necessárias no repositório GitHub
No GitHub, acesse **Settings > Secrets and variables > Actions** e cadastre:

| Secret | Descrição | Exemplo |
| :--- | :--- | :--- |
| `VPS_HOST` | Endereço IP ou hostname da VPS | `203.0.113.10` |
| `VPS_USER` | Usuário com permissão SSH | `deploy` |
| `VPS_SSH_KEY` | Chave privada SSH (sem passphrase) | `-----BEGIN OPENSSH PRIVATE KEY...` |
| `VPS_PORT` | Porta SSH (padrão 22) | `22` |
| `GH_PAT_REPO` | Personal Access Token (se JT_ApplicationLayer for privado) | `ghp_xxx...` |

### 4.2. Estratégia de Deploy Seguro e Rollback
O script de deploy:
1. Cria backup da versão atual em `/home/deploy/backups/jt_bi`.
2. Sincroniza os novos binários via `rsync`, ignorando e preservando arquivos locais (`appsettings.Production.json`, uploads e logs).
3. Reinicia o serviço `jt_bi`.
4. Aguarda 5 segundos e valida se o processo está ativo (`systemctl is-active`).
5. Caso o serviço falhe ao iniciar (crash por DLL ausente ou erro fatal), restaura o backup imediatamente e aborta o workflow com status de erro.

---

## 5. Publicação Local Manual

Se preferir realizar o build e upload manualmente:

### No Windows (PowerShell):
```powershell
./deploy/publish.ps1
```

### No Linux / macOS (Bash):
```bash
chmod +x ./deploy/publish.sh
./deploy/publish.sh
```

Os arquivos prontos serão gerados na pasta `./publish`. Em seguida, sincronize para a VPS:
```bash
rsync -avz --exclude 'appsettings.Production.json' ./publish/ deploy@<IP_VPS>:/tmp/release_jt_bi/
ssh deploy@<IP_VPS> "sudo cp -r /tmp/release_jt_bi/* /var/www/jt_bi/ && sudo systemctl restart jt_bi"
```

---

## 6. Diagnóstico e Monitoramento

- **Ver logs em tempo real do serviço:**
  ```bash
  sudo journalctl -u jt_bi -f -n 100
  ```
- **Verificar porta em escuta:**
  ```bash
  sudo ss -tulpn | grep 5010
  ```
- **Verificar logs do Nginx:**
  ```bash
  sudo tail -f /var/log/nginx/error.log
  ```
