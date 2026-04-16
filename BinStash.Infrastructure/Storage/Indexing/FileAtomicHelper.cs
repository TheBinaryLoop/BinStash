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

namespace BinStash.Infrastructure.Storage.Indexing;

/// <summary>
/// Cross-platform atomic file replacement.
///
/// Atomicity guarantees by platform:
///
/// Windows (NTFS): <c>File.Move(src, dest, overwrite: true)</c> is implemented
/// via <c>MoveFileExW</c> with <c>MOVEFILE_REPLACE_EXISTING</c>.  On NTFS this is
/// a single metadata operation and is atomic with respect to readers: they see
/// either the old file or the new file, never a partial state.
///
/// Linux / Docker-on-Linux (ext4, xfs, btrfs, tmpfs): the call lowers to
/// <c>rename(2)</c>, which POSIX guarantees to be atomic as long as source and
/// destination are on the same filesystem (same mount point).  Within a single
/// Docker volume this condition is always satisfied.  Crossing mount boundaries
/// (e.g. source on a tmpfs ramdisk, destination on the host bind-mount) will
/// cause the kernel to return <c>EXDEV</c> and .NET will throw; callers must
/// ensure source and destination share the same mount point.
///
/// macOS (not a target, noted for completeness): identical POSIX rename semantics.
/// </summary>
internal static class FileAtomicHelper
{
    /// <summary>
    /// Atomically replaces <paramref name="destination"/> with
    /// <paramref name="source"/>.  After a successful return, <paramref name="source"/>
    /// no longer exists and <paramref name="destination"/> contains the data that
    /// was in <paramref name="source"/>.
    ///
    /// The caller is responsible for ensuring that no <see cref="MemoryMappedFile"/>
    /// or open <see cref="FileStream"/> handle is held against
    /// <paramref name="destination"/> at the time of the call on Windows; on
    /// Linux open handles survive the rename and continue to read the old inode.
    /// </summary>
    /// <exception cref="IOException">
    /// Thrown on cross-device rename (different mount points under Linux/Docker)
    /// or if the destination directory does not exist.
    /// </exception>
    public static void ReplaceAtomic(string source, string destination)
    {
        // File.Move with overwrite:true maps to MoveFileExW on Windows and
        // rename(2) on POSIX — both atomic within the same filesystem.
        File.Move(source, destination, overwrite: true);
    }

    /// <summary>
    /// Writes <paramref name="data"/> to a sibling temporary file next to
    /// <paramref name="finalPath"/>, fsyncs it to durable storage, then
    /// atomically renames it into place.
    ///
    /// This is the standard "write-then-rename" durability pattern:
    /// a crash during the write leaves the old file intact; a crash after the
    /// rename leaves the new file intact.  There is no window where a reader
    /// can observe a partially written file.
    /// </summary>
    public static async Task WriteAtomicAsync(string finalPath, byte[] data, CancellationToken ct = default)
    {
        var dir = Path.GetDirectoryName(finalPath) ?? throw new InvalidOperationException($"Cannot determine directory for path: {finalPath}");

        Directory.CreateDirectory(dir);

        // Use a fixed-suffix temp name in the same directory to guarantee
        // same-filesystem placement on Linux.
        var tmp = finalPath + ".tmp";

        await using (var fs = new FileStream(
                         tmp,
                         FileMode.Create,
                         FileAccess.Write,
                         FileShare.None,
                         bufferSize: 65536,
                         options: FileOptions.Asynchronous))
        {
            await fs.WriteAsync(data.AsMemory(), ct).ConfigureAwait(false);
            await fs.FlushAsync(ct).ConfigureAwait(false);
            // fsync: ensures the data hits the storage device before rename.
            fs.Flush(flushToDisk: true);
        }

        ReplaceAtomic(tmp, finalPath);
    }
}
