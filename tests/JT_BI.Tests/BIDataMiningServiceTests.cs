using JT_BI.Models;
using JT_BI.Services;

namespace JT_BI.Tests;

public class BIDataMiningServiceTests
{
    private readonly BIDataMiningService _service = new();

    private static Dictionary<string, string> Registro(string nome, string valor1, string valor2)
        => new()
        {
            ["Nome"] = nome,
            ["Valor1"] = valor1,
            ["Valor2"] = valor2
        };

    private static void AssertZScore(double[] valores, double esperadoMedia = 0, double esperadoDesvio = 1)
    {
        var media = valores.Average();
        var desvio = Math.Sqrt(valores.Sum(v => Math.Pow(v - media, 2)) / valores.Length);

        Assert.Multiple(() =>
        {
            Assert.That(media, Is.EqualTo(esperadoMedia).Within(1e-9));
            Assert.That(desvio, Is.EqualTo(esperadoDesvio).Within(1e-9));
        });
    }

    [Test]
    public void ExecutarKMeans_RetornaVazio_QuandoNaoHaRegistros()
    {
        var resultado = _service.ExecutarKMeans(new List<Dictionary<string, string>>(), "Nome", "Valor1", "Valor2");

        Assert.Multiple(() =>
        {
            Assert.That(resultado.Grupos, Is.Empty);
            Assert.That(resultado.Itens, Is.Empty);
        });
    }

    [Test]
    public void ExecutarKMeans_NormalizaDimensoesEPreservaValoresOriginais()
    {
        var registros = new List<Dictionary<string, string>>
        {
            Registro("A", "100", "1"),
            Registro("B", "110", "2"),
            Registro("C", "120", "3"),
            Registro("D", "10", "50"),
            Registro("E", "12", "55"),
            Registro("F", "14", "60")
        };

        var resultado = _service.ExecutarKMeans(registros, "Nome", "Valor1", "Valor2", k: 2);

        Assert.That(resultado.Itens, Has.Count.EqualTo(6));
        AssertZScore(resultado.Itens.Select(i => i.Valores[0]).ToArray());
        AssertZScore(resultado.Itens.Select(i => i.Valores[1]).ToArray());

        var itemA = resultado.Itens.Single(i => i.Rotulo == "A");
        Assert.That(itemA.ValoresOriginais, Is.EqualTo(new double[] { 100, 1 }));
    }

    [Test]
    public void ExecutarKMeans_OrdenaClustersPorValorMedio()
    {
        var registros = new List<Dictionary<string, string>>
        {
            Registro("A", "100", "1"),
            Registro("B", "110", "2"),
            Registro("C", "120", "3"),
            Registro("D", "10", "50"),
            Registro("E", "12", "55"),
            Registro("F", "14", "60")
        };

        var resultado = _service.ExecutarKMeans(registros, "Nome", "Valor1", "Valor2", k: 2);
        var vip = resultado.Grupos.Single(g => g.Nome == "Alto Valor / VIP");
        var risco = resultado.Grupos.Single(g => g.Nome == "Baixo Valor / Risco");

        Assert.Multiple(() =>
        {
            Assert.That(vip.Centroide[0], Is.GreaterThan(risco.Centroide[0]));
            Assert.That(vip.QuantidadeItens + risco.QuantidadeItens, Is.EqualTo(6));
            Assert.That(vip.Percentual + risco.Percentual, Is.EqualTo(100).Within(0.2));
        });
    }

    [Test]
    public void ExecutarKMeans_ProduzAtribuicoesDeterministicas()
    {
        var registros = new List<Dictionary<string, string>>
        {
            Registro("A", "100", "1"),
            Registro("B", "110", "2"),
            Registro("C", "120", "3"),
            Registro("D", "10", "50"),
            Registro("E", "12", "55"),
            Registro("F", "14", "60")
        };

        var primeira = _service.ExecutarKMeans(registros, "Nome", "Valor1", "Valor2", k: 3);
        var segunda = _service.ExecutarKMeans(registros, "Nome", "Valor1", "Valor2", k: 3);

        var atribuicoes1 = primeira.Itens.Select(i => (i.Rotulo, i.ClusterId)).ToList();
        var atribuicoes2 = segunda.Itens.Select(i => (i.Rotulo, i.ClusterId)).ToList();

        Assert.That(atribuicoes2, Is.EqualTo(atribuicoes1));
    }

    [Test]
    public void ExecutarKMeans_LimitaK_AoNumeroDeRegistros()
    {
        var registros = new List<Dictionary<string, string>>
        {
            Registro("A", "1", "1"),
            Registro("B", "2", "2"),
            Registro("C", "3", "3")
        };

        var resultado = _service.ExecutarKMeans(registros, "Nome", "Valor1", "Valor2", k: 10);

        Assert.That(resultado.Grupos, Has.Count.EqualTo(3));
    }

