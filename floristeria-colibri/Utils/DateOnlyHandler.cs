using System.Data;
using Dapper;

namespace Colibri.Api.Utils;

/// <summary>
/// Dapper no sabe convertir DateTime a DateOnly y falla con un
/// InvalidCastException que menciona la columna pero no la causa.
///
/// Npgsql sí soporta DateOnly de forma nativa, pero Dapper construye el
/// deserializador con reflexión sobre el tipo declarado en el modelo y hace
/// el cast por su cuenta, sin preguntarle al proveedor. Este handler le dice
/// cómo cruzar el puente en las dos direcciones.
///
/// Se registra una vez, en Program.cs, junto a MatchNamesWithUnderscores.
/// </summary>
public class DateOnlyHandler : SqlMapper.TypeHandler<DateOnly>
{
    public static readonly DateOnlyHandler Instancia = new();

    public override DateOnly Parse(object value) => value switch
    {
        DateOnly d => d,
        DateTime dt => DateOnly.FromDateTime(dt),
        string s => DateOnly.Parse(s),
        _ => throw new InvalidCastException(
            $"No se puede convertir {value?.GetType().Name ?? "null"} a DateOnly.")
    };

    public override void SetValue(IDbDataParameter parameter, DateOnly value)
    {
        parameter.DbType = DbType.Date;
        // Npgsql acepta DateOnly directo como parámetro; el cast a DateTime
        // sería una vuelta innecesaria que además arrastra una hora falsa.
        parameter.Value = value;
    }
}

/// <summary>Lo mismo para las columnas nulables.</summary>
public class DateOnlyNullableHandler : SqlMapper.TypeHandler<DateOnly?>
{
    public static readonly DateOnlyNullableHandler Instancia = new();

    public override DateOnly? Parse(object value) => value switch
    {
        null or DBNull => null,
        DateOnly d => d,
        DateTime dt => DateOnly.FromDateTime(dt),
        string s => DateOnly.Parse(s),
        _ => throw new InvalidCastException(
            $"No se puede convertir {value.GetType().Name} a DateOnly?.")
    };

    public override void SetValue(IDbDataParameter parameter, DateOnly? value)
    {
        parameter.DbType = DbType.Date;
        parameter.Value = value.HasValue ? value.Value : DBNull.Value;
    }
}

/// <summary>
/// Para `time` de Postgres. No lo usa ningún módulo todavía, pero cuando
/// aparezca una hora de apertura de caja va a fallar por lo mismo.
/// </summary>
public class TimeOnlyHandler : SqlMapper.TypeHandler<TimeOnly>
{
    public static readonly TimeOnlyHandler Instancia = new();

    public override TimeOnly Parse(object value) => value switch
    {
        TimeOnly t => t,
        TimeSpan ts => TimeOnly.FromTimeSpan(ts),
        DateTime dt => TimeOnly.FromDateTime(dt),
        string s => TimeOnly.Parse(s),
        _ => throw new InvalidCastException(
            $"No se puede convertir {value?.GetType().Name ?? "null"} a TimeOnly.")
    };

    public override void SetValue(IDbDataParameter parameter, TimeOnly value)
    {
        parameter.DbType = DbType.Time;
        parameter.Value = value;
    }
}