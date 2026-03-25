using System.Runtime.InteropServices;

namespace NSTProxy;

/// <summary>
/// Sends raw byte data directly to a printer via the Windows spooler,
/// bypassing any driver-level rendering.
/// </summary>
public static class RawPrinterHelper
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private struct DOCINFOA
    {
        [MarshalAs(UnmanagedType.LPStr)] public string pDocName;
        [MarshalAs(UnmanagedType.LPStr)] public string? pOutputFile;
        [MarshalAs(UnmanagedType.LPStr)] public string pDataType;
    }

    [DllImport("winspool.drv", EntryPoint = "OpenPrinterA",
        SetLastError = true, CharSet = CharSet.Ansi, ExactSpelling = true)]
    private static extern bool OpenPrinter(string szPrinter, out IntPtr hPrinter, IntPtr pd);

    [DllImport("winspool.drv", EntryPoint = "ClosePrinter",
        SetLastError = true, ExactSpelling = true)]
    private static extern bool ClosePrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", EntryPoint = "StartDocPrinterA",
        SetLastError = true, CharSet = CharSet.Ansi, ExactSpelling = true)]
    private static extern bool StartDocPrinter(IntPtr hPrinter, int level, ref DOCINFOA di);

    [DllImport("winspool.drv", EntryPoint = "EndDocPrinter",
        SetLastError = true, ExactSpelling = true)]
    private static extern bool EndDocPrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", EntryPoint = "StartPagePrinter",
        SetLastError = true, ExactSpelling = true)]
    private static extern bool StartPagePrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", EntryPoint = "EndPagePrinter",
        SetLastError = true, ExactSpelling = true)]
    private static extern bool EndPagePrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", EntryPoint = "WritePrinter",
        SetLastError = true, ExactSpelling = true)]
    private static extern bool WritePrinter(IntPtr hPrinter, IntPtr pBytes, int dwCount, out int dwWritten);

    public static bool SendRawData(string printerName, byte[] data)
    {
        var di = new DOCINFOA
        {
            pDocName = "NSTProxy Raw Document",
            pOutputFile = null,
            pDataType = "RAW"
        };

        if (!OpenPrinter(printerName.Normalize(), out var hPrinter, IntPtr.Zero))
            return false;

        try
        {
            if (!StartDocPrinter(hPrinter, 1, ref di))
                return false;

            try
            {
                if (!StartPagePrinter(hPrinter))
                    return false;

                try
                {
                    var pUnmanagedBytes = Marshal.AllocCoTaskMem(data.Length);
                    try
                    {
                        Marshal.Copy(data, 0, pUnmanagedBytes, data.Length);
                        return WritePrinter(hPrinter, pUnmanagedBytes, data.Length, out _);
                    }
                    finally
                    {
                        Marshal.FreeCoTaskMem(pUnmanagedBytes);
                    }
                }
                finally
                {
                    EndPagePrinter(hPrinter);
                }
            }
            finally
            {
                EndDocPrinter(hPrinter);
            }
        }
        finally
        {
            ClosePrinter(hPrinter);
        }
    }
}
