using System.Text.Json;
using JT_BI.Models;

namespace JT_BI.Services;

public interface IBIImportService
{
    BIDataset ImportarCsv(string nome, string conteudoCsv);
    BIDataset ImportarJson(string nome, string conteudoJson);
    BIDashboard GerarDashboardAutomatico(BIDataset dataset);
}

public class BIImportService : IBIImportService
{
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

        // Detectar delimitador: ';' ou ',' ou '\t'
        char delimitador = ';';
        if (linhas[0].Contains(',') && !linhas[0].Contains(';'))
            delimitador = ',';
        else if (linhas[0].Contains('\t'))
            delimitador = '\t';

        var cabecalhos = linhas[0].Split(delimitador).Select(c => c.Trim().Trim('\"')).ToArray();
        foreach (var cab in cabecalhos)
        {
            dataset.Colunas.Add(new BIDatasetColuna
            {
                Nome = cab,
                TipoDado = "Texto"
            });
        }

        for (int i = 1; i < linhas.Length; i++)
        {
            var partes = linhas[i].Split(delimitador);
            var row = new Dictionary<string, object>();
            for (int col = 0; col < cabecalhos.Length; col++)
            {
                string valorStr = col < partes.Length ? partes[col].Trim().Trim('\"') : "";
                row[cabecalhos[col]] = valorStr;
            }
            dataset.Linhas.Add(row);
        }

        dataset.QtdLinhas = dataset.Linhas.Count;

        // Inferir tipos
        foreach (var col in dataset.Colunas)
        {
            int numerosCount = 0;
            int totalAmostra = Math.Min(dataset.Linhas.Count, 20);
            for (int i = 0; i < totalAmostra; i++)
            {
                if (dataset.Linhas[i].TryGetValue(col.Nome, out var v) && decimal.TryParse(v?.ToString()?.Replace(".", "").Replace(",", "."), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out _))
                {
                    numerosCount++;
                }
            }
            if (totalAmostra > 0 && (double)numerosCount / totalAmostra > 0.7)
            {
                col.TipoDado = "Numero";
            }
        }

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

        try
        {
            using var doc = JsonDocument.Parse(conteudoJson);
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in doc.RootElement.EnumerateArray())
                {
                    if (el.ValueKind == JsonValueKind.Object)
                    {
                        var row = new Dictionary<string, object>();
                        foreach (var prop in el.EnumerateObject())
                        {
                            if (!dataset.Colunas.Any(c => c.Nome == prop.Name))
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
        }
        catch { }

        return dataset;
    }

    public BIDashboard GerarDashboardAutomatico(BIDataset dataset)
    {
        var dash = new BIDashboard
        {
            Nome = $"Painel: {dataset.Nome}",
            Descricao = $"Gerado automaticamente a partir de {dataset.QtdLinhas} registros importados.",
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