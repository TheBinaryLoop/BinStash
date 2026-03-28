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

using BinStash.Contracts.Hashing;
using BinStash.Contracts.Release;
using BinStash.Core.Chunking;
using BinStash.Core.Ingestion.Abstractions;
using BinStash.Core.Ingestion.Models;

namespace BinStash.Core.Ingestion.Execution;

public sealed class ReleaseIngestionEngine : IReleaseIngestionEngine
{
    private readonly IInputFormatDetector _formatDetector;
    private readonly IIngestionPlanner _planner;
    private readonly IReadOnlyDictionary<string, IInputFormatHandler> _handlers;

    public ReleaseIngestionEngine(IInputFormatDetector formatDetector, IIngestionPlanner planner, IEnumerable<IInputFormatHandler> handlers)
    {
        _formatDetector = formatDetector;
        _planner = planner;
        
        var map = new Dictionary<string, IInputFormatHandler>(StringComparer.OrdinalIgnoreCase);
        foreach (var handler in handlers)
        {
            foreach (var formatId in handler.SupportedFormatIds)
            {
                map[formatId] = handler;
            }
        }

        _handlers = map;
    }

    public async Task<IngestionResult> IngestAsync(IReadOnlyCollection<InputItem> inputs, IReadOnlyDictionary<string, Component> componentMap, CancellationToken ct = default)
    {
        var result = new IngestionResult();
        var context = new IngestionExecutionContext(result);

        foreach (var input in inputs)
        {
            ct.ThrowIfCancellationRequested();

            var detectedFormat = await _formatDetector.DetectAsync(input, ct);
            var plan = await _planner.CreatePlanAsync(input, detectedFormat, ct);

            var handler = ResolveHandler(detectedFormat);
            await handler.HandleAsync(input, detectedFormat, plan, context, ct);
        }

        return result;
    }

    private IInputFormatHandler ResolveHandler(DetectedFormat detectedFormat)
    {
        if (_handlers.TryGetValue(detectedFormat.FormatId, out var exact))
            return exact;

        if (_handlers.TryGetValue("file", out var plain))
            return plain;

        throw new InvalidOperationException(
            $"No input format handler registered for format '{detectedFormat.FormatId}', and no fallback 'file' handler exists.");
    }
}

public sealed class IngestionExecutionContext
{
    private readonly IngestionResult _result;

    public IngestionExecutionContext(IngestionResult result)
    {
        _result = result;
    }

    public void RegisterOpaqueFile(InputItem input, string formatId)
    {
        RegisterFileLikeArtifact(input, formatId, ArtifactKind.File);
    }

    public void RegisterContainerFile(InputItem input, string formatId)
    {
        RegisterFileLikeArtifact(input, formatId, ArtifactKind.Container);
    }


    private void RegisterFileLikeArtifact(InputItem input, string formatId, ArtifactKind artifactKind)
    {
        var releaseFile = new ReleaseFile
        {
            Name = input.RelativePathWithinComponent,
            Hash = default,
            Chunks = new()
        };

        _result.Artifacts.Add(new LogicalArtifact
        {
            LogicalPath = input.RelativePath,
            RelativePathWithinComponent = input.RelativePathWithinComponent,
            Component = input.Component,
            Kind = artifactKind,
            SourcePath = input.AbsolutePath,
            FormatId = formatId,
            Length = input.Length,
            IsVirtual = false
        });

        _result.FileBindings.Add(new ReleaseFileBinding
        {
            Component = input.Component,
            File = releaseFile,
            SourcePath = input.AbsolutePath,
            Input = input
        });
    }
    
