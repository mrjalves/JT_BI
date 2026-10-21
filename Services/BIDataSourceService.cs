using System.Text.Json;
using JT_BI.Models;

namespace JT_BI.Services;

public class FonteDadosInfo
{
    public string Id { get; set; } = string.Empty;
    public string Nome { get; set; } = string.Empty;
    public string Descricao { get; set; } = string.Empty;
    public string Icone { get; set; } = "bi-database";
    public string Cor { get; set; } = "primary";
    public List<string> Metricas { get; set; } = new();
    public List<string> Dimensoes { get; set; } = new();
}

public interface IBIDataSourceService
{
    List<FonteDadosInfo> ObterFontesDisponiveis();
    Task<List<BIDataPoint>> ObterDadosWidgetAsync(BIWidget widget, List<BIDataset>? datasets = null);
    Task<(decimal Valor, decimal? Variacao)> ObterValorKpiAsync(BIWidget widget, List<BIDataset>? datasets = null);
}

public class BIDataSourceService : IBIDataSourceService
{
    public List<FonteDadosInfo> ObterFontesDisponiveis()
    {
        return new List<FonteDadosInfo>
        {
            new FonteDadosInfo
            {
                Id = "financeiro",
                Nome = "Financeiro & Tesouraria",
                Descricao = "Receitas, despesas, fluxo de caixa e inadimplência",
                Icone = "bi-cash-coin",
                Cor = "success",
                Metricas = new List<string> { "Faturamento", "Despesas", "Lucro Liquido", "Inadimplencia", "Saldo Caixa" },
                Dimensoes = new List<string> { "Mes", "Centro de Custo", "Forma Pagamento", "Conta Bancaria" }
            },
            new FonteDadosInfo
            {
                Id = "food",
                Nome = "Food & Restaurante (JT_Food)",
                Descricao = "Mesas, comandas, pratos mais vendidos e ticket médio",
                Icone = "bi-cup-straw",
                Cor = "warning",
                Metricas = new List<string> { "Faturamento Restaurante", "Ticket Medio", "Qtd Pedidos", "Giro Mesas", "Tempo Medio Atendimento" },
                Dimensoes = new List<string> { "Categoria Cardapio", "Ambiente Salao", "Turno / Horario", "Garcom / Atendente" }
            },
            new FonteDadosInfo
            {
                Id = "vendas",
                Nome = "Comercial & CRM",
                Descricao = "Leads, taxas de conversão e metas batidas",
                Icone = "bi-graph-up-arrow",
                Cor = "primary",
                Metricas = new List<string> { "Novos Leads", "Taxa Conversao", "Vendas Fechadas", "Valor Pipeline" },
                Dimensoes = new List<string> { "Canal Aquisicao", "Vendedor", "Status Negocio", "Segmento Cliente" }
            },
            new FonteDadosInfo
            {
                Id = "estoque",
                Nome = "Estoque & Suprimentos",
                Descricao = "Giro de mercadorias, curva ABC e reposição",
                Icone = "bi-box-seam",
                Cor = "info",
                Metricas = new List<string> { "Valor Total Estoque", "Itens Criticos", "Giro Estoque", "Perdas / Descarte" },
                Dimensoes = new List<string> { "Grupo Produto", "Fornecedor", "Local Armazenagem" }
            }
        };
    }

    public Task<(decimal Valor, decimal? Variacao)> ObterValorKpiAsync(BIWidget widget, List<BIDataset>? datasets = null)
    {
        // Se houver dataset vinculado customizado
        if (widget.FonteDadosId.StartsWith("dataset_") && datasets != null)
        {
            var dsId = widget.FonteDadosId.Replace("dataset_", "");
            var ds = datasets.FirstOrDefault(d => d.Id.ToString() == dsId);
            if (ds != null && ds.Linhas.Count > 0)
            {
                decimal soma = 0;
                int count = 0;
                foreach (var row in ds.Linhas)
                {
                    if (row.TryGetValue(widget.MetricaCampo, out var valObj) && decimal.TryParse(valObj?.ToString(), out var num))
                    {
                        soma += num;
                        count++;
                    }
                }
                decimal resultado = widget.Agregacao == "Media" && count > 0 ? soma / count : (widget.Agregacao == "Contagem" ? count : soma);
                return Task.FromResult((resultado, (decimal?)5.4m));
            }
        }

        // Mock inteligente baseado na métrica
        decimal valor = widget.MetricaCampo switch
        {
            "Faturamento" => 284560.00m,
            "Despesas" => 173200.00m,
            "Lucro Liquido" => 111360.00m,
            "Ticket Medio" => 84.50m,
            "Faturamento Restaurante" => 142800.00m,
            "Qtd Pedidos" => 1690m,
            "Giro Mesas" => 4.2m,
            "Novos Leads" => 380m,
            "Taxa Conversao" => 24.8m,
            "Vendas Fechadas" => 94m,
            "Valor Total Estoque" => 642000m,
            "Itens Criticos" => 14m,
            _ => widget.ValorAtual ?? 12500m
        };

        decimal? variacao = widget.VariacaoPercentual ?? 8.7m;
        return Task.FromResult((valor, variacao));
    }

