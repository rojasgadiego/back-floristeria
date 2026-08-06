namespace Colibri.Api.Common.Validacion;

/// <summary>
/// RUT chileno: normalización y dígito verificador.
///
/// Validar el dígito importa más de lo que parece en un punto de venta. Un
/// RUT mal tipeado crea una ficha que nunca más se encuentra —quien atiende
/// busca por el RUT correcto y no aparece—, así que se registra otra vez y
/// los puntos quedan repartidos entre dos clientes que son la misma persona.
/// </summary>
public static class Rut
{
    /// <summary>
    /// Deja el RUT en la forma que usa el índice único de la base: sin puntos
    /// ni guion y en mayúsculas. 12.345.678-5 y 123456785 son el mismo.
    /// </summary>
    public static string Normalizar(string? rut)
        => (rut ?? string.Empty)
            .Replace(".", string.Empty)
            .Replace("-", string.Empty)
            .Replace(" ", string.Empty)
            .Trim()
            .ToUpperInvariant();

    /// <summary>
    /// Da formato para mostrar: 12.345.678-5
    /// </summary>
    public static string Formatear(string? rut)
    {
        var limpio = Normalizar(rut);
        if (limpio.Length < 2) return limpio;

        var cuerpo = limpio[..^1];
        var dv = limpio[^1];

        if (!cuerpo.All(char.IsDigit)) return limpio;

        var conPuntos = long.Parse(cuerpo).ToString("N0",
            System.Globalization.CultureInfo.GetCultureInfo("es-CL"));

        return $"{conPuntos}-{dv}";
    }

    /// <summary>
    /// Verifica el dígito con módulo 11, que es el algoritmo del Registro
    /// Civil. Detecta el error humano típico: un dígito cambiado o dos
    /// transpuestos.
    /// </summary>
    public static bool EsValido(string? rut)
    {
        var limpio = Normalizar(rut);

        // Menos de 7 caracteres no es un RUT de persona ni de empresa
        if (limpio.Length is < 7 or > 9) return false;

        var cuerpo = limpio[..^1];
        var dv = limpio[^1];

        if (!cuerpo.All(char.IsDigit)) return false;

        return dv == CalcularDv(cuerpo);
    }

    /// <summary>Dígito verificador que corresponde al cuerpo indicado.</summary>
    public static char CalcularDv(string cuerpo)
    {
        var suma = 0;
        var multiplicador = 2;

        // Se recorre de derecha a izquierda multiplicando por 2,3,4,5,6,7 y
        // volviendo a 2. Es lo que define el módulo 11.
        for (var i = cuerpo.Length - 1; i >= 0; i--)
        {
            suma += (cuerpo[i] - '0') * multiplicador;
            multiplicador = multiplicador == 7 ? 2 : multiplicador + 1;
        }

        var resto = 11 - (suma % 11);

        return resto switch
        {
            11 => '0',
            10 => 'K',
            _ => (char)('0' + resto)
        };
    }
}