    [Test]
    public void ExecutarKMeans_ComUmRegistro_GeraUmCluster()
    {
        var registros = new List<Dictionary<string, string>> { Registro("A", "1", "1") };

        var resultado = _service.ExecutarKMeans(registros, "Nome", "Valor1", "Valor2", k: 3);

        Assert.That(resultado.Grupos, Has.Count.EqualTo(1));
        Assert.That(resultado.Grupos[0].QuantidadeItens, Is.EqualTo(1));
    }

    [Test]
    public void CalcularCurvaAbc_ItemQueCruzaFaixa_PertenceAClasseCompletada()
    {
        var registros = new List<Dictionary<string, string>>
        {
            new() { ["Nome"] = "Item 70", ["Valor"] = "70" },
            new() { ["Nome"] = "Item 20", ["Valor"] = "20" },
            new() { ["Nome"] = "Item 10", ["Valor"] = "10" }
        };

        var resultado = _service.CalcularCurvaAbc(registros, "Nome", "Valor");

        Assert.Multiple(() =>
        {
            Assert.That(resultado.TotalGeral, Is.EqualTo(100m));
            Assert.That(resultado.Itens.Select(i => i.Classe), Is.EqualTo(new[] { "A", "A", "B" }));
            Assert.That(resultado.QtdClasseA, Is.EqualTo(2));
            Assert.That(resultado.QtdClasseB, Is.EqualTo(1));
            Assert.That(resultado.QtdClasseC, Is.EqualTo(0));
            Assert.That(resultado.Itens.Select(i => i.PercentualAcumulado), Is.EqualTo(new[] { 70m, 90m, 100m }));
        });
    }

    [Test]
    public void CalcularCurvaAbc_OrdenaDecrescentePorValor()
    {
        var registros = new List<Dictionary<string, string>>
        {
            new() { ["Nome"] = "Baixo", ["Valor"] = "5" },
            new() { ["Nome"] = "Alto", ["Valor"] = "80" },
            new() { ["Nome"] = "Medio", ["Valor"] = "15" }
        };

        var resultado = _service.CalcularCurvaAbc(registros, "Nome", "Valor");

        Assert.That(resultado.Itens.Select(i => i.Nome), Is.EqualTo(new[] { "Alto", "Medio", "Baixo" }));
        Assert.That(resultado.Itens[0].Classe, Is.EqualTo("A"));
    }

    [Test]
    public void CalcularCurvaAbc_InterpretaValorNoFormatoPtBr()
    {
        var registros = new List<Dictionary<string, string>>
        {
            new() { ["Nome"] = "Item", ["Valor"] = "1.234,56" }
        };

        var resultado = _service.CalcularCurvaAbc(registros, "Nome", "Valor");

        Assert.That(resultado.TotalGeral, Is.EqualTo(1234.56m));
        Assert.That(resultado.Itens[0].Classe, Is.EqualTo("A"));
    }

    [Test]
    public void CalcularCurvaAbc_RetornaVazio_SemRegistros()
    {
        var resultado = _service.CalcularCurvaAbc(new List<Dictionary<string, string>>(), "Nome", "Valor");

        Assert.That(resultado.Itens, Is.Empty);
        Assert.That(resultado.TotalGeral, Is.EqualTo(0m));
    }

    [Test]
    public void CalcularScoreRiscoChurn_ClassificaCategoriasEOrdenaPorRisco()
    {
        var registros = new List<Dictionary<string, string>>
        {
            new() { ["Nome"] = "Baixo", ["Dias"] = "5", ["Cancelamentos"] = "0" },
            new() { ["Nome"] = "Critico", ["Dias"] = "80", ["Cancelamentos"] = "2" },
            new() { ["Nome"] = "Medio", ["Dias"] = "100", ["Cancelamentos"] = "0" }
        };

        var resultado = _service.CalcularScoreRiscoChurn(registros, "Nome", "Dias", "Cancelamentos");

        Assert.Multiple(() =>
        {
            Assert.That(resultado.Select(r => r.Nome), Is.EqualTo(new[] { "Critico", "Medio", "Baixo" }));
            Assert.That(resultado[0].ScoreRisco, Is.EqualTo(100m));
            Assert.That(resultado[0].CategoriaRisco, Is.EqualTo("Crítico"));
            Assert.That(resultado[1].ScoreRisco, Is.EqualTo(60m));
            Assert.That(resultado[1].CategoriaRisco, Is.EqualTo("Médio"));
            Assert.That(resultado[1].MotivoPrincipal, Does.Contain("100 dias"));
            Assert.That(resultado[2].CategoriaRisco, Is.EqualTo("Baixo"));
        });
    }

