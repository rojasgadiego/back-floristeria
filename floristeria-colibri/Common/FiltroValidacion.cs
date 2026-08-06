using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Colibri.Api.Common;

/// <summary>
/// Convierte los errores de validación al mismo formato de respuesta que usa
/// el resto de la API. Sin esto, un DTO inválido devuelve el ProblemDetails
/// de ASP.NET y el front tendría que manejar dos formas distintas de error.
/// </summary>
public class FiltroValidacion : IActionFilter
{
    public void OnActionExecuting(ActionExecutingContext contexto)
    {
        if (contexto.ModelState.IsValid) return;

        var errores = contexto.ModelState
            .Where(e => e.Value?.Errors.Count > 0)
            .SelectMany(e => e.Value!.Errors.Select(x =>
                string.IsNullOrWhiteSpace(x.ErrorMessage)
                    ? $"{e.Key}: valor no válido"
                    : x.ErrorMessage))
            .ToList();

        contexto.Result = new BadRequestObjectResult(
            ApiResponse<object>.Falla("Los datos enviados no son válidos.", errores));
    }

    public void OnActionExecuted(ActionExecutedContext contexto) { }
}
