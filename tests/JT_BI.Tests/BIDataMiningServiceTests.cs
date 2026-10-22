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
}
