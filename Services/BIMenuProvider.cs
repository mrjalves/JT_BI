using JT_Aplicacao.Classes;
using JT_Aplicacao.UserInterface;
using JT_UserInterface.Shared.Services.Interfaces.Comum;

namespace JT_BI.Services;

/// <summary>
/// Provider do menu lateral para o módulo JT_BI.
/// Implementa IModuloMenuProvider para exibir itens agrupados por módulo no MainLayout compartilhado.
/// Implementa também IMenuProvider para retrocompatibilidade.
/// </summary>
public class BIMenuProvider : IModuloMenuProvider, IMenuProvider
{
    public List<SModuloMenu> GetModulos()
    {
        return new List<SModuloMenu>
        {
            new SModuloMenu
            {
                NomeModulo = "Business Intelligence",
                Icone = "bi-speedometer2",
                Modulo = eModuloSistema.Gestao,
                Itens = new List<SItemMenu>
                {
                    new SItemMenu
                    {
                        Url = "",
                        Texto = "Studio BI (Canvas)",
                        Icone = "bi-bounding-box-circles",
                        MatchAll = true,
                        Titulo = "JT_BI Studio"
                    },
                    new SItemMenu
                    {
                        Url = "dashboards",
                        Texto = "Hub de Dashboards",
                        Icone = "bi-grid-fill",
                        MatchAll = false,
                        Titulo = "Todos os Dashboards"
                    }
                }
            }
        };
    }

    public List<SItemMenu> GetMenu()
        => GetModulos().SelectMany(m => m.Itens).ToList();
}
