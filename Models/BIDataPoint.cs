namespace JT_BI.Models;

public class BIDataPoint
{
    public string Rotulo { get; set; } = string.Empty;
    public decimal Valor { get; set; }
    public string ValorFormatado { get; set; } = string.Empty;
    public string Cor { get; set; } = "#3b82f6";
    public decimal? Percentual { get; set; }
}