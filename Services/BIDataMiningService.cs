using System.Globalization;
using JT_BI.Models;

namespace JT_BI.Services;

public class ClusterItem
{
    public string Id { get; set; } = string.Empty;
    public string Rotulo { get; set; } = string.Empty;
    public double[] Valores { get; set; } = Array.Empty<double>();
    public int ClusterId { get; set; }
}

public class ClusterGrupo
{
    public int Id { get; set; }
    public string Nome { get; set; } = string.Empty;
    public string Descricao { get; set; } = string.Empty;
    public string Cor { get; set; } = "#3b82f6";
    public double[] Centroide { get; set; } = Array.Empty<double>();
    public int QuantidadeItens { get; set; }
    public double Percentual { get; set; }
}

public class KMeansResultado
{
    public List<ClusterGrupo> Grupos { get; set; } = new();
    public List<ClusterItem> Itens { get; set; } = new();
    public int IteracoesRealizadas { get; set; }
    public string ResumoExecutivo { get; set; } = string.Empty;
}

public class ItemCurvaAbc
{
    public string Nome { get; set; } = string.Empty;
    public decimal Valor { get; set; }
    public decimal PercentualAcumulado { get; set; }
    public string Classe { get; set; } = "C"; // A, B ou C
}

public class ResultadoCurvaAbc
{
    public List<ItemCurvaAbc> Itens { get; set; } = new();
    public int QtdClasseA { get; set; }
    public int QtdClasseB { get; set; }
    public int QtdClasseC { get; set; }
    public decimal TotalGeral { get; set; }
}

public class ItemScoreRisco
{
    public string Nome { get; set; } = string.Empty;
    public decimal ScoreRisco { get; set; } // 0 a 100
    public string CategoriaRisco { get; set; } = "Baixo"; // Baixo, Médio, Crítico
    public string MotivoPrincipal { get; set; } = string.Empty;
}

public interface IBIDataMiningService
{
    KMeansResultado ExecutarKMeans(List<Dictionary<string, string>> registros, string campoNome, string campoValor1, string campoValor2, int k = 3);
    ResultadoCurvaAbc CalcularCurvaAbc(List<Dictionary<string, string>> registros, string campoNome, string campoValor);
    List<ItemScoreRisco> CalcularScoreRiscoChurn(List<Dictionary<string, string>> registros, string campoNome, string campoDiasSemComprar, string campoQtdCancelamentos);
    List<BIWidget> GerarWidgetsMinacao(KMeansResultado kMeans);
    List<BIWidget> GerarWidgetsCurvaAbc(ResultadoCurvaAbc curvaAbc);
}

public class BIDataMiningService : IBIDataMiningService
{
    private static readonly string[] CoresClusters = { "#2563eb", "#10b981", "#f59e0b", "#ef4444", "#8b5cf6" };

