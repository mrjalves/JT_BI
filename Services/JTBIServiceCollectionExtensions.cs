using JT_Aplicacao.Classes;
using JT_Aplicacao.Config;
using JT_Classes.Acessos;
using JT_Servicos.Acessos;
using JT_UserInterface.Shared;
using JT_UserInterface.Shared.Services.Interfaces.Comum;

namespace JT_BI.Services;

public static class JTBIServiceCollectionExtensions
{
    public static IServiceCollection AddJTBI(this IServiceCollection services, IConfiguration configuration)
    {
        var secaoConfig = configuration.GetSection("Config");
        var dadosConfig = secaoConfig.Get<Config>()
            ?? throw new InvalidOperationException(
                "Seção 'Config' não encontrada. Configure appsettings.{Environment}.json, User Secrets ou variáveis de ambiente (Config__*).");

        Config.SetInstancia(dadosConfig);

        var secaoFConfig = configuration.GetSection("Config:FirebaseConfig");
        var dadosFConfig = secaoFConfig.Get<FirebaseConfig>();
        if (dadosFConfig is null ||
            string.IsNullOrWhiteSpace(dadosFConfig.BaseApiUrl) ||
            string.IsNullOrWhiteSpace(dadosFConfig.ApiKey))
        {
            throw new InvalidOperationException(
                "Configuração 'Config:FirebaseConfig' (BaseApiUrl e ApiKey) é obrigatória. " +
                "Defina via User Secrets em desenvolvimento ou variáveis de ambiente (Config__FirebaseConfig__*) em produção.");
        }

        FirebaseConfig.SetInstancia(dadosFConfig);
        services.AddSingleton(FirebaseConfig.Instancia);

        // Registrar Camada de Interface Compartilhada e Segurança
        services.AddScoped<ISecureStorageService, WebSecureStorageService>();
        services.AddJTUserInterface();

        // Injetar HttpClient com JwtAuthorizationHandler registrado pelo AddJTUserInterface
        services.AddScoped(sp =>
        {
            var factory = sp.GetRequiredService<IHttpClientFactory>();
            return factory.CreateClient(string.Empty);
        });

        // Registro único por circuito: IJwtTokenService e JwtTokenService compartilham a mesma instância Scoped
        services.AddScoped<JwtTokenService>();
        services.AddScoped<IJwtTokenService>(sp => sp.GetRequiredService<JwtTokenService>());

        // Registrar Serviços do JT_BI
        services.AddScoped<IMenuProvider, BIMenuProvider>();
        services.AddScoped<IModuloMenuProvider, BIMenuProvider>();
        services.AddScoped<ITutorialProvider, BITutorialProvider>();
        services.AddSingleton<IBIDashboardService, BIDashboardService>();
        services.AddScoped<IBIDataSourceService, BIDataSourceService>();
        services.AddSingleton<IBIImportService, BIImportService>();
        services.AddSingleton<IBIDataMiningService, BIDataMiningService>();
        services.AddScoped<BIImportUiService>();
        services.AddScoped<PublicViewState>();
        services.AddScoped<IImportacaoDadosProvider, BIImportacaoProvider>();
        services.AddScoped<JT_Servicos.DW.OrigemDadosService>();
        services.AddScoped<JT_Servicos.DW.DwConsultaService>();
        services.AddScoped<JT_Servicos.DW.IndicadorDashboardService>();

        return services;
    }
}

