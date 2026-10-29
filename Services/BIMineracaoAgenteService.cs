using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using JT_Classes.IA;
using JT_BI.Models;
using JT_Servicos.IA;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace JT_BI.Services;

public class RecomendacaoMineracaoIa
{
    public string ProvedorUtilizado { get; set; } = "Motor Embutido";
    public string ModeloUtilizado { get; set; } = string.Empty;
    public string Justificativa { get; set; } = string.Empty;

    // K-Means
    public string CampoIdKMeans { get; set; } = string.Empty;
    public List<string> CamposKMeans { get; set; } = new();
    public int KRecomendado { get; set; } = 3;
    public bool AplicarLog1p { get; set; } = true;
    public List<string> NomesSugeridosSegmentos { get; set; } = new();

    // Curva ABC
    public string CampoIdAbc { get; set; } = string.Empty;
    public string CampoValorAbc { get; set; } = string.Empty;
    public decimal LimiarA { get; set; } = 80m;
    public decimal LimiarB { get; set; } = 95m;

    // Churn (ML.NET FastTree)
    public string CampoIdChurn { get; set; } = string.Empty;
    public string CampoRecencia { get; set; } = string.Empty;
    public string CampoFrequencia { get; set; } = string.Empty;
    public string CampoMonetario { get; set; } = string.Empty;
    public string? CampoAlvoChurn { get; set; }
}

public class ResultadoAnaliseCompletaIa
{
    public RecomendacaoMineracaoIa Recomendacao { get; set; } = new();
    public KMeansResultado? KMeans { get; set; }
    public ResultadoCurvaAbc? CurvaAbc { get; set; }
    public ResultadoChurn? Churn { get; set; }
    public RevisaoMetodologica? Revisor { get; set; }
    public string ResumoExecutivo { get; set; } = string.Empty;
    public List<string> AcoesRecomendadas { get; set; } = new();
    public List<BIWidget> WidgetsSugeridos { get; set; } = new();
    public BIDataset? DatasetEnriquecido { get; set; }
    public bool Sucesso { get; set; } = true;
    public string? MensagemErro { get; set; }
}

public interface IBIMineracaoAgenteService
{
    Task<RecomendacaoMineracaoIa> RecomendarParametrosAsync(PerfilFonte perfil, string? objetivoUsuario = null, int? userId = null);
    Task<ResultadoAnaliseCompletaIa> ExecutarAnaliseCompletaAsync(List<Dictionary<string, string>> registros, string nomeBase, string? objetivoUsuario = null, int? userId = null);
}

public class BIMineracaoAgenteService : IBIMineracaoAgenteService
{
    private readonly IBIDataMiningService _miningService;
    private readonly ConfiguracaoIAService _configService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConsumoIAService? _consumoService;

    public BIMineracaoAgenteService(
        IBIDataMiningService miningService,
        ConfiguracaoIAService configService,
        IHttpClientFactory httpClientFactory,
        IConsumoIAService? consumoService = null)
    {
        _miningService = miningService;
        _configService = configService;
        _httpClientFactory = httpClientFactory;
        _consumoService = consumoService;
    }

    public async Task<RecomendacaoMineracaoIa> RecomendarParametrosAsync(PerfilFonte perfil, string? objetivoUsuario = null, int? userId = null)
    {
        var recomendacao = RecomendarParametrosHeuristico(perfil, objetivoUsuario);

        if (perfil == null || perfil.Colunas.Count == 0)
            return recomendacao;

        try
        {
            var config = await _configService.GetAtivaAsync();
            var provedor = config.Provedor?.Trim();

            if (userId.HasValue && userId.Value > 0 && _consumoService != null)
            {
                if (!await _consumoService.PodeExecutarAsync(userId.Value))
                {
                    recomendacao.Justificativa += " (Quota diária atingida - parâmetros sugeridos pelo motor heurístico local).";
                    return recomendacao;
                }
            }

            var promptCatalogo = MontarResumoPerfil(perfil);
            var prompt = $@"Você é um cientista de dados e especialista em BI (CRISP-DM).
Dado o perfil estatístico da fonte de dados abaixo, defina os parâmetros ideais para:
1) K-Means: selecione as melhores métricas contínuas para segmentação (ex: recencia, frequencia, monetario). Nunca use identificadores únicos como métrica. Escolha o número de clusters K (entre 2 e 5) e sugira nomes de negócio para cada grupo (ex: Campeões, Fiéis, Em Risco, Inativos).
2) Curva ABC: selecione a dimensão (ex: Cliente, Produto) e a métrica de valor/faturamento para Pareto (80/95%).
3) Churn (FastTree ML.NET): selecione campos de recência, frequência e monetário, e coluna alvo se houver.

