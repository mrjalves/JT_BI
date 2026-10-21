using System.Text;
using System.Text.Json;
using JT_Aplicacao.Classes;
using JT_Aplicacao.Config;
using JT_BI.Components;
using JT_BI.Services;
using JT_Classes.Acessos;
using JT_Servicos.Acessos;
using JT_UserInterface.Shared;
using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Carregar e inicializar instâncias estáticas do ecossistema JT
var secaoConfig = builder.Configuration.GetSection("Config");
var dadosConfig = secaoConfig.Get<Config>() ?? new Config
{
    ClientId = "JT_Sistemas",
    TipoApp = "WEB",
    Executor = "Sistema"
};
Config.SetInstancia(dadosConfig);

var secaoFConfig = builder.Configuration.GetSection("Config:FirebaseConfig");
var dadosFConfig = secaoFConfig.Get<FirebaseConfig>() ?? new FirebaseConfig
{
    BaseApiUrl = "https://api.jtecsistemas.com.br",
    DatabaseUrl = "https://api.jtecsistemas.com.br",
    ProjetoId = "controlefinanceiro-35e2a",
    ApiKey = "AIzaSyBMGBmvLhvar7rpys7NDvzEexbH_TZUEJs"
};
FirebaseConfig.SetInstancia(dadosFConfig);
builder.Services.AddSingleton(FirebaseConfig.Instancia);

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

// Registrar Camada de Interface Compartilhada e Segurança
builder.Services.AddScoped<ISecureStorageService, WebSecureStorageService>();
builder.Services.AddJTUserInterface();

// Injetar HttpClient com JwtAuthorizationHandler registrado pelo AddJTUserInterface
builder.Services.AddScoped(sp =>
{
    var factory = sp.GetRequiredService<IHttpClientFactory>();
    return factory.CreateClient(string.Empty);
});
builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();
builder.Services.AddScoped<JwtTokenService>();

// Registrar Serviços do JT_BI
builder.Services.AddScoped<JT_UserInterface.Shared.Services.Interfaces.Comum.IMenuProvider, BIMenuProvider>();
builder.Services.AddScoped<JT_UserInterface.Shared.Services.Interfaces.Comum.IModuloMenuProvider, BIMenuProvider>();
builder.Services.AddScoped<JT_UserInterface.Shared.Services.Interfaces.Comum.ITutorialProvider, BITutorialProvider>();
builder.Services.AddSingleton<IBIDashboardService, BIDashboardService>();
builder.Services.AddSingleton<IBIDataSourceService, BIDataSourceService>();
builder.Services.AddSingleton<IBIImportService, BIImportService>();
builder.Services.AddSingleton<IBIDataMiningService, BIDataMiningService>();

var app = builder.Build();

app.UseForwardedHeaders();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddAdditionalAssemblies(
        typeof(JT_UserInterface.Shared._Imports).Assembly
    );

app.Run();