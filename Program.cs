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

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
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
    .AddInteractiveServerRenderMode();

app.Run();