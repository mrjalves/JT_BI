using System;
using System.Threading.Tasks;
using JT_Classes.Acessos;
using Microsoft.JSInterop;

namespace JT_BI.Services;

/// <summary>
/// Implementação de armazenamento seguro para Blazor Web usando localStorage do navegador.
/// </summary>
public class WebSecureStorageService : ISecureStorageService
{
    private readonly IJSRuntime _jsRuntime;

    public WebSecureStorageService(IJSRuntime jsRuntime)
    {
        _jsRuntime = jsRuntime;
    }

    public async Task SetTokenAsync(string key, string value)
    {
        try
        {
            await _jsRuntime.InvokeVoidAsync("localStorage.setItem", key, value);
        }
        catch (InvalidOperationException)
        {
            // Ignora se estiver em pre-rendering
        }
        catch (JSDisconnectedException)
        {
            // Conexão do circuito Blazor encerrada
        }
    }

    public async Task<string?> GetTokenAsync(string key)
    {
        try
        {
            return await _jsRuntime.InvokeAsync<string?>("localStorage.getItem", key);
        }
        catch (InvalidOperationException)
        {
            // JS Interop indisponível durante prerendering
            return null;
        }
        catch (JSDisconnectedException)
        {
            return null;
        }
    }

    public void RemoveToken(string key)
    {
        try
        {
            _ = _jsRuntime.InvokeVoidAsync("localStorage.removeItem", key).AsTask();
        }
        catch (InvalidOperationException)
        {
            // Ignora se estiver em pre-rendering
        }
        catch (JSDisconnectedException)
        {
        }
    }
}
