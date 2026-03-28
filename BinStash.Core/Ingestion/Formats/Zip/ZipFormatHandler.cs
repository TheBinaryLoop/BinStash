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

using BinStash.Core.Ingestion.Abstractions;
using BinStash.Core.Ingestion.Execution;
using BinStash.Core.Ingestion.Models;

namespace BinStash.Core.Ingestion.Formats.Zip;

public sealed class ZipFormatHandler : IInputFormatHandler
{
    private readonly ZipArchiveInspector _inspector;
    private readonly ZipMemberSelectionPolicy _selectionPolicy;
    private readonly ZipEntryStreamFactory _entryStreamFactory;
    public IReadOnlyCollection<string> SupportedFormatIds { get; } = ["zip", "jar", "apk", "nupkg"];

    public ZipFormatHandler(ZipArchiveInspector inspector, ZipMemberSelectionPolicy selectionPolicy, ZipEntryStreamFactory entryStreamFactory)
    {
        _inspector = inspector;
        _selectionPolicy = selectionPolicy;
        _entryStreamFactory = entryStreamFactory;
    }
    
    public Task HandleAsync(InputItem input, DetectedFormat detectedFormat, IngestionPlan plan, IngestionExecutionContext context, CancellationToken ct = default)
    {
        context.RegisterContainerFile(input, detectedFormat.FormatId);
        
        var entries = _inspector.Inspect(input.AbsolutePath);

        foreach (var entry in entries)
        {
            ct.ThrowIfCancellationRequested();

            context.RegisterContainerEntry(
                input: input,
                containerFormatId: detectedFormat.FormatId,
                entryPath: entry.FullName,
                isDirectory: entry.IsDirectory,
                uncompressedLength: entry.UncompressedLength,
                compressedLength: entry.CompressedLength);
            
            if (!_selectionPolicy.ShouldIngest(entry.FullName, entry.UncompressedLength, entry.IsDirectory))
                continue;
            
            var normalizedEntryPath = entry.FullName.Replace('\\', '/').TrimStart('/');

            var entryInput = new InputItem(
                AbsolutePath: input.AbsolutePath,
                RelativePath: $"{input.RelativePath}!/{normalizedEntryPath}",
                RelativePathWithinComponent: $"{input.RelativePathWithinComponent}!/{normalizedEntryPath}",
                Component: input.Component,
                Length: entry.UncompressedLength,
                LastWriteTimeUtc: input.LastWriteTimeUtc,
                Kind: InputItemKind.ContainerEntry,
                ParentLogicalPath: input.RelativePath,
                ContainerFormatId: detectedFormat.FormatId,
                EntryPath: normalizedEntryPath,
                OpenRead: _entryStreamFactory.Create(input.AbsolutePath, entry.FullName));

            context.RegisterExtractedContainerEntry(entryInput, detectedFormat.FormatId);
        }
        
        return Task.CompletedTask;
    }
}