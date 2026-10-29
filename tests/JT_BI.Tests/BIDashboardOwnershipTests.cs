using JT_BI.Models;
using JT_BI.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;

namespace JT_BI.Tests;

public class BIDashboardOwnershipTests
{
    private static readonly BIUsuarioContexto Alice = new(1, "uid-alice", "Alice", false);
    private static readonly BIUsuarioContexto Bob = new(2, "uid-bob", "Bob", false);
    private static readonly BIUsuarioContexto Admin = new(99, "uid-admin", "Admin", true);

    private string _diretorio = null!;
    private BIDashboardService _service = null!;

    [SetUp]
    public void Preparar()
    {
        _diretorio = Path.Combine(Path.GetTempPath(), $"JT_BI_Ownership_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_diretorio);

        _service = new BIDashboardService(new AmbienteFake
        {
            ContentRootPath = _diretorio,
            WebRootPath = _diretorio
        });
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
    public async Task Salvar_ComContexto_AtribuiDonoAoNovoDashboard()
    {
        var dash = new BIDashboard { Nome = "Painel da Alice" };

        await _service.SalvarAsync(dash, Alice);

        var salvo = await _service.ObterPorIdAsync(dash.Id);
        Assert.That(salvo, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(salvo!.UsuarioId, Is.EqualTo(Alice.UsuarioId));
            Assert.That(salvo.UsuarioUid, Is.EqualTo(Alice.UsuarioUid));
            Assert.That(salvo.UsuarioNome, Is.EqualTo(Alice.UsuarioNome));
        });
    }

    [Test]
    public async Task Salvar_AlteracaoNomeEDados_AtualizaPropriedadesComSucesso()
    {
        var dash = new BIDashboard
        {
            Nome = "Nome Original",
            Descricao = "Descricao Original",
            Categoria = "Personalizado",
            Icone = "bi-speedometer2"
        };
        await _service.SalvarAsync(dash, Alice);

        var salvo = await _service.ObterPorIdAsync(dash.Id);
        Assert.That(salvo, Is.Not.Null);
        Assert.That(salvo!.Nome, Is.EqualTo("Nome Original"));

        // Modifica o nome e detalhes
        salvo.Nome = "Novo Nome do Painel Executivo";
        salvo.Descricao = "Nova Descricao Detalhada";
        salvo.Categoria = "Vendas e Retenção";
        salvo.Icone = "bi-pie-chart-fill";

        await _service.SalvarAsync(salvo, Alice);

        var atualizado = await _service.ObterPorIdAsync(dash.Id);
        Assert.That(atualizado, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(atualizado!.Nome, Is.EqualTo("Novo Nome do Painel Executivo"));
            Assert.That(atualizado.Descricao, Is.EqualTo("Nova Descricao Detalhada"));
            Assert.That(atualizado.Categoria, Is.EqualTo("Vendas e Retenção"));
            Assert.That(atualizado.Icone, Is.EqualTo("bi-pie-chart-fill"));
            Assert.That(atualizado.UsuarioId, Is.EqualTo(Alice.UsuarioId));
        });
    }

    [Test]
    public async Task ObterVisiveis_FiltraDashboardsDeOutrosUsuarios()
    {
        var daAlice = new BIDashboard { Nome = "Painel da Alice" };
        await _service.SalvarAsync(daAlice, Alice);

        var visiveisBob = await _service.ObterVisiveisAsync(Bob);
        var visiveisAlice = await _service.ObterVisiveisAsync(Alice);
        var visiveisAdmin = await _service.ObterVisiveisAsync(Admin);

        Assert.Multiple(() =>
        {
            Assert.That(visiveisBob.Any(d => d.Id == daAlice.Id), Is.False);
            Assert.That(visiveisAlice.Any(d => d.Id == daAlice.Id), Is.True);
            Assert.That(visiveisAdmin.Any(d => d.Id == daAlice.Id), Is.True);
        });
    }

    [Test]
    public async Task DashboardsSemDono_ContinuamVisiveisParaTodos()
    {
        var legados = await _service.ObterTodosAsync();
        var semDono = legados.FirstOrDefault();

        Assert.That(semDono, Is.Not.Null);
        Assert.That(semDono!.UsuarioId, Is.Null);

        var visiveisBob = await _service.ObterVisiveisAsync(Bob);
        Assert.That(visiveisBob.Any(d => d.Id == semDono.Id), Is.True);
    }

    [Test]
    public async Task ObterAcessivel_NaoRetornaDashboardDeOutroUsuario()
    {
        var daAlice = new BIDashboard { Nome = "Painel da Alice" };
        await _service.SalvarAsync(daAlice, Alice);

        var paraBob = await _service.ObterAcessivelAsync(daAlice.Id, Bob);
        var paraAlice = await _service.ObterAcessivelAsync(daAlice.Id, Alice);

        Assert.Multiple(() =>
        {
            Assert.That(paraBob, Is.Null);
            Assert.That(paraAlice, Is.Not.Null);
        });
    }

