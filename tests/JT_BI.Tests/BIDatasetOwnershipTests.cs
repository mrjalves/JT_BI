using JT_BI.Models;
using JT_BI.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;

namespace JT_BI.Tests;

public class BIDatasetOwnershipTests
{
    private static readonly BIUsuarioContexto Alice = new(1, "uid-alice", "Alice", false);
    private static readonly BIUsuarioContexto Bob = new(2, "uid-bob", "Bob", false);
    private static readonly BIUsuarioContexto Admin = new(99, "uid-admin", "Admin", true);

    private string _diretorio = null!;
    private BIDashboardService _service = null!;

    [SetUp]
    public void Preparar()
    {
        _diretorio = Path.Combine(Path.GetTempPath(), $"JT_BI_DatasetOwnership_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_diretorio);
        _service = CriarServico();
    }

    [TearDown]
    public void Limpar()
    {
        try
        {
            if (Directory.Exists(_diretorio))
                Directory.Delete(_diretorio, true);
        }
        catch
        {
        }
    }

    [Test]
    public async Task SalvarDataset_ComContexto_AtribuiDono()
    {
        var dataset = new BIDataset { Nome = "Vendas Alice" };

        await _service.SalvarDatasetAsync(dataset, Alice);

        var salvo = (await _service.ObterDatasetsAsync()).Single(d => d.Id == dataset.Id);
        Assert.Multiple(() =>
        {
            Assert.That(salvo.UsuarioId, Is.EqualTo(Alice.UsuarioId));
            Assert.That(salvo.UsuarioUid, Is.EqualTo(Alice.UsuarioUid));
            Assert.That(salvo.UsuarioNome, Is.EqualTo(Alice.UsuarioNome));
        });
    }

    [Test]
    public async Task ObterDatasetsVisiveis_FiltraPorDonoEPreservaSemDono()
    {
        var daAlice = new BIDataset { Nome = "Dataset Alice" };
        var legado = new BIDataset { Nome = "Dataset Legado" };
        await _service.SalvarDatasetAsync(daAlice, Alice);
        await _service.SalvarDatasetAsync(legado);

        var visiveisBob = await _service.ObterDatasetsVisiveisAsync(Bob);
        var visiveisAlice = await _service.ObterDatasetsVisiveisAsync(Alice);
        var visiveisAdmin = await _service.ObterDatasetsVisiveisAsync(Admin);

        Assert.Multiple(() =>
        {
            Assert.That(visiveisBob.Any(d => d.Id == daAlice.Id), Is.False);
            Assert.That(visiveisBob.Any(d => d.Id == legado.Id), Is.True);
            Assert.That(visiveisAlice.Any(d => d.Id == daAlice.Id), Is.True);
            Assert.That(visiveisAdmin.Any(d => d.Id == daAlice.Id), Is.True);
        });
    }

    [Test]
    public void SalvarDataset_SemPermissao_LancaUnauthorizedAccessException()
    {
        var daAlice = new BIDataset { Nome = "Dataset Alice" };
        _service.SalvarDatasetAsync(daAlice, Alice).GetAwaiter().GetResult();

        var tentativa = new BIDataset { Id = daAlice.Id, Nome = "Tentativa do Bob" };

        Assert.ThrowsAsync<UnauthorizedAccessException>(() => _service.SalvarDatasetAsync(tentativa, Bob));
    }

    [Test]
    public async Task SalvarDataset_PreservaDonoOriginal_QuandoAdminEdita()
    {
        var daAlice = new BIDataset { Nome = "Dataset Alice" };
        await _service.SalvarDatasetAsync(daAlice, Alice);

        var editadoPeloAdmin = new BIDataset
        {
            Id = daAlice.Id,
            Nome = "Editado pelo Admin",
            UsuarioId = Bob.UsuarioId,
            UsuarioUid = Bob.UsuarioUid,
            UsuarioNome = Bob.UsuarioNome
        };

        await _service.SalvarDatasetAsync(editadoPeloAdmin, Admin);

        var salvo = (await _service.ObterDatasetsAsync()).Single(d => d.Id == daAlice.Id);
        Assert.Multiple(() =>
        {
            Assert.That(salvo.UsuarioId, Is.EqualTo(Alice.UsuarioId));
            Assert.That(salvo.UsuarioNome, Is.EqualTo(Alice.UsuarioNome));
            Assert.That(salvo.Nome, Is.EqualTo("Editado pelo Admin"));
        });
    }

    [Test]
    public async Task ObterDatasetsPorIds_RetornaVinculadosMesmoDeOutroDono()
    {
        var daAlice = new BIDataset { Nome = "Dataset Alice" };
        var doBob = new BIDataset { Nome = "Dataset Bob" };
        await _service.SalvarDatasetAsync(daAlice, Alice);
        await _service.SalvarDatasetAsync(doBob, Bob);

        var vinculados = await _service.ObterDatasetsPorIdsAsync(new[] { daAlice.Id });

        Assert.Multiple(() =>
        {
            Assert.That(vinculados, Has.Count.EqualTo(1));
            Assert.That(vinculados[0].Id, Is.EqualTo(daAlice.Id));
        });
    }

    [Test]
    public async Task DatasetsPersistemDonoEntreInstanciasDoServico()
    {
        var daAlice = new BIDataset { Nome = "Dataset Alice" };
        await _service.SalvarDatasetAsync(daAlice, Alice);

        var outraInstancia = CriarServico();
        var visiveisBob = await outraInstancia.ObterDatasetsVisiveisAsync(Bob);
        var visiveisAlice = await outraInstancia.ObterDatasetsVisiveisAsync(Alice);

        Assert.Multiple(() =>
        {
            Assert.That(visiveisBob.Any(d => d.Id == daAlice.Id), Is.False);
            Assert.That(visiveisAlice.Any(d => d.Id == daAlice.Id), Is.True);
        });
    }

    private BIDashboardService CriarServico()
        => new(new AmbienteFake { ContentRootPath = _diretorio, WebRootPath = _diretorio });

    private sealed class AmbienteFake : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "JT_BI.Tests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = string.Empty;
        public string EnvironmentName { get; set; } = "Development";
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
