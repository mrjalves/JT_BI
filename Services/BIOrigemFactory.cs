using JT_Classes.DW;

namespace JT_BI.Services;

public static class BIOrigemFactory
{
    public static string NomeSimples(string? nome)
    {
        if (string.IsNullOrWhiteSpace(nome)) return string.Empty;

        var semAssembly = nome.Split(',')[0].Trim();
        return semAssembly.Split('.').Last();
    }

    public static string NomeSugerido(EntidadeModeloDto entidade)
    {
        var nome = NomeSimples(entidade.Tabela);
        return string.IsNullOrWhiteSpace(nome) ? NomeSimples(entidade.Classe) : nome;
    }

    public static OrigemConfiguracao ConstruirConfiguracao(EntidadeModeloDto entidade, bool incluirRelacionamentos = true)
    {
        ArgumentNullException.ThrowIfNull(entidade);

        var config = new OrigemConfiguracao
        {
            ClasseOrigem = entidade.Classe,
            TabelaOrigem = entidade.Tabela,
            Colunas = entidade.Colunas
                .Select(c => c.NomeColuna)
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList()
        };

        if (!incluirRelacionamentos || entidade.Relacionamentos == null)
            return config;

        var aliasesUsados = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var relacao in entidade.Relacionamentos)
        {
            var colunas = (relacao.Colunas ?? new List<ColunaModeloDto>())
                .Select(c => c.NomeColuna)
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (colunas.Count == 0)
                continue;

            var aliasBase = NomeSimples(relacao.TabelaAlvo);
            if (string.IsNullOrWhiteSpace(aliasBase))
                aliasBase = relacao.Propriedade;

            var alias = aliasBase;
            var sufixo = 2;
            while (!aliasesUsados.Add(alias))
            {
                alias = $"{aliasBase}_{sufixo++}";
            }

            config.Relacionamentos.Add(new OrigemRelacionamentoConfig
            {
                Propriedade = relacao.Propriedade,
                Alias = alias,
                Colunas = colunas
            });
        }

        return config;
    }

    public static OrigemConfiguracao ComRelacionamentos(EntidadeModeloDto entidade, OrigemConfiguracao? existente, bool incluirRelacionamentos)
    {
        var completa = ConstruirConfiguracao(entidade, incluirRelacionamentos);

        if (existente == null)
            return completa;

        return new OrigemConfiguracao
        {
            ClasseOrigem = completa.ClasseOrigem,
            TabelaOrigem = completa.TabelaOrigem,
            Colunas = existente.Colunas.Count > 0 ? existente.Colunas : completa.Colunas,
            Relacionamentos = incluirRelacionamentos
                ? (existente.Relacionamentos.Count > 0 ? existente.Relacionamentos : completa.Relacionamentos)
                : new List<OrigemRelacionamentoConfig>()
        };
    }
}
