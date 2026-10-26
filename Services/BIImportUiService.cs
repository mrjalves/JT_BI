using JT_BI.Models;
using JT_UserInterface.Shared.Services.Interfaces.Comum;

namespace JT_BI.Services;

/// <summary>
/// Estado compartilhado do modal de importação do JT_BI. Permite que qualquer
/// ponto da interface (inclusive o assistente de IA) abra o BIImportModal.
/// </summary>
public class BIImportUiService
{
    public event Func<Task>? Solicitado;

    public Func<BIDashboard, Task>? Callback { get; private set; }

    public async Task AbrirAsync(Func<BIDashboard, Task>? callback = null)
    {
        Callback = callback;

        var handler = Solicitado;
        if (handler != null)
            await handler.Invoke();
    }

    public Func<BIDashboard, Task>? ConsumirCallback()
    {
        var callback = Callback;
        Callback = null;
        return callback;
    }
}

/// <summary>
/// Faz o assistente de IA abrir o BIImportModal em vez do modal padrão.
/// </summary>
public class BIImportacaoProvider : IImportacaoDadosProvider
{
    private readonly BIImportUiService _ui;

    public BIImportacaoProvider(BIImportUiService ui)
    {
        _ui = ui;
    }

    public bool PodeTratar(string uri) => true;

    public Task AbrirAsync() => _ui.AbrirAsync();
}
