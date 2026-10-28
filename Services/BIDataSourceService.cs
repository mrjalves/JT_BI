using System.Globalization;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using JT_Aplicacao.Atributos;
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
    Task<List<EntidadeModeloDto>> ObterCatalogoEntidadesAsync();
    Task<List<MOrigemDados>> ObterOrigensConfiguradasAsync();
    Task<MOrigemDados> SalvarOrigemAsync(MOrigemDados origem);
    Task<bool> ExcluirOrigemAsync(int origemId);
    Task<EdwExecucaoResultado> ExecutarCargaOrigemAsync(int origemId);
    Task<List<BIDataPoint>> ObterDadosWidgetAsync(BIWidget widget, List<BIDataset>? datasets = null);
    Task<(decimal Valor, decimal? Variacao)> ObterValorKpiAsync(BIWidget widget, List<BIDataset>? datasets = null);
    Task<List<Dictionary<string, string>>> ObterAmostraAsync(int origemId, List<string> colunas, int limite = 1000);
}

public class BIDataSourceService : IBIDataSourceService
{
    private readonly OrigemDadosService _origemDadosService;
    private readonly DwConsultaService _dwConsultaService;
    private readonly HttpClient _httpClient;
    private readonly string _pastaDados;
    private readonly string _arquivoOrigens;
    private static readonly SemaphoreSlim _lock = new(1, 1);

    private static readonly string[] Cores =
    {
        "#3b82f6", "#10b981", "#f59e0b", "#ec4899", "#8b5cf6", "#06b6d4", "#64748b"
    };

    private static readonly HashSet<string> TabelasInternasIgnoradas = new(StringComparer.OrdinalIgnoreCase)
    {
        "OrigensDados",
        "IndicadoresPainel",
        "ConfiguracaoHotkeys",
        "UserDevice",
        "AcoplamentoRegra",
        "SessaoInstrucao"
    };

    public BIDataSourceService(
        OrigemDadosService origemDadosService,
        DwConsultaService dwConsultaService,
        HttpClient httpClient,
        IWebHostEnvironment env)
    {
        _origemDadosService = origemDadosService;
        _dwConsultaService = dwConsultaService;
        _httpClient = httpClient;
        _pastaDados = Path.Combine(env.ContentRootPath, "Data");
        Directory.CreateDirectory(_pastaDados);
        _arquivoOrigens = Path.Combine(_pastaDados, "origens_edw.json");
    }

