using System.Text.Json;
using JT_BI.Models;

namespace JT_BI.Services;

public interface IBIDashboardService
{
    Task<List<BIDashboard>> ObterTodosAsync();
    Task<BIDashboard?> ObterPorIdAsync(Guid id);
    Task<BIDashboard?> ObterPorTokenPublicoAsync(string token);
    Task SalvarAsync(BIDashboard dashboard);
    Task ExcluirAsync(Guid id);
    Task<BIDashboard> DuplicarAsync(Guid id);
    Task<List<BIDataset>> ObterDatasetsAsync();
    Task SalvarDatasetAsync(BIDataset dataset);
}

public class BIDashboardService : IBIDashboardService
{
    private readonly string _storageDir;
    private readonly string _dashboardsFilePath;
    private readonly string _datasetsFilePath;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private List<BIDashboard>? _cacheDashboards;
    private List<BIDataset>? _cacheDatasets;

    public BIDashboardService(IWebHostEnvironment env)
    {
        _storageDir = Path.Combine(env.ContentRootPath, "Data");
        if (!Directory.Exists(_storageDir))
            Directory.CreateDirectory(_storageDir);

        _dashboardsFilePath = Path.Combine(_storageDir, "dashboards.json");
        _datasetsFilePath = Path.Combine(_storageDir, "datasets.json");
    }

