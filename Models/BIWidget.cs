namespace JT_BI.Models;

public class BIWidget
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Titulo { get; set; } = "Indicador";
    public string Subtitulo { get; set; } = "";
    public BIWidgetTipo Tipo { get; set; } = BIWidgetTipo.CardKpi;

    // Posicionamento no Canvas Visual (%)
    public double PosX { get; set; } = 5;
    public double PosY { get; set; } = 5;
    public double Largura { get; set; } = 25;
    public double Altura { get; set; } = 20;

    // Customizacao Visual
    public string CorTema { get; set; } = "primary"; // primary, success, info, warning, danger, purple, dark
    public string Icone { get; set; } = "bi-graph-up";
    public string FormatoValor { get; set; } = "Moeda"; // Moeda, Numero, Percentual, Decimal
    public decimal? MetaValor { get; set; }
    public decimal? ValorAtual { get; set; }
    public decimal? VariacaoPercentual { get; set; }

    // Vinculacao de Dados
    public string FonteDadosId { get; set; } = "financeiro"; // financeiro, food, vendas, estoque, dataset_{id}
    public string MetricaCampo { get; set; } = "Faturamento";
    public string DimensaoCampo { get; set; } = "Periodo";
    public string Agregacao { get; set; } = "Soma"; // Soma, Media, Contagem, Max, Min
}