    public Task<List<EntidadeModeloDto>> ObterCatalogoEntidadesAsync()
    {
        // Garante que o assembly JT_Classes esteja carregado no AppDomain
        var jtClassesAssembly = typeof(MOrigemDados).Assembly;
        var assemblies = AppDomain.CurrentDomain.GetAssemblies()
            .Append(jtClassesAssembly)
            .Distinct()
            .ToList();

        var tipos = assemblies
            .SelectMany(a =>
            {
                try { return a.GetTypes(); }
                catch { return Array.Empty<Type>(); }
            })
            .Where(t => t.IsClass && !t.IsAbstract && t.GetCustomAttribute<NomeTabelaAttribute>() != null)
            .OrderBy(t => t.Name)
            .ToList();

        var catalogo = new List<EntidadeModeloDto>();

        foreach (var tipo in tipos)
        {
            var tabelaAttr = tipo.GetCustomAttribute<NomeTabelaAttribute>()!;
            if (string.IsNullOrWhiteSpace(tabelaAttr.Nome) || TabelasInternasIgnoradas.Contains(tabelaAttr.Nome))
                continue;

            var modulos = tipo.GetCustomAttributes<ModuloAttribute>()
                .Select(m => (int)m.Modulo)
                .ToList();

            var colunas = ExtrairColunas(tipo);
            if (colunas.Count == 0)
                continue;

            var relacoes = ExtrairRelacionamentos(tipo);

            catalogo.Add(new EntidadeModeloDto
            {
                Classe = $"{tipo.FullName}, {tipo.Assembly.GetName().Name}",
                Tabela = tabelaAttr.Nome,
                Modulos = modulos,
                Colunas = colunas,
                Relacionamentos = relacoes
            });
        }

        return Task.FromResult(catalogo
            .GroupBy(c => c.Tabela, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(c => ObterCategoriaModulo(c))
            .ThenBy(c => c.Tabela)
            .ToList());
    }

    public static string ObterCategoriaModulo(EntidadeModeloDto entidade)
    {
        var ns = entidade.Classe;
        if (ns.Contains(".Financeiro.", StringComparison.OrdinalIgnoreCase)) return "Financeiro";
        if (ns.Contains(".Restaurante.", StringComparison.OrdinalIgnoreCase) || ns.Contains(".Food.", StringComparison.OrdinalIgnoreCase)) return "Food & Restaurante";
        if (ns.Contains(".CRM.", StringComparison.OrdinalIgnoreCase) || ns.Contains(".Vendas.", StringComparison.OrdinalIgnoreCase)) return "Comercial & Vendas";
        if (ns.Contains(".Projetos.", StringComparison.OrdinalIgnoreCase)) return "Projetos";
        if (ns.Contains(".Acessos.", StringComparison.OrdinalIgnoreCase)) return "Acessos & Usuários";
        return "Cadastros Gerais";
    }

    public async Task<List<MOrigemDados>> ObterOrigensConfiguradasAsync()
    {
        var locais = await LerOrigensLocaisAsync();

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(4));
            var resp = await _httpClient.GetAsync("/api/origemdados", cts.Token);
            if (resp.IsSuccessStatusCode)
            {
                var remotas = await resp.Content.ReadFromJsonAsync<List<MOrigemDados>>(cancellationToken: cts.Token);
                if (remotas is { Count: > 0 })
                {
                    foreach (var rem in remotas)
                    {
                        var idx = locais.FindIndex(l => l.Id == rem.Id || string.Equals(l.Nome, rem.Nome, StringComparison.OrdinalIgnoreCase));
                        if (idx >= 0)
                            locais[idx] = rem;
                        else
                            locais.Add(rem);
                    }
                    await SalvarOrigensLocaisAsync(locais);
                }
            }
        }
        catch
        {
            // Usa persistência local se a rota /api/origemdados não estiver disponível na API remota
        }

