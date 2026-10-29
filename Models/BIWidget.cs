namespace JT_BI.Models;

public class BIWidget
{
    private int _larguraColunas = 6;
    private double _largura = 50;

    public Guid Id { get; set; } = Guid.NewGuid();
    public string Titulo { get; set; } = "Indicador";
    public string Subtitulo { get; set; } = "";
    public BIWidgetTipo Tipo { get; set; } = BIWidgetTipo.CardKpi;

    // Posicionamento no Canvas Visual (%)
    public double PosX { get; set; } = 5;
    public double PosY { get; set; } = 5;

    /// <summary>
    /// Largura percentual no Canvas Visual (ex: 25% = 3 colunas, 33.3% = 4 colunas, 50% = 6 colunas, 66.7% = 8 colunas, 100% = 12 colunas).
    /// Mantém sincronia automática com a grade de colunas.
    /// </summary>
    public double Largura
    {
        get => _largura;
        set
        {
            _largura = value;
            if (value > 0)
            {
                if (value <= 28) _larguraColunas = 3;
                else if (value <= 38) _larguraColunas = 4;
                else if (value <= 58) _larguraColunas = 6;
                else if (value <= 78) _larguraColunas = 8;
                else _larguraColunas = 12;
            }
        }
    }

    public double Altura { get; set; } = 20;

    // Customizacao Visual
    public string CorTema { get; set; } = "primary"; // primary, success, info, warning, danger, purple, dark
    public string Icone { get; set; } = "bi-graph-up";
    public string FormatoValor { get; set; } = "Moeda"; // Moeda, Numero, Percentual, Decimal
    public decimal? MetaValor { get; set; }
    public decimal? ValorAtual { get; set; }
    public decimal? VariacaoPercentual { get; set; }

    // Vinculacao de Dados
    public string FonteDadosId { get; set; } = "financeiro"; // origem_{id}, dataset_{id} ou fonte demonstrativa
    public int? OrigemId { get; set; }
    public string MetricaCampo { get; set; } = "Faturamento";
    public string DimensaoCampo { get; set; } = "Periodo";
    public string? ColunaData { get; set; }
    public bool AgruparPorMes { get; set; }
    public string Agregacao { get; set; } = "Soma"; // Soma, Media, Contagem, Max, Min

    // Resultados de mineração ou dados materializados no próprio widget
    public List<BIDataPoint> DadosEstaticos { get; set; } = new();

    // Helpers e Compatibilidade de Grid / UI
    public string CorDestaque { get => CorTema; set => CorTema = value; }

    /// <summary>
    /// Quantidade de colunas Bootstrap na grade de 12 colunas (3, 4, 6, 8, 12).
    /// Mantém sincronia automática com a Largura percentual.
    /// </summary>
    public int LarguraColunas
    {
        get => _larguraColunas;
        set
        {
            _larguraColunas = value;
            _largura = value switch
            {
                <= 3 => 25,
                4 => 33.3,
                6 => 50,
                8 => 66.7,
                >= 12 => 100,
                _ => Math.Round((value / 12.0) * 100.0, 1)
            };
        }
    }

    public string? ValorKpiAtual { get; set; }
    public string? SubtituloKpi { get => Subtitulo; set => Subtitulo = value ?? string.Empty; }
}