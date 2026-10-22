using System.Globalization;
using System.Text.Json;
using JT_BI.Models;
using JT_Classes.DW;
using JT_Servicos.DW;

namespace JT_BI.Services;

public class FonteDadosInfo
{
    public string Id { get; set; } = string.Empty;
    public int? OrigemId { get; set; }
    public string Nome { get; set; } = string.Empty;
    public string Descricao { get; set; } = string.Empty;
    public string Icone { get; set; } = "bi-database";
    public string Cor { get; set; } = "primary";
    public bool Demonstrativa { get; set; }
    public List<string> Metricas { get; set; } = new();
    public List<string> Dimensoes { get; set; } = new();
}

public interface IBIDataSourceService
{
    Task<List<FonteDadosInfo>> ObterFontesDisponiveisAsync();
    Task<List<BIDataPoint>> ObterDadosWidgetAsync(BIWidget widget, List<BIDataset>? datasets = null);
    Task<(decimal Valor, decimal? Variacao)> ObterValorKpiAsync(BIWidget widget, List<BIDataset>? datasets = null);
    Task<List<Dictionary<string, string>>> ObterAmostraAsync(int origemId, List<string> colunas, int limite = 1000);
}

public class BIDataSourceService : IBIDataSourceService
{
    private readonly OrigemDadosService _origemDadosService;
    private readonly DwConsultaService _dwConsultaService;

    private static readonly string[] Cores =
    {
        "#3b82f6", "#10b981", "#f59e0b", "#ec4899", "#8b5cf6", "#06b6d4", "#64748b"
    };

    public BIDataSourceService(OrigemDadosService origemDadosService, DwConsultaService dwConsultaService)
    {
        _origemDadosService = origemDadosService;
        _dwConsultaService = dwConsultaService;
    }

    public async Task<List<FonteDadosInfo>> ObterFontesDisponiveisAsync()
    {
        var origens = await _origemDadosService.GetAll();
        if (origens.Count == 0)
            return FontesDemonstrativas();

        var fontes = new List<FonteDadosInfo>();
        var indice = 0;

        foreach (var origem in origens.OrderBy(o => o.Nome))
        {
            var colunasBase = origem.Configuracao?.Colunas ?? new List<string>();
            var colunasRelacionadas = origem.Configuracao?.Relacionamentos
                .SelectMany(r => r.Colunas.Select(c => $"{r.Alias}_{c}"))
                ?? Enumerable.Empty<string>();

            var colunas = colunasBase
                .Concat(colunasRelacionadas)
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            fontes.Add(new FonteDadosInfo
            {
                Id = $"origem_{origem.Id}",
                OrigemId = origem.Id,
                Nome = origem.Nome,
                Descricao = string.IsNullOrWhiteSpace(origem.Descricao)
                    ? $"Tabela EDW: Fato_{origem.TabelaOrigem}"
                    : origem.Descricao,
                Icone = "bi-database-fill",
                Cor = Cores[indice++ % Cores.Length],
                Metricas = colunas,
                Dimensoes = colunas
            });
        }

        return fontes;
    }

    public async Task<(decimal Valor, decimal? Variacao)> ObterValorKpiAsync(
        BIWidget widget,
        List<BIDataset>? datasets = null)
    {
        if (string.Equals(widget.FonteDadosId, "mineracao", StringComparison.OrdinalIgnoreCase))
            return (widget.ValorAtual ?? 0, widget.VariacaoPercentual);

        if (widget.FonteDadosId.StartsWith("dataset_", StringComparison.OrdinalIgnoreCase) && datasets != null)
            return ObterValorKpiDataset(widget, datasets);

        if (!TentarObterOrigemId(widget, out var origemId))
            return ObterValorKpiDemonstrativo(widget);

        var resultado = await _dwConsultaService.ConsultarAsync(new ConsultaDwRequest
        {
            OrigemId = origemId,
            ColunaValor = string.IsNullOrWhiteSpace(widget.MetricaCampo) ? null : widget.MetricaCampo,
            ColunaData = widget.ColunaData,
            Agregacao = MapearAgregacao(widget.Agregacao)
        });

        if (!resultado.Sucesso)
            return (0, null);

        return (resultado.Pontos.FirstOrDefault()?.Valor ?? 0, null);
    }