    public async Task<List<BIDashboard>> ObterTodosAsync()
    {
        await _lock.WaitAsync();
        try
        {
            if (_cacheDashboards == null)
            {
                if (File.Exists(_dashboardsFilePath))
                {
                    var json = await File.ReadAllTextAsync(_dashboardsFilePath);
                    _cacheDashboards = JsonSerializer.Deserialize<List<BIDashboard>>(json) ?? new();
                }
                else
                {
                    _cacheDashboards = CriarDashboardsIniciais();
                    await SalvarEmArquivoAsync();
                }
            }
            return _cacheDashboards.OrderByDescending(d => d.DataAtualizacao).ToList();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<BIDashboard?> ObterPorIdAsync(Guid id)
    {
        var todos = await ObterTodosAsync();
        return todos.FirstOrDefault(d => d.Id == id);
    }

    public async Task<BIDashboard?> ObterPorTokenPublicoAsync(string token)
    {
        var todos = await ObterTodosAsync();
        return todos.FirstOrDefault(d => d.PublicToken.Equals(token, StringComparison.OrdinalIgnoreCase) && d.PermiteAcessoPublico);
    }

    public async Task SalvarAsync(BIDashboard dashboard)
    {
        await _lock.WaitAsync();
        try
        {
            dashboard.DataAtualizacao = DateTime.Now;
            var index = _cacheDashboards?.FindIndex(d => d.Id == dashboard.Id) ?? -1;
            if (index >= 0 && _cacheDashboards != null)
            {
                _cacheDashboards[index] = dashboard;
            }
            else
            {
                _cacheDashboards ??= new List<BIDashboard>();
                _cacheDashboards.Add(dashboard);
            }
            await SalvarEmArquivoAsync();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task ExcluirAsync(Guid id)
    {
        await _lock.WaitAsync();
        try
        {
            if (_cacheDashboards != null)
            {
                _cacheDashboards.RemoveAll(d => d.Id == id);
                await SalvarEmArquivoAsync();
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<BIDashboard> DuplicarAsync(Guid id)
    {
        var original = await ObterPorIdAsync(id);
        if (original == null)
            throw new Exception("Dashboard nÃƒÂ£o encontrado.");

        var json = JsonSerializer.Serialize(original);
        var copia = JsonSerializer.Deserialize<BIDashboard>(json)!;
        copia.Id = Guid.NewGuid();
        copia.PublicToken = Guid.NewGuid().ToString("N");
        copia.Nome = $"{original.Nome} (CÃ³pia)";
        copia.DataCriacao = DateTime.Now;
        copia.DataAtualizacao = DateTime.Now;

        foreach (var w in copia.Widgets)
        {
            w.Id = Guid.NewGuid();
        }

        await SalvarAsync(copia);
        return copia;
    }

    public async Task<List<BIDataset>> ObterDatasetsAsync()
    {
        await _lock.WaitAsync();
        try
        {
            if (_cacheDatasets == null)
            {
                if (File.Exists(_datasetsFilePath))
                {
                    var json = await File.ReadAllTextAsync(_datasetsFilePath);
                    _cacheDatasets = JsonSerializer.Deserialize<List<BIDataset>>(json) ?? new();
                }
                else
                {
                    _cacheDatasets = new List<BIDataset>();
                }
            }
            return _cacheDatasets;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SalvarDatasetAsync(BIDataset dataset)
    {
        await _lock.WaitAsync();
        try
        {
            _cacheDatasets ??= new List<BIDataset>();
            var idx = _cacheDatasets.FindIndex(d => d.Id == dataset.Id);
            if (idx >= 0)
                _cacheDatasets[idx] = dataset;
            else
                _cacheDatasets.Add(dataset);

            var json = JsonSerializer.Serialize(_cacheDatasets, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(_datasetsFilePath, json);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task SalvarEmArquivoAsync()
    {
        if (_cacheDashboards != null)
        {
            var json = JsonSerializer.Serialize(_cacheDashboards, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(_dashboardsFilePath, json);
        }
    }

    private List<BIDashboard> CriarDashboardsIniciais()
    {
        var lista = new List<BIDashboard>();

        // 1. VisÃ£o Executiva 360Â°
        var exec = new BIDashboard
        {
            Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Nome = "VisÃ£o Executiva 360Â°",
            Descricao = "ConsolidaÃ§Ã£o de receitas, margem lÃ­quida, lucratividade e metas empresariais.",
            Categoria = "EstratÃ©gico",
            Icone = "bi-speedometer2",
            CorDestaque = "primary",
            DataCriacao = DateTime.Now.AddDays(-10),
            DataAtualizacao = DateTime.Now
        };

        exec.Widgets.Add(new BIWidget
        {
            Id = Guid.NewGuid(),
            Titulo = "Faturamento Mensal",
            Subtitulo = "Receita bruta consolidada",
            Tipo = BIWidgetTipo.CardKpi,
            PosX = 2,
            PosY = 2,
            Largura = 22,
            Altura = 18,
            CorTema = "primary",
            Icone = "bi-currency-dollar",
            FonteDadosId = "financeiro",
            MetricaCampo = "Faturamento",
            DimensaoCampo = "Mes",
            FormatoValor = "Moeda",
            ValorAtual = 284560m,
            VariacaoPercentual = 12.4m
        });

        exec.Widgets.Add(new BIWidget
        {
            Id = Guid.NewGuid(),
            Titulo = "Lucro LÃ­quido",
            Subtitulo = "Margem operacional de 39%",
            Tipo = BIWidgetTipo.CardKpi,
            PosX = 26,
            PosY = 2,
            Largura = 22,
            Altura = 18,
            CorTema = "success",
            Icone = "bi-graph-up-arrow",
            FonteDadosId = "financeiro",
            MetricaCampo = "Lucro Liquido",
            DimensaoCampo = "Mes",
            FormatoValor = "Moeda",
            ValorAtual = 111360m,
            VariacaoPercentual = 8.6m
        });

        exec.Widgets.Add(new BIWidget
        {
            Id = Guid.NewGuid(),
            Titulo = "Ticket MÃ©dio",
            Subtitulo = "MÃ©dia por transaÃ§Ã£o",
            Tipo = BIWidgetTipo.CardKpi,
            PosX = 50,
            PosY = 2,
            Largura = 22,
            Altura = 18,
            CorTema = "info",
            Icone = "bi-receipt",
            FonteDadosId = "financeiro",
            MetricaCampo = "Ticket Medio",
            DimensaoCampo = "Mes",
            FormatoValor = "Moeda",
            ValorAtual = 84.50m,
            VariacaoPercentual = 3.2m
        });

        exec.Widgets.Add(new BIWidget
        {
            Id = Guid.NewGuid(),
            Titulo = "Metas Comercial",
            Subtitulo = "Meta batida no trimestre",
            Tipo = BIWidgetTipo.CardKpi,
            PosX = 74,
            PosY = 2,
            Largura = 24,
            Altura = 18,
            CorTema = "purple",
            Icone = "bi-award",
            FonteDadosId = "vendas",
            MetricaCampo = "Taxa Conversao",
            DimensaoCampo = "Canal Aquisicao",
            FormatoValor = "Percentual",
            ValorAtual = 89.5m,
            VariacaoPercentual = 4.1m
        });

        exec.Widgets.Add(new BIWidget
        {
            Id = Guid.NewGuid(),
            Titulo = "EvoluÃ§Ã£o do Faturamento Mensal",
            Subtitulo = "HistÃ³rico dos Ãºltimos meses",
            Tipo = BIWidgetTipo.GraficoLinhas,
            PosX = 2,
            PosY = 23,
            Largura = 58,
            Altura = 45,
            CorTema = "primary",
            Icone = "bi-graph-up",
            FonteDadosId = "financeiro",
            MetricaCampo = "Faturamento",
            DimensaoCampo = "Mes",
            FormatoValor = "Moeda"
        });

        exec.Widgets.Add(new BIWidget
        {
            Id = Guid.NewGuid(),
            Titulo = "Faturamento por Categoria",
            Subtitulo = "ParticipaÃ§Ã£o percentual no faturamento",
            Tipo = BIWidgetTipo.GraficoPizza,
            PosX = 62,
            PosY = 23,
            Largura = 36,
            Altura = 45,
            CorTema = "purple",
            Icone = "bi-pie-chart",
            FonteDadosId = "food",
            MetricaCampo = "Faturamento Restaurante",
            DimensaoCampo = "Categoria Cardapio",
            FormatoValor = "Percentual"
        });

        lista.Add(exec);

        // 2. Painel Restaurante & Food
        var food = new BIDashboard
        {
            Id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            Nome = "Performance Restaurante & Food",
            Descricao = "Monitoramento de giro de mesas, consumo por ambiente, comandas e vendas por categoria.",
            Categoria = "Operacional",
            Icone = "bi-cup-straw",
            CorDestaque = "warning",
            DataCriacao = DateTime.Now.AddDays(-5),
            DataAtualizacao = DateTime.Now
        };

        food.Widgets.Add(new BIWidget
        {
            Id = Guid.NewGuid(),
            Titulo = "Faturamento Restaurante",
            Subtitulo = "Vendas totais do salÃ£o e delivery",
            Tipo = BIWidgetTipo.CardKpi,
            PosX = 2,
            PosY = 2,
            Largura = 22,
            Altura = 18,
            CorTema = "warning",
            Icone = "bi-currency-dollar",
            FonteDadosId = "food",
            MetricaCampo = "Faturamento Restaurante",
            DimensaoCampo = "Categoria Cardapio",
            FormatoValor = "Moeda",
            ValorAtual = 142800m,
            VariacaoPercentual = 14.8m
        });

        food.Widgets.Add(new BIWidget
        {
            Id = Guid.NewGuid(),
            Titulo = "Giro MÃ©dio de Mesas",
            Subtitulo = "OcupaÃ§Ãµes completas por mesa / dia",
            Tipo = BIWidgetTipo.CardKpi,
            PosX = 26,
            PosY = 2,
            Largura = 22,
            Altura = 18,
            CorTema = "primary",
            Icone = "bi-arrow-repeat",
            FonteDadosId = "food",
            MetricaCampo = "Giro Mesas",
            DimensaoCampo = "Ambiente Salao",
            FormatoValor = "Decimal",
            ValorAtual = 4.2m,
            VariacaoPercentual = 5.0m
        });

        food.Widgets.Add(new BIWidget
        {
            Id = Guid.NewGuid(),
            Titulo = "Total de Pedidos",
            Subtitulo = "Comandas atendidas",
            Tipo = BIWidgetTipo.CardKpi,
            PosX = 50,
            PosY = 2,
            Largura = 22,
            Altura = 18,
            CorTema = "success",
            Icone = "bi-cart-check",
            FonteDadosId = "food",
            MetricaCampo = "Qtd Pedidos",
            DimensaoCampo = "Turno / Horario",
            FormatoValor = "Numero",
            ValorAtual = 1690m,
            VariacaoPercentual = 9.2m
        });

        food.Widgets.Add(new BIWidget
        {
            Id = Guid.NewGuid(),
            Titulo = "Receita por Ambiente do SalÃ£o",
            Subtitulo = "Comparativo entre salÃ£o principal, varanda e deck",
            Tipo = BIWidgetTipo.GraficoBarras,
            PosX = 2,
            PosY = 23,
            Largura = 48,
            Altura = 45,
            CorTema = "warning",
            Icone = "bi-bar-chart",
            FonteDadosId = "food",
            MetricaCampo = "Faturamento Restaurante",
            DimensaoCampo = "Ambiente Salao",
            FormatoValor = "Moeda"
        });

        food.Widgets.Add(new BIWidget
        {
            Id = Guid.NewGuid(),
            Titulo = "Categorias Mais Pedidas",
            Subtitulo = "Volume por pratos e bebidas",
            Tipo = BIWidgetTipo.GraficoPizza,
            PosX = 52,
            PosY = 23,
            Largura = 46,
            Altura = 45,
            CorTema = "info",
            Icone = "bi-pie-chart",
            FonteDadosId = "food",
            MetricaCampo = "Faturamento Restaurante",
            DimensaoCampo = "Categoria Cardapio",
            FormatoValor = "Percentual"
        });

        lista.Add(food);

        return lista;
    }
}