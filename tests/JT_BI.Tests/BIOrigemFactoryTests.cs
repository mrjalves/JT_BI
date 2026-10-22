using JT_BI.Services;
using JT_Classes.DW;

namespace JT_BI.Tests;

public class BIOrigemFactoryTests
{
    private static EntidadeModeloDto Entidade()
    {
        var entidade = new EntidadeModeloDto
        {
            Classe = "JT_Classes.Financeiro.MConta, JT_Classes",
            Tabela = "Contas",
            Colunas = new List<ColunaModeloDto>
            {
                new() { NomeColuna = "Id", EhPk = true },
                new() { NomeColuna = "Nome" },
                new() { NomeColuna = "Valor" }
            }
        };

        entidade.Relacionamentos.Add(new RelacionamentoModeloDto
        {
            Propriedade = "Categoria",
            TabelaAlvo = "Categorias",
            ClasseAlvo = "JT_Classes.Financeiro.MCategoria",
            Colunas = new List<ColunaModeloDto>
            {
                new() { NomeColuna = "Id" },
                new() { NomeColuna = "Descricao" }
            }
        });

        entidade.Relacionamentos.Add(new RelacionamentoModeloDto
        {
            Propriedade = "CategoriaPai",
            TabelaAlvo = "Categorias",
            ClasseAlvo = "JT_Classes.Financeiro.MCategoria",
            Colunas = new List<ColunaModeloDto>
            {
                new() { NomeColuna = "Id" },
                new() { NomeColuna = "Nome" }
            }
        });

        entidade.Relacionamentos.Add(new RelacionamentoModeloDto
        {
            Propriedade = "SemColunas",
            TabelaAlvo = "Vazia",
            Colunas = new List<ColunaModeloDto>()
        });

        return entidade;
    }

    [Test]
    public void NomeSimples_RemoveNamespaceEAssembly()
    {
        Assert.That(BIOrigemFactory.NomeSimples("JT_Classes.Financeiro.MConta, JT_Classes"), Is.EqualTo("MConta"));
    }

    [Test]
    public void NomeSugerido_UsaTabela()
    {
        Assert.That(BIOrigemFactory.NomeSugerido(Entidade()), Is.EqualTo("Contas"));
    }

    [Test]
    public void ConstruirConfiguracao_IncluiTodasAsColunasSemDuplicatas()
    {
        var config = BIOrigemFactory.ConstruirConfiguracao(Entidade(), incluirRelacionamentos: false);

        Assert.Multiple(() =>
        {
            Assert.That(config.ClasseOrigem, Is.EqualTo("JT_Classes.Financeiro.MConta, JT_Classes"));
            Assert.That(config.TabelaOrigem, Is.EqualTo("Contas"));
            Assert.That(config.Colunas, Is.EqualTo(new[] { "Id", "Nome", "Valor" }));
            Assert.That(config.Relacionamentos, Is.Empty);
        });
    }

    [Test]
    public void ConstruirConfiguracao_IncluiRelacionamentosComAliasesUnicos()
    {
        var config = BIOrigemFactory.ConstruirConfiguracao(Entidade());

        Assert.Multiple(() =>
        {
            Assert.That(config.Relacionamentos, Has.Count.EqualTo(2));
            Assert.That(config.Relacionamentos[0].Alias, Is.EqualTo("Categorias"));
            Assert.That(config.Relacionamentos[1].Alias, Is.EqualTo("Categorias_2"));
            Assert.That(config.Relacionamentos[0].Colunas, Is.EqualTo(new[] { "Id", "Descricao" }));
            Assert.That(config.Relacionamentos.All(r => r.Colunas.Count > 0), Is.True);
        });
    }

    [Test]
    public void ComRelacionamentos_PreservaColunasPersonalizadasAoEditar()
    {
        var existente = new OrigemConfiguracao
        {
            ClasseOrigem = "JT_Classes.Financeiro.MConta, JT_Classes",
            TabelaOrigem = "Contas",
            Colunas = new List<string> { "Id", "Valor" },
            Relacionamentos = new List<OrigemRelacionamentoConfig>
            {
                new() { Propriedade = "Categoria", Alias = "Cat", Colunas = new List<string> { "Descricao" } }
            }
        };

        var config = BIOrigemFactory.ComRelacionamentos(Entidade(), existente, incluirRelacionamentos: true);

        Assert.Multiple(() =>
        {
            Assert.That(config.Colunas, Is.EqualTo(new[] { "Id", "Valor" }));
            Assert.That(config.Relacionamentos, Has.Count.EqualTo(1));
            Assert.That(config.Relacionamentos[0].Alias, Is.EqualTo("Cat"));
        });
    }

    [Test]
    public void ComRelacionamentos_RemoveRelacionamentosQuandoDesativado()
    {
        var existente = new OrigemConfiguracao
        {
            Colunas = new List<string> { "Id" },
            Relacionamentos = new List<OrigemRelacionamentoConfig>
            {
                new() { Propriedade = "Categoria", Alias = "Cat" }
            }
        };

        var config = BIOrigemFactory.ComRelacionamentos(Entidade(), existente, incluirRelacionamentos: false);

        Assert.Multiple(() =>
        {
            Assert.That(config.Colunas, Is.EqualTo(new[] { "Id" }));
            Assert.That(config.Relacionamentos, Is.Empty);
        });
    }
}
