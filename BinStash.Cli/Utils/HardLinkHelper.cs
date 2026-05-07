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

internal static class HardLinkHelper
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
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateHardLink(
        string lpFileName,
        string lpExistingFileName,
        IntPtr lpSecurityAttributes);

    // Unix (Linux/macOS)
    [DllImport("libc", SetLastError = true)]
    private static extern int link(string oldpath, string newpath);
}