    [Test]
    public void GerarWidgetsMinacao_UsaFonteMineracaoEDadosEstaticos()
    {
        var registros = new List<Dictionary<string, string>>
        {
            Registro("A", "100", "1"),
            Registro("B", "110", "2"),
            Registro("C", "120", "3"),
            Registro("D", "10", "50"),
            Registro("E", "12", "55"),
            Registro("F", "14", "60")
        };
        var kMeans = _service.ExecutarKMeans(registros, "Nome", "Valor1", "Valor2", k: 2);

        var widgets = _service.GerarWidgetsMinacao(kMeans);

        Assert.Multiple(() =>
        {
            Assert.That(widgets, Has.Count.EqualTo(3));
            Assert.That(widgets.All(w => w.FonteDadosId == "mineracao"), Is.True);
            Assert.That(widgets[0].ValorAtual, Is.EqualTo(2));
            Assert.That(widgets[1].DadosEstaticos.Sum(p => p.Valor), Is.EqualTo(6));
            Assert.That(widgets[2].DadosEstaticos, Is.Not.Empty);
        });
    }

    [Test]
    public void GerarWidgetsMinacao_RetornaVazio_QuandoNaoHaGrupos()
    {
        Assert.That(_service.GerarWidgetsMinacao(new KMeansResultado()), Is.Empty);
    }

    [Test]
    public void GerarWidgetsCurvaAbc_CalculaPercentuaisDasClasses()
    {
        var registros = new List<Dictionary<string, string>>
        {
            new() { ["Nome"] = "A", ["Valor"] = "70" },
            new() { ["Nome"] = "B", ["Valor"] = "20" },
            new() { ["Nome"] = "C", ["Valor"] = "10" }
        };
        var curva = _service.CalcularCurvaAbc(registros, "Nome", "Valor");

        var widgets = _service.GerarWidgetsCurvaAbc(curva);
        var barras = widgets.Single(w => w.Tipo == BIWidgetTipo.GraficoBarras);

        Assert.Multiple(() =>
        {
            Assert.That(widgets, Has.Count.EqualTo(2));
            Assert.That(widgets.All(w => w.FonteDadosId == "mineracao"), Is.True);
            Assert.That(barras.DadosEstaticos, Has.Count.EqualTo(3));
            Assert.That(barras.DadosEstaticos.Sum(p => p.Percentual), Is.EqualTo(100m).Within(0.01m));
        });
    }

    [Test]
    public void GerarWidgetsCurvaAbc_RetornaVazio_QuandoCurvaNula()
    {
        Assert.That(_service.GerarWidgetsCurvaAbc(null!), Is.Empty);
    }

    [Test]
    public void EnriquecerDataset_ComIdsDuplicados_NaoLancaArgumentExceptionChaveDuplicada()
    {
        var registros = new List<Dictionary<string, string>>
        {
            new() { ["CustomerID"] = "17850", ["Faturamento"] = "100" },
            new() { ["CustomerID"] = "17850", ["Faturamento"] = "200" },
            new() { ["CustomerID"] = "17850", ["Faturamento"] = "300" },
            new() { ["CustomerID"] = "17851", ["Faturamento"] = "500" }
        };

        var kmeans = new KMeansResultado
        {
            Itens = new List<KMeansItem>
            {
                new() { Rotulo = "17850", ClusterIndex = 0, NomeCluster = "VIP" },
                new() { Rotulo = "17850", ClusterIndex = 0, NomeCluster = "VIP" },
                new() { Rotulo = "17851", ClusterIndex = 1, NomeCluster = "Regular" }
            }
        };

        var abc = new ResultadoCurvaAbc
        {
            Itens = new List<ItemCurvaAbc>
            {
                new() { Rotulo = "17850", Classe = "A", Valor = 600m },
                new() { Rotulo = "17850", Classe = "A", Valor = 600m },
                new() { Rotulo = "17851", Classe = "B", Valor = 500m }
            }
        };

        var churn = new ResultadoChurn
        {
            Previsoes = new List<PrevisaoChurnItem>
            {
                new() { Id = "17850", ProbabilidadeChurn = 0.15f, ClassificacaoRisco = "Baixo" },
                new() { Id = "17850", ProbabilidadeChurn = 0.15f, ClassificacaoRisco = "Baixo" },
                new() { Id = "17851", ProbabilidadeChurn = 0.75f, ClassificacaoRisco = "Alto" }
            }
        };

        Assert.DoesNotThrow(() =>
        {
            var ds = _service.EnriquecerDataset(registros, "Online_Retail", kmeans, abc, churn);
            Assert.That(ds, Is.Not.Null);
            Assert.That(ds.Linhas, Has.Count.EqualTo(4));
            Assert.That(ds.Linhas[0]["Cluster_Nome"], Is.EqualTo("VIP"));
            Assert.That(ds.Linhas[0]["Curva_ABC"], Is.EqualTo("A"));
            Assert.That(ds.Linhas[0]["Risco_Churn"], Is.EqualTo("Baixo"));
        });
    }