    public void RegisterContainerEntry(InputItem input, string containerFormatId, string entryPath, bool isDirectory, long uncompressedLength, long compressedLength)
    {
        var normalizedEntryPath = NormalizeEntryPath(entryPath);
        if (string.IsNullOrWhiteSpace(normalizedEntryPath))
            return;

        var containerLogicalPath = input.RelativePath;
        var childLogicalPath = $"{containerLogicalPath}!/{normalizedEntryPath}";
        var childRelativeWithinComponent = $"{input.RelativePathWithinComponent}!/{normalizedEntryPath}";

        _result.Artifacts.Add(new LogicalArtifact
        {
            LogicalPath = childLogicalPath,
            RelativePathWithinComponent = childRelativeWithinComponent,
            Component = input.Component,
            Kind = isDirectory ? ArtifactKind.Directory : ArtifactKind.File,
            SourcePath = input.AbsolutePath,
            FormatId = containerFormatId,
            ParentLogicalPath = containerLogicalPath,
            EntryPath = normalizedEntryPath,
            Length = isDirectory ? 0 : uncompressedLength,
            CompressedLength = isDirectory ? 0 : compressedLength,
            IsVirtual = true
        });
    }
    
    public void RegisterExtractedContainerEntry(InputItem entryInput, string formatId)
    {
        var releaseFile = new ReleaseFile
        {
            Name = entryInput.RelativePathWithinComponent,
            Hash = default,
            Chunks = new()
        };

        _result.FileBindings.Add(new ReleaseFileBinding
        {
            Component = entryInput.Component,
            File = releaseFile,
            SourcePath = entryInput.AbsolutePath,
            Input = entryInput
        });

        _result.Artifacts.Add(new LogicalArtifact
        {
            LogicalPath = entryInput.RelativePath,
            RelativePathWithinComponent = entryInput.RelativePathWithinComponent,
            Component = entryInput.Component,
            Kind = ArtifactKind.File,
            SourcePath = entryInput.AbsolutePath,
            FormatId = formatId,
            ParentLogicalPath = entryInput.ParentLogicalPath,
            EntryPath = entryInput.EntryPath,
            Length = entryInput.Length,
            IsVirtual = false
        });
    }
    
    public void BindFileHash(InputItem input, Hash32 hash, long length, string? formatId = null)
    {
        foreach (var artifact in _result.Artifacts.Where(x =>
                     string.Equals(x.RelativePathWithinComponent, input.RelativePathWithinComponent, StringComparison.OrdinalIgnoreCase)
                     && string.Equals(x.ParentLogicalPath, input.ParentLogicalPath, StringComparison.OrdinalIgnoreCase)
                     && x.IsVirtual == false))
        {
            artifact.FileHash = hash;
            artifact.Length = length;
            if (artifact.FormatId == null)
                artifact.FormatId = formatId;
        }

        foreach (var binding in _result.FileBindings.Where(x => BindingMatchesInput(x, input)))
        {
            binding.File.Hash = hash;
        }

        _result.Contents[hash] = new ContentDescriptor
        {
            FileHash = hash,
            Length = length,
            SourcePath = input.AbsolutePath,
            FormatId = formatId,
            IsContainerEntry = input.Kind == InputItemKind.ContainerEntry,
            ParentLogicalPath = input.ParentLogicalPath,
            EntryPath = input.EntryPath
        };
    }

    public void BindChunkMap(Hash32 hash, List<ChunkMapEntry> chunkMap)
    {
        if (_result.Contents.TryGetValue(hash, out var content))
        {
            content.ChunkMap = chunkMap;
        }
    }
    
    private static bool BindingMatchesInput(ReleaseFileBinding binding, InputItem input)
    {
        if (binding.Input == null)
            return string.Equals(binding.SourcePath, input.AbsolutePath, StringComparison.OrdinalIgnoreCase);

        if (binding.Input.Kind != input.Kind)
            return false;

        if (!string.Equals(binding.Input.AbsolutePath, input.AbsolutePath, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!string.Equals(binding.Input.EntryPath, input.EntryPath, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!string.Equals(binding.Input.ParentLogicalPath, input.ParentLogicalPath, StringComparison.OrdinalIgnoreCase))
            return false;

        return true;
    }

    
    private static string NormalizeEntryPath(string path)
    {
        return path.Replace('\\', '/').TrimStart('/');
    }
}