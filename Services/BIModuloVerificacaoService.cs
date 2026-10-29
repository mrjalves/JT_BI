using JT_Aplicacao.Classes;
using JT_UserInterface.Shared.Services.Interfaces.Comum;

namespace JT_BI.Services;

/// <summary>
/// Decorador do IModuloVerificacaoService para assegurar que módulos essenciais do ecossistema analítico
/// (como Inteligência Artificial - Gemini e Business Intelligence) estejam permanentemente ativos no JT_BI,
/// permitindo a exibição e salvamento da aba de IA padrão em /configuracoes.
/// </summary>
public class BIModuloVerificacaoService : IModuloVerificacaoService
{
    private readonly IModuloVerificacaoService _inner;

    public BIModuloVerificacaoService(IModuloVerificacaoService inner)
    {
        _inner = inner;
    }

    public async Task<List<eModuloSistema>> GetModulosAtivosAsync()
    {
        var modulos = await _inner.GetModulosAtivosAsync() ?? new List<eModuloSistema>();
        if (!modulos.Contains(eModuloSistema.IA))
            modulos.Add(eModuloSistema.IA);
        if (!modulos.Contains(eModuloSistema.Comum))
            modulos.Add(eModuloSistema.Comum);
        return modulos;
    }

    public async Task<bool> IsModuloAtivoAsync(eModuloSistema modulo)
    {
        if (modulo == eModuloSistema.IA || modulo == eModuloSistema.Comum)
            return true;

        return await _inner.IsModuloAtivoAsync(modulo);
    }
}

