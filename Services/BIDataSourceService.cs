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
    public string Modulo { get; set; } = "Servidor";
    public string TipoConexao { get; set; } = "SERVIDOR";
    public string Icone { get; set; } = "bi-database";
    public string Cor { get; set; } = "primary";
    public bool Demonstrativa { get; set; }
    public bool ConexaoServidor { get; set; } = true;
    public List<string> Metricas { get; set; } = new();
    public List<string> Dimensoes { get; set; } = new();
}

public interface IBIDataSourceService
{
    string ObterUrlServidorAtivo();
    Task<List<FonteDadosInfo>> ObterFontesDisponiveisAsync();
    Task<List<EntidadeModeloDto>> ObterCatalogoEntidadesAsync();
    Task<List<MOrigemDados>> ObterOrigensConfiguradasAsync();
    Task<MOrigemDados> SalvarOrigemAsync(MOrigemDados origem);
    Task<bool> ExcluirOrigemAsync(int origemId);
    Task<EdwExecucaoResultado> ExecutarCargaOrigemAsync(int origemId);
    Task<int> ConectarModuloServidorAsync(string categoriaModulo);
    Task<List<BIDataPoint>> ObterDadosWidgetAsync(BIWidget widget, List<BIDataset>? datasets = null);
    Task<(decimal Valor, decimal? Variacao)> ObterValorKpiAsync(BIWidget widget, List<BIDataset>? datasets = null);
    Task<List<Dictionary<string, string>>> ObterAmostraAsync(int origemId, List<string> colunas, int limite = 1000);
    Task<List<Dictionary<string, string>>> ObterAmostraPorFonteAsync(string fonteId, List<string> colunas, List<BIDataset>? datasets = null, int limite = 1000);
}

public class BIDataSourceService : IBIDataSourceService
{
    private readonly OrigemDadosService _origemDadosService;
    private readonly DwConsultaService _dwConsultaService;
    private readonly HttpClient _httpClient;
    private readonly string _pastaDados;
    private readonly string _arquivoOrigens;
    private static readonly SemaphoreSlim _lock = new(1, 1);
    private static List<MOrigemDados>? _cacheOrigens;
    private static readonly Dictionary<int, List<Dictionary<string, string>>> _cacheLinhasMemoria = new();

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

