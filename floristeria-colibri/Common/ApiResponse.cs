namespace Colibri.Api.Common;

/// <summary>
/// Envoltura uniforme de las respuestas, equivalente al TransformInterceptor
/// que tenías en Nest. Que todas las respuestas tengan la misma forma le
/// ahorra al front tener que adivinar dónde viene el dato en cada endpoint.
/// </summary>
public class ApiResponse<T>
{
    public bool Exito { get; init; }
    public T? Datos { get; init; }
    public string? Mensaje { get; init; }
    public IEnumerable<string>? Errores { get; init; }

    public static ApiResponse<T> Ok(T datos, string? mensaje = null) =>
        new() { Exito = true, Datos = datos, Mensaje = mensaje };

    public static ApiResponse<T> Falla(string mensaje, IEnumerable<string>? errores = null) =>
        new() { Exito = false, Mensaje = mensaje, Errores = errores };
}

/// <summary>Página de resultados con el total, para las tablas del front.</summary>
public class Pagina<T>
{
    public IReadOnlyList<T> Items { get; init; } = Array.Empty<T>();
    public int Total { get; init; }
    public int Pagina_ { get; init; }
    public int PorPagina { get; init; }
    public int TotalPaginas => PorPagina > 0 ? (int)Math.Ceiling(Total / (double)PorPagina) : 0;
}
