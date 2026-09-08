namespace Colibri.Api.Models.Tablas;

/// <summary>Las tarjetas de arriba de la grilla. Sale de sp_inv_c_resumen.</summary>
public class ResumenInventario
{
    public long Productos { get; set; }
    public long Activos { get; set; }

    /// <summary>Contra el total de los dos lados, no solo contra bodega.</summary>
    public long BajoMinimo { get; set; }

    public long SinStock { get; set; }

    /// <summary>Cuántos productos tienen existencias adelante.</summary>
    public long EnMostrador { get; set; }

    /// <summary>Unidades totales, sumando bodega y mostrador.</summary>
    public long Unidades { get; set; }

    /// <summary>Al costo, no al precio: es lo invertido, no lo que se ganaría.</summary>
    public decimal Valorizado { get; set; }
}