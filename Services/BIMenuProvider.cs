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
                NomeModulo = "Gestão",
                Icone = "bi-sliders",
                Modulo = eModuloSistema.Gestao,
                Itens = new List<SItemMenu>
                {
                    new SItemMenu { Url = "/gerenciar-categorias", Texto = "Categorias", Icone = "bi-tags-fill", Titulo = "Gerenciar Categorias" },
                    new SItemMenu { Titulo = "Regras Negócio", Texto = "Regras Negócio", Url = "/regras", Icone = "bi-airplane-engines-fill" },
                    new SItemMenu { Url = "/gestao/campos-personalizados", Texto = "Campos Adicionais", Icone = "bi-layout-text-sidebar-reverse", Titulo = "Campos Personalizados" },
                    new SItemMenu { Url = "/gestao/migrador", Texto = "Migrador BD", Icone = "bi-database-gear", Titulo = "Migrador de Banco de Dados" }
                }
            },
            new SModuloMenu
            {
                NomeModulo = "BI",
                Icone = "bi-speedometer2",
                // Usa um valor de enum distinto de eModuloSistema.Gestao para que o NavMenu
                // (que agrupa o estado aberto por Modulo.ToString()) não abra o menu de BI ao clicar em Gestão.
                Modulo = (eModuloSistema)14,
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
