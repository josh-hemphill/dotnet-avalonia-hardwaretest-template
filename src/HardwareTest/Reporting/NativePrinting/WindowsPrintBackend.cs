using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace HardwareTest.Reporting.NativePrinting;

[SupportedOSPlatform("windows")]
public sealed class WindowsPrintBackend : IWindowsPrintBackend
{
    internal const uint DialogFlags = 0x00000100 | 0x00040000 | 0x00000004 | 0x00800000 | 0x00100000 | 0x00080000;
    public IWindowsPrintSession ShowDialog(nint owner, int pageCount)
    {
        if (owner == 0 || pageCount < 1) throw new ArgumentException("A valid owner and PDF page count are required.");
        var ranges = Marshal.AllocHGlobal(64 * Marshal.SizeOf<NativePageRange>());
        var dialog = new NativePrintDialog
        {
            StructSize = (uint)Marshal.SizeOf<NativePrintDialog>(),
            Owner = owner,
            Flags = DialogFlags,
            MaxPageRanges = 64,
            PageRanges = ranges,
            MinPage = 1,
            MaxPage = (uint)pageCount,
            Copies = 1,
            StartPage = uint.MaxValue,
        };
        try
        {
            Marshal.Copy(new byte[64 * Marshal.SizeOf<NativePageRange>()], 0, ranges, 64 * Marshal.SizeOf<NativePageRange>());
            Marshal.ThrowExceptionForHR(PrintDlgExW(ref dialog));
            return new Session(dialog, ranges);
        }
        catch { Release(ref dialog, ranges); throw; }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativePrintDialog
    {
        public uint StructSize;
        public nint Owner, DevMode, DevNames, DeviceContext;
        public uint Flags, Flags2, ExclusionFlags, PageRangeCount, MaxPageRanges;
        public nint PageRanges;
        public uint MinPage, MaxPage, Copies;
        public nint Instance, PrintTemplate, Callback;
        public uint PropertyPageCount;
        public nint PropertyPages;
        public uint StartPage, ResultAction;
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativePageRange { public uint From, To; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DocumentInfo
    {
        public int Size;
        [MarshalAs(UnmanagedType.LPWStr)] public string Name;
        [MarshalAs(UnmanagedType.LPWStr)] public string? Output;
        [MarshalAs(UnmanagedType.LPWStr)] public string? DataType;
        public uint Type;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public uint Size;
        public int Width, Height;
        public ushort Planes, BitCount;
        public uint Compression, ImageSize;
        public int XPixelsPerMeter, YPixelsPerMeter;
        public uint ColorsUsed, ColorsImportant, Color;
    }

    private sealed class Session : IWindowsPrintSession
    {
        private NativePrintDialog _dialog;
        private nint _ranges;
        public PrintDialogAction Action { get; }
        public bool UsesPageRanges => (_dialog.Flags & 2) != 0;
        public IReadOnlyList<PrintPageRange> PageRanges { get; }
        public PrinterMetrics Metrics { get; }
        public Session(NativePrintDialog dialog, nint ranges)
        {
            _dialog = dialog;
            _ranges = ranges;
            Action = dialog.ResultAction switch
            {
                0 => PrintDialogAction.Cancel,
                1 => PrintDialogAction.Print,
                2 => PrintDialogAction.Apply,
                _ => throw new InvalidDataException("The printer dialog returned an unknown action."),
            };
            var selected = new List<PrintPageRange>();
            if (Action == PrintDialogAction.Print)
            {
                if (dialog.DeviceContext == 0) throw new InvalidDataException("The printer did not return a device context.");
                if (dialog.PageRangeCount > 64) throw new InvalidDataException("The printer returned too many page ranges.");
                for (var i = 0; i < dialog.PageRangeCount; i++)
                {
                    var range = Marshal.PtrToStructure<NativePageRange>(ranges + i * Marshal.SizeOf<NativePageRange>());
                    selected.Add(new(checked((int)range.From), checked((int)range.To)));
                }
                Metrics = new(GetDeviceCaps(dialog.DeviceContext, 8), GetDeviceCaps(dialog.DeviceContext, 10),
                    GetDeviceCaps(dialog.DeviceContext, 88), GetDeviceCaps(dialog.DeviceContext, 90),
                    GetDeviceCaps(dialog.DeviceContext, 112), GetDeviceCaps(dialog.DeviceContext, 113));
            }
            PageRanges = selected;
        }
        public void StartDocument(string title)
        {
            var info = new DocumentInfo
            {
                Size = Marshal.SizeOf<DocumentInfo>(),
                Name = title,
                Output = (_dialog.Flags & 0x20) != 0 ? "FILE:" : null,
            };
            Check(StartDocW(_dialog.DeviceContext, ref info), "StartDoc");
        }
        public void StartPage() => Check(NativeStartPage(_dialog.DeviceContext), "StartPage");
        public void EndPage() => Check(NativeEndPage(_dialog.DeviceContext), "EndPage");
        public void EndDocument() => Check(EndDoc(_dialog.DeviceContext), "EndDoc");
        public void AbortDocument() => AbortDoc(_dialog.DeviceContext);
        public void DrawPage(IPrintPage page, PrintRectangle rectangle)
        {
            if (page.Width < 1 || page.Height < 1 || page.Stride != checked(page.Width * 4) || page.Pixels == 0
                || (long)page.Width * page.Height > PrintGeometry.MaxPixels)
                throw new InvalidDataException("The printer requires a tightly packed BGRA32 bitmap.");
            var info = new BitmapInfo
            {
                Size = 40,
                Width = page.Width,
                Height = -page.Height,
                Planes = 1,
                BitCount = 32,
                Compression = 0,
                ImageSize = checked((uint)(page.Stride * page.Height)),
            };
            if (SetStretchBltMode(_dialog.DeviceContext, 4) == 0) throw Failure("SetStretchBltMode");
            if (!SetBrushOrgEx(_dialog.DeviceContext, 0, 0, 0)) throw Failure("SetBrushOrgEx");
            var drawn = StretchDIBits(_dialog.DeviceContext, rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height,
                0, 0, page.Width, page.Height, page.Pixels, ref info, 0, 0x00CC0020);
            if (drawn is 0 or -1) throw Failure("StretchDIBits");
        }
        public void Dispose()
        {
            var ranges = Interlocked.Exchange(ref _ranges, 0);
            if (ranges != 0) Release(ref _dialog, ranges);
        }
    }

    private static void Check(int result, string operation) { if (result <= 0) throw Failure(operation); }
    private static Win32Exception Failure(string operation) => new(Marshal.GetLastWin32Error(), operation + " failed.");
    private static void Release(ref NativePrintDialog dialog, nint ranges)
    {
        try { if (dialog.DeviceContext != 0) DeleteDC(dialog.DeviceContext); }
        finally
        {
            dialog.DeviceContext = 0;
            try { if (dialog.DevMode != 0) GlobalFree(dialog.DevMode); }
            finally
            {
                dialog.DevMode = 0;
                try { if (dialog.DevNames != 0) GlobalFree(dialog.DevNames); }
                finally { dialog.DevNames = 0; Marshal.FreeHGlobal(ranges); }
            }
        }
    }

    [DllImport("comdlg32.dll", ExactSpelling = true)] private static extern int PrintDlgExW(ref NativePrintDialog dialog);
    [DllImport("gdi32.dll", EntryPoint = "StartDocW", CharSet = CharSet.Unicode, SetLastError = true)] private static extern int StartDocW(nint dc, ref DocumentInfo info);
    [DllImport("gdi32.dll", EntryPoint = "StartPage", SetLastError = true)] private static extern int NativeStartPage(nint dc);
    [DllImport("gdi32.dll", EntryPoint = "EndPage", SetLastError = true)] private static extern int NativeEndPage(nint dc);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern int EndDoc(nint dc);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern int AbortDoc(nint dc);
    [DllImport("gdi32.dll")] private static extern int GetDeviceCaps(nint dc, int index);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern int SetStretchBltMode(nint dc, int mode);
    [DllImport("gdi32.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetBrushOrgEx(nint dc, int x, int y, nint oldPoint);
    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern int StretchDIBits(nint dc, int x, int y, int width, int height,
        int sourceX, int sourceY, int sourceWidth, int sourceHeight, nint bits, ref BitmapInfo info, uint usage, uint rasterOperation);
    [DllImport("gdi32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteDC(nint dc);
    [DllImport("kernel32.dll")] private static extern nint GlobalFree(nint memory);
}
