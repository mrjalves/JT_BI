using JT_BI.Models;
using JT_Classes.DW;
using JT_UserInterface.Shared.Services.Interfaces.Comum;

namespace JT_BI.Services;

/// <summary>
/// Estado compartilhado do modal de importação do JT_BI. Permite que qualquer
/// ponto da interface (inclusive o assistente de IA e o gerenciador de fontes) abra o BIImportModal.
/// </summary>
public class BIImportUiService
{
    public event Func<Task>? Solicitado;
    public event Func<MOrigemDados, Task>? FonteImportada;

    public Func<BIDashboard, Task>? Callback { get; private set; }
    public Func<MOrigemDados, Task>? CallbackFonte { get; private set; }

    public async Task AbrirAsync(
        Func<BIDashboard, Task>? callback = null,
        Func<MOrigemDados, Task>? callbackFonte = null)
    {
        Callback = callback;
        CallbackFonte = callbackFonte;

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

    public async Task NotificarFonteImportadaAsync(MOrigemDados origem)
    {
        var cbFonte = CallbackFonte;
        CallbackFonte = null;
        if (cbFonte != null)
            await cbFonte(origem);

        var handler = FonteImportada;
        if (handler != null)
            await handler.Invoke(origem);
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
