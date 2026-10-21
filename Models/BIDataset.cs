namespace JT_BI.Models;

public class BIDataset
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Nome { get; set; } = "Novo Dataset";
    public string Origem { get; set; } = "CSV";
    public DateTime DataImportacao { get; set; } = DateTime.Now;
    public int QtdLinhas { get; set; }
    public List<BIDatasetColuna> Colunas { get; set; } = new();
    public List<Dictionary<string, object>> Linhas { get; set; } = new();
}