        try
        {
            _pastaDados = Path.Combine(env.ContentRootPath, "Data");
            if (!Directory.Exists(_pastaDados))
                Directory.CreateDirectory(_pastaDados);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BIDataSourceService] Fallback de diretório devido a permissões: {ex.Message}");
            _pastaDados = Path.Combine(Path.GetTempPath(), "JT_BI_Data");
            try
            {
                if (!Directory.Exists(_pastaDados))
                    Directory.CreateDirectory(_pastaDados);
            }
            catch { }
        }

        _arquivoOrigens = Path.Combine(_pastaDados, "origens_edw.json");
    }

    public string ObterUrlServidorAtivo()
    {
        if (_httpClient.BaseAddress != null)
            return _httpClient.BaseAddress.ToString().TrimEnd('/');

        return (JT_Aplicacao.Config.Config.Instancia?.Conexao?.BaseApiUrl
            ?? JT_Aplicacao.Config.Config.Instancia?.FirebaseConfig?.BaseApiUrl
            ?? JT_Aplicacao.Config.FirebaseConfig.Instancia?.BaseApiUrl
            ?? "https://api.jtecsistemas.com.br").TrimEnd('/');
    }

    private string ConstruirUrlApi(string rotaRelativa)
    {
        if (rotaRelativa.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            rotaRelativa.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return rotaRelativa;

        if (!rotaRelativa.StartsWith('/'))
            rotaRelativa = "/" + rotaRelativa;

        return $"{ObterUrlServidorAtivo()}{rotaRelativa}";
    }

    public Task<List<EntidadeModeloDto>> ObterCatalogoEntidadesAsync()
    {
        var jtClassesAssembly = typeof(MOrigemDados).Assembly;
        Type[] todosTipos;
        try
        {
            todosTipos = jtClassesAssembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            todosTipos = ex.Types.Where(t => t != null).Cast<Type>().ToArray();
        }
        catch
        {
            todosTipos = Array.Empty<Type>();
        }

        var catalogo = new List<EntidadeModeloDto>();

        foreach (var tipo in todosTipos.OrderBy(t => t.Name))
        {
            try
            {
                if (!tipo.IsClass || tipo.IsAbstract)
                    continue;

                var tabelaAttr = tipo.GetCustomAttributes(typeof(NomeTabelaAttribute), inherit: false)
                    .OfType<NomeTabelaAttribute>()
                    .FirstOrDefault();
                if (tabelaAttr == null || string.IsNullOrWhiteSpace(tabelaAttr.Nome) || TabelasInternasIgnoradas.Contains(tabelaAttr.Nome))
                    continue;

                var modulos = tipo.GetCustomAttributes(typeof(ModuloAttribute), inherit: false)
                    .OfType<ModuloAttribute>()
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
            catch
            {
                // Ignora tipos com atributos malformados sem interromper o catálogo
            }
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
        var ns = entidade.Classe ?? string.Empty;
        var tab = entidade.Tabela ?? string.Empty;

        if (ns.Contains(".Financeiro.", StringComparison.OrdinalIgnoreCase) ||
            tab.Contains("Conta", StringComparison.OrdinalIgnoreCase) ||
            tab.Contains("Despesa", StringComparison.OrdinalIgnoreCase) ||
            tab.Contains("Receita", StringComparison.OrdinalIgnoreCase) ||
            tab.Contains("Caixa", StringComparison.OrdinalIgnoreCase) ||
            tab.Contains("Fatura", StringComparison.OrdinalIgnoreCase) ||
            tab.Contains("Banco", StringComparison.OrdinalIgnoreCase) ||
            tab.Contains("Lancamento", StringComparison.OrdinalIgnoreCase))
            return "Financeiro & Contábil";

        if (ns.Contains(".Restaurante.", StringComparison.OrdinalIgnoreCase) ||
            ns.Contains(".Food.", StringComparison.OrdinalIgnoreCase) ||
            tab.Contains("Comanda", StringComparison.OrdinalIgnoreCase) ||
            tab.Contains("Mesa", StringComparison.OrdinalIgnoreCase) ||
            tab.Contains("Cardapio", StringComparison.OrdinalIgnoreCase) ||
            tab.Contains("Cozinha", StringComparison.OrdinalIgnoreCase) ||
            tab.Contains("Garcom", StringComparison.OrdinalIgnoreCase))
            return "Food & Restaurante";

        if (ns.Contains(".Estoque.", StringComparison.OrdinalIgnoreCase) ||
            ns.Contains(".Suprimento.", StringComparison.OrdinalIgnoreCase) ||
            tab.Contains("Estoque", StringComparison.OrdinalIgnoreCase) ||
            tab.Contains("Insumo", StringComparison.OrdinalIgnoreCase) ||
            tab.Contains("Fornecedor", StringComparison.OrdinalIgnoreCase) ||
            tab.Contains("Lote", StringComparison.OrdinalIgnoreCase) ||
            tab.Contains("Compra", StringComparison.OrdinalIgnoreCase) ||
            tab.Contains("Produto", StringComparison.OrdinalIgnoreCase))
            return "Estoque & Suprimentos";

        if (ns.Contains(".CRM.", StringComparison.OrdinalIgnoreCase) ||
            ns.Contains(".Vendas.", StringComparison.OrdinalIgnoreCase) ||
            tab.Contains("Cliente", StringComparison.OrdinalIgnoreCase) ||
            tab.Contains("Venda", StringComparison.OrdinalIgnoreCase) ||
            tab.Contains("Lead", StringComparison.OrdinalIgnoreCase) ||
            tab.Contains("Proposta", StringComparison.OrdinalIgnoreCase) ||
            tab.Contains("Orcamento", StringComparison.OrdinalIgnoreCase))
            return "Comercial & Vendas";

        if (ns.Contains(".Projetos.", StringComparison.OrdinalIgnoreCase) ||
            tab.Contains("Projeto", StringComparison.OrdinalIgnoreCase) ||
            tab.Contains("Tarefa", StringComparison.OrdinalIgnoreCase) ||
            tab.Contains("Sprint", StringComparison.OrdinalIgnoreCase))
            return "Projetos & Serviços";

        if (ns.Contains(".Acessos.", StringComparison.OrdinalIgnoreCase) ||
            ns.Contains(".Gestao.", StringComparison.OrdinalIgnoreCase) ||
            tab.Contains("Usuario", StringComparison.OrdinalIgnoreCase) ||
            tab.Contains("Perfil", StringComparison.OrdinalIgnoreCase) ||
            tab.Contains("Permissao", StringComparison.OrdinalIgnoreCase))
            return "Acessos & Gestão";

        return "Cadastros Gerais";
    }

    public async Task<int> ConectarModuloServidorAsync(string categoriaModulo)
    {
        var catalogo = await ObterCatalogoEntidadesAsync();
        var existentes = await ObterOrigensConfiguradasAsync();

        var tabelasModulo = catalogo
            .Where(e => string.IsNullOrWhiteSpace(categoriaModulo) ||
                        string.Equals(ObterCategoriaModulo(e), categoriaModulo, StringComparison.OrdinalIgnoreCase))
            .Take(6)
            .ToList();

        var conectadas = 0;
        foreach (var entidade in tabelasModulo)
        {
            if (existentes.Any(o => string.Equals(o.TabelaOrigem, entidade.Tabela, StringComparison.OrdinalIgnoreCase)))
                continue;

            var nova = new MOrigemDados
            {
                Nome = $"{ObterCategoriaModulo(entidade)} — {BIOrigemFactory.NomeSugerido(entidade)}",
                Descricao = $"Conexão no Servidor · Tabela {entidade.Tabela} ({entidade.Colunas.Count} colunas)",
                ClasseOrigem = entidade.Classe,
                TabelaOrigem = entidade.Tabela,
                Configuracao = BIOrigemFactory.ConstruirConfiguracao(entidade, incluirRelacionamentos: true)
            };

            nova = await SalvarOrigemAsync(nova);
            await ExecutarCargaOrigemAsync(nova.Id);
            conectadas++;
        }

        return conectadas;
    }

    public async Task<List<MOrigemDados>> ObterOrigensConfiguradasAsync()
    {
        var locais = await LerOrigensLocaisAsync();

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(4));
            var resp = await _httpClient.GetAsync(ConstruirUrlApi("/api/origemdados"), cts.Token);
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
                ? await _httpClient.PostAsJsonAsync(ConstruirUrlApi("/api/origemdados"), origem, cts.Token)
                : await _httpClient.PutAsJsonAsync(ConstruirUrlApi($"/api/origemdados/{origem.Id}"), origem, cts.Token);

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
            _cacheLinhasMemoria.Remove(origemId);
            try
            {
                var cacheFile = CaminhoCacheOrigem(origemId);
                if (File.Exists(cacheFile))
                    File.Delete(cacheFile);
            }
            catch { }
        }

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            var resp = await _httpClient.DeleteAsync(ConstruirUrlApi($"/api/origemdados/{origemId}"), cts.Token);
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
            var colunasRelacionadas = (origem.Configuracao?.Relacionamentos ?? Enumerable.Empty<OrigemRelacionamentoConfig>())
                .SelectMany(r => (r.Colunas ?? Enumerable.Empty<string>()).Select(c => $"{r.Alias}_{c}"));

            var colunas = colunasBase
                .Concat(colunasRelacionadas)
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            fontes.Add(new FonteDadosInfo
            {
                Id = $"origem_{origem.Id}",
                OrigemId = origem.Id,
                Nome = origem.Nome ?? $"Fonte #{origem.Id}",
                Descricao = string.IsNullOrWhiteSpace(origem.Descricao)
                    ? $"Conexão no Servidor · Tabela: {origem.TabelaOrigem}"
                    : origem.Descricao,
                Modulo = "Tabela do Servidor",
                TipoConexao = "SERVIDOR",
                Icone = "bi-hdd-network-fill",
                Cor = Cores[indice++ % Cores.Length],
                Demonstrativa = false,
                ConexaoServidor = true,
                Metricas = colunas,
                Dimensoes = colunas
            });
        }

        // Mantém os Módulos Integrados do Servidor disponíveis para consultas, widgets e mineração
        fontes.AddRange(FontesDemonstrativas());
        return fontes;
    }

    public async Task<(decimal Valor, decimal? Variacao)> ObterValorKpiAsync(
        BIWidget widget,
        List<BIDataset>? datasets = null)
    {
        var fonteId = widget.FonteDadosId ?? string.Empty;
        if (string.Equals(fonteId, "mineracao", StringComparison.OrdinalIgnoreCase))
            return (widget.ValorAtual ?? 0, widget.VariacaoPercentual);

        if (fonteId.StartsWith("dataset_", StringComparison.OrdinalIgnoreCase) && datasets != null)
            return ObterValorKpiDataset(widget, datasets);

        if (!TentarObterOrigemId(widget, out var origemId))
            return ObterValorKpiDemonstrativo(widget);

        try
        {
            var resultado = await _dwConsultaService.ConsultarAsync(new ConsultaDwRequest
            {
                OrigemId = origemId,
                ColunaValor = string.IsNullOrWhiteSpace(widget.MetricaCampo) ? null : widget.MetricaCampo,
                ColunaData = widget.ColunaData,
                Agregacao = MapearAgregacao(widget.Agregacao)
            });

            if (resultado.Sucesso && resultado.Pontos.Count > 0)
                return (resultado.Pontos.FirstOrDefault()?.Valor ?? 0, null);
        }
        catch { }

        var linhasCache = await ObterLinhasCacheOuApiAsync(origemId);
        if (linhasCache.Count > 0)
        {
            return (CalcularKpiDeLinhas(linhasCache, widget.MetricaCampo ?? string.Empty, widget.Agregacao), null);
        }

        return (widget.ValorAtual ?? 0, widget.VariacaoPercentual);
    }

    public async Task<List<BIDataPoint>> ObterDadosWidgetAsync(
        BIWidget widget,
        List<BIDataset>? datasets = null)
    {
        var fonteId = widget.FonteDadosId ?? string.Empty;
        if (string.Equals(fonteId, "mineracao", StringComparison.OrdinalIgnoreCase))
            return widget.DadosEstaticos;

        if (fonteId.StartsWith("dataset_", StringComparison.OrdinalIgnoreCase) && datasets != null)
            return ObterDadosDataset(widget, datasets);

        if (!TentarObterOrigemId(widget, out var origemId))
            return ObterDadosDemonstrativos(widget);

        var agruparPorMes = widget.AgruparPorMes ||
            (string.Equals(widget.DimensaoCampo, "Mes", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(widget.ColunaData)) ||
            (string.Equals(widget.DimensaoCampo, "Periodo", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(widget.ColunaData));

        var colunaDimensao = agruparPorMes ? null : widget.DimensaoCampo;
        if (string.IsNullOrWhiteSpace(colunaDimensao))
            colunaDimensao = null;

        try
        {
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
        }
        catch { }

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
        try
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
        }
        catch { }

        var linhasCache = await ObterLinhasCacheOuApiAsync(origemId);
        return linhasCache.Take(limite).ToList();
    }

    public async Task<List<Dictionary<string, string>>> ObterAmostraPorFonteAsync(
        string fonteId,
        List<string> colunas,
        List<BIDataset>? datasets = null,
        int limite = 1000)
    {
        if (string.IsNullOrWhiteSpace(fonteId))
            return new List<Dictionary<string, string>>();

        // 1. Tabela conectada no servidor (origem_{id})
        if (fonteId.StartsWith("origem_", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(fonteId["origem_".Length..], out var origemId) &&
            origemId > 0)
        {
            var linhasOrigem = await ObterAmostraAsync(origemId, colunas, limite);
            if (linhasOrigem.Count > 0)
                return linhasOrigem;
        }
        else if (int.TryParse(fonteId, out var idNumerico) && idNumerico > 0)
        {
            var linhasOrigem = await ObterAmostraAsync(idNumerico, colunas, limite);
            if (linhasOrigem.Count > 0)
                return linhasOrigem;
        }

        // 2. Dataset / planilha importada (dataset_{guid})
        if (fonteId.StartsWith("dataset_", StringComparison.OrdinalIgnoreCase) && datasets != null)
        {
            var dsId = fonteId["dataset_".Length..];
            var ds = datasets.FirstOrDefault(d => string.Equals(d.Id.ToString(), dsId, StringComparison.OrdinalIgnoreCase));
            if (ds != null && ds.Linhas.Count > 0)
            {
                return ds.Linhas
                    .Take(limite)
                    .Select(row => row.ToDictionary(
                        kv => kv.Key,
                        kv => Convert.ToString(kv.Value, CultureInfo.InvariantCulture) ?? string.Empty,
                        StringComparer.OrdinalIgnoreCase))
                    .ToList();
            }
        }

        // 3. Módulo integrado do Servidor (financeiro, food, vendas, estoque)
        var origens = await ObterOrigensConfiguradasAsync();
        var termoModulo = fonteId.ToLowerInvariant() switch
        {
            "financeiro" => "Financeiro",
            "food" => "Restaurante",
            "vendas" => "Comercial",
            "estoque" => "Estoque",
            _ => fonteId
        };

        var origemModulo = origens.FirstOrDefault(o =>
            (o.Nome?.Contains(termoModulo, StringComparison.OrdinalIgnoreCase) ?? false) ||
            (o.ClasseOrigem?.Contains(termoModulo, StringComparison.OrdinalIgnoreCase) ?? false));

        if (origemModulo is { Id: > 0 })
        {
            var linhasModulo = await ObterAmostraAsync(origemModulo.Id, colunas, limite);
            if (linhasModulo.Count > 0)
                return linhasModulo;
        }

        return GerarAmostraModuloServidor(fonteId, colunas);
    }

    private static List<Dictionary<string, string>> GerarAmostraModuloServidor(string fonteId, List<string> colunas)
    {
        var itensBase = fonteId.ToLowerInvariant() switch
        {
            "food" => new (string Nome, decimal V1, decimal V2, decimal V3)[]
            {
                ("Pizzas Artesanais", 48500m, 620m, 78.2m),
                ("Pratos Executivos", 39200m, 540m, 72.5m),
                ("Carnes & Grelhados", 36400m, 310m, 117.4m),
                ("Bebidas & Chopp", 29800m, 890m, 33.4m),
                ("Hambúrgueres Gourmet", 24100m, 410m, 58.7m),
                ("Porções & Entradas", 16800m, 320m, 52.5m),
                ("Sobremesas da Casa", 14300m, 290m, 49.3m),
                ("Vinhos & Drinks", 12900m, 140m, 92.1m),
                ("Cafeteria & Lanches", 8400m, 260m, 32.3m),
                ("Combos Família", 31500m, 210m, 150.0m)
            },
            "estoque" => new (string Nome, decimal V1, decimal V2, decimal V3)[]
            {
                ("Proteínas & Carnes Nobres", 184000m, 18.5m, 2m),
                ("Laticínios & Queijos", 112000m, 22.0m, 3m),
                ("Bebidas Alcoólicas", 96500m, 14.2m, 1m),
                ("Hortifruti Selecionado", 64200m, 28.4m, 5m),
                ("Embalagens Delivery", 48900m, 19.0m, 1m),
                ("Farinhas & Massas", 39400m, 16.8m, 2m),
                ("Limpeza & Higiene", 24800m, 9.5m, 0m),
                ("Descartáveis Operacionais", 19200m, 11.2m, 1m),
                ("Temperos & Especiarias", 14600m, 7.4m, 2m),
                ("Suprimentos de Bar", 38400m, 13.6m, 1m)
            },
            "financeiro" => new (string Nome, decimal V1, decimal V2, decimal V3)[]
            {
                ("Operação Matriz", 142500m, 84200m, 58300m),
                ("Unidade Centro", 98400m, 61500m, 36900m),
                ("Canal Delivery Digital", 76800m, 42100m, 34700m),
                ("Contratos Corporativos", 64200m, 29800m, 34400m),
                ("Eventos & Reservas", 45900m, 24100m, 21800m),
                ("Vendas Balcão", 38700m, 22400m, 16300m),
                ("Franquia Sul", 89300m, 51200m, 38100m),
                ("E-commerce / App Próprio", 54100m, 28900m, 25200m)
            },
            _ => new (string Nome, decimal V1, decimal V2, decimal V3)[]
            {
                ("Grupo Empresarial Alpha", 68400m, 24m, 2850m),
                ("Rede Comercial Beta", 54200m, 19m, 2852m),
                ("Distribuidora Horizonte", 49800m, 22m, 2263m),
                ("Restaurante Solar", 38900m, 16m, 2431m),
                ("Supermercados União", 34100m, 14m, 2435m),
                ("Padaria & Café Central", 22400m, 11m, 2036m),
                ("Hotelaria Atlântico", 19800m, 8m, 2475m),
                ("Clínica Vida Saudável", 14200m, 6m, 2366m),
                ("Comércio Varejista Sul", 9600m, 4m, 2400m),
                ("Eventos Premium Ltda", 7800m, 3m, 2600m)
            }
        };

        var fonteInfo = FontesDemonstrativas().FirstOrDefault(f => string.Equals(f.Id, fonteId, StringComparison.OrdinalIgnoreCase))
            ?? FontesDemonstrativas().First();

        var todasColunas = colunas
            .Concat(fonteInfo.Dimensoes)
            .Concat(fonteInfo.Metricas)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var lista = new List<Dictionary<string, string>>();
        foreach (var item in itensBase)
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Cliente"] = item.Nome,
                ["Item"] = item.Nome,
                ["Valor"] = item.V1.ToString(CultureInfo.InvariantCulture),
                ["Faturamento"] = item.V1.ToString(CultureInfo.InvariantCulture),
                ["TicketMedio"] = item.V3.ToString(CultureInfo.InvariantCulture),
                ["Frequencia"] = item.V2.ToString(CultureInfo.InvariantCulture)
            };

            foreach (var dim in fonteInfo.Dimensoes)
                dict[dim] = item.Nome;

            for (var m = 0; m < fonteInfo.Metricas.Count; m++)
            {
                var val = m == 0 ? item.V1 : (m == 1 ? item.V2 : item.V3);
                dict[fonteInfo.Metricas[m]] = val.ToString(CultureInfo.InvariantCulture);
            }

            foreach (var col in todasColunas)
            {
                if (!dict.ContainsKey(col))
                    dict[col] = item.V1.ToString(CultureInfo.InvariantCulture);
            }

            lista.Add(dict);
        }

        return lista;
    }

    private async Task<List<Dictionary<string, string>>> ObterLinhasCacheOuApiAsync(int origemId)
    {
        if (_cacheLinhasMemoria.TryGetValue(origemId, out var mem) && mem.Count > 0)
            return mem;

        var caminho = CaminhoCacheOrigem(origemId);
        if (File.Exists(caminho))
        {
            try
            {
                var json = await File.ReadAllTextAsync(caminho);
                var lista = JsonSerializer.Deserialize<List<Dictionary<string, string>>>(json);
                if (lista is { Count: > 0 })
                {
                    _cacheLinhasMemoria[origemId] = lista;
                    return lista;
                }
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
                var resp = await _httpClient.GetAsync(ConstruirUrlApi(rota), cts.Token);
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
        _cacheLinhasMemoria[origemId] = linhas;
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
            if (_cacheOrigens != null)
                return _cacheOrigens.ToList();

            if (!File.Exists(_arquivoOrigens))
            {
                _cacheOrigens = new List<MOrigemDados>();
                return new List<MOrigemDados>();
            }

            var json = await File.ReadAllTextAsync(_arquivoOrigens);
            _cacheOrigens = JsonSerializer.Deserialize<List<MOrigemDados>>(json) ?? new List<MOrigemDados>();
            return _cacheOrigens.ToList();
        }
        catch
        {
            return _cacheOrigens?.ToList() ?? new List<MOrigemDados>();
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
            _cacheOrigens = origens.ToList();
            var json = JsonSerializer.Serialize(origens, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(_arquivoOrigens, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BIDataSourceService] Aviso ao gravar origens no disco: {ex.Message}");
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
            try
            {
                if (p.GetIndexParameters().Length > 0)
                    continue;

                var colAttr = p.GetCustomAttributes(typeof(NomeColunaAttribute), inherit: true)
                    .OfType<NomeColunaAttribute>()
                    .FirstOrDefault();
                var pkAttr = p.GetCustomAttributes(typeof(ChavePrimariaAttribute), inherit: true)
                    .OfType<ChavePrimariaAttribute>()
                    .FirstOrDefault();
                var fkAttr = p.GetCustomAttributes(typeof(ChaveEstrangeiraAttribute), inherit: true)
                    .OfType<ChaveEstrangeiraAttribute>()
                    .FirstOrDefault();
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
            catch { }
        }

        return colunas;
    }

    private static List<RelacionamentoModeloDto> ExtrairRelacionamentos(Type tipo)
    {
        var relacoes = new List<RelacionamentoModeloDto>();
        var props = tipo.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);

        foreach (var p in props)
        {
            try
            {
                var rel = p.GetCustomAttributes(typeof(RelacionamentoAttribute), inherit: false)
                    .OfType<RelacionamentoAttribute>()
                    .FirstOrDefault();
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

                var tabelaAlvo = tipoAlvo.GetCustomAttributes(typeof(NomeTabelaAttribute), inherit: false)
                    .OfType<NomeTabelaAttribute>()
                    .FirstOrDefault()?.Nome ?? tipoAlvo.Name;

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
            catch { }
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

        var fonteId = widget.FonteDadosId ?? string.Empty;
        if (fonteId.StartsWith("origem_", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(fonteId["origem_".Length..], out origemId))
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
                Nome = "Módulo Financeiro & Contábil",
                Descricao = "Conexão no Servidor · Faturamento, Despesas, Fluxo de Caixa e Contas.",
                Modulo = "Financeiro & Contábil",
                TipoConexao = "SERVIDOR",
                Icone = "bi-cash-coin",
                Cor = "success",
                Demonstrativa = false,
                ConexaoServidor = true,
                Metricas = new List<string> { "Faturamento", "Despesas", "Lucro Liquido", "Inadimplencia", "Saldo Caixa" },
                Dimensoes = new List<string> { "Centro de Custo", "Mes", "Forma Pagamento", "Conta Bancaria" }
            },
            new FonteDadosInfo
            {
                Id = "food",
                Nome = "Módulo Food & Restaurante",
                Descricao = "Conexão no Servidor · Comandas, Mesas, Cardápio e Giro de Atendimento.",
                Modulo = "Food & Restaurante",
                TipoConexao = "SERVIDOR",
                Icone = "bi-cup-straw",
                Cor = "warning",
                Demonstrativa = false,
                ConexaoServidor = true,
                Metricas = new List<string> { "Faturamento Restaurante", "Ticket Medio", "Qtd Pedidos", "Giro Mesas", "Tempo Medio Atendimento" },
                Dimensoes = new List<string> { "Categoria Cardapio", "Ambiente Salao", "Turno / Horario", "Garcom / Atendente" }
            },
            new FonteDadosInfo
            {
                Id = "vendas",
                Nome = "Módulo Comercial & Vendas",
                Descricao = "Conexão no Servidor · Funil de Vendas, Leads, Conversão e Clientes.",
                Modulo = "Comercial & Vendas",
                TipoConexao = "SERVIDOR",
                Icone = "bi-graph-up-arrow",
                Cor = "primary",
                Demonstrativa = false,
                ConexaoServidor = true,
                Metricas = new List<string> { "Valor Pipeline", "Vendas Fechadas", "Novos Leads", "Taxa Conversao" },
                Dimensoes = new List<string> { "Segmento Cliente", "Canal Aquisicao", "Vendedor", "Status Negocio" }
            },
            new FonteDadosInfo
            {
                Id = "estoque",
                Nome = "Módulo Estoque & Suprimentos",
                Descricao = "Conexão no Servidor · Insumos, Giro de Estoque, Lotes e Compras.",
                Modulo = "Estoque & Suprimentos",
                TipoConexao = "SERVIDOR",
                Icone = "bi-box-seam",
                Cor = "info",
                Demonstrativa = false,
                ConexaoServidor = true,
                Metricas = new List<string> { "Valor Total Estoque", "Giro Estoque", "Itens Criticos", "Perdas / Descarte" },
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
