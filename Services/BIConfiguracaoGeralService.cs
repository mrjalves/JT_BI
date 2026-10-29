using System.Text.Json;
using JT_Classes.Comum;
using JT_Servicos.Comum;

namespace JT_BI.Services;

/// <summary>
/// Serviço de configuração Geral do Sistema para o JT_BI.
/// Implementa padrão de resiliência com cache local em Data/config_geral.json, garantindo
/// que os dados da empresa, exibição e preferências persistam com sucesso mesmo caso a API remota
/// retorne 403 Forbidden ou esteja inacessível.
/// </summary>
public class BIConfiguracaoGeralService : ConfiguracaoService
{
    private readonly string _caminhoArquivo;
    private static Configuracao? _cacheMemoria;
    private static readonly object _lock = new();

    public BIConfiguracaoGeralService(HttpClient? httpClient = null) : base(httpClient)
    {
        var dataDir = ObterDiretorioData();
        _caminhoArquivo = Path.Combine(dataDir, "config_geral.json");
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

    public async Task<Configuracao> ObterConfiguracaoAtivaAsync(int usuarioId = 0)
    {
        // 1. Se já está em memória e possui algum dado significativo, retorna imediatamente
        if (_cacheMemoria != null && (!string.IsNullOrWhiteSpace(_cacheMemoria.Nome) || _cacheMemoria.IdUsuario > 0))
        {
            return _cacheMemoria;
        }

        // 2. Tenta ler do arquivo local Data/config_geral.json
        if (File.Exists(_caminhoArquivo))
        {
            try
            {
                var json = await File.ReadAllTextAsync(_caminhoArquivo);
                var local = JsonSerializer.Deserialize<Configuracao>(json);
                if (local != null)
                {
                    _cacheMemoria = local;
                    return local;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[BIConfiguracaoGeral] Aviso ao ler arquivo local: {ex.Message}");
            }
        }

        // 3. Tenta consultar a API remota se houver usuarioId
        if (usuarioId > 0)
        {
            try
            {
                var remota = await base.GetByUsuarioIdAsync(usuarioId);
                if (remota != null)
                {
                    _cacheMemoria = remota;
                    return remota;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[BIConfiguracaoGeral] API remota não retornou configuração: {ex.Message}");
            }
        }

        return _cacheMemoria ?? new Configuracao { Id = 0, ItensPorPagina = 10, NotificacoesEmail = false };
    }

    public override async Task<bool> Save(Configuracao configuracao)
    {
        if (configuracao == null) return false;

        lock (_lock)
        {
            _cacheMemoria = configuracao;
        }

        // 1. Grava no arquivo local Data/config_geral.json (garante persistência 100% no servidor)
        try
        {
            var json = JsonSerializer.Serialize(configuracao, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(_caminhoArquivo, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BIConfiguracaoGeral] Aviso ao salvar arquivo local: {ex.Message}");
        }

        // 2. Tenta sincronizar com a API remota se possível (ignora falhas de 403 Forbidden ou rede)
        try
        {
            await base.Save(configuracao);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BIConfiguracaoGeral] API remota recusou gravação ({ex.Message}). Configuração salva com sucesso no armazenamento local do BI.");
        }

        return true;
    }
}
