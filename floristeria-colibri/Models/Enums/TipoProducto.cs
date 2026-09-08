namespace Colibri.Api.Models.Enums;

/// <summary>
/// ⚠️ VERIFICA LAS ETIQUETAS antes de arrancar:
///
///     SELECT enumlabel FROM pg_enum e
///     JOIN pg_type t ON t.oid = e.enumtypid
///     WHERE t.typname = 'tipo_producto' ORDER BY enumsortorder;
///
/// Los nombres de los miembros tienen que calzar con esas etiquetas. Npgsql
/// traduce a snake_case por defecto, así que un miembro en minúscula calza con
/// una etiqueta en minúscula. Si no calzan, el MapEnum no falla al arrancar:
/// falla en el primer request que devuelva la columna.
/// </summary>
public enum TipoProducto
{
    simple,
    armado
}
