#nullable enable

using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Terrain.Editor.Platform;

/// <summary>
/// Windows native file dialog using Shell32.
/// Works without WPF/WinForms dependency.
/// </summary>
internal static class FileDialog
{
    public static bool ShowOpenDialog(nint hwndOwner, string? filter, string? title, out string? filePath)
    {
        filePath = null;

        // Use GetOpenFileName from comdlg32
        var ofn = new OPENFILENAME
        {
            lStructSize = Marshal.SizeOf<OPENFILENAME>(),
            hwndOwner = hwndOwner,
            lpstrFilter = ConvertFilter(filter),
            lpstrFile = new string(new char[260]),
            nMaxFile = 260,
            lpstrTitle = title,
            Flags = OFN_FILEMUSTEXIST | OFN_PATHMUSTEXIST
        };

        if (GetOpenFileName(ref ofn))
        {
            filePath = ofn.lpstrFile.TrimEnd('\0');
            return !string.IsNullOrEmpty(filePath);
        }

        return false;
    }

    public static bool ShowSaveDialog(nint hwndOwner, string? filter, string? title, string? defaultFileName, out string? filePath)
    {
        filePath = null;

        var ofn = new OPENFILENAME
        {
            lStructSize = Marshal.SizeOf<OPENFILENAME>(),
            hwndOwner = hwndOwner,
            lpstrFilter = ConvertFilter(filter),
            lpstrFile = new string(new char[260]),
            nMaxFile = 260,
            lpstrTitle = title,
            lpstrDefExt = "toml",
            Flags = OFN_OVERWRITEPROMPT
        };

        if (!string.IsNullOrEmpty(defaultFileName))
        {
            ofn.lpstrFile = defaultFileName.PadRight(260, '\0');
            ofn.nMaxFile = 260;
        }

        if (GetSaveFileName(ref ofn))
        {
            filePath = ofn.lpstrFile.TrimEnd('\0');
            return !string.IsNullOrEmpty(filePath);
        }

        return false;
    }

    private static string? ConvertFilter(string? filter)
    {
        // Convert "PNG Files (*.png)|*.png" to "PNG Files (*.png)\0*.png\0\0"
        if (string.IsNullOrEmpty(filter))
            return null;

        var parts = filter.Split('|');
        if (parts.Length >= 2)
        {
            return $"{parts[0]}\0{parts[1]}\0\0";
        }

        return filter;
    }

    [DllImport("comdlg32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetOpenFileName(ref OPENFILENAME ofn);

    [DllImport("comdlg32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSaveFileName(ref OPENFILENAME ofn);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OPENFILENAME
    {
        public int lStructSize;
        public nint hwndOwner;
        public nint hInstance;
        public string? lpstrFilter;
        public string? lpstrCustomFilter;
        public int nMaxCustFilter;
        public int nFilterIndex;
        public string lpstrFile;
        public int nMaxFile;
        public string? lpstrFileTitle;
        public int nMaxFileTitle;
        public string? lpstrInitialDir;
        public string? lpstrTitle;
        public int Flags;
        public short nFileOffset;
        public short nFileExtension;
        public string? lpstrDefExt;
        public nint lCustData;
        public nint lpfnHook;
        public string? lpTemplateName;
        public nint pvReserved;
        public int dwReserved;
        public int FlagsEx;
    }

    private const int OFN_FILEMUSTEXIST = 0x00001000;
    private const int OFN_PATHMUSTEXIST = 0x00000800;
    private const int OFN_OVERWRITEPROMPT = 0x00000002;
}