    [Test]
    public void Salvar_SemPermissao_LancaUnauthorizedAccessException()
    {
        var daAlice = new BIDashboard { Nome = "Painel da Alice" };
        _service.SalvarAsync(daAlice, Alice).GetAwaiter().GetResult();

        daAlice.Nome = "Tentativa do Bob";

        Assert.ThrowsAsync<UnauthorizedAccessException>(() => _service.SalvarAsync(daAlice, Bob));
    }

    [Test]
    public async Task Salvar_PreservaDonoOriginal_QuandoAdminEdita()
    {
        var daAlice = new BIDashboard { Nome = "Painel da Alice" };
        await _service.SalvarAsync(daAlice, Alice);

        daAlice.Nome = "Editado pelo Admin";
        daAlice.UsuarioId = Bob.UsuarioId;
        daAlice.UsuarioUid = Bob.UsuarioUid;
        daAlice.UsuarioNome = Bob.UsuarioNome;

        await _service.SalvarAsync(daAlice, Admin);

        var salvo = await _service.ObterPorIdAsync(daAlice.Id);
        Assert.That(salvo!.UsuarioId, Is.EqualTo(Alice.UsuarioId));
        Assert.That(salvo.UsuarioNome, Is.EqualTo(Alice.UsuarioNome));
    }

    [Test]
    public async Task Excluir_SemPermissao_RetornaFalseEPreservaDashboard()
    {
        var daAlice = new BIDashboard { Nome = "Painel da Alice" };
        await _service.SalvarAsync(daAlice, Alice);

        var removidoPeloBob = await _service.ExcluirAsync(daAlice.Id, Bob);
        var aindaExiste = await _service.ObterPorIdAsync(daAlice.Id);
        var removidoPelaAlice = await _service.ExcluirAsync(daAlice.Id, Alice);
        var depoisDaExclusao = await _service.ObterPorIdAsync(daAlice.Id);

        Assert.Multiple(() =>
        {
            Assert.That(removidoPeloBob, Is.False);
            Assert.That(aindaExiste, Is.Not.Null);
            Assert.That(removidoPelaAlice, Is.True);
            Assert.That(depoisDaExclusao, Is.Null);
        });
    }

    [Test]
    public async Task Duplicar_AtribuiCopiaAoUsuarioQueDuplicou()
    {
        var daAlice = new BIDashboard { Nome = "Painel da Alice", PermiteAcessoPublico = true };
        await _service.SalvarAsync(daAlice, Alice);

        var copia = await _service.DuplicarAsync(daAlice.Id, Alice);

        Assert.Multiple(() =>
        {
            Assert.That(copia.Id, Is.Not.EqualTo(daAlice.Id));
            Assert.That(copia.UsuarioId, Is.EqualTo(Alice.UsuarioId));
            Assert.That(copia.UsuarioNome, Is.EqualTo(Alice.UsuarioNome));
            Assert.That(copia.PublicToken, Is.Not.EqualTo(daAlice.PublicToken));
            Assert.That(copia.PermiteAcessoPublico, Is.False);
        });
    }

    [Test]
    public async Task Duplicar_FeitaPorAdmin_AtribuiCopiaAoAdmin()
    {
        var daAlice = new BIDashboard { Nome = "Painel da Alice" };
        await _service.SalvarAsync(daAlice, Alice);

        var copia = await _service.DuplicarAsync(daAlice.Id, Admin);

        Assert.Multiple(() =>
        {
            Assert.That(copia.UsuarioId, Is.EqualTo(Admin.UsuarioId));
            Assert.That(copia.UsuarioNome, Is.EqualTo(Admin.UsuarioNome));
        });
    }

    [Test]
    public void Duplicar_SemPermissao_LancaInvalidOperationException()
    {
        var daAlice = new BIDashboard { Nome = "Painel da Alice" };
        _service.SalvarAsync(daAlice, Alice).GetAwaiter().GetResult();

        Assert.ThrowsAsync<InvalidOperationException>(() => _service.DuplicarAsync(daAlice.Id, Bob));
    }

    [Test]
    public async Task DashboardSemDono_PodeSerAssumidoPorQualquerUsuarioAoCriar()
    {
        var legado = new BIDashboard { Nome = "Legado" };
        await _service.SalvarAsync(legado);

        var salvo = await _service.ObterPorIdAsync(legado.Id);
        Assert.That(salvo!.UsuarioId, Is.Null);

        salvo.Nome = "Legado editado";
        await _service.SalvarAsync(salvo, Bob);

        var atualizado = await _service.ObterPorIdAsync(legado.Id);
        Assert.Multiple(() =>
        {
            Assert.That(atualizado!.UsuarioId, Is.Null);
            Assert.That(atualizado.Nome, Is.EqualTo("Legado editado"));
        });
    }

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
