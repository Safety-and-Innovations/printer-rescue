using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace PrinterRescue.Adapters.Windows.Winspool;

/// <summary>
/// Parte de alto nível do adapter: enumerações e leituras compostas sobre os
/// P/Invoke declarados em WinspoolNative (parte gerada por LibraryImport).
/// </summary>
[SupportedOSPlatform("windows")]
internal static partial class WinspoolNative
{
    public sealed record InfoImpressora(string Nome, string? ShareName, string? PortName, string? DriverName, string? DriverVersion);

    public sealed record Job(uint JobId, uint Status, DateTime SubmittedUtc);

    public sealed record DriverLocal(string Nome, string? Versao, string? InfName);

    public sealed record DevModeLocal(int PaperSize, int Copies, int Color, int Duplex);

    public static List<string> EnumPrinterNames()
    {
        var nomes = new List<string>();
        var ok = EnumPrintersW(
            PrinterEnumLocal | PrinterEnumConnections,
            null, 4 /* PRINTER_INFO_4 */,
            null, 0, out var needed, out var returned);
        if (!ok && needed == 0)
        {
            return nomes;
        }

        var buffer = new byte[needed];
        if (!EnumPrintersW(PrinterEnumLocal | PrinterEnumConnections, null, 4, buffer, needed, out _, out returned))
        {
            return nomes;
        }

        return LerNomesDoBuffer(buffer, returned);
    }

    /// <summary>Leitura correta do array embutido de PRINTER_INFO_4W no buffer.</summary>
    private static List<string> LerNomesDoBuffer(byte[] buffer, uint count)
    {
        var nomes = new List<string>((int)count);
        var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        try
        {
            var basePtr = handle.AddrOfPinnedObject();
            var size = Marshal.SizeOf<PRINTER_INFO_4W>();
            for (var i = 0; i < count; i++)
            {
                var elementPtr = IntPtr.Add(basePtr, i * size);
                var info = Marshal.PtrToStructure<PRINTER_INFO_4W>(elementPtr);
                var nome = PtrToString(info.pPrinterName);
                if (nome.Length > 0)
                {
                    nomes.Add(nome);
                }
            }
        }
        finally
        {
            handle.Free();
        }

        return nomes;
    }

    public static bool ImpressoraExiste(string nome)
    {
        if (!OpenPrinterW(nome, out var handle, IntPtr.Zero))
        {
            return false;
        }

        _ = ClosePrinter(handle);
        return true;
    }

    public static InfoImpressora? ObterInfoImpressora(string nome)
    {
        if (!OpenPrinterW(nome, out var handle, IntPtr.Zero))
        {
            return null;
        }

        try
        {
            if (!GetPrinterW(handle, 2, null, 0, out var needed) || needed == 0)
            {
                return null;
            }

            var buffer = new byte[needed];
            if (!GetPrinterW(handle, 2, buffer, needed, out _))
            {
                return null;
            }

            var h = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            try
            {
                var info2 = Marshal.PtrToStructure<PRINTER_INFO_2W>(h.AddrOfPinnedObject());
                return new InfoImpressora(
                    nome,
                    ShareName: PtrToOptional(info2.pShareName),
                    PortName: PtrToOptional(info2.pPortName),
                    DriverName: PtrToOptional(info2.pDriverName),
                    DriverVersion: null); // versão vem do repositório de drivers, abaixo
                ;
            }
            finally
            {
                h.Free();
            }
        }
        finally
        {
            _ = ClosePrinter(handle);
        }
    }

    public static List<Job> ObterJobs(string impressora)
    {
        var jobs = new List<Job>();
        if (!OpenPrinterW(impressora, out var handle, IntPtr.Zero))
        {
            return jobs;
        }

        try
        {
            if (!EnumJobsW(handle, 0, 0xFFFF, 1, null, 0, out var needed, out _) || needed == 0)
            {
                return jobs;
            }

            var buffer = new byte[needed];
            if (!EnumJobsW(handle, 0, 0xFFFF, 1, buffer, needed, out _, out var returned))
            {
                return jobs;
            }

            var h = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            try
            {
                var size = Marshal.SizeOf<JOB_INFO_1W>();
                for (var i = 0; i < returned; i++)
                {
                    var job = Marshal.PtrToStructure<JOB_INFO_1W>(IntPtr.Add(h.AddrOfPinnedObject(), i * size));
                    jobs.Add(new Job(job.JobId, job.Status, job.Submitted.ToDateTimeUtc()));
                }
            }
            finally
            {
                h.Free();
            }

            return jobs;
        }
        finally
        {
            _ = ClosePrinter(handle);
        }
    }

    public static List<DriverLocal> EnumDrivers()
    {
        var drivers = new List<DriverLocal>();
        if (!EnumPrinterDriversW(null, null, 2, null, 0, out var needed, out _))
        {
            if (needed == 0)
            {
                return drivers;
            }
        }

        var buffer = new byte[Math.Max(needed, 1)];
        if (!EnumPrinterDriversW(null, null, 2, buffer, (uint)buffer.Length, out _, out var returned) || returned == 0)
        {
            return drivers;
        }

        // DRIVER_INFO_2W: DWORD cVersion; LPWSTR pName, pEnvironment, pDriverPath,
        // pDataFile, pConfigFile — tamanho fixo calculável.
        var size = Marshal.SizeOf<IntPtr>() * 6 + sizeof(int);
        var h = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        try
        {
            for (var i = 0; i < returned; i++)
            {
                var basePtr = IntPtr.Add(h.AddrOfPinnedObject(), i * size);
                var cVersion = Marshal.ReadInt32(basePtr);
                var pName = Marshal.ReadIntPtr(basePtr, IntPtr.Size * 1);
                var pDriverPath = Marshal.ReadIntPtr(basePtr, IntPtr.Size * 3);
                drivers.Add(new DriverLocal(
                    PtrToString(pName),
                    Versao: cVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    InfName: ExtrairInfDeCaminho(PtrToString(pDriverPath))));
            }
        }
        finally
        {
            h.Free();
        }

        return drivers;
    }

