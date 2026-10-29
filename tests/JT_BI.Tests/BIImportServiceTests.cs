using System.Text;
using JT_BI.Services;
using NUnit.Framework;

namespace JT_BI.Tests;

[TestFixture]
public class BIImportServiceTests
{
    private BIImportService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _service = new BIImportService();
    }

    [Test]
    public void DetectarDelimitador_IdentificaPontoEVirgulaOuVirgula()
    {
        Assert.Multiple(() =>
        {
            Assert.That(BIImportService.DetectarDelimitador("Codigo;Nome;Valor;Data"), Is.EqualTo(';'));
            Assert.That(BIImportService.DetectarDelimitador("InvoiceNo,StockCode,Description,Quantity,UnitPrice,CustomerID"), Is.EqualTo(','));
            Assert.That(BIImportService.DetectarDelimitador("col1\tcol2\tcol3"), Is.EqualTo('\t'));
            Assert.That(BIImportService.DetectarDelimitador("id|nome|score"), Is.EqualTo('|'));
        });
    }

    [Test]
    public void ParseLinhaCsv_RespeitaAspasEDelimitadoresInternos()
    {
        var linha = "\"12345\",\"Produto, Especial com virgula\",99.50,\"Texto com \"\"aspas duplas\"\"\"";
        var colunas = BIImportService.ParseLinhaCsv(linha, ',');

        Assert.Multiple(() =>
        {
            Assert.That(colunas, Has.Count.EqualTo(4));
            Assert.That(colunas[0], Is.EqualTo("12345"));
            Assert.That(colunas[1], Is.EqualTo("Produto, Especial com virgula"));
            Assert.That(colunas[2], Is.EqualTo("99.50"));
            Assert.That(colunas[3], Is.EqualTo("Texto com \"aspas duplas\""));
        });
    }

    [Test]
    public async Task ImportarStreamAsync_ProcessaCsvComAmostraEContagemExata()
    {
        var sb = new StringBuilder();
        sb.AppendLine("InvoiceNo,StockCode,Description,Quantity,UnitPrice,CustomerID");
        for (int i = 1; i <= 100; i++)
        {
            sb.AppendLine($"{500000 + i},PROD{i:D3},\"Item {i}\",{i * 2},{i * 1.5:F2},{1000 + (i % 10)}");
        }

        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        using var stream = new MemoryStream(bytes);

        long progressoFinal = 0;
        var dataset = await _service.ImportarStreamAsync(
            stream: stream,
            nome: "Online Retail Test",
            formato: "CSV",
            tamanhoBytes: bytes.Length,
            nomeArquivoOriginal: "online_retail_test.csv",
            onProgresso: (lidos, _) => progressoFinal = lidos);

        Assert.Multiple(() =>
        {
            Assert.That(dataset, Is.Not.Null);
            Assert.That(dataset.Nome, Is.EqualTo("Online Retail Test"));
            Assert.That(dataset.Origem, Is.EqualTo("CSV"));
            Assert.That(dataset.QtdLinhas, Is.EqualTo(100));
            Assert.That(dataset.Linhas, Has.Count.EqualTo(100));
            Assert.That(dataset.Colunas, Has.Count.EqualTo(6));
            Assert.That(dataset.Colunas.First(c => c.Nome == "Quantity").TipoDado, Is.EqualTo("Numero"));
            Assert.That(dataset.Colunas.First(c => c.Nome == "UnitPrice").TipoDado, Is.EqualTo("Numero"));
            Assert.That(dataset.Colunas.First(c => c.Nome == "Description").TipoDado, Is.EqualTo("Texto"));
            Assert.That(progressoFinal, Is.EqualTo(bytes.Length));
            Assert.That(File.Exists(dataset.CaminhoArquivo), Is.True);
        });

        // Limpeza do arquivo de teste
        try
        {
            if (File.Exists(dataset.CaminhoArquivo))
                File.Delete(dataset.CaminhoArquivo);
        }
        catch { }
    }

    [Test]
    public void TentarConverterDecimal_SuportaFormatoPtBrEInvariant()
    {
        Assert.Multiple(() =>
        {
            Assert.That(BIImportService.TentarConverterDecimal("1234.56", out var v1), Is.True);
            Assert.That(v1, Is.EqualTo(1234.56m));

            Assert.That(BIImportService.TentarConverterDecimal("1.234,56", out var v2), Is.True);
            Assert.That(v2, Is.EqualTo(1234.56m));

            Assert.That(BIImportService.TentarConverterDecimal("R$ 49,90", out var v3), Is.True);
            Assert.That(v3, Is.EqualTo(49.90m));

            Assert.That(BIImportService.TentarConverterDecimal("texto-invalido", out _), Is.False);
        });
    }
}

