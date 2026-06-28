// Copyright (C) 2025-2026  Lukas Eßmann
// 
//      This program is free software: you can redistribute it and/or modify
//      it under the terms of the GNU Affero General Public License as published
//      by the Free Software Foundation, either version 3 of the License, or
//      (at your option) any later version.
// 
//      This program is distributed in the hope that it will be useful,
//      but WITHOUT ANY WARRANTY; without even the implied warranty of
//      MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//      GNU Affero General Public License for more details.
// 
//      You should have received a copy of the GNU Affero General Public License
//      along with this program.  If not, see <https://www.gnu.org/licenses/>.

using System.Runtime.InteropServices;

namespace BinStash.Cli.Utils;

internal static partial class HardLinkHelper
{
    public static void CreateHardLink(string linkPath, string existingFilePath)
    {
        if (OperatingSystem.IsWindows())
        {
            if (!CreateHardLink(linkPath, existingFilePath, IntPtr.Zero))
            {
                var error = Marshal.GetLastWin32Error();
                throw new IOException($"Failed to create hard link '{linkPath}' -> '{existingFilePath}'. Win32 Error: {error}");
            }
        }
        else
        {
            if (link(existingFilePath, linkPath) != 0)
            {
                var errno = Marshal.GetLastWin32Error();
                throw new IOException($"Failed to create hard link '{linkPath}' -> '{existingFilePath}'. errno: {errno}");
            }
        }
    }

    // Windows
    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CreateHardLink(string lpFileName, string lpExistingFileName, IntPtr lpSecurityAttributes);

    // Unix (Linux/macOS)
    [LibraryImport("libc", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial int link(string oldpath, string newpath);
}