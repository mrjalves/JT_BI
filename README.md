# JT_BI - Business Intelligence & Dashboard Executivo

Módulo de Business Intelligence e Dashboards Gerenciais da plataforma **JT Sistemas**, desenvolvido em **.NET 10** com **Blazor Interactive Server**.

---

## 🚀 Tecnologias

- **Framework:** .NET 10 (C# 13)
- **UI & Interatividade:** Blazor Interactive Server Components
- **Estilização:** CSS moderno com design tokens, suporte a Dark/Light Theme e layout responsivo
- **Serviços:** `IBIDashboardService`, `IBIDataSourceService`, `IBIImportService`, `IBIDataMiningService`, `BIMenuProvider`, `BITutorialProvider`
- **Persistência:** arquivos JSON em `Data/` (`dashboards.json` e `datasets.json`), com cache em memória e fallback para diretório temporário
- **Integração:** Dependência direta dos projetos de camada compartilhada `JT_ApplicationLayer` (`JT_Classes`, `JT_Servicos`, `JT_Aplicacao` e `JT_UserInterface.Shared`)

---

## 🏛️ Arquitetura

O JT_BI é um host único **Blazor Web App (Interactive Server)**, com separação interna entre apresentação, serviços de BI, modelo de domínio e persistência JSON. A documentação completa de camadas, fluxos, serviços, mineração de dados, compartilhamento público e segurança está em:

👉 **[Documento de Arquitetura (docs/ARQUITETURA.md)](docs/ARQUITETURA.md)**

---

## 🛠️ Execução Local

### Pré-requisitos
- .NET 10 SDK instalado
- Repositório `JT_ApplicationLayer` clonado na mesma pasta raiz pai

### Rodando o projeto
```bash
# Restaurar pacotes
dotnet restore

# Compilar
dotnet build

# Executar com hot reload
dotnet watch
```
Acesse no navegador: `https://localhost:7256` ou `http://localhost:5148` (ou a porta informada no terminal).

---

## 📦 Governança e Deploy

Para detalhes sobre arquivos ignorados pelo Git, automação de CI/CD, configuração na VPS Linux (Systemd e Nginx com WebSockets), consulte o guia completo:

👉 **[Guia Completo de Deploy (DEPLOY.md)](file:///c:/Users/sirja/source/repos/mrjalves/JT_BI/DEPLOY.md)**

### Principais Arquivos de Infraestrutura:
- [`.gitignore`](file:///c:/Users/sirja/source/repos/mrjalves/JT_BI/.gitignore): Regras de exclusão do Git (binários, segredos, caches)
- [`.github/workflows/deploy.yml`](file:///c:/Users/sirja/source/repos/mrjalves/JT_BI/.github/workflows/deploy.yml): Pipeline de CI/CD para VPS
- [`deploy/jt_bi.service`](file:///c:/Users/sirja/source/repos/mrjalves/JT_BI/deploy/jt_bi.service): Configuração do Systemd no Linux
- [`deploy/nginx-jt_bi.conf`](file:///c:/Users/sirja/source/repos/mrjalves/JT_BI/deploy/nginx-jt_bi.conf): Proxy reverso com suporte a SignalR / WebSockets
- [`deploy/publish.ps1`](file:///c:/Users/sirja/source/repos/mrjalves/JT_BI/deploy/publish.ps1) / [`deploy/publish.sh`](file:///c:/Users/sirja/source/repos/mrjalves/JT_BI/deploy/publish.sh): Scripts de compilação em Release
- [`Dockerfile`](file:///c:/Users/sirja/source/repos/mrjalves/JT_BI/Dockerfile) & [`docker-compose.yml`](file:///c:/Users/sirja/source/repos/mrjalves/JT_BI/docker-compose.yml): Suporte a containers
