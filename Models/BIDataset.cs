namespace JT_BI.Models;

public class BIDataset
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Nome { get; set; } = "Novo Dataset";
    public string Origem { get; set; } = "CSV";
    public DateTime DataImportacao { get; set; } = DateTime.Now;
    public int QtdLinhas { get; set; }
    public long TamanhoBytes { get; set; }
    public string? CaminhoArquivo { get; set; }
    public string? Delimitador { get; set; }

    // Propriedade do dataset (datasets antigos sem dono permanecem compartilhados)
    public int? UsuarioId { get; set; }
    public string? UsuarioUid { get; set; }
    public string? UsuarioNome { get; set; }

    public List<BIDatasetColuna> Colunas { get; set; } = new();
    public List<Dictionary<string, object>> Linhas { get; set; } = new();
}