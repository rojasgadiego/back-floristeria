namespace Colibri.Api.Auth;

/// <summary>
/// BCrypt.Net-Next lee $2a$ y $2b$ indistintamente, y el work factor viaja
/// dentro del propio hash: los usuarios viejos con $2b$10$ siguen entrando
/// aunque los nuevos se guarden con factor 12.
/// </summary>
public static class PasswordHasher
{
    public const int WorkFactor = 12;

    public static string Hash(string clave) => BCrypt.Net.BCrypt.HashPassword(clave, WorkFactor);

    /// <summary>
    /// Atrapa la excepción a propósito: un hash corrupto o de otro formato en la
    /// tabla no debe tumbar el login con un 500, solo negar el acceso a ese
    /// usuario.
    /// </summary>
    public static bool Verificar(string clave, string hash)
    {
        try { return BCrypt.Net.BCrypt.Verify(clave, hash); }
        catch { return false; }
    }
}