    [Test]
    public void ExecutarTratamentoDados_AgregaRfm_FiltraNegativosENulos()
    {
        var registros = new List<Dictionary<string, string>>
        {
            // Venda válida cliente 1
            new() { ["CustomerID"] = "17850", ["InvoiceDate"] = "2026-01-01", ["Quantity"] = "10", ["UnitPrice"] = "2.5" },
            // Segunda venda cliente 1
            new() { ["CustomerID"] = "17850", ["InvoiceDate"] = "2026-01-10", ["Quantity"] = "4", ["UnitPrice"] = "5.0" },
            // Devolução cliente 1 (negativo - deve ser filtrado)
            new() { ["CustomerID"] = "17850", ["InvoiceDate"] = "2026-01-15", ["Quantity"] = "-1", ["UnitPrice"] = "10.0" },
            // Venda válida cliente 2
            new() { ["CustomerID"] = "17851", ["InvoiceDate"] = "2026-01-05", ["Quantity"] = "2", ["UnitPrice"] = "50.0" },
            // Registro sem CustomerID (deve ser filtrado)
            new() { ["CustomerID"] = "", ["InvoiceDate"] = "2026-01-05", ["Quantity"] = "10", ["UnitPrice"] = "10.0" }
        };

        var config = new TratamentoDadosConfig
        {
            CampoId = "CustomerID",
            CampoData = "InvoiceDate",
            CampoQuantidade = "Quantity",
            CampoPrecoUnitario = "UnitPrice",
            RemoverNegativosZero = true,
            RemoverNulosOuVazios = true
        };

        var resultado = _service.ExecutarTratamentoDados(registros, config);

        Assert.Multiple(() =>
        {
            Assert.That(resultado.Sucesso, Is.True);
            Assert.That(resultado.LinhasOriginais, Is.EqualTo(5));
            Assert.That(resultado.LinhasDescartadas, Is.EqualTo(2)); // 1 negativo + 1 vazio
            Assert.That(resultado.LinhasResultantes, Is.EqualTo(2)); // clientes 17850 e 17851

            var c1 = resultado.RegistrosTratados.FirstOrDefault(r => r["CustomerID"] == "17850");
            Assert.That(c1, Is.Not.Null);
            Assert.That(c1!["frequencia"], Is.EqualTo("2")); // 2 compras válidas
            Assert.That(decimal.Parse(c1["monetario"], System.Globalization.CultureInfo.InvariantCulture), Is.EqualTo(45.0m)); // 10*2.5 + 4*5 = 25+20 = 45

            var c2 = resultado.RegistrosTratados.FirstOrDefault(r => r["CustomerID"] == "17851");
            Assert.That(c2, Is.Not.Null);
            Assert.That(c2!["frequencia"], Is.EqualTo("1"));
            Assert.That(decimal.Parse(c2["monetario"], System.Globalization.CultureInfo.InvariantCulture), Is.EqualTo(100.0m));
        });
    }

    [Test]
    public void EhFonteTransacional_DetectaCorretamente()
    {
        var colunasTransacionais = new[] { "InvoiceNo", "StockCode", "Description", "Quantity", "InvoiceDate", "UnitPrice", "CustomerID", "Country" };
        var colunasNaoTransacionais = new[] { "ClienteID", "Faturamento", "TicketMedio", "Score" };

        Assert.That(_service.EhFonteTransacional(colunasTransacionais), Is.True);
        Assert.That(_service.EhFonteTransacional(colunasNaoTransacionais), Is.False);
    }

    [Test]
    public void TraduzirMensagemErro_TraduzChaveDuplicadaEExcecoesComunsParaPtBr()
    {
        var exDup = new ArgumentException("An item with the same key has already been added. Key: 17850");
        var traduzidaDup = _service.TraduzirMensagemErro(exDup);

        Assert.That(traduzidaDup, Does.Contain("chave duplicada"));
        Assert.That(traduzidaDup, Does.Contain("17850"));

        var exOom = new OutOfMemoryException();
        var traduzidaOom = _service.TraduzirMensagemErro(exOom);
        Assert.That(traduzidaOom, Does.Contain("Memória insuficiente"));
    }
}