    public Task<List<BIDataPoint>> ObterDadosWidgetAsync(BIWidget widget, List<BIDataset>? datasets = null)
    {
        // Dataset customizado
        if (widget.FonteDadosId.StartsWith("dataset_") && datasets != null)
        {
            var dsId = widget.FonteDadosId.Replace("dataset_", "");
            var ds = datasets.FirstOrDefault(d => d.Id.ToString() == dsId);
            if (ds != null && ds.Linhas.Count > 0)
            {
                var agrupado = new Dictionary<string, (decimal Soma, int Count)>();
                foreach (var row in ds.Linhas)
                {
                    string dimVal = row.TryGetValue(widget.DimensaoCampo, out var dVal) ? dVal?.ToString() ?? "Outros" : "Outros";
                    decimal metVal = 0;
                    if (row.TryGetValue(widget.MetricaCampo, out var mVal) && decimal.TryParse(mVal?.ToString(), out var parsedMet))
                    {
                        metVal = parsedMet;
                    }

                    if (!agrupado.ContainsKey(dimVal))
                        agrupado[dimVal] = (0, 0);

                    var atual = agrupado[dimVal];
                    agrupado[dimVal] = (atual.Soma + metVal, atual.Count + 1);
                }

                string[] cores = new[] { "#3b82f6", "#10b981", "#f59e0b", "#ec4899", "#8b5cf6", "#06b6d4", "#64748b" };
                int corIdx = 0;
                var pontos = agrupado.Take(10).Select(kv =>
                {
                    decimal valFinal = widget.Agregacao == "Media" && kv.Value.Count > 0 ? kv.Value.Soma / kv.Value.Count : (widget.Agregacao == "Contagem" ? kv.Value.Count : kv.Value.Soma);
                    string cor = cores[corIdx++ % cores.Length];
                    return new BIDataPoint
                    {
                        Rotulo = kv.Key,
                        Valor = valFinal,
                        ValorFormatado = widget.FormatoValor == "Moeda" ? $"R$ {valFinal:N2}" : $"{valFinal:N0}",
                        Cor = cor
                    };
                }).ToList();

                return Task.FromResult(pontos);
            }
        }

        // Mock predefinido para cada dimensão
        var lista = new List<BIDataPoint>();
        if (widget.DimensaoCampo == "Mes" || widget.DimensaoCampo == "Periodo")
        {
            string[] meses = new[] { "Jan", "Fev", "Mar", "Abr", "Mai", "Jun", "Jul", "Ago", "Set", "Out" };
            decimal[] valores = new[] { 22000m, 26000m, 29500m, 31000m, 28000m, 35000m, 42000m, 46000m, 49000m, 54000m };
            for (int i = 0; i < meses.Length; i++)
            {
                lista.Add(new BIDataPoint
                {
                    Rotulo = meses[i],
                    Valor = valores[i],
                    ValorFormatado = $"R$ {valores[i]:N0}",
                    Cor = widget.CorTema == "success" ? "#10b981" : (widget.CorTema == "warning" ? "#f59e0b" : "#3b82f6")
                });
            }
        }
        else if (widget.DimensaoCampo == "Categoria Cardapio" || widget.DimensaoCampo == "Grupo Produto")
        {
            string[] cats = new[] { "Pizzas & Massas", "Carnes & Grelhados", "Bebidas & Chopp", "Sobremesas", "Entradas" };
            decimal[] vals = new[] { 48500m, 36200m, 29800m, 14300m, 11400m };
            string[] cores = new[] { "#3b82f6", "#10b981", "#f59e0b", "#ec4899", "#8b5cf6" };
            decimal total = vals.Sum();

            for (int i = 0; i < cats.Length; i++)
            {
                lista.Add(new BIDataPoint
                {
                    Rotulo = cats[i],
                    Valor = vals[i],
                    ValorFormatado = $"R$ {vals[i]:N0}",
                    Cor = cores[i],
                    Percentual = total > 0 ? (vals[i] / total) * 100 : 0
                });
            }
        }
        else if (widget.DimensaoCampo == "Ambiente Salao")
        {
            string[] ambs = new[] { "Salão Principal", "Varanda Externa", "Área VIP / Lounge", "Deck Superior" };
            decimal[] vals = new[] { 68400m, 39100m, 24500m, 10800m };
            string[] cores = new[] { "#3b82f6", "#06b6d4", "#8b5cf6", "#f97316" };
            decimal total = vals.Sum();

            for (int i = 0; i < ambs.Length; i++)
            {
                lista.Add(new BIDataPoint
                {
                    Rotulo = ambs[i],
                    Valor = vals[i],
                    ValorFormatado = $"R$ {vals[i]:N0}",
                    Cor = cores[i],
                    Percentual = total > 0 ? (vals[i] / total) * 100 : 0
                });
            }
        }
        else
        {
            // Genérico
            string[] itens = new[] { "Direto", "WhatsApp", "iFood / Delivery", "Indicação", "Redes Sociais" };
            decimal[] vals = new[] { 520m, 380m, 640m, 210m, 190m };
            string[] cores = new[] { "#3b82f6", "#10b981", "#f59e0b", "#8b5cf6", "#ec4899" };
            decimal total = vals.Sum();

            for (int i = 0; i < itens.Length; i++)
            {
                lista.Add(new BIDataPoint
                {
                    Rotulo = itens[i],
                    Valor = vals[i],
                    ValorFormatado = $"{vals[i]:N0}",
                    Cor = cores[i],
                    Percentual = total > 0 ? (vals[i] / total) * 100 : 0
                });
            }
        }

        return Task.FromResult(lista);
    }
}