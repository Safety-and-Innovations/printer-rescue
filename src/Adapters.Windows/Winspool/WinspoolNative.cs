using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace PrinterRescue.Adapters.Windows.Winspool;

/// <summary>
/// Declarações P/Invoke do winspool.drv. Classe interna, marcada como Windows-only:
/// só é referenciada por código já protegido por guarda OperatingSystem.IsWindows().
/// REGRA Nº 1: nenhuma função aqui instala ou baixa driver — AddPrinter usa apenas
/// drivers já presentes na máquina (ex.: Microsoft IPP Class Driver).
/// </summary>
[SupportedOSPlatform("windows")]
internal static partial class WinspoolNative
{
    public const uint PrinterEnumLocal = 2;
    public const uint PrinterEnumConnections = 4;

    public const uint PrinterControlPause = 1;
    public const uint PrinterControlResume = 2;

    public const int JobControlDelete = 3;

    public const uint PrinterAccessUse = 0x0000_0008;
    public const uint PrinterAccessAdminister = 0x0000_0004;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct PRINTER_INFO_4W
    {
        public IntPtr pPrinterName;
        public IntPtr pServerName;
        public uint Attributes;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct PRINTER_INFO_2W
    {
        public IntPtr pServerName;
        public IntPtr pPrinterName;
        public IntPtr pShareName;
        public IntPtr pPortName;
        public IntPtr pDriverName;
        public IntPtr pComment;
        public IntPtr pLocation;
        public IntPtr pDevMode;
        public IntPtr pSecDesc;
        public uint Attributes;
        public uint Priority;
        public uint DefaultPriority;
        public uint StartTime;
        public uint UntilTime;
        public uint Status;
        public uint cJobs;
        public uint AveragePPM;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SYSTEMTIME
    {
        public ushort wYear;
        public ushort wMonth;
        public ushort wDayOfWeek;
        public ushort wDay;
        public ushort wHour;
        public ushort wMinute;
        public ushort wSecond;
        public ushort wMilliseconds;

        public readonly DateTime ToDateTimeUtc()
            => new(wYear, wMonth, wDay, wHour, wMinute, wSecond, DateTimeKind.Utc);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct JOB_INFO_1W
    {
        public uint JobId;
        public IntPtr pPrinterName;
        public IntPtr pMachineName;
        public IntPtr pUserName;
        public IntPtr pDocument;
        public IntPtr pDatatype;
        public IntPtr pStatus;
        public uint Status;
        public uint Priority;
        public uint Position;
        public uint TotalPages;
        public uint PagesPrinted;
        public SYSTEMTIME Submitted;
    }

    [DllImport("winspool.drv", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool EnumPrintersW(
        uint flags,
        string? name,
        uint level,
        byte[]? printerEnum,
        uint cbBuf,
        out uint pcbNeeded,
        out uint pcReturned);

    [DllImport("winspool.drv", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool OpenPrinterW(
        string printerName,
        out IntPtr phPrinter,
        IntPtr pDefault);

    [DllImport("winspool.drv", SetLastError = true)]
    public static extern bool ClosePrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool GetPrinterW(
        IntPtr hPrinter,
        uint level,
        byte[]? printerInfo,
        uint cbBuf,
        out uint pcbNeeded);

    [DllImport("winspool.drv", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool EnumJobsW(
        IntPtr hPrinter,
        uint firstJob,
        uint noJobs,
        uint level,
        byte[]? jobEnum,
        uint cbBuf,
        out uint pcbNeeded,
        out uint pcReturned);

    [DllImport("winspool.drv", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool SetJobW(
        IntPtr hPrinter,
        uint jobId,
        uint level,
        byte[]? jobInfo,
        int command);

    [DllImport("winspool.drv", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool EnumPrinterDriversW(
        string? serverName,
        string? environment,
        uint level,
        byte[]? driverEnum,
        uint cbBuf,
        out uint pcbNeeded,
        out uint pcReturned);

    [DllImport("winspool.drv", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool DeletePrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr AddPrinterW(
        string? serverName,
        uint level,
        ref PRINTER_INFO_2W printerInfo);

    // ---- helpers de marshal -------------------------------------------------

    public static string PtrToString(IntPtr ptr)
        => ptr == IntPtr.Zero ? string.Empty : Marshal.PtrToStringUni(ptr) ?? string.Empty;
}
