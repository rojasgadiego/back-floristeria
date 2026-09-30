using System.Net.Mail;
using Colibri.Api.BLL;
using Colibri.Api.DAL;
using Microsoft.Extensions.Options;
using static Colibri.Api.Correo.PlantillaCorreo;

namespace Colibri.Api.Correo;

/// <summary>
/// Los avisos por correo. Cada método solo encola un id y vuelve al
/// instante: la venta, el cierre o la compra ya están guardados, y el correo
/// se arma y se manda después, en <see cref="EnvioCorreoWorker"/>.
///
///   Venta con cliente con correo  → comprobante al cliente
///   Venta con descuento autorizado → alerta interna
///   Venta anulada                 → alerta interna
///   Caja cerrada                  → resumen interno (con arqueo)
///   Compra recibida               → aviso interno
///   Merma sobre el umbral         → alerta interna
///
/// "Interno" es la lista Correo__Avisos. Esos correos llevan montos de todas
/// las ventas: no van nunca a un vendedor.
/// </summary>
public sealed class AvisosCorreo
{
    private const string PieInterno =
        "Aviso automático del sistema de la floristería. Llega a las direcciones configuradas en Correo__Avisos.";

    private readonly ColaCorreo _cola;
    private readonly CorreoOpciones _op;

    public AvisosCorreo(ColaCorreo cola, IOptions<CorreoOpciones> op)
    {
        _cola = cola;
        _op = op.Value;
    }

    private string[] Internos => _op.Avisos.Where(EsCorreo).ToArray();

    private static bool EsCorreo(string? c)
        => !string.IsNullOrWhiteSpace(c) && MailAddress.TryCreate(c.Trim(), out _);

    // ============================================================
    // Ventas
    // ============================================================

    public void VentaRegistrada(int ventaId) => _cola.Encolar("venta", async (sp, ct) =>
    {
        var ticket = await sp.GetRequiredService<VentasBLL>().Ticket(ventaId, ct);
        if (ticket is null) return [];

        var v = ticket.Venta;
        var salida = new List<MensajeCorreo>();

        // Descuento que tuvo que firmar un admin: pasó el umbral.
        if (!string.IsNullOrWhiteSpace(v.AutorizadoPor) && v.DescuentoManual > 0 && Internos.Length > 0)
        {
            salida.Add(new MensajeCorreo(Internos,
                $"Descuento autorizado en {v.Folio}: {Pesos(v.DescuentoManual)}",
                Pagina("Descuento autorizado",
                    Destacado($"{H(v.AutorizadoPor)} autorizó un descuento de <b>{Pesos(v.DescuentoManual)}</b>.") +
                    Datos(
                        ("Boleta", H(v.Folio)),
                        ("Fecha", Fecha(v.CreadoEn)),
                        ("Vendió", H(v.Usuario)),
                        ("Monto sin descuentos", Pesos(v.Bruto)),
                        ("Descuento manual", Pesos(v.DescuentoManual)),
                        ("Total cobrado", $"<b>{Pesos(v.Total)}</b>")),
                    PieInterno)));
        }

        // Comprobante al cliente, si dejó su correo.
        if (v.ClienteId is int clienteId)
        {
            var cliente = await sp.GetRequiredService<ClientesDAL>().ConsultarUno(clienteId, ct);
            if (EsCorreo(cliente?.Correo))
            {
                var local = ticket.Local;
                var nombreLocal = Dato(local, "nombre") ?? "Floristería Colibrí";
                var direccion = string.Join(", ", new[]
                    { Dato(local, "direccion"), Dato(local, "comuna"), Dato(local, "ciudad") }
                    .Where(x => x is not null));

                var descuentos = new List<(string, string)>();
                if (v.DescuentoPromo > 0) descuentos.Add(("Promoción" + (v.Promocion is null ? "" : $" ({v.Promocion})"), "-" + Pesos(v.DescuentoPromo)));
                if (v.DescuentoManual > 0) descuentos.Add(("Descuento", "-" + Pesos(v.DescuentoManual)));
                if (v.DescuentoCanje > 0) descuentos.Add(("Canje de puntos", "-" + Pesos(v.DescuentoCanje)));

                var puntos = v.PuntosGanados > 0
                    ? Parrafo($"Sumaste <b>{v.PuntosGanados}</b> puntos con esta compra.")
                    : "";

                salida.Add(new MensajeCorreo([cliente!.Correo!.Trim()],
                    $"Tu compra en {nombreLocal} · {v.Folio}",
                    Pagina("¡Gracias por tu compra!",
                        Parrafo($"Hola {H(cliente.Nombre)}, este es el detalle de tu compra del {Fecha(v.CreadoEn)}.") +
                        Lineas(v.Items.Select(i => (i.Nombre, (long)i.Cantidad, (long)i.Subtotal))) +
                        (descuentos.Count > 0
                            ? Datos(descuentos.Select(d => (d.Item1, H(d.Item2))).ToArray())
                            : "") +
                        Total("Total", v.Total) +
                        Datos(("Boleta", H(v.Folio)), ("Medio de pago", H(Medio(v.MedioPago)))) +
                        puntos,
                        $"{H(nombreLocal)}" +
                        (direccion.Length > 0 ? $" · {H(direccion)}" : "") +
                        (Dato(local, "telefono") is { } tel ? $" · WhatsApp {H(tel)}" : "") +
                        "<br>Este correo es un comprobante de tu compra y no reemplaza la boleta."),
                    Dato(local, "correo"))); // las respuestas del cliente van al correo del local
            }
        }

        return salida;
    });

