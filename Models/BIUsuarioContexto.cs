using JT_Classes.Acessos;

namespace JT_BI.Models;

public sealed record BIUsuarioContexto(int? UsuarioId, string? UsuarioUid, string? UsuarioNome, bool AcessoTotal)
{
    public bool TemIdentidade => UsuarioId is > 0 || !string.IsNullOrWhiteSpace(UsuarioUid);

    public static BIUsuarioContexto De(CurrentUserState estado)
    {
        return new BIUsuarioContexto(
            estado.UsuarioID > 0 ? estado.UsuarioID : null,
            string.IsNullOrWhiteSpace(estado.UsuarioUID) ? null : estado.UsuarioUID,
            estado.UsuarioName,
            EhAdministrador(estado));
    }

    private static bool EhAdministrador(CurrentUserState estado)
    {
        return string.Equals(estado.Role, "Administrador", StringComparison.OrdinalIgnoreCase)
            || string.Equals(estado.Role, "Supervisor", StringComparison.OrdinalIgnoreCase)
            || string.Equals(estado.UsuarioName, "supervisor", StringComparison.OrdinalIgnoreCase);
    }
}
