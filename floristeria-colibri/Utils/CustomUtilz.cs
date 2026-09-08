using Colibri.Api.Dto;

namespace Colibri.Api.Utils;

public static class CustomUtilz
{
    public static ResponseDto CreateResponse(int status, string message, object? data)
        => new() { StatusCode = status, Message = message, Data = data };
}
