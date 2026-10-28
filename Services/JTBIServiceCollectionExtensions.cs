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
        var dadosFConfig = secaoFConfig.Get<FirebaseConfig>() ?? new FirebaseConfig();

        if (string.IsNullOrWhiteSpace(dadosFConfig.BaseApiUrl))
        {
            dadosFConfig.BaseApiUrl = dadosConfig.Conexao?.BaseApiUrl ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(dadosFConfig.DatabaseUrl))
        {
            dadosFConfig.DatabaseUrl = dadosFConfig.BaseApiUrl;
        }

        if (string.IsNullOrWhiteSpace(dadosFConfig.BaseApiUrl))
        {
            throw new InvalidOperationException(
                "Configuração 'Config:Conexao:BaseApiUrl' ou 'Config:FirebaseConfig:BaseApiUrl' é obrigatória.");
        }

        FirebaseConfig.SetInstancia(dadosFConfig);
        services.AddSingleton(FirebaseConfig.Instancia);

        // Registrar Camada de Interface Compartilhada e Segurança
        services.AddScoped<ISecureStorageService, WebSecureStorageService>();
        services.AddJTUserInterface();

        // Remove o GestaoMenuProvider padrão para não exibir as páginas descontinuadas (/gestao/edw e /gestao/indicadores)
        var gestaoProviderDescriptor = services.FirstOrDefault(d =>
            d.ServiceType == typeof(IModuloMenuProvider) &&
            d.ImplementationType == typeof(JT_UserInterface.Shared.Layout.GestaoMenuProvider));
        if (gestaoProviderDescriptor != null)
        {
            services.Remove(gestaoProviderDescriptor);
        }

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

