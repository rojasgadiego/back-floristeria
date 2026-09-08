namespace Colibri.Api.Utils;

/// <summary>
/// Para las operaciones que además de "salió bien / salió mal" devuelven datos
/// que el cliente necesita —el producto creado, con su categoría y su margen—.
///
/// Donde no hay datos que devolver (eliminar), se sigue usando string: "" es
/// éxito, exactamente el contrato del @dg_resultado del legacy.
/// </summary>
public sealed record ResultadoOp<T>(bool Ok, string Mensaje, T? Datos)
{
    public static ResultadoOp<T> Exito(T datos) => new(true, string.Empty, datos);
    public static ResultadoOp<T> Error(string mensaje) => new(false, mensaje, default);
}
