using JT_BI.Models;

namespace JT_BI.Services;

/// <summary>
/// Estado reativo para a visualização pública/compartilhada de painéis.
/// Controla se a barra lateral deve ser renderizada, dados de cabeçalho e contadores.
/// </summary>
public class PublicViewState
{
    private bool _exibirMenu = false;
    private string _titulo = "Visualização do Painel";
    private string _descricao = string.Empty;
    private string _icone = "bi-speedometer2";
    private string _corDestaque = "primary";
    private int _autoRefreshSegundos = 0;
    private int _contadorRefresh = 0;
    private bool _isModoEscuro = false;

    public bool ExibirMenu
    {
        get => _exibirMenu;
        set { _exibirMenu = value; NotifyChanged(); }
    }

    public string Titulo
    {
        get => _titulo;
        set { _titulo = value; NotifyChanged(); }
    }

    public string Descricao
    {
        get => _descricao;
        set { _descricao = value; NotifyChanged(); }
    }

    public string Icone
    {
        get => _icone;
        set { _icone = value; NotifyChanged(); }
    }

    public string CorDestaque
    {
        get => _corDestaque;
        set { _corDestaque = value; NotifyChanged(); }
    }

    public int AutoRefreshSegundos
    {
        get => _autoRefreshSegundos;
        set { _autoRefreshSegundos = value; NotifyChanged(); }
    }

    public int ContadorRefresh
    {
        get => _contadorRefresh;
        set { _contadorRefresh = value; NotifyChanged(); }
    }

    public bool IsModoEscuro
    {
        get => _isModoEscuro;
        set { _isModoEscuro = value; NotifyChanged(); }
    }

    public event Action? OnChange;

    public void AlternarTema()
    {
        _isModoEscuro = !_isModoEscuro;
        NotifyChanged();
    }

    public void Configurar(BIDashboard? dashboard)
    {
        if (dashboard != null)
        {
            _titulo = string.IsNullOrWhiteSpace(dashboard.Nome) ? "Painel Sem Nome" : dashboard.Nome;
            _descricao = dashboard.Descricao ?? string.Empty;
            _icone = string.IsNullOrWhiteSpace(dashboard.Icone) ? "bi-speedometer2" : dashboard.Icone;
            _corDestaque = string.IsNullOrWhiteSpace(dashboard.CorDestaque) ? "primary" : dashboard.CorDestaque;
            _exibirMenu = dashboard.ExibirMenuPublico;
            _autoRefreshSegundos = dashboard.AutoRefreshSegundos;
            _contadorRefresh = dashboard.AutoRefreshSegundos;
        }
        else
        {
            _titulo = "Visualização do Painel";
            _descricao = string.Empty;
            _icone = "bi-speedometer2";
            _corDestaque = "primary";
            _exibirMenu = false;
            _autoRefreshSegundos = 0;
            _contadorRefresh = 0;
        }
        NotifyChanged();
    }

    public void AtualizarContador(int novoValor)
    {
        _contadorRefresh = novoValor;
        NotifyChanged();
    }

    private void NotifyChanged() => OnChange?.Invoke();
}