        return locais.OrderBy(o => o.Nome).ToList();
    }

    public async Task<MOrigemDados> SalvarOrigemAsync(MOrigemDados origem)
    {
        var locais = await LerOrigensLocaisAsync();

        // Tenta sincronizar com a API se o endpoint existir
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(4));
            var resp = origem.Id <= 0
                ? await _httpClient.PostAsJsonAsync("/api/origemdados", origem, cts.Token)
                : await _httpClient.PutAsJsonAsync($"/api/origemdados/{origem.Id}", origem, cts.Token);

            if (resp.IsSuccessStatusCode && origem.Id <= 0)
            {
                var salvoRemoto = await resp.Content.ReadFromJsonAsync<MOrigemDados>(cancellationToken: cts.Token);
                if (salvoRemoto is { Id: > 0 })
                    origem.Id = salvoRemoto.Id;
            }
        }
        catch
        {
            // Continua salvando localmente
        }

        if (origem.Id <= 0)
        {
            origem.Id = locais.Count == 0 ? 1 : locais.Max(o => o.Id) + 1;
        }

        var idx = locais.FindIndex(o => o.Id == origem.Id);
        if (idx >= 0)
            locais[idx] = origem;
        else
            locais.Add(origem);

        await SalvarOrigensLocaisAsync(locais);
        return origem;
    }

    public async Task<bool> ExcluirOrigemAsync(int origemId)
    {
        var locais = await LerOrigensLocaisAsync();
        var removido = locais.RemoveAll(o => o.Id == origemId) > 0;
        if (removido)
        {
            await SalvarOrigensLocaisAsync(locais);
            var cacheFile = CaminhoCacheOrigem(origemId);
            if (File.Exists(cacheFile))
                File.Delete(cacheFile);
        }

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            var resp = await _httpClient.DeleteAsync($"/api/origemdados/{origemId}", cts.Token);
            if (resp.IsSuccessStatusCode)
                removido = true;
        }
        catch { }

        return removido;
    }

    public async Task<EdwExecucaoResultado> ExecutarCargaOrigemAsync(int origemId)
    {
        var origens = await ObterOrigensConfiguradasAsync();
        var origem = origens.FirstOrDefault(o => o.Id == origemId);
        if (origem == null)
        {
            return new EdwExecucaoResultado
            {
                Sucesso = false,
                OrigemId = origemId,
                Mensagem = "Fonte de dados não encontrada."
            };
        }

        // 1. Tenta carga oficial no EDW via API
        try
        {
            var resultadoEdw = await _origemDadosService.ExecutarCargaAsync(origemId);
            if (resultadoEdw.Sucesso)
            {
                origem.UltimaExecucao = resultadoEdw.DataCarregamento ?? DateTime.Now;
                origem.LinhasCarregadas = resultadoEdw.LinhasCarregadas;
                origem.UltimaMensagem = resultadoEdw.Mensagem ?? "Carga EDW concluída com sucesso.";
                await SalvarOrigemAsync(origem);
                return resultadoEdw;
            }
        }
        catch { }

        // 2. Fallback direto na rota REST padrão da entidade na API (/api/{tabela})
        var linhas = await BaixarLinhasEntidadeApiAsync(origem);
        await SalvarCacheOrigemAsync(origemId, linhas);

        origem.UltimaExecucao = DateTime.Now;
        origem.LinhasCarregadas = linhas.Count;
        origem.UltimaMensagem = linhas.Count > 0
            ? $"Conectado ({linhas.Count} registros sincronizados)"
            : "Fonte conectada (0 registros retornados pela API no momento)";

        await SalvarOrigemAsync(origem);

        return new EdwExecucaoResultado
        {
            Sucesso = true,
            OrigemId = origemId,
            OrigemNome = origem.Nome,
            TabelaDestino = origem.TabelaOrigem,
            LinhasCarregadas = linhas.Count,
            DataCarregamento = origem.UltimaExecucao,
            Mensagem = origem.UltimaMensagem
        };
    }

    public async Task<List<FonteDadosInfo>> ObterFontesDisponiveisAsync()
    {
        var origens = await ObterOrigensConfiguradasAsync();
        var fontes = new List<FonteDadosInfo>();
        var indice = 0;

        foreach (var origem in origens)
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
                    ? $"Tabela: {origem.TabelaOrigem}"
                    : origem.Descricao,
                Icone = "bi-database-fill-check",
                Cor = Cores[indice++ % Cores.Length],
                Demonstrativa = false,
                Metricas = colunas,
                Dimensoes = colunas
            });
        }

        // Mantém as fontes padrão de demonstração disponíveis para que painéis e templates funcionem sempre
        fontes.AddRange(FontesDemonstrativas());
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

        if (resultado.Sucesso && resultado.Pontos.Count > 0)
            return (resultado.Pontos.FirstOrDefault()?.Valor ?? 0, null);

        var linhasCache = await ObterLinhasCacheOuApiAsync(origemId);
        if (linhasCache.Count > 0)
        {
            return (CalcularKpiDeLinhas(linhasCache, widget.MetricaCampo, widget.Agregacao), null);
        }

        return (widget.ValorAtual ?? 0, widget.VariacaoPercentual);
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

        if (resultado.Sucesso && resultado.Pontos.Count > 0)
        {
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

        var linhasCache = await ObterLinhasCacheOuApiAsync(origemId);
        if (linhasCache.Count > 0)
        {
            return CalcularPontosDeLinhas(linhasCache, widget);
        }

        return ObterDadosDemonstrativos(widget);
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

        if (resultado.Sucesso && resultado.Linhas.Count > 0)
        {
            return resultado.Linhas
                .Select(linha => linha.ToDictionary(
                    par => par.Key,
                    par => par.Value ?? string.Empty,
                    StringComparer.OrdinalIgnoreCase))
                .ToList();
        }

        var linhasCache = await ObterLinhasCacheOuApiAsync(origemId);
        return linhasCache.Take(limite).ToList();
    }

    private async Task<List<Dictionary<string, string>>> ObterLinhasCacheOuApiAsync(int origemId)
    {
        var caminho = CaminhoCacheOrigem(origemId);
        if (File.Exists(caminho))
        {
            try
            {
                var json = await File.ReadAllTextAsync(caminho);
                var lista = JsonSerializer.Deserialize<List<Dictionary<string, string>>>(json);
                if (lista is { Count: > 0 })
                    return lista;
            }
            catch { }
        }

        var origens = await LerOrigensLocaisAsync();
        var origem = origens.FirstOrDefault(o => o.Id == origemId);
        if (origem == null)
            return new List<Dictionary<string, string>>();

        var baixadas = await BaixarLinhasEntidadeApiAsync(origem);
        if (baixadas.Count > 0)
            await SalvarCacheOrigemAsync(origemId, baixadas);

        return baixadas;
    }

    private async Task<List<Dictionary<string, string>>> BaixarLinhasEntidadeApiAsync(MOrigemDados origem)
    {
        var resultado = new List<Dictionary<string, string>>();
        if (string.IsNullOrWhiteSpace(origem.TabelaOrigem))
            return resultado;

        var rotasCandidatas = new[]
        {
            $"/api/{origem.TabelaOrigem.ToLowerInvariant()}",
            $"/api/{BIOrigemFactory.NomeSimples(origem.ClasseOrigem).ToLowerInvariant()}"
        }.Distinct();

        foreach (var rota in rotasCandidatas)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                var resp = await _httpClient.GetAsync(rota, cts.Token);
                if (!resp.IsSuccessStatusCode)
                    continue;

                var json = await resp.Content.ReadAsStringAsync(cts.Token);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (var item in doc.RootElement.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object)
                        continue;

                    var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var prop in item.EnumerateObject())
                    {
                        if (prop.Value.ValueKind == JsonValueKind.Object || prop.Value.ValueKind == JsonValueKind.Array)
                            continue;

                        dict[prop.Name] = prop.Value.ToString();
                    }
                    resultado.Add(dict);
                }

                if (resultado.Count > 0)
                    break;
            }
            catch { }
        }

        return resultado;
    }

    private static decimal CalcularKpiDeLinhas(List<Dictionary<string, string>> linhas, string metrica, string? agregacao)
    {
        if (string.Equals(agregacao, "Contagem", StringComparison.OrdinalIgnoreCase))
            return linhas.Count;

        var valores = new List<decimal>();
        foreach (var row in linhas)
        {
            if (row.TryGetValue(metrica, out var str) &&
                decimal.TryParse(str, NumberStyles.Any, CultureInfo.InvariantCulture, out var num))
            {
                valores.Add(num);
            }
        }

        if (valores.Count == 0)
            return linhas.Count;

        return agregacao?.Trim().ToLowerInvariant() switch
        {
            "media" or "média" => Math.Round(valores.Average(), 2),
            "max" or "máximo" or "maximo" => valores.Max(),
            "min" or "mínimo" or "minimo" => valores.Min(),
            _ => valores.Sum()
        };
    }

    private static List<BIDataPoint> CalcularPontosDeLinhas(List<Dictionary<string, string>> linhas, BIWidget widget)
    {
        var agrupado = new Dictionary<string, List<decimal>>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in linhas)
        {
            var rotulo = !string.IsNullOrWhiteSpace(widget.DimensaoCampo) && row.TryGetValue(widget.DimensaoCampo, out var dVal) && !string.IsNullOrWhiteSpace(dVal)
                ? dVal
                : "Total";

            decimal valor = 1m;
            if (!string.IsNullOrWhiteSpace(widget.MetricaCampo) &&
                row.TryGetValue(widget.MetricaCampo, out var mVal) &&
                decimal.TryParse(mVal, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
            {
                valor = parsed;
            }

            if (!agrupado.TryGetValue(rotulo, out var lista))
            {
                lista = new List<decimal>();
                agrupado[rotulo] = lista;
            }
            lista.Add(valor);
        }

        var pares = agrupado.Select(kv =>
        {
            var v = widget.Agregacao?.Trim().ToLowerInvariant() switch
            {
                "media" or "média" => kv.Value.Count > 0 ? Math.Round(kv.Value.Average(), 2) : 0,
                "contagem" => kv.Value.Count,
                "max" or "máximo" or "maximo" => kv.Value.Count > 0 ? kv.Value.Max() : 0,
                "min" or "mínimo" or "minimo" => kv.Value.Count > 0 ? kv.Value.Min() : 0,
                _ => kv.Value.Sum()
            };
            return (Rotulo: kv.Key, Valor: v);
        })
        .OrderByDescending(x => x.Valor)
        .Take(12)
        .ToList();

        var total = pares.Sum(p => p.Valor);
        var pontos = new List<BIDataPoint>();
        for (var i = 0; i < pares.Count; i++)
        {
            pontos.Add(new BIDataPoint
            {
                Rotulo = pares[i].Rotulo,
                Valor = pares[i].Valor,
                ValorFormatado = FormatarValor(pares[i].Valor, widget.FormatoValor),
                Cor = Cores[i % Cores.Length],
                Percentual = total == 0 ? 0 : Math.Round(pares[i].Valor / total * 100, 2)
            });
        }

        return pontos;
    }

    private string CaminhoCacheOrigem(int origemId)
        => Path.Combine(_pastaDados, $"cache_origem_{origemId}.json");

    private async Task SalvarCacheOrigemAsync(int origemId, List<Dictionary<string, string>> linhas)
    {
        try
        {
            var json = JsonSerializer.Serialize(linhas);
            await File.WriteAllTextAsync(CaminhoCacheOrigem(origemId), json);
        }
        catch { }
    }

    private async Task<List<MOrigemDados>> LerOrigensLocaisAsync()
    {
        await _lock.WaitAsync();
        try
        {
            if (!File.Exists(_arquivoOrigens))
                return new List<MOrigemDados>();

            var json = await File.ReadAllTextAsync(_arquivoOrigens);
            return JsonSerializer.Deserialize<List<MOrigemDados>>(json) ?? new List<MOrigemDados>();
        }
        catch
        {
            return new List<MOrigemDados>();
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task SalvarOrigensLocaisAsync(List<MOrigemDados> origens)
    {
        await _lock.WaitAsync();
        try
        {
            var json = JsonSerializer.Serialize(origens, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(_arquivoOrigens, json);
        }
        finally
        {
            _lock.Release();
        }
    }

    private static List<ColunaModeloDto> ExtrairColunas(Type tipo)
    {
        var props = tipo.GetProperties(BindingFlags.Instance | BindingFlags.Public);
        var colunas = new List<ColunaModeloDto>();

        foreach (var p in props)
        {
            var colAttr = p.GetCustomAttribute<NomeColunaAttribute>();
            var pkAttr = p.GetCustomAttribute<ChavePrimariaAttribute>();
            var fkAttr = p.GetCustomAttribute<ChaveEstrangeiraAttribute>();
            if (colAttr == null && pkAttr == null)
                continue;

            var nomeColuna = colAttr?.Nome?.Trim() ?? p.Name;
            if (colunas.Any(c => string.Equals(c.NomeColuna, nomeColuna, StringComparison.OrdinalIgnoreCase)))
                continue;

            var clrType = Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType;
            colunas.Add(new ColunaModeloDto
            {
                Propriedade = p.Name,
                NomeColuna = nomeColuna,
                TipoClr = clrType.Name,
                EhPk = pkAttr != null,
                EhFk = fkAttr != null,
                Referencia = fkAttr?.Referencia
            });
        }

        return colunas;
    }

    private static List<RelacionamentoModeloDto> ExtrairRelacionamentos(Type tipo)
    {
        var relacoes = new List<RelacionamentoModeloDto>();
        var props = tipo.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);

        foreach (var p in props)
        {
            var rel = p.GetCustomAttribute<RelacionamentoAttribute>();
            if (rel == null)
                continue;

            Type? tipoAlvo = null;
            if (rel.TipoRelacionamento == TipoRelacionamento.Agregacao)
            {
                tipoAlvo = p.PropertyType;
            }
            else if (rel.TipoRelacionamento == TipoRelacionamento.Composicao)
            {
                tipoAlvo = p.PropertyType.IsGenericType
                    ? p.PropertyType.GetGenericArguments().FirstOrDefault()
                    : p.PropertyType.GetElementType();
            }

            if (tipoAlvo == null)
                continue;

            var tabelaAlvo = tipoAlvo.GetCustomAttribute<NomeTabelaAttribute>()?.Nome ?? tipoAlvo.Name;
            relacoes.Add(new RelacionamentoModeloDto
            {
                Propriedade = p.Name,
                TipoRelacionamento = rel.TipoRelacionamento.ToString(),
                PropriedadeChaveEstrangeira = rel.PropriedadeChaveEstrangeira,
                ClasseAlvo = $"{tipoAlvo.FullName}, {tipoAlvo.Assembly.GetName().Name}",
                TabelaAlvo = tabelaAlvo,
                Colunas = ExtrairColunas(tipoAlvo)
            });
        }

        return relacoes;
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
                Nome = "Módulo Financeiro & Tesouraria",
                Descricao = "Indicadores padrão de Faturamento, Despesas, Fluxo de Caixa e Contas.",
                Icone = "bi-cash-coin",
                Cor = "success",
                Demonstrativa = true,
                Metricas = new List<string> { "Faturamento", "Despesas", "Lucro Liquido", "Inadimplencia", "Saldo Caixa" },
                Dimensoes = new List<string> { "Mes", "Centro de Custo", "Forma Pagamento", "Conta Bancaria" }
            },
            new FonteDadosInfo
            {
                Id = "food",
                Nome = "Módulo Food & Restaurante",
                Descricao = "Indicadores padrão de Comandas, Mesas, Cardápio e Giro de Atendimento.",
                Icone = "bi-cup-straw",
                Cor = "warning",
                Demonstrativa = true,
                Metricas = new List<string> { "Faturamento Restaurante", "Ticket Medio", "Qtd Pedidos", "Giro Mesas", "Tempo Medio Atendimento" },
                Dimensoes = new List<string> { "Categoria Cardapio", "Ambiente Salao", "Turno / Horario", "Garcom / Atendente" }
            },
            new FonteDadosInfo
            {
                Id = "vendas",
                Nome = "Módulo Comercial & CRM",
                Descricao = "Indicadores padrão de Funil de Vendas, Leads, Conversão e Clientes.",
                Icone = "bi-graph-up-arrow",
                Cor = "primary",
                Demonstrativa = true,
                Metricas = new List<string> { "Novos Leads", "Taxa Conversao", "Vendas Fechadas", "Valor Pipeline" },
                Dimensoes = new List<string> { "Canal Aquisicao", "Vendedor", "Status Negocio", "Segmento Cliente" }
            },
            new FonteDadosInfo
            {
                Id = "estoque",
                Nome = "Módulo Estoque & Suprimentos",
                Descricao = "Indicadores padrão de Insumos, Giro de Estoque, Lotes e Compras.",
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
