namespace JT_BI.Models;

public class BIDashboard
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Nome { get; set; } = "Novo Painel de BI";
    public string Descricao { get; set; } = "Painel analítico customizado";
    public string Categoria { get; set; } = "Geral";
    public string Icone { get; set; } = "bi-speedometer2";
    public string CorDestaque { get; set; } = "primary";
    
    public DateTime DataCriacao { get; set; } = DateTime.Now;
    public DateTime DataAtualizacao { get; set; } = DateTime.Now;

    // Propriedade do painel (dashboards antigos sem dono permanecem compartilhados)
    public int? UsuarioId { get; set; }
    public string? UsuarioUid { get; set; }
    public string? UsuarioNome { get; set; }

    // Compartilhamento e Acesso Publico (Sem necessidade de login ativo)
    public bool PermiteAcessoPublico { get; set; } = false;
    public string PublicToken { get; set; } = Guid.NewGuid().ToString("N");
    public int AutoRefreshSegundos { get; set; } = 0; // 0 = desligado, 30, 60, 300
    public bool ExibirMenuPublico { get; set; } = false; // Exibir ou não o menu na página compartilhada

    public List<BIWidget> Widgets { get; set; } = new();
    public List<Guid> DatasetsVinculadosIds { get; set; } = new();
}