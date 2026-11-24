using System;
using AutoCAD.Gateway.Contracts;

namespace AutoCAD.Gateway.Core;

/// <summary>
/// Typed exception raised by Gateway services and transaction wrappers with stable error codes.
/// </summary>
public sealed class AcadGatewayException : Exception
{
    /// <summary>Stable machine-readable error code.</summary>
    public string Code { get; }

    /// <summary>Optional diagnostic detail.</summary>
    public string? Detail { get; }

    /// <summary>Initializes a new instance of the <see cref="AcadGatewayException"/> class.</summary>
    public AcadGatewayException(string code, string message, string? detail = null)
        : base(message)
    {
        Code = code;
        Detail = detail;
    }

    /// <summary>Initializes a new instance of the <see cref="AcadGatewayException"/> class with an inner exception.</summary>
    public AcadGatewayException(string code, string message, Exception innerException, string? detail = null)
        : base(message, innerException)
    {
        Code = code;
        Detail = detail;
    }

    /// <summary>Maps an internal error code to the corresponding HTTP status code.</summary>
    public int ToHttpStatusCode() => GatewayProtocol.MapErrorCodeToHttpStatus(Code);
}
