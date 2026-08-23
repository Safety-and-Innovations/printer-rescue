using System.Runtime.Versioning;

namespace PrinterRescue.Adapters.Windows.Winspool;

/// <summary>
/// Stub de compilação no Linux. A implementação P/Invoke real (winspool.drv)
/// é entregue pelo agente B1 e só executa em Windows.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class WinspoolNative
{
}
