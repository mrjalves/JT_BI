# JT_BI - Business Intelligence, Mineração de Dados & IA Executiva

Módulo de Business Intelligence, Analytics Avançado e Dashboards Gerenciais da plataforma **JT Sistemas**, desenvolvido em **.NET 10** com **Blazor Interactive Server**, **ML.NET** e suporte a **Modelos de Linguagem (Google Gemini / OpenAI)**.

---

## 🚀 Tecnologias e Capacidades

- **Framework:** .NET 10 (C# 13)
- **UI & Interatividade:** Blazor Interactive Server Components com layout responsivo e barras laterais com rolagem fixa
- **Machine Learning & Mineração:** **ML.NET** (`Microsoft.ML.FastTree`), K-Means Multidimensional com cálculo de Silhueta, Curva ABC (Princípio de Pareto 80/95) e Score Supervisionado de Churn
- **Inteligência Artificial (LLM):** Agente analítico CRISP-DM integrado a **Google Gemini** e provedores compatíveis com **OpenAI**, com revisor científico e parecer executivo
- **Consultor Executivo IA Público:** `BIPainelRelatorioIA` integrado ao visualizador público com diagnóstico 360°, segmentação e previsão de riscos
- **Uploads de Alta Capacidade:** Suporte a arquivos CSV e JSON de **até 5 GB** via streaming com Kestrel e SignalR configurados para grandes payloads
- **Serviços Principais:** `IBIDashboardService`, `IBIDataSourceService`, `IBIImportService`, `IBIDataMiningService`, `IBIMineracaoAgenteService`, `ConfiguracaoIAService`, `BIModuloVerificacaoService`, `DwConsultaService`, `OrigemDadosService`
- **Dados:** origens reais do EDW (`Fato_{TabelaOrigem}`) via `DwConsultaService` (`/api/dw` ou SQL local) e datasets importados gerenciáveis
- **Persistência:** arquivos JSON estruturados em `Data/` (`dashboards.json` e `datasets.json`), com cache em memória e tratamento de concorrência por semáforo
- **Ownership:** dashboards e datasets vinculados ao usuário autenticado (`CurrentUserState`), com visão global para Administrador/Supervisor
- **Qualidade & Confiabilidade:** **54 testes unitários automatizados** cobrindo algoritmos, ML, tratamento de dados e renderização de grade Bootstrap

---

## 🏛️ Arquitetura

O JT_BI é um host único **Blazor Web App (Interactive Server)**, com separação interna entre apresentação, serviços de BI, modelo de domínio e persistência JSON. A documentação completa de camadas, fluxos, serviços, mineração de dados, compartilhamento público e segurança está em:

👉 **[Documento de Arquitetura (docs/ARQUITETURA.md)](docs/ARQUITETURA.md)**

Detalhamento da metodologia CRISP-DM, K-Means multidimensional, Curva ABC, FastTree Churn e testes automatizados:

👉 **[Mineração de Dados & Inteligência Artificial (docs/MINERACAO_DE_DADOS.md)](docs/MINERACAO_DE_DADOS.md)**

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

# Executar testes unitários (54 testes)
dotnet test tests/JT_BI.Tests/JT_BI.Tests.csproj

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
