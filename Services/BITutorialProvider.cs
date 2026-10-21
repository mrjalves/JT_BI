using System.Collections.Generic;
using JT_UserInterface.Shared.Models;
using JT_UserInterface.Shared.Services.Interfaces.Comum;

namespace JT_BI.Services
{
    /// <summary>
    /// Provedor oficial de tutoriais e instruções guiadas para as páginas do JT_BI Studio.
    /// Registrado via <see cref="ITutorialProvider"/> e gerenciado pelo TutorialService / TutorialHost.
    /// Oferece tour interativo na primeira visita e quando acionado pelo botão de ajuda (?).
    /// </summary>
    public class BITutorialProvider : ITutorialProvider
    {
        public List<TutorialDefinicao> ObterTutoriais() => new()
        {
            // ──────────────────────────────────────────────
            // 1. Studio Principal de BI (SPA Analítica)
            // ──────────────────────────────────────────────
            new TutorialDefinicao
            {
                Chave = "bi-studio",
                Rotas = new List<string> { "/", "/home", "/bi", "/bidashboard" },
                Titulo = "Instruções do JT_BI Studio",
                Descricao = "Guia interativo para criação e visualização de dashboards executivos, conexão de dados e inteligência artificial.",
                Icone = "bi-pie-chart-fill",
                MostrarUmaVez = true,
                Passos = new List<TutorialPasso>
                {
                    new TutorialPasso
                    {
                        Titulo = "Barra Executiva do Studio",
                        Descricao = "Aqui você seleciona o painel ativo, aplica templates prontos, importa planilhas (CSV/JSON), acessa a mineração e salva suas alterações.",
                        Seletor = "[data-tutorial='bi-header']",
                        Posicao = "bottom"
                    },
                    new TutorialPasso
                    {
                        Titulo = "Catálogo de Widgets Analíticos",
                        Descricao = "Arraste componentes para o Canvas Central ou clique neles para inserir rapidamente Cards KPI, Gráficos de Barras, Linhas, Pizza ou Tabelas.",
                        Seletor = "[data-tutorial='bi-toolbox']",
                        Posicao = "right"
                    },
                    new TutorialPasso
                    {
                        Titulo = "Fontes de Dados Conectadas",
                        Descricao = "Acompanhe a integridade e o status das conexões automáticas com os módulos Financeiro, Food, Estoque e Vendas da plataforma.",
                        Seletor = "[data-tutorial='bi-datasources']",
                        Posicao = "right"
                    },
                    new TutorialPasso
                    {
                        Titulo = "Canvas Central (Área de Montagem)",
                        Descricao = "Sua área de trabalho analítica. Aqui você visualiza todos os cards e gráficos em tempo real, podendo reorganizá-los e configurá-los.",
                        Seletor = "[data-tutorial='bi-canvas']",
                        Posicao = "bottom"
                    },
                    new TutorialPasso
                    {
                        Titulo = "Grade de Widgets & Rolagem Completa",
                        Descricao = "Role verticalmente por este painel para inspecionar todas as métricas, indicadores consolidados e gráficos temporais ativos.",
                        Seletor = "[data-tutorial='bi-canvas-body']",
                        Posicao = "bottom"
                    },
                    new TutorialPasso
                    {
                        Titulo = "Consultor Executivo de IA",
                        Descricao = "Converse com o Agente de Inteligência Artificial para solicitar diagnósticos, projeções orçamentárias e gerar novos blocos no Canvas.",
                        Seletor = "[data-tutorial='bi-ai-tab']",
                        Posicao = "left"
                    },
                    new TutorialPasso
                    {
                        Titulo = "Inspetor de Propriedades",
                        Descricao = "Ao selecionar qualquer widget no Canvas, utilize o Inspetor para customizar títulos, métricas, dimensões, cores e largura das colunas.",
                        Seletor = "[data-tutorial='bi-inspector-tab']",
                        Posicao = "left"
                    },
                    new TutorialPasso
                    {
                        Titulo = "Mineração de Dados (K-Means / Curva ABC)",
                        Descricao = "Acesse algoritmos de clusterização e classificação para segmentar clientes, identificar produtos com maior faturamento e prever demandas.",
                        Seletor = "[data-tutorial='bi-mining-btn']",
                        Posicao = "bottom"
                    },
                    new TutorialPasso
                    {
                        Titulo = "Compartilhamento & Exportação",
                        Descricao = "Gere links de visualização pública para exibição em TVs e relatórios de diretoria, ou exporte a arquitetura do painel em JSON.",
                        Seletor = "[data-tutorial='bi-share-btn']",
                        Posicao = "bottom"
                    }
                }
            },

            // ──────────────────────────────────────────────
            // 2. Hub de Todos os Dashboards
            // ──────────────────────────────────────────────
            new TutorialDefinicao
            {
                Chave = "bi-hub",
                Rotas = new List<string> { "/dashboards" },
                Titulo = "Hub de Dashboards Analíticos",
                Descricao = "Gerenciamento central de todos os seus painéis, duplicações rápidas e controle de permissões.",
                Icone = "bi-grid-3x3-gap-fill",
                MostrarUmaVez = true,
                Passos = new List<TutorialPasso>
                {
                    new TutorialPasso
                    {
                        Titulo = "Gestão de Painéis",
                        Descricao = "Crie novos painéis analíticos em branco ou duplicados a partir de estruturas existentes.",
                        Seletor = "[data-tutorial='hub-header']",
                        Posicao = "bottom"
                    },
                    new TutorialPasso
                    {
                        Titulo = "Cards dos Dashboards",
                        Descricao = "Abra qualquer dashboard no modo edição do Studio, duplique estruturas ou copie links para visualização pública.",
                        Seletor = "[data-tutorial='hub-cards']",
                        Posicao = "bottom"
                    }
                }
            }
        };
    }
}