    public void VentaAnulada(int ventaId) => _cola.Encolar("anulacion", async (sp, ct) =>
    {
        if (Internos.Length == 0) return [];

        var v = await sp.GetRequiredService<VentasBLL>().Obtener(ventaId, ct);
        if (v is null) return [];

        return [new MensajeCorreo(Internos,
            $"Venta anulada: {v.Folio} por {Pesos(v.Total)}",
            Pagina("Se anuló una venta",
                Destacado($"Motivo: {H(v.MotivoAnulacion)}") +
                Datos(
                    ("Boleta", H(v.Folio)),
                    ("Vendida el", Fecha(v.CreadoEn)),
                    ("Anulada el", Fecha(v.AnuladaEn)),
                    ("Vendió", H(v.Usuario)),
                    ("Cliente", H(v.Cliente ?? "—")),
                    ("Total", $"<b>{Pesos(v.Total)}</b>")),
                PieInterno))];
    });

    // ============================================================
    // Caja
    // ============================================================

    public void CajaCerrada(int cajaId) => _cola.Encolar("cierre-caja", async (sp, ct) =>
    {
        if (Internos.Length == 0) return [];

        // Siempre la vista completa, con esperado y diferencia: esto no lo
        // lee quien cerró sino la administración.
        var c = await sp.GetRequiredService<CajaDAL>().ConsultarCaja(cajaId, ct);
        if (c is null) return [];

        var diferencia = c.Diferencia ?? 0;
        var arqueo = diferencia == 0
            ? Parrafo("El efectivo contado cuadra con lo esperado.")
            : Destacado(diferencia < 0
                ? $"Faltan <b>{Pesos(-diferencia)}</b> en el cajón."
                : $"Sobran <b>{Pesos(diferencia)}</b> en el cajón.");

        return [new MensajeCorreo(Internos,
            $"Cierre de caja · {Pesos(c.TotalVendido)} en {c.Boletas} ventas" +
                (diferencia != 0 ? $" · diferencia {Pesos(diferencia)}" : ""),
            Pagina("Resumen de caja",
                Total("Vendido", c.TotalVendido) +
                arqueo +
                Datos(
                    ("Abierta", $"{Fecha(c.AbiertaEn)} · {H(c.AbiertaPor)}"),
                    ("Cerrada", $"{Fecha(c.CerradaEn)} · {H(c.CerradaPor)}"),
                    ("Ventas", c.Boletas.ToString()),
                    ("Anuladas", c.Anuladas.ToString()),
                    ("Efectivo", Pesos(c.Efectivo)),
                    ("Débito", Pesos(c.Debito)),
                    ("Crédito", Pesos(c.Credito)),
                    ("Transferencia", Pesos(c.Transferencia)),
                    ("Descuentos", Pesos(c.TotalDescuentos)),
                    ("Fondo inicial", Pesos(c.FondoInicial)),
                    ("Efectivo esperado", c.EfectivoEsperado is int e ? Pesos(e) : "—"),
                    ("Efectivo contado", c.EfectivoContado is int k ? Pesos(k) : "—")) +
                (string.IsNullOrWhiteSpace(c.NotaCierre) ? "" : Parrafo($"Nota del cierre: {H(c.NotaCierre)}")),
                PieInterno))];
    });

