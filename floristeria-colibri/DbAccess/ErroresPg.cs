using Npgsql;

namespace Colibri.Api.DbAccess;

/// <summary>
/// EL REEMPLAZO DEL @dg_resultado.
///
/// En SQL Server el SP devolvía un parámetro de salida: vacío = éxito, texto =
/// error de negocio. Acá el equivalente es que la función haga
///
///     RAISE EXCEPTION 'Ya existe un producto con el código ROSA-01.';
///
/// que llega como PostgresException con SqlState 'P0001' y el texto en
/// MessageText. Se traduce a string para que el DAL siga devolviendo
/// "vacío = todo bien, texto = error de negocio".
///
/// Todo lo que NO esté acá se deja propagar: es un bug, no una regla de
/// negocio, y tiene que llegar al log como 500.
/// </summary>
public static class ErroresPg
{
    public static bool EsDeNegocio(PostgresException ex) => ex.SqlState switch
    {
        "P0001" => true,   // RAISE EXCEPTION de las funciones
        "23505" => true,   // unique_violation
        "23503" => true,   // foreign_key_violation
        "23514" => true,   // check_violation
        "23502" => true,   // not_null_violation
        "23P01" => true,   // exclusion_violation
        "22003" => true,   // numeric_value_out_of_range
        _ => false
    };

    /// <summary>
    /// P0001 va tal cual: ese texto ya lo escribiste pensando en el usuario
    /// final. Los demás códigos traen jerga de Postgres y se reformulan.
    /// </summary>
    public static string Mensaje(PostgresException ex) => ex.SqlState switch
    {
        "P0001" => ex.MessageText,
        "23505" => $"Ya existe un registro con ese valor ({ex.ConstraintName}).",
        "23503" => "El registro está referenciado por otros datos y no se puede modificar.",
        "23514" => $"Dato inválido: no cumple la regla {ex.ConstraintName}.",
        "23502" => $"Falta un campo obligatorio ({ex.ColumnName}).",
        "23P01" => "El registro se superpone con otro existente.",
        "22003" => "Un valor numérico está fuera del rango permitido.",
        _ => "Error de base de datos."
    };

    /// <summary>Para el middleware, cuando la excepción se escapa del DAL.</summary>
    public static int HttpStatus(PostgresException ex) => ex.SqlState switch
    {
        "23505" or "23503" or "23P01" => 409,
        "P0001" or "23514" or "23502" or "22003" => 400,
        _ => 500
    };
}