    public async Task<List<BIDataPoint>> ObterDadosWidgetAsync(
        BIWidget widget,
        List<BIDataset>? datasets = null)
    {
        if (string.Equals(widget.FonteDadosId, "mineracao", StringComparison.OrdinalIgnoreCase))
            return widget.DadosEstaticos;

        if (widget.FonteDadosId.StartsWith("dataset_", StringComparison.OrdinalIgnoreCase) && datasets != null)
            return ObterDadosDataset(widget, datasets);

        if (!TentarObterOrigemId(widget, out var origemId))
            return ObterDadosDemonstrativos(widget);

        var agruparPorMes = widget.AgruparPorMes ||
            (string.Equals(widget.DimensaoCampo, "Mes", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(widget.ColunaData)) ||
            (string.Equals(widget.DimensaoCampo, "Periodo", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(widget.ColunaData));

        var colunaDimensao = agruparPorMes ? null : widget.DimensaoCampo;

        if (string.IsNullOrWhiteSpace(colunaDimensao))
            colunaDimensao = null;

        var resultado = await _dwConsultaService.ConsultarAsync(new ConsultaDwRequest
        {
            OrigemId = origemId,
            ColunaValor = string.IsNullOrWhiteSpace(widget.MetricaCampo) ? null : widget.MetricaCampo,
            ColunaDimensao = colunaDimensao,
            ColunaData = widget.ColunaData,
            Agregacao = MapearAgregacao(widget.Agregacao),
            AgruparPorMes = agruparPorMes,
            Limite = 20
        });

        if (!resultado.Sucesso || resultado.Pontos.Count == 0)
            return new List<BIDataPoint>();

        var total = resultado.Pontos.Sum(p => p.Valor ?? 0);
        var pontos = new List<BIDataPoint>();

        for (var i = 0; i < resultado.Pontos.Count; i++)
        {
            var ponto = resultado.Pontos[i];
            var valor = ponto.Valor ?? 0;

            pontos.Add(new BIDataPoint
            {
                Rotulo = ponto.Rotulo,
                Valor = valor,
                ValorFormatado = FormatarValor(valor, widget.FormatoValor),
                Cor = Cores[i % Cores.Length],
                Percentual = total == 0 ? 0 : Math.Round(valor / total * 100, 2)
            });
        }

        return pontos;
    }

    public async Task<List<Dictionary<string, string>>> ObterAmostraAsync(
        int origemId,
        List<string> colunas,
        int limite = 1000)
    {
        var resultado = await _dwConsultaService.ObterAmostraAsync(new DwAmostraRequest
        {
            OrigemId = origemId,
            Colunas = colunas,
            Limite = limite
        });

        if (!resultado.Sucesso)
            return new List<Dictionary<string, string>>();

        return resultado.Linhas
            .Select(linha => linha.ToDictionary(
                par => par.Key,
                par => par.Value ?? string.Empty,
                StringComparer.OrdinalIgnoreCase))
            .ToList();
    }

    private static bool TentarObterOrigemId(BIWidget widget, out int origemId)
    {
        origemId = 0;

        if (widget.OrigemId is > 0)
        {
            origemId = widget.OrigemId.Value;
            return true;
        }

        if (widget.FonteDadosId.StartsWith("origem_", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(widget.FonteDadosId["origem_".Length..], out origemId))
            return true;

        return false;
    }

    private static eTipoOperacaoIndicador MapearAgregacao(string? agregacao)
        => agregacao?.Trim().ToLowerInvariant() switch
        {
            "media" or "média" => eTipoOperacaoIndicador.Media,
            "contagem" => eTipoOperacaoIndicador.Contagem,
            "max" or "máximo" or "maximo" => eTipoOperacaoIndicador.Maximo,
            "min" or "mínimo" or "minimo" => eTipoOperacaoIndicador.Minimo,
            _ => eTipoOperacaoIndicador.Soma
        };

    private static string FormatarValor(decimal valor, string? formato)
        => formato switch
        {
            "Moeda" => $"R$ {valor:N2}",
            "Percentual" => $"{valor:N1}%",
            "Decimal" => $"{valor:N2}",
            _ => $"{valor:N0}"
        };

    private static (decimal Valor, decimal? Variacao) ObterValorKpiDataset(
        BIWidget widget,
        List<BIDataset> datasets)
    {
        var dsId = widget.FonteDadosId["dataset_".Length..];
        var ds = datasets.FirstOrDefault(d => d.Id.ToString() == dsId);
        if (ds == null || ds.Linhas.Count == 0)
            return (0, null);

        decimal soma = 0;
        var count = 0;

        foreach (var row in ds.Linhas)
        {
            if (row.TryGetValue(widget.MetricaCampo, out var valObj) &&
                decimal.TryParse(valObj?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var num))
            {
                soma += num;
                count++;
            }
        }

        var resultado = widget.Agregacao == "Media" && count > 0
            ? soma / count
            : widget.Agregacao == "Contagem" ? count : soma;

        return (resultado, null);
    }

    private static List<BIDataPoint> ObterDadosDataset(BIWidget widget, List<BIDataset> datasets)
    {
        var dsId = widget.FonteDadosId["dataset_".Length..];
        var ds = datasets.FirstOrDefault(d => d.Id.ToString() == dsId);
        if (ds == null || ds.Linhas.Count == 0)
            return new List<BIDataPoint>();

        var agrupado = new Dictionary<string, (decimal Soma, int Count)>();

        foreach (var row in ds.Linhas)
        {
            var dimVal = row.TryGetValue(widget.DimensaoCampo, out var dVal)
                ? dVal?.ToString() ?? "Outros"
                : "Outros";

            decimal metVal = 0;
            if (row.TryGetValue(widget.MetricaCampo, out var mVal) &&
                decimal.TryParse(mVal?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsedMet))
                metVal = parsedMet;

            if (!agrupado.ContainsKey(dimVal))
                agrupado[dimVal] = (0, 0);

            var atual = agrupado[dimVal];
            agrupado[dimVal] = (atual.Soma + metVal, atual.Count + 1);
        }

        var pontos = new List<BIDataPoint>();
        var indice = 0;

        foreach (var kv in agrupado.Take(10))
        {
            var valor = widget.Agregacao == "Media" && kv.Value.Count > 0
                ? kv.Value.Soma / kv.Value.Count
                : widget.Agregacao == "Contagem" ? kv.Value.Count : kv.Value.Soma;

            pontos.Add(new BIDataPoint
            {
                Rotulo = kv.Key,
                Valor = valor,
                ValorFormatado = FormatarValor(valor, widget.FormatoValor),
                Cor = Cores[indice++ % Cores.Length]
            });
        }

        return pontos;
    }

    private static List<FonteDadosInfo> FontesDemonstrativas()
        => new()
        {
            new FonteDadosInfo
            {
                Id = "financeiro",
                Nome = "Demonstração — Financeiro & Tesouraria",
                Descricao = "Configure uma origem de dados no EDW para substituir estes valores.",
                Icone = "bi-cash-coin",
                Cor = "success",
                Demonstrativa = true,
                Metricas = new List<string> { "Faturamento", "Despesas", "Lucro Liquido", "Inadimplencia", "Saldo Caixa" },
                Dimensoes = new List<string> { "Mes", "Centro de Custo", "Forma Pagamento", "Conta Bancaria" }
            },
            new FonteDadosInfo
            {
                Id = "food",
                Nome = "Demonstração — Food & Restaurante",
                Descricao = "Configure uma origem de dados no EDW para substituir estes valores.",
                Icone = "bi-cup-straw",
                Cor = "warning",
                Demonstrativa = true,
                Metricas = new List<string> { "Faturamento Restaurante", "Ticket Medio", "Qtd Pedidos", "Giro Mesas", "Tempo Medio Atendimento" },
                Dimensoes = new List<string> { "Categoria Cardapio", "Ambiente Salao", "Turno / Horario", "Garcom / Atendente" }
            },
            new FonteDadosInfo
            {
                Id = "vendas",
                Nome = "Demonstração — Comercial & CRM",
                Descricao = "Configure uma origem de dados no EDW para substituir estes valores.",
                Icone = "bi-graph-up-arrow",
                Cor = "primary",
                Demonstrativa = true,
                Metricas = new List<string> { "Novos Leads", "Taxa Conversao", "Vendas Fechadas", "Valor Pipeline" },
                Dimensoes = new List<string> { "Canal Aquisicao", "Vendedor", "Status Negocio", "Segmento Cliente" }
            },
            new FonteDadosInfo
            {
                Id = "estoque",
                Nome = "Demonstração — Estoque & Suprimentos",
                Descricao = "Configure uma origem de dados no EDW para substituir estes valores.",
                Icone = "bi-box-seam",
                Cor = "info",
                Demonstrativa = true,
                Metricas = new List<string> { "Valor Total Estoque", "Itens Criticos", "Giro Estoque", "Perdas / Descarte" },
                Dimensoes = new List<string> { "Grupo Produto", "Fornecedor", "Local Armazenagem" }
            }
        };

    private static (decimal Valor, decimal? Variacao) ObterValorKpiDemonstrativo(BIWidget widget)
    {
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

        return (valor, widget.VariacaoPercentual);
    }

    private static List<BIDataPoint> ObterDadosDemonstrativos(BIWidget widget)
    {
        var lista = new List<BIDataPoint>();
        if (widget.DimensaoCampo == "Mes" || widget.DimensaoCampo == "Periodo")
        {
            string[] meses = { "Jan", "Fev", "Mar", "Abr", "Mai", "Jun", "Jul", "Ago", "Set", "Out" };
            decimal[] valores = { 22000m, 26000m, 29500m, 31000m, 28000m, 35000m, 42000m, 46000m, 49000m, 54000m };

            for (var i = 0; i < meses.Length; i++)
            {
                lista.Add(new BIDataPoint
                {
                    Rotulo = meses[i],
                    Valor = valores[i],
                    ValorFormatado = $"R$ {valores[i]:N0}",
                    Cor = CorTema(widget)
                });
            }
        }
        else if (widget.DimensaoCampo == "Categoria Cardapio" || widget.DimensaoCampo == "Grupo Produto")
        {
            string[] categorias = { "Pizzas & Massas", "Carnes & Grelhados", "Bebidas & Chopp", "Sobremesas", "Entradas" };
            decimal[] valores = { 48500m, 36200m, 29800m, 14300m, 11400m };
            AdicionarDemonstrativos(lista, categorias, valores);
        }
        else if (widget.DimensaoCampo == "Ambiente Salao")
        {
            string[] ambientes = { "Salão Principal", "Varanda Externa", "Área VIP / Lounge", "Deck Superior" };
            decimal[] valores = { 68400m, 39100m, 24500m, 10800m };
            AdicionarDemonstrativos(lista, ambientes, valores);
        }
        else
        {
            string[] itens = { "Direto", "WhatsApp", "iFood / Delivery", "Indicação", "Redes Sociais" };
            decimal[] valores = { 520m, 380m, 640m, 210m, 190m };
            AdicionarDemonstrativos(lista, itens, valores);
        }

        return lista;
    }

    private static void AdicionarDemonstrativos(List<BIDataPoint> lista, string[] rotulos, decimal[] valores)
    {
        var total = valores.Sum();

        for (var i = 0; i < rotulos.Length; i++)
        {
            lista.Add(new BIDataPoint
            {
                Rotulo = rotulos[i],
                Valor = valores[i],
                ValorFormatado = $"R$ {valores[i]:N0}",
                Cor = Cores[i % Cores.Length],
                Percentual = total > 0 ? Math.Round(valores[i] / total * 100, 2) : 0
            });
        }
    }

    private static string CorTema(BIWidget widget)
        => widget.CorTema switch
        {
            "success" => "#10b981",
            "warning" => "#f59e0b",
            "danger" => "#ef4444",
            "purple" => "#8b5cf6",
            "info" => "#06b6d4",
            _ => "#3b82f6"
        };
}