    public KMeansResultado ExecutarKMeans(
        List<Dictionary<string, string>> registros,
        string campoNome,
        string campoValor1,
        string campoValor2,
        int k = 3)
    {
        var resultado = new KMeansResultado();
        if (registros == null || registros.Count == 0)
            return resultado;

        k = Math.Max(2, Math.Min(k, 5));

        // 1. Converter registros em itens com 2 dimensões numéricas
        var itens = new List<ClusterItem>();
        for (int i = 0; i < registros.Count; i++)
        {
            var r = registros[i];
            var nome = r.TryGetValue(campoNome, out var valNome) ? valNome : $"Item {i + 1}";
            double v1 = ExtrairDouble(r, campoValor1, 0);
            double v2 = ExtrairDouble(r, campoValor2, 0);

            itens.Add(new ClusterItem
            {
                Id = Guid.NewGuid().ToString("N"),
                Rotulo = nome,
                Valores = new double[] { v1, v2 },
                ClusterId = 0
            });
        }

        if (itens.Count < k)
        {
            k = Math.Max(1, itens.Count);
        }

        // 2. Inicializar centróides aleatoriamente baseados nos extremos
        var minV1 = itens.Min(it => it.Valores[0]);
        var maxV1 = itens.Max(it => it.Valores[0]);
        var minV2 = itens.Min(it => it.Valores[1]);
        var maxV2 = itens.Max(it => it.Valores[1]);

        var centroides = new List<double[]>();
        var random = new Random(42);
        for (int i = 0; i < k; i++)
        {
            var seedItem = itens[(i * (itens.Count / k)) % itens.Count];
            centroides.Add(new double[] { seedItem.Valores[0], seedItem.Valores[1] });
        }

        // 3. Iterações K-Means
        bool mudou = true;
        int maxIter = 50;
        int iter = 0;

        while (mudou && iter < maxIter)
        {
            iter++;
            mudou = false;

            // Atribuir cada ponto ao centróide mais próximo (distância euclidiana)
            foreach (var item in itens)
            {
                int melhorCluster = 0;
                double menorDist = double.MaxValue;

                for (int c = 0; c < k; c++)
                {
                    double d1 = item.Valores[0] - centroides[c][0];
                    double d2 = item.Valores[1] - centroides[c][1];
                    double dist = (d1 * d1) + (d2 * d2);

                    if (dist < menorDist)
                    {
                        menorDist = dist;
                        melhorCluster = c;
                    }
                }

                if (item.ClusterId != melhorCluster)
                {
                    item.ClusterId = melhorCluster;
                    mudou = true;
                }
            }

            // Recalcular centróides
            for (int c = 0; c < k; c++)
            {
                var grupoItens = itens.Where(it => it.ClusterId == c).ToList();
                if (grupoItens.Count > 0)
                {
                    centroides[c][0] = grupoItens.Average(it => it.Valores[0]);
                    centroides[c][1] = grupoItens.Average(it => it.Valores[1]);
                }
            }
        }

        // 4. Montar clusters finais
        resultado.IteracoesRealizadas = iter;
        resultado.Itens = itens;

        var nomesPadrao = new[]
        {
            "Grupo 1: Alto Volume / VIP",
            "Grupo 2: Médio Potencial",
            "Grupo 3: Em Risco / Inicial",
            "Grupo 4: Esporádicos",
            "Grupo 5: Oportunidades"
        };

        for (int c = 0; c < k; c++)
        {
            var qtd = itens.Count(it => it.ClusterId == c);
            var perc = itens.Count > 0 ? (double)qtd / itens.Count * 100 : 0;

            resultado.Grupos.Add(new ClusterGrupo
            {
                Id = c,
                Nome = c < nomesPadrao.Length ? nomesPadrao[c] : $"Cluster #{c + 1}",
                Descricao = $"Centróide médio: ({centroides[c][0]:N1}, {centroides[c][1]:N1})",
                Cor = CoresClusters[c % CoresClusters.Length],
                Centroide = centroides[c],
                QuantidadeItens = qtd,
                Percentual = Math.Round(perc, 1)
            });
        }

        resultado.ResumoExecutivo = $"Mineração K-Means finalizada em {iter} iterações. Identificados {k} clusters distintos entre {itens.Count} registros analisados.";
        return resultado;
    }

    public ResultadoCurvaAbc CalcularCurvaAbc(List<Dictionary<string, string>> registros, string campoNome, string campoValor)
    {
        var res = new ResultadoCurvaAbc();
        if (registros == null || registros.Count == 0) return res;

        var itens = new List<ItemCurvaAbc>();
        foreach (var r in registros)
        {
            var nome = r.TryGetValue(campoNome, out var n) ? n : "Desconhecido";
            decimal val = (decimal)ExtrairDouble(r, campoValor, 0);
            itens.Add(new ItemCurvaAbc { Nome = nome, Valor = val });
        }

        itens = itens.OrderByDescending(i => i.Valor).ToList();
        var total = itens.Sum(i => i.Valor);
        res.TotalGeral = total;

        decimal acumulado = 0;
        foreach (var it in itens)
        {
            acumulado += it.Valor;
            it.PercentualAcumulado = total > 0 ? (acumulado / total) * 100m : 0;

            if (it.PercentualAcumulado <= 80m)
            {
                it.Classe = "A";
                res.QtdClasseA++;
            }
            else if (it.PercentualAcumulado <= 95m)
            {
                it.Classe = "B";
                res.QtdClasseB++;
            }
            else
            {
                it.Classe = "C";
                res.QtdClasseC++;
            }
        }

        res.Itens = itens;
        return res;
    }