    public static string? ObterSddl(string impressora)
    {
        // Leitura do descritor de segurança via GetPrinter nível 3 (PRINTER_INFO_3W)
        // exige o mesmo handle aberto com READ_CONTROL; simplificado para retorno vazio
        // quando não concedido — snapshot grava o que conseguiu ler.
        if (!OpenPrinterW(impressora, out var handle, IntPtr.Zero))
        {
            return null;
        }

        try
        {
            if (!GetPrinterW(handle, 3, null, 0, out var needed) || needed == 0)
            {
                return null;
            }

            var buffer = new byte[needed];
            if (!GetPrinterW(handle, 3, buffer, needed, out _))
            {
                return null;
            }

            var h = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            try
            {
                var pSecurity = Marshal.ReadIntPtr(h.AddrOfPinnedObject());
                if (pSecurity == IntPtr.Zero)
                {
                    return null;
                }

                var descriptor = new CommonSecurityDescriptorForSddl(pSecurity);
                return descriptor.GetSddl();
            }
            finally
            {
                h.Free();
            }
        }
        finally
        {
            _ = ClosePrinter(handle);
        }
    }

    public static DevModeLocal? ObterDevMode(string impressora)
    {
        if (!OpenPrinterW(impressora, out var handle, IntPtr.Zero))
        {
            return null;
        }

        try
        {
            if (!GetPrinterW(handle, 2, null, 0, out var needed) || needed == 0)
            {
                return null;
            }

            var buffer = new byte[needed];
            if (!GetPrinterW(handle, 2, buffer, needed, out _))
            {
                return null;
            }

            var h = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            try
            {
                var info2 = Marshal.PtrToStructure<PRINTER_INFO_2W>(h.AddrOfPinnedObject());
                if (info2.pDevMode == IntPtr.Zero)
                {
                    return null;
                }

                // DEVMODEW: dmDeviceName[32], dmSpecVersion..dmSize/dmDriverExtra (offsets fixos),
                // depois campos. Offsets estáveis em processos de 64 bits também.
                const short offsetCopies = 34 * 2 + sizeof(short) * 8; // após dmFields
                _ = offsetCopies; // cálculo detalhado fica no leitor dedicado abaixo
                return LerDevMode(info2.pDevMode);
            }
            finally
            {
                h.Free();
            }
        }
        finally
        {
            _ = ClosePrinter(handle);
        }
    }

    /// <summary>Leitura dos campos do DEVMODEW usados pelo snapshot.</summary>
    private static DevModeLocal? LerDevMode(IntPtr devMode)
    {
        // Estrutura DEVMODEW (winuser.h): offsets em bytes a partir do início.
        // dmDeviceName[32 wchar]=64B; dmSpecVersion(2) dmDriverVersion(2) dmSize(2)
        // dmDriverExtra(2) dmFields(4) => 76B até aqui; depois:
        // dmOrientation(2) dmPaperSize(2) dmPaperLength(2) dmPaperWidth(2)
        // dmScale(2) dmCopies(2) dmDefaultSource(2) dmPrintQuality(2)
        // dmColor(2) dmDuplex(2) ...
        const int offPaperSize = 76 + 2;
        const int offCopies = 76 + 10;
        const int offColor = 76 + 16;
        const int offDuplex = 76 + 18;

        return new DevModeLocal(
            Marshal.ReadInt16(devMode, offPaperSize),
            Marshal.ReadInt16(devMode, offCopies),
            Marshal.ReadInt16(devMode, offColor),
            Marshal.ReadInt16(devMode, offDuplex));
    }

    private static string? PtrToOptional(IntPtr ptr)
    {
        var s = PtrToString(ptr);
        return s.Length == 0 ? null : s;
    }

    private static string? ExtrairInfDeCaminho(string caminho)
    {
        if (string.IsNullOrEmpty(caminho))
        {
            return null;
        }

        var nome = Path.GetFileName(caminho);
        return nome.EndsWith(".inf", StringComparison.OrdinalIgnoreCase) ? nome : null;
    }

    /// <summary>Wrapper mínimo para converter um SECURITY_DESCRIPTOR nativo em SDDL.</summary>
    private sealed class CommonSecurityDescriptorForSddl
    {
        private readonly IntPtr _descriptor;

        public CommonSecurityDescriptorForSddl(IntPtr descriptor) => _descriptor = descriptor;

        public string GetSddl()
        {
            if (_descriptor == IntPtr.Zero)
            {
                return string.Empty;
            }

            if (!ConvertSecurityDescriptorToStringSecurityDescriptorW(
                    _descriptor, 1 /* SDDL_REVISION_1 */,
                    0xF /* OWNER|GROUP|DACL|SACL */, out var sddl, out _))
            {
                return string.Empty;
            }

            try
            {
                return PtrToString(sddl);
            }
            finally
            {
                _ = LocalFree(sddl);
            }
        }
    }

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool ConvertSecurityDescriptorToStringSecurityDescriptorW(
        IntPtr securityDescriptor,
        uint requestedStringRevision,
        uint securityInformation,
        out IntPtr stringValue,
        out uint stringLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LocalFree(IntPtr handle);
}
