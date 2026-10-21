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
    ClientId = "JT_BI",
    TipoApp = "WEB",
    Executor = "API"
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

// Configurar HttpClient com header X-Client-Config apontando para o ClientId JT_BI
builder.Services.AddHttpClient();
builder.Services.AddScoped(sp =>
{
    var factory = sp.GetRequiredService<IHttpClientFactory>();
    var httpClient = factory.CreateClient(string.Empty);

    var baseUrl = builder.Configuration["Config:Conexao:BaseApiUrl"]
               ?? builder.Configuration["Config:FirebaseConfig:BaseApiUrl"]
               ?? "https://api.jtecsistemas.com.br";
    httpClient.BaseAddress = new Uri(baseUrl);

    var configPayload = new ClientConfigPayload { ClientId = "JT_BI" };
    if (Config.Instancia != null)
    {
        Config.Instancia.ClientId = configPayload.ClientId;
    }

    var json = JsonSerializer.Serialize(configPayload);
    var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
    if (!httpClient.DefaultRequestHeaders.Contains("X-Client-Config"))
    {
        httpClient.DefaultRequestHeaders.Add("X-Client-Config", encoded);
    }

    return httpClient;
});

// Registrar Camada de Interface Compartilhada e Segurança
builder.Services.AddScoped<ISecureStorageService, WebSecureStorageService>();
builder.Services.AddJTUserInterface();
builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();
builder.Services.AddScoped<JwtTokenService>();

// Registrar Serviços do JT_BI
builder.Services.AddSingleton<IBIDashboardService, BIDashboardService>();
builder.Services.AddSingleton<IBIDataSourceService, BIDataSourceService>();
builder.Services.AddSingleton<IBIImportService, BIImportService>();

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