    public List<ItemScoreRisco> CalcularScoreRiscoChurn(
        List<Dictionary<string, string>> registros,
        string campoNome,
        string campoDiasSemComprar,
        string campoQtdCancelamentos)
    {
        var res = new List<ItemScoreRisco>();
        if (registros == null) return res;

        foreach (var r in registros)
        {
            var nome = r.TryGetValue(campoNome, out var n) ? n : "Cliente";
            double dias = ExtrairDouble(r, campoDiasSemComprar, 0);
            double cancelamentos = ExtrairDouble(r, campoQtdCancelamentos, 0);

            // Fórmula de risco: dias sem comprar (peso 0.6) + cancelamentos/reclamações (peso 0.4)
            double scoreDias = Math.Min(60, dias * 0.8);
            double scoreCanc = Math.Min(40, cancelamentos * 20);
            double scoreTotal = Math.Min(100, Math.Max(0, scoreDias + scoreCanc));

            string cat = scoreTotal >= 70 ? "Crítico" : (scoreTotal >= 40 ? "Médio" : "Baixo");
            string motivo = dias > 60
                ? $"{dias:N0} dias sem atividade no sistema"
                : (cancelamentos > 0 ? $"{cancelamentos:N0} chamados ou cancelamentos reportados" : "Engajamento regular");

            res.Add(new ItemScoreRisco
            {
                Nome = nome,
                ScoreRisco = (decimal)scoreTotal,
                CategoriaRisco = cat,
                MotivoPrincipal = motivo
            });
        }

        return res.OrderByDescending(r => r.ScoreRisco).ToList();
    }

    public List<BIWidget> GerarWidgetsMinacao(KMeansResultado kMeans)
    {
        var widgets = new List<BIWidget>();
        if (kMeans == null || kMeans.Grupos.Count == 0) return widgets;

        // 1. KPI Resumo
        widgets.Add(new BIWidget
        {
            Id = Guid.NewGuid(),
            Tipo = BIWidgetTipo.CardKpi,
            Titulo = "Clusters K-Means Ativos",
            LarguraColunas = 4,
            CorTema = "primary",
            ValorAtual = kMeans.Grupos.Count,
            Subtitulo = $"{kMeans.Itens.Count} amostras clusterizadas"
        });

        // 2. Gráfico de Pizza Distribuição dos Grupos
        widgets.Add(new BIWidget
        {
            Id = Guid.NewGuid(),
            Tipo = BIWidgetTipo.GraficoPizza,
            Titulo = "Segmentação K-Means dos Clientes",
            LarguraColunas = 8,
            CorTema = "info"
        });

        // 3. Tabela com os Itens e Clusters
        widgets.Add(new BIWidget
        {
            Id = Guid.NewGuid(),
            Tipo = BIWidgetTipo.Tabela,
            Titulo = "Tabela Detalhada por Cluster",
            LarguraColunas = 12,
            DimensaoCampo = "Grupo / Cliente",
            MetricaCampo = "Classificação"
        });

        return widgets;
    }

    public List<BIWidget> GerarWidgetsCurvaAbc(ResultadoCurvaAbc curvaAbc)
    {
        var widgets = new List<BIWidget>();
        if (curvaAbc == null) return widgets;

        // 1. KPI Classe A
        widgets.Add(new BIWidget
        {
            Id = Guid.NewGuid(),
            Tipo = BIWidgetTipo.CardKpi,
            Titulo = "Itens Classe A (80% Faturamento)",
            LarguraColunas = 4,
            CorTema = "success",
            ValorAtual = curvaAbc.QtdClasseA,
            Subtitulo = "Foco em retenção prioritária"
        });

        // 2. Gráfico de Barras Curva ABC
        widgets.Add(new BIWidget
        {
            Id = Guid.NewGuid(),
            Tipo = BIWidgetTipo.GraficoBarras,
            Titulo = "Distribuição de Itens por Classe (A, B, C)",
            LarguraColunas = 8,
            CorTema = "warning"
        });

        return widgets;
    }

    private double ExtrairDouble(Dictionary<string, string> r, string chave, double padrao)
    {
        if (string.IsNullOrWhiteSpace(chave) || !r.TryGetValue(chave, out var texto))
            return padrao;

        if (double.TryParse(texto, NumberStyles.Any, CultureInfo.InvariantCulture, out var vInv))
            return vInv;

        if (double.TryParse(texto, NumberStyles.Any, new CultureInfo("pt-BR"), out var vPt))
            return vPt;

        return padrao;
    }
}
