using System;
using System.Linq;
using System.Runtime.CompilerServices;

namespace AutoCAD.Gateway.Services;

/// <summary>
/// Resuelve el servicio de Civil 3D adecuado según el host. Aísla la carga de AeccDbMgd:
/// solo se instancia <see cref="Civil3DService"/> si el ensamblado ya está cargado en el proceso.
/// </summary>
public static class Civil3DServiceFactory
{
    private static readonly ICivil3DService Nulo = new NullCivil3DService();
    private static ICivil3DService? _civil;

    /// <summary>Obtiene el servicio de Civil 3D (real si Civil 3D está activo, nulo en caso contrario).</summary>
    public static ICivil3DService Resolve()
    {
        if (_civil != null)
        {
            return _civil;
        }

        bool civilCargado = AppDomain.CurrentDomain.GetAssemblies()
            .Any(a => string.Equals(a.GetName().Name, "AeccDbMgd", StringComparison.OrdinalIgnoreCase));
        if (!civilCargado)
        {
            return Nulo;
        }

        _civil = CrearServicioReal();
        return _civil;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ICivil3DService CrearServicioReal() => new Civil3DService();
}
