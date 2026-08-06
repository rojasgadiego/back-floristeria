using Microsoft.AspNetCore.Http;

namespace Colibri.Api.Common;

/// <summary>
/// Error esperable de reglas de negocio: "no alcanza el stock", "la caja está
/// cerrada". Se traduce a 400 y su mensaje sí se muestra al usuario, a
/// diferencia de una excepción no controlada.
/// </summary>
public class ExcepcionNegocio : Exception
{
    public int CodigoHttp { get; }

    public ExcepcionNegocio(string mensaje, int codigoHttp = StatusCodes.Status400BadRequest)
        : base(mensaje) => CodigoHttp = codigoHttp;
}

public class NoEncontradoException : ExcepcionNegocio
{
    public NoEncontradoException(string recurso)
        : base($"{recurso} no existe.", StatusCodes.Status404NotFound) { }
}
