using System.Text.Json;
using JT_BI.Models;

namespace JT_BI.Services;

public enum BIStatusAcessoPublico
{
    Ok,
    NaoEncontrado,
    DesativadoPeloProprietario
}

public record BIRespAcessoPublico(BIStatusAcessoPublico Status, BIDashboard? Dashboard);

public interface IBIDashboardService
{
    Task<List<BIDashboard>> ObterTodosAsync();
    Task<List<BIDashboard>> ObterVisiveisAsync(BIUsuarioContexto? usuario);
    Task<BIDashboard?> ObterPorIdAsync(Guid id);
    Task<BIDashboard?> ObterAcessivelAsync(Guid id, BIUsuarioContexto? usuario);
    Task<bool> PodeAcessarAsync(Guid id, BIUsuarioContexto? usuario);
    Task<BIDashboard?> ObterPorTokenPublicoAsync(string token);
    Task<BIRespAcessoPublico> ObterStatusPorTokenPublicoAsync(string token);
    Task SalvarAsync(BIDashboard dashboard);
    Task SalvarAsync(BIDashboard dashboard, BIUsuarioContexto? usuario);
    Task ExcluirAsync(Guid id);
    Task<bool> ExcluirAsync(Guid id, BIUsuarioContexto? usuario);
    Task<BIDashboard> DuplicarAsync(Guid id);
    Task<BIDashboard> DuplicarAsync(Guid id, BIUsuarioContexto? usuario);
    Task<List<BIDataset>> ObterDatasetsAsync();
    Task<List<BIDataset>> ObterDatasetsVisiveisAsync(BIUsuarioContexto? usuario);
    Task<List<BIDataset>> ObterDatasetsPorIdsAsync(IEnumerable<Guid> ids);
    Task SalvarDatasetAsync(BIDataset dataset);
    Task SalvarDatasetAsync(BIDataset dataset, BIUsuarioContexto? usuario);
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
        try
        {
            _storageDir = Path.Combine(env.ContentRootPath, "Data");
            if (!Directory.Exists(_storageDir))
                Directory.CreateDirectory(_storageDir);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BIDashboardService] Fallback de diretório devido a permissões: {ex.Message}");
            _storageDir = Path.Combine(Path.GetTempPath(), "JT_BI_Data");
            try
            {
                if (!Directory.Exists(_storageDir))
                    Directory.CreateDirectory(_storageDir);
            }
            catch { }
        }

        _dashboardsFilePath = Path.Combine(_storageDir, "dashboards.json");
        _datasetsFilePath = Path.Combine(_storageDir, "datasets.json");
        Console.WriteLine($"[BIDashboardService] Diretório de persistência configurado: {_storageDir}");
    }

    public async Task<List<BIDashboard>> ObterTodosAsync()
    {
        await _lock.WaitAsync();
        try
        {
            await GarantirCacheDashboardsAsync();
            return _cacheDashboards!
                .OrderByDescending(d => d.DataAtualizacao)
                .Select(ClonarDashboard)
                .ToList();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<List<BIDashboard>> ObterVisiveisAsync(BIUsuarioContexto? usuario)
    {
        var todos = await ObterTodosAsync();
        if (usuario == null)
            return todos;

        return todos.Where(d => PodeAcessar(d, usuario)).ToList();
    }

    public async Task<BIDashboard?> ObterPorIdAsync(Guid id)
    {
        var todos = await ObterTodosAsync();
        return todos.FirstOrDefault(d => d.Id == id);
    }

    public async Task<BIDashboard?> ObterAcessivelAsync(Guid id, BIUsuarioContexto? usuario)
    {
        var dashboard = await ObterPorIdAsync(id);
        return dashboard != null && PodeAcessar(dashboard, usuario) ? dashboard : null;
    }

    public async Task<bool> PodeAcessarAsync(Guid id, BIUsuarioContexto? usuario)
    {
        var dashboard = await ObterPorIdAsync(id);
        return dashboard != null && PodeAcessar(dashboard, usuario);
    }

    public async Task<BIRespAcessoPublico> ObterStatusPorTokenPublicoAsync(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return new BIRespAcessoPublico(BIStatusAcessoPublico.NaoEncontrado, null);

        var todos = await ObterTodosAsync();
        var dash = todos.FirstOrDefault(d => d.PublicToken.Equals(token, StringComparison.OrdinalIgnoreCase));

        if (dash == null)
            return new BIRespAcessoPublico(BIStatusAcessoPublico.NaoEncontrado, null);

        if (!dash.PermiteAcessoPublico)
            return new BIRespAcessoPublico(BIStatusAcessoPublico.DesativadoPeloProprietario, dash);

        return new BIRespAcessoPublico(BIStatusAcessoPublico.Ok, dash);
    }

    public async Task<BIDashboard?> ObterPorTokenPublicoAsync(string token)
    {
        var resp = await ObterStatusPorTokenPublicoAsync(token);
        return resp.Status == BIStatusAcessoPublico.Ok ? resp.Dashboard : null;
    }

    public Task SalvarAsync(BIDashboard dashboard)
        => SalvarAsync(dashboard, null);

    public async Task SalvarAsync(BIDashboard dashboard, BIUsuarioContexto? usuario)
    {
        ArgumentNullException.ThrowIfNull(dashboard);

        await _lock.WaitAsync();
        try
        {
            await GarantirCacheDashboardsAsync();
            var lista = _cacheDashboards!;
            var index = lista.FindIndex(d => d.Id == dashboard.Id);

            if (index >= 0)
            {
                var existente = lista[index];
                if (!PodeAcessar(existente, usuario))
                    throw new UnauthorizedAccessException("Você não tem permissão para alterar este dashboard.");

                // Preserva o dono original, impedindo troca de propriedade pela edição.
                dashboard.UsuarioId = existente.UsuarioId;
                dashboard.UsuarioUid = existente.UsuarioUid;
                dashboard.UsuarioNome = existente.UsuarioNome;

                if (dashboard.DataCriacao == default)
                    dashboard.DataCriacao = existente.DataCriacao;
            }
            else if (usuario is { } dono && dono.TemIdentidade)
            {
                dashboard.UsuarioId = dono.UsuarioId;
                dashboard.UsuarioUid = dono.UsuarioUid;
                dashboard.UsuarioNome = dono.UsuarioNome;
            }

            dashboard.DataAtualizacao = DateTime.Now;
            var snapshot = ClonarDashboard(dashboard);
            if (index >= 0)
                lista[index] = snapshot;
            else
                lista.Add(snapshot);

            await SalvarEmArquivoAsync();
        }
        finally
        {
            _lock.Release();
        }
    }

    public Task ExcluirAsync(Guid id)
        => ExcluirAsync(id, null);

    public async Task<bool> ExcluirAsync(Guid id, BIUsuarioContexto? usuario)
    {
        await _lock.WaitAsync();
        try
        {
            await GarantirCacheDashboardsAsync();
            var lista = _cacheDashboards!;
            var alvo = lista.FirstOrDefault(d => d.Id == id);
            if (alvo == null || !PodeAcessar(alvo, usuario))
                return false;

            lista.Remove(alvo);
            await SalvarEmArquivoAsync();
            return true;
        }
        finally
        {
            _lock.Release();
        }
    }

    public Task<BIDashboard> DuplicarAsync(Guid id)
        => DuplicarAsync(id, null);

    public async Task<BIDashboard> DuplicarAsync(Guid id, BIUsuarioContexto? usuario)
    {
        var original = await ObterAcessivelAsync(id, usuario);
        if (original == null)
            throw new InvalidOperationException("Dashboard não encontrado ou sem permissão de acesso.");

        var json = JsonSerializer.Serialize(original);
        var copia = JsonSerializer.Deserialize<BIDashboard>(json)!;
        copia.Id = Guid.NewGuid();
        copia.PublicToken = Guid.NewGuid().ToString("N");
        copia.Nome = $"{original.Nome} (Cópia)";
        copia.DataCriacao = DateTime.Now;
        copia.DataAtualizacao = DateTime.Now;
        copia.PermiteAcessoPublico = false;

        // A cópia pertence a quem duplicou; sem identidade, herda o dono do original.
        if (usuario is { } dono && dono.TemIdentidade)
        {
            copia.UsuarioId = dono.UsuarioId;
            copia.UsuarioUid = dono.UsuarioUid;
            copia.UsuarioNome = dono.UsuarioNome;
        }

        foreach (var w in copia.Widgets)
        {
            w.Id = Guid.NewGuid();
        }

        await SalvarAsync(copia, usuario);
        return copia;
    }

    public async Task<List<BIDataset>> ObterDatasetsAsync()
    {
        await _lock.WaitAsync();
        try
        {
            await GarantirCacheDatasetsAsync();
            return _cacheDatasets!;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<List<BIDataset>> ObterDatasetsVisiveisAsync(BIUsuarioContexto? usuario)
    {
        var todos = await ObterDatasetsAsync();
        if (usuario == null)
            return todos;

        return todos.Where(d => PodeAcessar(d, usuario)).ToList();
    }

    public async Task<List<BIDataset>> ObterDatasetsPorIdsAsync(IEnumerable<Guid> ids)
    {
        var alvo = ids?.ToHashSet() ?? new HashSet<Guid>();
        var todos = await ObterDatasetsAsync();
        return todos.Where(d => alvo.Contains(d.Id)).ToList();
    }

    public Task SalvarDatasetAsync(BIDataset dataset)
        => SalvarDatasetAsync(dataset, null);

    public async Task SalvarDatasetAsync(BIDataset dataset, BIUsuarioContexto? usuario)
    {
        ArgumentNullException.ThrowIfNull(dataset);

        await _lock.WaitAsync();
        try
        {
            await GarantirCacheDatasetsAsync();
            var lista = _cacheDatasets!;
            var idx = lista.FindIndex(d => d.Id == dataset.Id);

            if (idx >= 0)
            {
                var existente = lista[idx];
                if (!PodeAcessar(existente, usuario))
                    throw new UnauthorizedAccessException("Você não tem permissão para alterar este dataset.");

                // Preserva o dono original, impedindo troca de propriedade pela edição.
                dataset.UsuarioId = existente.UsuarioId;
                dataset.UsuarioUid = existente.UsuarioUid;
                dataset.UsuarioNome = existente.UsuarioNome;
                lista[idx] = dataset;
            }
            else
            {
                if (usuario is { } dono && dono.TemIdentidade)
                {
                    dataset.UsuarioId = dono.UsuarioId;
                    dataset.UsuarioUid = dono.UsuarioUid;
                    dataset.UsuarioNome = dono.UsuarioNome;
                }

                lista.Add(dataset);
            }

            await SalvarDatasetsEmArquivoAsync();
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task GarantirCacheDashboardsAsync()
    {
        if (_cacheDashboards != null) return;

        if (File.Exists(_dashboardsFilePath))
        {
            var json = await File.ReadAllTextAsync(_dashboardsFilePath);
            _cacheDashboards = JsonSerializer.Deserialize<List<BIDashboard>>(json) ?? new();
            foreach (var d in _cacheDashboards)
            {
                SanitizarDashboard(d);
            }
        }
        else
        {
            _cacheDashboards = CriarDashboardsIniciais();
            await SalvarEmArquivoAsync();
        }
    }

    private async Task GarantirCacheDatasetsAsync()
    {
        if (_cacheDatasets != null) return;

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

    private static BIDashboard ClonarDashboard(BIDashboard dashboard)
        => JsonSerializer.Deserialize<BIDashboard>(JsonSerializer.Serialize(dashboard))!;

    private static bool PodeAcessar(BIDashboard dashboard, BIUsuarioContexto? usuario)
        => Permitido(dashboard.UsuarioId, dashboard.UsuarioUid, usuario);

    private static bool PodeAcessar(BIDataset dataset, BIUsuarioContexto? usuario)
        => Permitido(dataset.UsuarioId, dataset.UsuarioUid, usuario);

    private static bool Permitido(int? donoId, string? donoUid, BIUsuarioContexto? usuario)
    {
        if (usuario == null) return true;
        if (usuario.AcessoTotal) return true;

        var semDono = donoId is null or <= 0 && string.IsNullOrWhiteSpace(donoUid);
        if (semDono) return true;

        if (donoId is > 0 && usuario.UsuarioId == donoId)
            return true;

        if (!string.IsNullOrWhiteSpace(donoUid) &&
            !string.IsNullOrWhiteSpace(usuario.UsuarioUid) &&
            string.Equals(donoUid, usuario.UsuarioUid, StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    private async Task SalvarEmArquivoAsync()
    {
        if (_cacheDashboards != null)
        {
            try
            {
                var json = JsonSerializer.Serialize(_cacheDashboards, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(_dashboardsFilePath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[BIDashboardService] Aviso ao gravar dashboards no disco: {ex.Message}");
            }
        }
    }

    private async Task SalvarDatasetsEmArquivoAsync()
    {
        if (_cacheDatasets == null) return;

        try
        {
            var json = JsonSerializer.Serialize(_cacheDatasets, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(_datasetsFilePath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BIDashboardService] Aviso ao gravar datasets no disco: {ex.Message}");
        }
    }

    private List<BIDashboard> CriarDashboardsIniciais()
    {
        var lista = new List<BIDashboard>();

        // 1. Visão Executiva 360°
        var exec = new BIDashboard
        {
            Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Nome = "Visão Executiva 360°",
            Descricao = "Consolidação de receitas, margem líquida, lucratividade e metas empresariais.",
            Categoria = "Estratégico",
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
            LarguraColunas = 3,
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
            Titulo = "Lucro Líquido",
            Subtitulo = "Margem operacional de 39%",
            Tipo = BIWidgetTipo.CardKpi,
            LarguraColunas = 3,
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
            Titulo = "Ticket Médio",
            Subtitulo = "Média por transação",
            Tipo = BIWidgetTipo.CardKpi,
            LarguraColunas = 3,
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
            LarguraColunas = 3,
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
            Titulo = "Evolução do Faturamento Mensal",
            Subtitulo = "Histórico dos últimos meses",
            Tipo = BIWidgetTipo.GraficoLinhas,
            LarguraColunas = 8,
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
            Subtitulo = "Participação percentual no faturamento",
            Tipo = BIWidgetTipo.GraficoPizza,
            LarguraColunas = 4,
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
            Subtitulo = "Vendas totais do salão e delivery",
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
            Titulo = "Giro Médio de Mesas",
            Subtitulo = "Ocupações completas por mesa / dia",
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
            Titulo = "Receita por Ambiente do Salão",
            Subtitulo = "Comparativo entre salão principal, varanda e deck",
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

    private static void SanitizarDashboard(BIDashboard? d)
    {
        if (d == null) return;
        d.Nome = SanitizarTexto(d.Nome);
        d.Descricao = SanitizarTexto(d.Descricao);
        d.Categoria = SanitizarTexto(d.Categoria);
        if (d.Widgets != null)
        {
            foreach (var w in d.Widgets)
            {
                w.Titulo = SanitizarTexto(w.Titulo);
                w.Subtitulo = SanitizarTexto(w.Subtitulo);
                w.MetricaCampo = SanitizarTexto(w.MetricaCampo);
                w.DimensaoCampo = SanitizarTexto(w.DimensaoCampo);

                // Normalização de colunas para layout perfeito
                if (w.Tipo == BIWidgetTipo.CardKpi && (w.LarguraColunas == 6 || w.LarguraColunas <= 0))
                {
                    w.LarguraColunas = 3;
                }
                else if (w.Tipo == BIWidgetTipo.GraficoLinhas && (w.LarguraColunas == 6 || w.LarguraColunas <= 0))
                {
                    w.LarguraColunas = 8;
                }
                else if (w.Tipo == BIWidgetTipo.GraficoPizza && (w.LarguraColunas == 6 || w.LarguraColunas <= 0))
                {
                    w.LarguraColunas = 4;
                }
            }
        }
    }

    private static string SanitizarTexto(string? texto)
    {
        if (string.IsNullOrEmpty(texto)) return string.Empty;
        return texto
            .Replace("Ã¡", "á")
            .Replace("Ã ", "à")
            .Replace("Ã¢", "â")
            .Replace("Ã£", "ã")
            .Replace("Ã©", "é")
            .Replace("Ãª", "ê")
            .Replace("Ã­", "í")
            .Replace("Ã³", "ó")
            .Replace("Ã´", "ô")
            .Replace("Ãµ", "õ")
            .Replace("Ãº", "ú")
            .Replace("Ã§", "ç")
            .Replace("Ã‰", "É")
            .Replace("ÃŠ", "Ê")
            .Replace("Ã“", "Ó")
            .Replace("Ã”", "Ô")
            .Replace("Ãš", "Ú")
            .Replace("Ã‡", "Ç")
            .Replace("Â°", "°")
            .Replace("Âº", "º")
            .Replace("Âª", "ª")
            .Replace("nÃƒÂ£o", "não")
            .Replace("nÃÂ£o", "não")
            .Replace("CÃ³pia", "Cópia")
            .Replace("VisÃ£o", "Visão")
            .Replace("EstratÃ©gico", "Estratégico")
            .Replace("PÃºblico", "Público")
            .Replace("lÃ­quida", "líquida")
            .Replace("ConsolidaÃ§Ã£o", "Consolidação");
    }
}