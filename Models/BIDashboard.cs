namespace JT_BI.Models;

public class BIDashboard
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Nome { get; set; } = "Novo Painel de BI";
    public string Descricao { get; set; } = "Painel analÃ­tico customizado";
    public string Categoria { get; set; } = "Geral";
    public string Icone { get; set; } = "bi-speedometer2";
    public string CorDestaque { get; set; } = "primary";
    
    public DateTime DataCriacao { get; set; } = DateTime.Now;
    public DateTime DataAtualizacao { get; set; } = DateTime.Now;

    // Compartilhamento e Acesso Publico (Sem necessidade de login ativo)
    public bool PermiteAcessoPublico { get; set; } = true;
    public string PublicToken { get; set; } = Guid.NewGuid().ToString("N");
    public int AutoRefreshSegundos { get; set; } = 0; // 0 = desligado, 30, 60, 300

    public List<BIWidget> Widgets { get; set; } = new();
    public List<Guid> DatasetsVinculadosIds { get; set; } = new();
}