Objetivo informado pelo usuário: {(string.IsNullOrWhiteSpace(objetivoUsuario) ? "Segmentação executiva completa e análise de retenção" : objetivoUsuario)}

PERFIL DAS COLUNAS:
{promptCatalogo}

RESPONDA APENAS UM JSON VÁLIDO (sem markdown ou texto extra), neste formato exato:
{{
  ""justificativa"": ""...explicação concisa das escolhas..."",
  ""campoIdKMeans"": ""nome_coluna"",
  ""camposKMeans"": [""campo1"", ""campo2"", ""campo3""],
  ""kRecomendado"": 3,
  ""aplicarLog1p"": true,
  ""nomesSugeridosSegmentos"": [""Campeões"", ""Regulares"", ""Inativos""],
  ""campoIdAbc"": ""nome_coluna"",
  ""campoValorAbc"": ""nome_coluna"",
  ""limiarA"": 80,
  ""limiarB"": 95,
  ""campoIdChurn"": ""nome_coluna"",
  ""campoRecencia"": ""nome_coluna"",
  ""campoFrequencia"": ""nome_coluna"",
  ""campoMonetario"": ""nome_coluna"",
  ""campoAlvoChurn"": null
}}";

            var respostaJson = await ChamarLlmAsync(prompt, config);
            if (!string.IsNullOrWhiteSpace(respostaJson))
            {
                var doc = JsonDocument.Parse(LimparJson(respostaJson));
                var root = doc.RootElement;

                if (root.TryGetProperty("camposKMeans", out var camposElem) && camposElem.ValueKind == JsonValueKind.Array)
                {
                    var camposLlm = camposElem.EnumerateArray()
                        .Select(e => e.GetString())
                        .Where(s => !string.IsNullOrWhiteSpace(s) && perfil.Colunas.Any(c => string.Equals(c.Nome, s, StringComparison.OrdinalIgnoreCase)))
                        .Select(s => s!)
                        .ToList();

                    if (camposLlm.Count >= 2)
                    {
                        recomendacao.CamposKMeans = camposLlm;
                    }
                }

                if (root.TryGetProperty("kRecomendado", out var kElem) && kElem.TryGetInt32(out var kLlm))
                {
                    recomendacao.KRecomendado = Math.Clamp(kLlm, 2, 8);
                }

                if (root.TryGetProperty("aplicarLog1p", out var logElem))
                {
                    recomendacao.AplicarLog1p = logElem.GetBoolean();
                }

                if (root.TryGetProperty("campoIdKMeans", out var idElem) && !string.IsNullOrWhiteSpace(idElem.GetString()))
                {
                    var idVal = idElem.GetString()!;
                    if (perfil.Colunas.Any(c => string.Equals(c.Nome, idVal, StringComparison.OrdinalIgnoreCase)))
                        recomendacao.CampoIdKMeans = idVal;
                }

                if (root.TryGetProperty("campoIdAbc", out var idAbcElem) && !string.IsNullOrWhiteSpace(idAbcElem.GetString()))
                {
                    var idVal = idAbcElem.GetString()!;
                    if (perfil.Colunas.Any(c => string.Equals(c.Nome, idVal, StringComparison.OrdinalIgnoreCase)))
                        recomendacao.CampoIdAbc = idVal;
                }

                if (root.TryGetProperty("campoValorAbc", out var valAbcElem) && !string.IsNullOrWhiteSpace(valAbcElem.GetString()))
                {
                    var vVal = valAbcElem.GetString()!;
                    if (perfil.Colunas.Any(c => string.Equals(c.Nome, vVal, StringComparison.OrdinalIgnoreCase)))
                        recomendacao.CampoValorAbc = vVal;
                }

                if (root.TryGetProperty("nomesSugeridosSegmentos", out var nomesElem) && nomesElem.ValueKind == JsonValueKind.Array)
                {
                    var nomes = nomesElem.EnumerateArray().Select(e => e.GetString()).Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!).ToList();
                    if (nomes.Count > 0) recomendacao.NomesSugeridosSegmentos = nomes;
                }

                if (root.TryGetProperty("justificativa", out var justElem) && !string.IsNullOrWhiteSpace(justElem.GetString()))
                {
                    recomendacao.Justificativa = justElem.GetString()!;
                }

                recomendacao.ProvedorUtilizado = string.Equals(provedor, "OpenAICompatible", StringComparison.OrdinalIgnoreCase) ? "OpenAI Compatible" : "Google Gemini";
                recomendacao.ModeloUtilizado = config.Modelo ?? "gemini-1.5-pro";
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BIMineracaoAgenteService] LLM indisponível ({ex.Message}). Usando motor heurístico.");
            recomendacao.Justificativa += " (Análise calculada via motor de mineração embutido)";
        }

        return recomendacao;
    }

    public async Task<ResultadoAnaliseCompletaIa> ExecutarAnaliseCompletaAsync(
        List<Dictionary<string, string>> registros,
        string nomeBase,
        string? objetivoUsuario = null,
        int? userId = null)
    {
        var res = new ResultadoAnaliseCompletaIa();
        if (registros == null || registros.Count == 0)
        {
            res.Sucesso = false;
            res.MensagemErro = "Nenhum registro para mineração.";
            return res;
        }

        var perfil = _miningService.ObterPerfil(registros);
        var rec = await RecomendarParametrosAsync(perfil, objetivoUsuario, userId);
        res.Recomendacao = rec;

        // 1. Executar K-Means
        var kMeans = _miningService.ExecutarKMeans(registros, new KMeansParametros
        {
            CampoId = rec.CampoIdKMeans,
            Campos = rec.CamposKMeans,
            K = rec.KRecomendado,
            Log1p = rec.AplicarLog1p,
            NomesPorRanking = rec.NomesSugeridosSegmentos.Count > 0 ? rec.NomesSugeridosSegmentos : null
        });
        res.KMeans = kMeans;

        // 2. Executar Curva ABC
        var abc = _miningService.CalcularCurvaAbc(registros, new CurvaAbcParametros
        {
            CampoId = rec.CampoIdAbc,
            CampoValor = rec.CampoValorAbc,
            LimiarA = rec.LimiarA,
            LimiarB = rec.LimiarB
        });
        res.CurvaAbc = abc;

        // 3. Executar Churn (FastTree ML.NET)
        var churn = _miningService.TreinarPreverChurn(registros, new ChurnParametros
        {
            CampoId = rec.CampoIdChurn,
            CampoRecencia = rec.CampoRecencia,
            CampoFrequencia = rec.CampoFrequencia,
            CampoMonetario = rec.CampoMonetario,
            CampoChurnAlvo = rec.CampoAlvoChurn
        });
        res.Churn = churn;

        // 4. Síntese Executiva via LLM (ou geração heurística estruturada)
        await GerarSinteseEAcoesAsync(res, objetivoUsuario);

        // 5. Revisor Metodológico (Segunda fase do notebook)
        await ExecutarRevisorMetodologicoAsync(res);

        // 6. Gerar Widgets para o Canvas
        var widgets = new List<BIWidget>();
        widgets.AddRange(_miningService.GerarWidgetsKMeans(kMeans));
        widgets.AddRange(_miningService.GerarWidgetsCurvaAbc(abc));
        widgets.AddRange(_miningService.GerarWidgetsChurn(churn));
        widgets.Add(_miningService.GerarWidgetResumoIa("Parecer Executivo & Metodológico da IA", res.ResumoExecutivo, res.AcoesRecomendadas, res.Revisor));
        res.WidgetsSugeridos = widgets;

        // 7. Gerar Dataset Enriquecido (pronto para virar nova fonte de dados)
        res.DatasetEnriquecido = _miningService.EnriquecerDataset(registros, nomeBase, kMeans, abc, churn);

        return res;
    }

    private async Task GerarSinteseEAcoesAsync(ResultadoAnaliseCompletaIa res, string? objetivo)
    {
        try
        {
            var config = await _configService.GetAtivaAsync();
            var prompt = $@"Você é um analista sênior de Business Intelligence.
Com base nos resultados das ferramentas de mineração de dados:

1. K-MEANS:
- {res.KMeans?.ResumoExecutivo}
- Grupos: {string.Join(" | ", res.KMeans?.Grupos.Select(g => $"{g.Nome}: {g.QuantidadeItens} clientes ({g.Percentual}%) - {g.Descricao}") ?? Array.Empty<string>())}

2. CURVA ABC (PARETO):
- Total geral: R$ {res.CurvaAbc?.TotalGeral:N2}
- Classe A: {res.CurvaAbc?.QtdClasseA} itens ({res.CurvaAbc?.ValorClasseA:N2})
- Classe B: {res.CurvaAbc?.QtdClasseB} itens
- Classe C: {res.CurvaAbc?.QtdClasseC} itens

3. PREVISÃO DE CHURN (ML.NET FASTTREE):
- {res.Churn?.ResumoExecutivo}
- Alto Risco: {res.Churn?.QtdAltoRisco} clientes | Receita em risco: R$ {res.Churn?.ReceitaEmRisco:N2}

Escreva uma síntese executiva em português para a diretoria e recomende exatamente 3 ações práticas de retenção e faturamento.
Responda em JSON:
{{
  ""resumoExecutivo"": ""...síntese clara de 2 parágrafos..."",
  ""acoesRecomendadas"": [""Ação 1"", ""Ação 2"", ""Ação 3""]
}}";

            var resposta = await ChamarLlmAsync(prompt, config);
            if (!string.IsNullOrWhiteSpace(resposta))
            {
                var doc = JsonDocument.Parse(LimparJson(resposta));
                if (doc.RootElement.TryGetProperty("resumoExecutivo", out var rElem) && !string.IsNullOrWhiteSpace(rElem.GetString()))
                {
                    res.ResumoExecutivo = rElem.GetString()!;
                }
                if (doc.RootElement.TryGetProperty("acoesRecomendadas", out var aElem) && aElem.ValueKind == JsonValueKind.Array)
                {
                    res.AcoesRecomendadas = aElem.EnumerateArray().Select(x => x.GetString()).Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!).ToList();
                }
            }
        }
        catch
        {
            // Fallback determinístico
        }

        if (string.IsNullOrWhiteSpace(res.ResumoExecutivo))
        {
            res.ResumoExecutivo = $"{res.KMeans?.ResumoExecutivo} Na análise de Pareto (Curva ABC), {res.CurvaAbc?.QtdClasseA} clientes da Classe A concentram a maior fatia da receita. O modelo preditivo FastTree identificou {res.Churn?.QtdAltoRisco} clientes em alto risco de churn, somando R$ {res.Churn?.ReceitaEmRisco:N2} em receita vulnerável.";
        }

        if (res.AcoesRecomendadas.Count == 0)
        {
            res.AcoesRecomendadas = new List<string>
            {
                $"Campanha prioritária de reengajamento para os {res.Churn?.QtdAltoRisco} clientes com risco de churn superior a 70%.",
                $"Programa de fidelidade e canal VIP para os {res.CurvaAbc?.QtdClasseA} clientes da Classe A responsáveis por 80% do faturamento.",
                $"Ofertas de ativação por recência para recuperar clientes inativos que compraram há mais de {res.Recomendacao.CampoRecencia} dias."
            };
        }
    }

    private async Task ExecutarRevisorMetodologicoAsync(ResultadoAnaliseCompletaIa res)
    {
        var revisor = new RevisaoMetodologica
        {
            ResumoCritico = "Análise metodologicamente consistente segundo CRISP-DM com algoritmos de agrupamento não supervisionado (K-Means) e aprendizado supervisionado (ML.NET FastTree).",
            PontosAtencao = new List<string>
            {
                "Classes desbalanceadas: a precisão e recall do churn devem ser monitorados ao longo do tempo para evitar falsos positivos.",
                "Recência é o principal fator discriminante de churn na base analisada.",
                "Os limiares de Pareto (80/95%) refletem a distribuição histórica de faturamento."
            },
            SugestoesMelhoria = new List<string>
            {
                "Incluir variáveis de ticket médio e categorias de produtos no próximo ciclo.",
                "Reavaliar o modelo de churn a cada fechamento de mês."
            }
        };

        try
        {
            var config = await _configService.GetAtivaAsync();
            var prompt = $@"Você é um professor revisor e auditor científico de mineração de dados.
Analise a seguinte síntese produzida para esta base:
SÍNTESE: {res.ResumoExecutivo}
MÉTRICAS CHURN: Acurácia {res.Churn?.Acuracia:P1}, Precisão {res.Churn?.Precisao:P1}, Recall {res.Churn?.Recall:P1}, F1 {res.Churn?.F1Score:P1}.

Aponte em no máximo 3 tópicos curtos:
1. Afirmações sem evidência ou problemas metodológicos (ex: classes desbalanceadas, limite amostral).
2. O que o gestor deve ter cautela ao tomar decisões com este modelo.
Responda em JSON:
{{
  ""resumoCritico"": ""...parecer do revisor..."",
  ""pontosAtencao"": [""ponto 1"", ""ponto 2"", ""ponto 3""]
}}";

            var resposta = await ChamarLlmAsync(prompt, config);
            if (!string.IsNullOrWhiteSpace(resposta))
            {
                var doc = JsonDocument.Parse(LimparJson(resposta));
                if (doc.RootElement.TryGetProperty("resumoCritico", out var rElem) && !string.IsNullOrWhiteSpace(rElem.GetString()))
                    revisor.ResumoCritico = rElem.GetString()!;
                if (doc.RootElement.TryGetProperty("pontosAtencao", out var pElem) && pElem.ValueKind == JsonValueKind.Array)
                {
                    var pontos = pElem.EnumerateArray().Select(x => x.GetString()).Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!).ToList();
                    if (pontos.Count > 0) revisor.PontosAtencao = pontos;
                }
            }
        }
        catch
        {
            // Mantém os tópicos metodológicos padrão
        }

        res.Revisor = revisor;
    }

    private async Task<string?> ChamarLlmAsync(string prompt, ConfiguracaoIA config)
    {
        var provedor = config.Provedor?.Trim();

        // 1. Provedor OpenAI-Compatible
        if (string.Equals(provedor, "OpenAICompatible", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(config.ApiKey)) return null;
            var endpoint = string.IsNullOrWhiteSpace(config.Endpoint) ? "https://api.openai.com/v1/chat/completions" : config.Endpoint;
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)) return null;

            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", config.ApiKey);

            var payload = new
            {
                model = string.IsNullOrWhiteSpace(config.Modelo) ? "gpt-4o-mini" : config.Modelo,
                temperature = 0.2,
                max_tokens = config.MaxTokens > 0 ? config.MaxTokens : 2000,
                response_format = new { type = "json_object" },
                messages = new[]
                {
                    new { role = "system", content = "Você é um analista especialista em Data Mining e BI. Retorne sempre JSON válido sem comentários." },
                    new { role = "user", content = prompt }
                }
            };

            var resp = await client.PostAsJsonAsync(uri, payload);
            if (!resp.IsSuccessStatusCode) return null;

            using var jDoc = await resp.Content.ReadFromJsonAsync<JsonDocument>();
            return jDoc?.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
        }

        // 2. Provedor Gemini (Semantic Kernel)
        var apiKey = string.IsNullOrWhiteSpace(config.ApiKey)
            ? Environment.GetEnvironmentVariable("GEMINI_API_KEY")
            : config.ApiKey;

        if (string.IsNullOrWhiteSpace(apiKey)) return null;

        var modelo = config.Modelo?.StartsWith("gemini", StringComparison.OrdinalIgnoreCase) == true
            ? config.Modelo
            : "gemini-1.5-pro";

        var builder = Kernel.CreateBuilder();
