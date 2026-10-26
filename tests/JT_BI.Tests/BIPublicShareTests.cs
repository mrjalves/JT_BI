using JT_BI.Models;
using JT_BI.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using NUnit.Framework;

namespace JT_BI.Tests;

[TestFixture]
public class BIPublicShareTests
{
    private string _diretorio = null!;
    private BIDashboardService _service = null!;

    [SetUp]
    public void Preparar()
    {
        _diretorio = Path.Combine(Path.GetTempPath(), $"JT_BI_Share_{Guid.NewGuid():N}");
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
        catch { }
    }

    [Test]
    public async Task ObterStatusPorTokenPublicoAsync_TokenInexistente_RetornaNaoEncontrado()
    {
        var resp = await _service.ObterStatusPorTokenPublicoAsync("token-que-nao-existe-123");

        Assert.That(resp.Status, Is.EqualTo(BIStatusAcessoPublico.NaoEncontrado));
        Assert.That(resp.Dashboard, Is.Null);
    }

    [Test]
    public async Task ObterStatusPorTokenPublicoAsync_TokenNuloOuVazio_RetornaNaoEncontrado()
    {
        var respVazio = await _service.ObterStatusPorTokenPublicoAsync(string.Empty);
        Assert.That(respVazio.Status, Is.EqualTo(BIStatusAcessoPublico.NaoEncontrado));

        var respEspacos = await _service.ObterStatusPorTokenPublicoAsync("   ");
        Assert.That(respEspacos.Status, Is.EqualTo(BIStatusAcessoPublico.NaoEncontrado));
    }

    [Test]
    public async Task ObterStatusPorTokenPublicoAsync_PainelDesativado_RetornaDesativadoPeloProprietario()
    {
        var dash = new BIDashboard
        {
            Nome = "Painel Privado de Vendas",
            PermiteAcessoPublico = false,
            PublicToken = "token-privado-xyz"
        };
        await _service.SalvarAsync(dash);

        var resp = await _service.ObterStatusPorTokenPublicoAsync("token-privado-xyz");

        Assert.That(resp.Status, Is.EqualTo(BIStatusAcessoPublico.DesativadoPeloProprietario));
        Assert.That(resp.Dashboard, Is.Not.Null);
        Assert.That(resp.Dashboard!.Nome, Is.EqualTo("Painel Privado de Vendas"));
    }

    [Test]
    public async Task ObterStatusPorTokenPublicoAsync_PainelAtivo_RetornaOkComDashboard()
    {
        var dash = new BIDashboard
        {
            Nome = "Painel Executivo Compartilhado",
            PermiteAcessoPublico = true,
            PublicToken = "token-publico-abc",
            ExibirMenuPublico = true,
            AutoRefreshSegundos = 30
        };
        await _service.SalvarAsync(dash);

        var resp = await _service.ObterStatusPorTokenPublicoAsync("token-publico-abc");

        Assert.That(resp.Status, Is.EqualTo(BIStatusAcessoPublico.Ok));
        Assert.That(resp.Dashboard, Is.Not.Null);
        Assert.That(resp.Dashboard!.Nome, Is.EqualTo("Painel Executivo Compartilhado"));
        Assert.That(resp.Dashboard.ExibirMenuPublico, Is.True);
        Assert.That(resp.Dashboard.AutoRefreshSegundos, Is.EqualTo(30));
    }

    [Test]
    public async Task Persistencia_ExibirMenuPublico_PreservaAposRecarregarDoDisco()
    {
        var dash = new BIDashboard
        {
            Nome = "Painel com Menu",
            PermiteAcessoPublico = true,
            ExibirMenuPublico = true,
            PublicToken = "token-menu-teste"
        };
        await _service.SalvarAsync(dash);

        // Novo service apontando para o mesmo diretório físico para validar persistência
        var novoService = new BIDashboardService(new AmbienteFake
        {
            ContentRootPath = _diretorio,
            WebRootPath = _diretorio
        });

        var recuperado = await novoService.ObterPorTokenPublicoAsync("token-menu-teste");

        Assert.That(recuperado, Is.Not.Null);
        Assert.That(recuperado!.ExibirMenuPublico, Is.True);
    }

    [Test]
    public void PublicViewState_Configurar_AplicaConfiguracoesDoPainel()
    {
        var state = new PublicViewState();
        var dash = new BIDashboard
        {
            Nome = "Dashboard Financeiro",
            Descricao = "Fluxo de Caixa",
            Icone = "bi-wallet2",
            CorDestaque = "success",
            ExibirMenuPublico = true,
            AutoRefreshSegundos = 60
        };

        state.Configurar(dash);

        Assert.That(state.Titulo, Is.EqualTo("Dashboard Financeiro"));
        Assert.That(state.Descricao, Is.EqualTo("Fluxo de Caixa"));
        Assert.That(state.Icone, Is.EqualTo("bi-wallet2"));
        Assert.That(state.CorDestaque, Is.EqualTo("success"));
        Assert.That(state.ExibirMenu, Is.True);
        Assert.That(state.AutoRefreshSegundos, Is.EqualTo(60));
        Assert.That(state.ContadorRefresh, Is.EqualTo(60));
    }

    [Test]
    public void PublicViewState_AlternarTema_InverteModoEscuro()
    {
        var state = new PublicViewState();
        Assert.That(state.IsModoEscuro, Is.False);

        state.AlternarTema();
        Assert.That(state.IsModoEscuro, Is.True);

        state.AlternarTema();
        Assert.That(state.IsModoEscuro, Is.False);
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
