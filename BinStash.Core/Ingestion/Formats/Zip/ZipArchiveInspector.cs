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

using System.IO.Compression;

namespace BinStash.Core.Ingestion.Formats.Zip;

public sealed class ZipArchiveInspector
{
    public IReadOnlyList<ZipArchiveEntryInfo> Inspect(string zipFilePath)
    {
        using var stream = new FileStream(zipFilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.SequentialScan);

        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);

        var entries = new List<ZipArchiveEntryInfo>(archive.Entries.Count);

        for (var i = 0; i < archive.Entries.Count; i++)
        {
            var entry = archive.Entries[i];
            var fullName = NormalizeEntryPath(entry.FullName);
            var isDirectory = IsDirectoryEntry(entry);

            entries.Add(new ZipArchiveEntryInfo(
                FullName: fullName,
                Name: entry.Name,
                UncompressedLength: isDirectory ? 0 : entry.Length,
                CompressedLength: isDirectory ? 0 : entry.CompressedLength,
                IsDirectory: isDirectory,
                Index: i));
        }


        return entries;
    }

    private static string NormalizeEntryPath(string path)
    {
        return path.Replace('\\', '/');
    }

    private static bool IsDirectoryEntry(ZipArchiveEntry entry)
    {
        if (entry.FullName.EndsWith('/'))
            return true;

        return string.IsNullOrEmpty(entry.Name)
               && !string.IsNullOrEmpty(entry.FullName);
    }
}