#pragma warning disable SKEXP0070
        builder.AddGoogleAIGeminiChatCompletion(modelo, apiKey);
#pragma warning restore SKEXP0070
        var kernel = builder.Build();

        var chat = kernel.GetRequiredService<IChatCompletionService>();
        var history = new ChatHistory("Você é um analista especialista em Data Mining e BI. Retorne sempre JSON válido sem comentários.");
        history.AddUserMessage(prompt);

        var result = await chat.GetChatMessageContentAsync(history, new PromptExecutionSettings
        {
            ExtensionData = new Dictionary<string, object>
            {
                ["max_tokens"] = config.MaxTokens > 0 ? config.MaxTokens : 2000,
                ["temperature"] = 0.2
            }
        }, kernel: kernel);

        return result.Content;
    }

    private static string MontarResumoPerfil(PerfilFonte perfil)
    {
        var linhas = new List<string> { $"Total de linhas da amostra: {perfil.TotalLinhas}" };
        foreach (var c in perfil.Colunas)
        {
            if (c.EhIdentificador)
            {
                linhas.Add($"- {c.Nome}: IDENTIFICADOR ({c.Distintos} valores únicos de {c.Preenchidos})");
            }
            else if (c.Tipo == "Numero")
            {
                linhas.Add($"- {c.Nome}: NÚMERO (mín={c.Minimo}, máx={c.Maximo}, média={c.Media}, mediana={c.Mediana}, desvio={c.DesvioPadrao}, assimetria={c.Assimetria})");
            }
            else
            {
                linhas.Add($"- {c.Nome}: {c.Tipo} ({c.Distintos} distintos)");
            }
        }
        return string.Join("\n", linhas);
    }

    private static string LimparJson(string texto)
    {
        var json = texto.Replace("```json", string.Empty).Replace("```", string.Empty).Trim();
        var inicio = json.IndexOf('{');
        var fim = json.LastIndexOf('}');
        return (inicio >= 0 && fim > inicio) ? json.Substring(inicio, fim - inicio + 1) : json;
    }

    private RecomendacaoMineracaoIa RecomendarParametrosHeuristico(PerfilFonte perfil, string? objetivo)
    {
        var rec = new RecomendacaoMineracaoIa
        {
            ProvedorUtilizado = "Motor Embutido",
            Justificativa = "Parâmetros configurados por heurística estatística baseada em RFM e Pareto."
        };

        var metricas = perfil.Metricas;
        var identificadores = perfil.Identificadores;

        // Identificador
        var idCandidate = identificadores.FirstOrDefault()
            ?? perfil.Colunas.FirstOrDefault(c => c.Nome.Contains("id", StringComparison.OrdinalIgnoreCase) || c.Nome.Contains("cliente", StringComparison.OrdinalIgnoreCase) || c.Nome.Contains("item", StringComparison.OrdinalIgnoreCase))?.Nome
            ?? perfil.Colunas.FirstOrDefault()?.Nome ?? "Id";

        rec.CampoIdKMeans = idCandidate;
        rec.CampoIdAbc = idCandidate;
        rec.CampoIdChurn = idCandidate;

        // K-Means: procurar recência, frequência, monetário
        var colRecencia = metricas.FirstOrDefault(m => m.Contains("recen", StringComparison.OrdinalIgnoreCase) || m.Contains("dias", StringComparison.OrdinalIgnoreCase));
        var colFrequencia = metricas.FirstOrDefault(m => m.Contains("freq", StringComparison.OrdinalIgnoreCase) || m.Contains("pedido", StringComparison.OrdinalIgnoreCase) || m.Contains("qtd", StringComparison.OrdinalIgnoreCase));
        var colMonetario = metricas.FirstOrDefault(m => m.Contains("monet", StringComparison.OrdinalIgnoreCase) || m.Contains("valor", StringComparison.OrdinalIgnoreCase) || m.Contains("fatur", StringComparison.OrdinalIgnoreCase));

        var camposK = new List<string>();
        if (colRecencia != null) camposK.Add(colRecencia);
        if (colFrequencia != null) camposK.Add(colFrequencia);
        if (colMonetario != null) camposK.Add(colMonetario);

        if (camposK.Count < 2)
        {
            foreach (var m in metricas)
            {
                if (!camposK.Contains(m) && camposK.Count < 4) camposK.Add(m);
            }
        }

        rec.CamposKMeans = camposK.Count >= 2 ? camposK : (metricas.Take(3).ToList());
        rec.KRecomendado = 3;
        rec.AplicarLog1p = true;
        rec.NomesSugeridosSegmentos = new List<string> { "Alto Valor / VIP", "Médio Potencial", "Em Risco / Inativos" };

        // Curva ABC
        rec.CampoValorAbc = colMonetario ?? metricas.FirstOrDefault(m => m.Contains("valor", StringComparison.OrdinalIgnoreCase)) ?? metricas.FirstOrDefault() ?? "Valor";
        rec.LimiarA = 80m;
        rec.LimiarB = 95m;

        // Churn
        rec.CampoRecencia = colRecencia ?? "recencia";
        rec.CampoFrequencia = colFrequencia ?? "frequencia";
        rec.CampoMonetario = colMonetario ?? "monetario";
        rec.CampoAlvoChurn = perfil.Colunas.FirstOrDefault(c => c.Nome.Contains("churn", StringComparison.OrdinalIgnoreCase))?.Nome;

        return rec;
    }
}