    // ============================================================
    // Compras
    // ============================================================

    public void CompraRecibida(int compraId) => _cola.Encolar("compra", async (sp, ct) =>
    {
        if (Internos.Length == 0) return [];

        var c = await sp.GetRequiredService<AbastecimientoBLL>().ObtenerDetalle(compraId, ct);
        if (c is null) return [];

        return [new MensajeCorreo(Internos,
            $"Compra recibida: {c.Proveedor} · {Pesos(c.Total)}",
            Pagina("Llegó una compra",
                Datos(
                    ("Compra", H(c.Folio)),
                    ("Proveedor", H(c.Proveedor)),
                    ("Documento", H(c.Documento ?? "—")),
                    ("Recibida", $"{Fecha(c.RecibidaEn)} · {H(c.Usuario)}"),
                    ("Varas", c.VarasTotales.ToString())) +
                Lineas(c.Items.Select(i => ($"{i.Producto} · {i.Presentacion}", (long)i.Cantidad, (long)i.Subtotal))) +
                Datos(("Neto", Pesos(c.Neto)), ("IVA", Pesos(c.Iva))) +
                Total("Total", c.Total) +
                (string.IsNullOrWhiteSpace(c.Notas) ? "" : Parrafo($"Notas: {H(c.Notas)}")),
                PieInterno))];
    });

    // ============================================================
    // Mermas
    // ============================================================

    /// <summary>Solo avisa las que tuvo que firmar un admin: las que pasan el umbral.</summary>
    public void MermaRegistrada(int mermaId) => _cola.Encolar("merma", async (sp, ct) =>
    {
        if (Internos.Length == 0) return [];

        var m = await sp.GetRequiredService<MermasDAL>().ConsultarUna(mermaId, null, ct);
        if (m is null || string.IsNullOrWhiteSpace(m.AutorizadoPor)) return [];

        return [new MensajeCorreo(Internos,
            $"Merma sobre el umbral: {m.Cantidad} × {m.Producto} · {Pesos(m.CostoPerdido)}",
            Pagina("Merma autorizada",
                Destacado($"{H(m.AutorizadoPor)} autorizó una merma de <b>{Pesos(m.CostoPerdido)}</b>.") +
                Datos(
                    ("Producto", H(m.Producto)),
                    ("Cantidad", m.Cantidad.ToString()),
                    ("Origen", H($"{m.Origen} {m.OrigenCodigo}".Trim())),
                    ("Motivo", H(m.Motivo)),
                    ("Detalle", H(m.Detalle ?? "—")),
                    ("Registró", H(m.Usuario)),
                    ("Fecha", Fecha(m.CreadoEn)),
                    ("Escaneada", m.Escaneado ? "Sí" : "No, se tipeó a mano")),
                PieInterno))];
    });
}
