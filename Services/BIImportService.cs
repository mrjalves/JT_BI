using System.Globalization;
using System.Text;
using System.Text.Json;
using JT_BI.Models;
using Microsoft.AspNetCore.Hosting;

namespace JT_BI.Services;

public interface IBIImportService
{
    BIDataset ImportarCsv(string nome, string conteudoCsv);
    BIDataset ImportarJson(string nome, string conteudoJson);
    BIDashboard GerarDashboardAutomatico(BIDataset dataset);

    Task<BIDataset> ImportarStreamAsync(
        Stream stream,
        string nome,
        string formato,
        long tamanhoBytes,
        string? nomeArquivoOriginal = null,
        Action<long, long>? onProgresso = null,
        CancellationToken cancellationToken = default);

    Task<BIDataset> ImportarArquivoEmDiscoAsync(
        string caminhoArquivo,
        string nome,
        string formato,
        long tamanhoBytes,
        CancellationToken cancellationToken = default);

    string ObterDiretorioUploads();
}

public class BIImportService : IBIImportService
{
    private readonly string _storageDir;
    private const int MaxLinhasAmostraMemoria = 25000;

    public BIImportService(IWebHostEnvironment? env = null)
    {
        var basePath = env?.ContentRootPath ?? AppContext.BaseDirectory;
        try
        {
            _storageDir = Path.Combine(basePath, "Data", "Uploads");
            if (!Directory.Exists(_storageDir))
                Directory.CreateDirectory(_storageDir);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BIImportService] Fallback de diretório de upload: {ex.Message}");
            _storageDir = Path.Combine(Path.GetTempPath(), "JT_BI_Uploads");
            try
            {
                if (!Directory.Exists(_storageDir))
                    Directory.CreateDirectory(_storageDir);
            }
            catch { }
        }
    }

    public string ObterDiretorioUploads()
    {
        if (!Directory.Exists(_storageDir))
        {
            try { Directory.CreateDirectory(_storageDir); } catch { }
        }
        return _storageDir;
    }

    public async Task<BIDataset> ImportarStreamAsync(
        Stream stream,
        string nome,
        string formato,
        long tamanhoBytes,
        string? nomeArquivoOriginal = null,
        Action<long, long>? onProgresso = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var dir = ObterDiretorioUploads();
        var id = Guid.NewGuid();
        var safeNome = string.IsNullOrWhiteSpace(nomeArquivoOriginal)
            ? $"upload_{id}"
            : string.Join("_", Path.GetFileName(nomeArquivoOriginal).Split(Path.GetInvalidFileNameChars()));

        var caminhoDestino = Path.Combine(dir, $"{id}_{safeNome}");

        // Grava o stream diretamente no disco usando buffer de 128 KB
        await using (var fs = new FileStream(caminhoDestino, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 128 * 1024, useAsync: true))
        {
            var buffer = new byte[128 * 1024];
            long totalLidos = 0;
            int lidos;
            var sw = System.Diagnostics.Stopwatch.StartNew();

            while ((lidos = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
            {
                await fs.WriteAsync(buffer.AsMemory(0, lidos), cancellationToken);
                totalLidos += lidos;

                if (sw.ElapsedMilliseconds >= 300)
                {
                    onProgresso?.Invoke(totalLidos, tamanhoBytes > 0 ? tamanhoBytes : totalLidos);
                    sw.Restart();
                }
            }

            onProgresso?.Invoke(totalLidos, totalLidos);
            tamanhoBytes = totalLidos;
        }

        return await ImportarArquivoEmDiscoAsync(caminhoDestino, nome, formato, tamanhoBytes, cancellationToken);
    }

    public async Task<BIDataset> ImportarArquivoEmDiscoAsync(
        string caminhoArquivo,
        string nome,
        string formato,
        long tamanhoBytes,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(caminhoArquivo))
            throw new FileNotFoundException($"Arquivo de dataset não encontrado em: {caminhoArquivo}");

        var dataset = new BIDataset
        {
            Id = Guid.NewGuid(),
            Nome = string.IsNullOrWhiteSpace(nome) ? Path.GetFileNameWithoutExtension(caminhoArquivo) : nome.Trim(),
            Origem = formato.ToUpperInvariant(),
            DataImportacao = DateTime.Now,
            CaminhoArquivo = caminhoArquivo,
            TamanhoBytes = tamanhoBytes > 0 ? tamanhoBytes : new FileInfo(caminhoArquivo).Length
        };

        if (formato.Equals("JSON", StringComparison.OrdinalIgnoreCase))
        {
            await ImportarJsonEmDiscoAsync(dataset, caminhoArquivo, cancellationToken);
        }
        else
        {
            await ImportarCsvEmDiscoAsync(dataset, caminhoArquivo, cancellationToken);
        }

        return dataset;
    }

    private static async Task ImportarCsvEmDiscoAsync(
        BIDataset dataset,
        string caminhoArquivo,
        CancellationToken cancellationToken)
    {
        await using var fs = new FileStream(caminhoArquivo, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, bufferSize: 64 * 1024, useAsync: true);
        using var reader = new StreamReader(fs, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 64 * 1024);

        string? linhaCabecalho = null;
        string? l;
        while ((l = await reader.ReadLineAsync(cancellationToken)) != null)
        {
            if (!string.IsNullOrWhiteSpace(l))
            {
                linhaCabecalho = l;
                break;
            }
        }

        if (linhaCabecalho == null)
            return;

        char delimitador = DetectarDelimitador(linhaCabecalho);
        dataset.Delimitador = delimitador.ToString();

        var cabecalhos = ParseLinhaCsv(linhaCabecalho, delimitador);
        for (int i = 0; i < cabecalhos.Count; i++)
        {
            var cab = cabecalhos[i];
            if (string.IsNullOrWhiteSpace(cab))
                cab = $"Coluna_{i + 1}";

            dataset.Colunas.Add(new BIDatasetColuna
            {
                Nome = cab,
                TipoDado = "Texto"
            });
        }

        int totalLinhas = 0;
        string? linha;
        while ((linha = await reader.ReadLineAsync(cancellationToken)) != null)
        {
            if (string.IsNullOrWhiteSpace(linha)) continue;

            totalLinhas++;

            if (dataset.Linhas.Count < MaxLinhasAmostraMemoria)
            {
                var partes = ParseLinhaCsv(linha, delimitador);
                var row = new Dictionary<string, object>(cabecalhos.Count, StringComparer.OrdinalIgnoreCase);

                for (int col = 0; col < cabecalhos.Count; col++)
                {
                    string valorStr = col < partes.Count ? partes[col] : string.Empty;
                    row[cabecalhos[col]] = valorStr;
                }

                dataset.Linhas.Add(row);
            }
        }

        dataset.QtdLinhas = totalLinhas;
        InferirTiposColunas(dataset);
    }

    private static async Task ImportarJsonEmDiscoAsync(
        BIDataset dataset,
        string caminhoArquivo,
        CancellationToken cancellationToken)
    {
        await using var fs = new FileStream(caminhoArquivo, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, bufferSize: 64 * 1024, useAsync: true);
        using var reader = new StreamReader(fs, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 64 * 1024);

        string? primeiraLinha = await reader.ReadLineAsync(cancellationToken);
        if (primeiraLinha == null) return;

        var inicio = primeiraLinha.TrimStart();
        if (inicio.StartsWith("{"))
        {
            // Linha-a-linha (NDJSON)
            int totalLinhas = 0;
            string? linha = primeiraLinha;
            do
            {
                if (string.IsNullOrWhiteSpace(linha)) continue;
                totalLinhas++;

                if (dataset.Linhas.Count < MaxLinhasAmostraMemoria)
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(linha);
                        if (doc.RootElement.ValueKind == JsonValueKind.Object)
                        {
                            var row = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                            foreach (var prop in doc.RootElement.EnumerateObject())
                            {
                                if (!dataset.Colunas.Any(c => c.Nome.Equals(prop.Name, StringComparison.OrdinalIgnoreCase)))
                                {
                                    dataset.Colunas.Add(new BIDatasetColuna
                                    {
                                        Nome = prop.Name,
                                        TipoDado = prop.Value.ValueKind == JsonValueKind.Number ? "Numero" : "Texto"
                                    });
                                }
                                row[prop.Name] = prop.Value.ToString();
                            }
                            dataset.Linhas.Add(row);
                        }
                    }
                    catch { }
                }
            } while ((linha = await reader.ReadLineAsync(cancellationToken)) != null);

            dataset.QtdLinhas = totalLinhas;
        }
        else
        {
            // Array JSON padrão
            fs.Position = 0;
            using var doc = await JsonDocument.ParseAsync(fs, cancellationToken: cancellationToken);
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                int count = 0;
                foreach (var el in doc.RootElement.EnumerateArray())
                {
                    count++;
                    if (dataset.Linhas.Count < MaxLinhasAmostraMemoria && el.ValueKind == JsonValueKind.Object)
                    {
                        var row = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                        foreach (var prop in el.EnumerateObject())
                        {
                            if (!dataset.Colunas.Any(c => c.Nome.Equals(prop.Name, StringComparison.OrdinalIgnoreCase)))
                            {
                                dataset.Colunas.Add(new BIDatasetColuna
                                {
                                    Nome = prop.Name,
                                    TipoDado = prop.Value.ValueKind == JsonValueKind.Number ? "Numero" : "Texto"
                                });
                            }
                            row[prop.Name] = prop.Value.ToString();
                        }
                        dataset.Linhas.Add(row);
                    }
                }
                dataset.QtdLinhas = count;
            }
        }

        InferirTiposColunas(dataset);
    }

    public BIDataset ImportarCsv(string nome, string conteudoCsv)
    {
        var dataset = new BIDataset
        {
            Nome = string.IsNullOrWhiteSpace(nome) ? "Dataset Importado CSV" : nome,
            Origem = "CSV",
            DataImportacao = DateTime.Now
        };

        if (string.IsNullOrWhiteSpace(conteudoCsv))
            return dataset;

        var linhas = conteudoCsv.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        if (linhas.Length == 0)
            return dataset;

        char delimitador = DetectarDelimitador(linhas[0]);
        dataset.Delimitador = delimitador.ToString();

        var cabecalhos = ParseLinhaCsv(linhas[0], delimitador);
        for (int i = 0; i < cabecalhos.Count; i++)
        {
            var cab = cabecalhos[i];
            if (string.IsNullOrWhiteSpace(cab))
                cab = $"Coluna_{i + 1}";

            dataset.Colunas.Add(new BIDatasetColuna
            {
                Nome = cab,
                TipoDado = "Texto"
            });
        }

        for (int i = 1; i < linhas.Length; i++)
        {
            var partes = ParseLinhaCsv(linhas[i], delimitador);
            var row = new Dictionary<string, object>(cabecalhos.Count, StringComparer.OrdinalIgnoreCase);
            for (int col = 0; col < cabecalhos.Count; col++)
            {
                string valorStr = col < partes.Count ? partes[col] : string.Empty;
                row[cabecalhos[col]] = valorStr;
            }
            dataset.Linhas.Add(row);
        }

        dataset.QtdLinhas = dataset.Linhas.Count;
        InferirTiposColunas(dataset);

        return dataset;
    }

    public BIDataset ImportarJson(string nome, string conteudoJson)
    {
        var dataset = new BIDataset
        {
            Nome = string.IsNullOrWhiteSpace(nome) ? "Dataset Importado JSON" : nome,
            Origem = "JSON",
            DataImportacao = DateTime.Now
        };

        if (string.IsNullOrWhiteSpace(conteudoJson))
            return dataset;

        try
        {
            using var doc = JsonDocument.Parse(conteudoJson);
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in doc.RootElement.EnumerateArray())
                {
                    if (el.ValueKind == JsonValueKind.Object)
                    {
                        var row = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                        foreach (var prop in el.EnumerateObject())
                        {
                            if (!dataset.Colunas.Any(c => c.Nome.Equals(prop.Name, StringComparison.OrdinalIgnoreCase)))
                            {
                                dataset.Colunas.Add(new BIDatasetColuna
                                {
                                    Nome = prop.Name,
                                    TipoDado = prop.Value.ValueKind == JsonValueKind.Number ? "Numero" : "Texto"
                                });
                            }
                            row[prop.Name] = prop.Value.ToString();
                        }
                        dataset.Linhas.Add(row);
                    }
                }
            }
            dataset.QtdLinhas = dataset.Linhas.Count;
            InferirTiposColunas(dataset);
        }
        catch { }

        return dataset;
    }

    public static char DetectarDelimitador(string linhaCabecalho)
    {
        if (string.IsNullOrEmpty(linhaCabecalho)) return ';';

        int countPontoVirgula = linhaCabecalho.Count(c => c == ';');
        int countVirgula = linhaCabecalho.Count(c => c == ',');
        int countTab = linhaCabecalho.Count(c => c == '\t');
        int countPipe = linhaCabecalho.Count(c => c == '|');

        int max = Math.Max(countPontoVirgula, Math.Max(countVirgula, Math.Max(countTab, countPipe)));
        if (max == 0) return ';';

        if (countPontoVirgula == max) return ';';
        if (countVirgula == max) return ',';
        if (countTab == max) return '\t';
        return '|';
    }

    public static List<string> ParseLinhaCsv(string linha, char delimitador)
    {
        var resultado = new List<string>();
        if (string.IsNullOrEmpty(linha)) return resultado;

        var sb = new StringBuilder();
        bool emAspas = false;

        for (int i = 0; i < linha.Length; i++)
        {
            char c = linha[i];
            if (c == '\"')
            {
                if (emAspas && i + 1 < linha.Length && linha[i + 1] == '\"')
                {
                    sb.Append('\"');
                    i++;
                }
                else
                {
                    emAspas = !emAspas;
                }
            }
            else if (c == delimitador && !emAspas)
            {
                resultado.Add(sb.ToString().Trim());
                sb.Clear();
            }
            else
            {
                sb.Append(c);
            }
        }
        resultado.Add(sb.ToString().Trim());
        return resultado;
    }

    public static bool TentarConverterDecimal(string? texto, out decimal valor)
    {
        valor = 0;
        if (string.IsNullOrWhiteSpace(texto)) return false;

        var limpo = texto.Trim().Trim('\"', '\'', '$', 'R', '$', ' ', '%');

        // Se tem vírgula e não tem ponto, é formato PT-BR / europeu (ex: "49,90")
        if (limpo.Contains(',') && !limpo.Contains('.'))
        {
            if (decimal.TryParse(limpo, NumberStyles.Number | NumberStyles.AllowCurrencySymbol, CultureInfo.GetCultureInfo("pt-BR"), out valor))
                return true;
        }

        // Se tem ambos (ex: "1.234,56" ou "1,234.56")
        if (limpo.Contains('.') && limpo.Contains(','))
        {
            if (limpo.LastIndexOf(',') > limpo.LastIndexOf('.'))
            {
                if (decimal.TryParse(limpo, NumberStyles.Number | NumberStyles.AllowCurrencySymbol, CultureInfo.GetCultureInfo("pt-BR"), out valor))
                    return true;
            }
            else
            {
                if (decimal.TryParse(limpo, NumberStyles.Number | NumberStyles.AllowCurrencySymbol, CultureInfo.InvariantCulture, out valor))
                    return true;
            }
        }

        if (decimal.TryParse(limpo, NumberStyles.Number | NumberStyles.AllowCurrencySymbol, CultureInfo.InvariantCulture, out valor))
            return true;

        if (decimal.TryParse(limpo, NumberStyles.Number | NumberStyles.AllowCurrencySymbol, CultureInfo.GetCultureInfo("pt-BR"), out valor))
            return true;

        return false;
    }

    private static void InferirTiposColunas(BIDataset dataset)
    {
        if (dataset.Linhas.Count == 0) return;

        int totalAmostra = Math.Min(dataset.Linhas.Count, 50);

        foreach (var col in dataset.Colunas)
        {
            int numerosCount = 0;
            int datasCount = 0;
            int validos = 0;

            for (int i = 0; i < totalAmostra; i++)
            {
                if (dataset.Linhas[i].TryGetValue(col.Nome, out var v) && v != null)
                {
                    var str = v.ToString()?.Trim();
                    if (!string.IsNullOrEmpty(str))
                    {
                        validos++;
                        if (TentarConverterDecimal(str, out _))
                            numerosCount++;
                        else if (DateTime.TryParse(str, CultureInfo.InvariantCulture, DateTimeStyles.None, out _) ||
                                 DateTime.TryParse(str, CultureInfo.GetCultureInfo("pt-BR"), DateTimeStyles.None, out _))
                            datasCount++;
                    }
                }
            }

            if (validos > 0)
            {
                if ((double)numerosCount / validos >= 0.7)
                    col.TipoDado = "Numero";
                else if ((double)datasCount / validos >= 0.7)
                    col.TipoDado = "Data";
                else
                    col.TipoDado = "Texto";
            }
        }
    }

    public BIDashboard GerarDashboardAutomatico(BIDataset dataset)
    {
        var dash = new BIDashboard
        {
            Nome = $"Painel: {dataset.Nome}",
            Descricao = $"Gerado automaticamente a partir de {dataset.QtdLinhas:N0} registros importados.",
            Categoria = "Importados",
            Icone = "bi-file-earmark-spreadsheet",
            CorDestaque = "primary",
            DatasetsVinculadosIds = new List<Guid> { dataset.Id }
        };

        var colNumerica = dataset.Colunas.FirstOrDefault(c => c.TipoDado == "Numero") ?? dataset.Colunas.FirstOrDefault();
        var colTexto = dataset.Colunas.FirstOrDefault(c => c.TipoDado != "Numero") ?? dataset.Colunas.LastOrDefault();

        // 1. KPI Total
        dash.Widgets.Add(new BIWidget
        {
            Titulo = $"Total {colNumerica?.Nome ?? "Registros"}",
            Subtitulo = "Soma consolidada do dataset",
            Tipo = BIWidgetTipo.CardKpi,
            PosX = 2,
            PosY = 2,
            Largura = 22,
            Altura = 18,
            CorTema = "primary",
            Icone = "bi-cash-coin",
            FonteDadosId = $"dataset_{dataset.Id}",
            MetricaCampo = colNumerica?.Nome ?? "",
            DimensaoCampo = colTexto?.Nome ?? "",
            Agregacao = "Soma",
            FormatoValor = "Numero"
        });

        // 2. KPI Média
        dash.Widgets.Add(new BIWidget
        {
            Titulo = $"Média {colNumerica?.Nome ?? "Valores"}",
            Subtitulo = "Média por ocorrência",
            Tipo = BIWidgetTipo.CardKpi,
            PosX = 26,
            PosY = 2,
            Largura = 22,
            Altura = 18,
            CorTema = "info",
            Icone = "bi-calculator",
            FonteDadosId = $"dataset_{dataset.Id}",
            MetricaCampo = colNumerica?.Nome ?? "",
            DimensaoCampo = colTexto?.Nome ?? "",
            Agregacao = "Media",
            FormatoValor = "Decimal"
        });

        // 3. KPI Total de Linhas
        dash.Widgets.Add(new BIWidget
        {
            Titulo = "Volume de Linhas",
            Subtitulo = "Registros processados",
            Tipo = BIWidgetTipo.CardKpi,
            PosX = 50,
            PosY = 2,
            Largura = 22,
            Altura = 18,
            CorTema = "success",
            Icone = "bi-list-check",
            FonteDadosId = $"dataset_{dataset.Id}",
            MetricaCampo = colNumerica?.Nome ?? "",
            DimensaoCampo = colTexto?.Nome ?? "",
            Agregacao = "Contagem",
            FormatoValor = "Numero",
            ValorAtual = dataset.QtdLinhas
        });

        // 4. Gráfico de Barras por Categoria
        dash.Widgets.Add(new BIWidget
        {
            Titulo = $"Distribuição por {colTexto?.Nome ?? "Categoria"}",
            Subtitulo = $"Valores de {colNumerica?.Nome ?? "Métrica"}",
            Tipo = BIWidgetTipo.GraficoBarras,
            PosX = 2,
            PosY = 24,
            Largura = 46,
            Altura = 42,
            CorTema = "primary",
            Icone = "bi-bar-chart",
            FonteDadosId = $"dataset_{dataset.Id}",
            MetricaCampo = colNumerica?.Nome ?? "",
            DimensaoCampo = colTexto?.Nome ?? "",
            Agregacao = "Soma",
            FormatoValor = "Numero"
        });

        // 5. Gráfico de Pizza / Donut
        dash.Widgets.Add(new BIWidget
        {
            Titulo = $"Participação % por {colTexto?.Nome ?? "Categoria"}",
            Subtitulo = "Proporção comparativa",
            Tipo = BIWidgetTipo.GraficoPizza,
            PosX = 50,
            PosY = 24,
            Largura = 46,
            Altura = 42,
            CorTema = "purple",
            Icone = "bi-pie-chart",
            FonteDadosId = $"dataset_{dataset.Id}",
            MetricaCampo = colNumerica?.Nome ?? "",
            DimensaoCampo = colTexto?.Nome ?? "",
            Agregacao = "Soma",
            FormatoValor = "Percentual"
        });

        return dash;
    }
}
