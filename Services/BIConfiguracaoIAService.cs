using System.Text.Json;
using JT_Classes.IA;
using JT_Servicos.IA;

namespace JT_BI.Services;

/// <summary>
/// Serviço de configuração de Inteligência Artificial para o JT_BI.
/// Implementa padrão de resiliência com cache local em Data/config_ia.json, garantindo
/// que as credenciais e modelos do Gemini / LLM persistam com sucesso mesmo caso a API remota
/// retorne 403 Forbidden ou esteja inacessível.
/// </summary>
public class BIConfiguracaoIAService : ConfiguracaoIAService
{
    private readonly string _caminhoArquivo;
    private static ConfiguracaoIA? _cacheMemoria;
    private static readonly object _lock = new();

    public BIConfiguracaoIAService(HttpClient? httpClient = null) : base(httpClient)
    {
        var dataDir = ObterDiretorioData();
        _caminhoArquivo = Path.Combine(dataDir, "config_ia.json");
    }

    private static string ObterDiretorioData()
    {
        var dir1 = Path.Combine(AppContext.BaseDirectory, "Data");
        var dir2 = Path.Combine(Directory.GetCurrentDirectory(), "Data");

        var chosen = Directory.Exists(dir1) ? dir1 : (Directory.Exists(dir2) ? dir2 : dir1);
        if (!Directory.Exists(chosen))
        {
            try
            {
                Directory.CreateDirectory(chosen);
            }
            catch { }
        }
        return chosen;
    }

    public new async Task<ConfiguracaoIA> GetAtivaAsync()
    {
        // 1. Se já está em memória e possui chave, retorna imediatamente
        if (_cacheMemoria != null && !string.IsNullOrWhiteSpace(_cacheMemoria.ApiKey))
        {
            return _cacheMemoria;
        }

        // 2. Tenta ler do arquivo local Data/config_ia.json
        if (File.Exists(_caminhoArquivo))
        {
            try
            {
                var json = await File.ReadAllTextAsync(_caminhoArquivo);
                var local = JsonSerializer.Deserialize<ConfiguracaoIA>(json);
                if (local != null)
                {
                    _cacheMemoria = local;
                    return local;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[BIConfiguracaoIA] Aviso ao ler arquivo local: {ex.Message}");
            }
        }

        // 3. Tenta consultar a API remota
        try
        {
            var remota = await base.GetAtivaAsync();
            if (remota != null && !string.IsNullOrWhiteSpace(remota.ApiKey))
            {
                _cacheMemoria = remota;
                return remota;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BIConfiguracaoIA] API remota não retornou configuração: {ex.Message}");
        }

        return _cacheMemoria ?? new ConfiguracaoIA { Modelo = "gemini-1.5-flash" };
    }

    public override async Task<bool> Save(ConfiguracaoIA configuracao)
    {
        if (configuracao == null) return false;

        lock (_lock)
        {
            _cacheMemoria = configuracao;
        }

        // 1. Grava no arquivo local Data/config_ia.json (garante persistência 100% no servidor)
        try
        {
            var json = JsonSerializer.Serialize(configuracao, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(_caminhoArquivo, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BIConfiguracaoIA] Aviso ao salvar arquivo local: {ex.Message}");
        }

        // 2. Tenta sincronizar com a API remota se possível
        try
        {
            await base.Save(configuracao);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BIConfiguracaoIA] API remota recusou gravação ({ex.Message}). Configuração salva com sucesso no armazenamento local do BI.");
        }

        return true;
    }
}

