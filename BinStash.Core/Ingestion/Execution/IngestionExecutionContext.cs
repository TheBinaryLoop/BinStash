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
using BinStash.Core.Ingestion.Models;

namespace BinStash.Core.Ingestion.Execution;

public sealed class IngestionExecutionContext
{
    private readonly IngestionResult _result;

    public IngestionExecutionContext(IngestionResult result)
    {
        _result = result;
    }

    public void RegisterOpaqueOutputArtifact(InputItem input, string formatId, bool requiresBytePerfectReconstruction)
    {
        _result.OutputArtifacts.Add(new OutputArtifact
        {
            Path = input.RelativePath,
            ComponentName = input.Component.Name,
            Kind = OutputArtifactKind.File,
            RequiresBytePerfectReconstruction = requiresBytePerfectReconstruction,
            Backing = new OpaqueBlobBacking()
        });

        _result.LogicalArtifacts.Add(new LogicalArtifact
        {
            LogicalPath = input.RelativePath,
            RelativePathWithinComponent = input.RelativePathWithinComponent,
            Component = input.Component,
            Kind = formatId == "file" ? ArtifactKind.File : ArtifactKind.Container,
            SourcePath = input.AbsolutePath,
            FormatId = formatId,
            Length = input.Length,
            IsVirtual = false
        });

        _result.StorageWorkItems.Add(new StorageWorkItem
        {
            Identity = $"opaque::{input.RelativePath}",
            Kind = StorageWorkItemKind.OpaqueFile,
            OutputArtifactPath = input.RelativePath,
            SourcePath = input.AbsolutePath,
            FormatId = formatId,
            LengthHint = input.Length,
            OpenRead = () => new FileStream(input.AbsolutePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.SequentialScan)
        });
    }

    public void RegisterReconstructedContainerOutputArtifact(InputItem input, string formatId, ReconstructionKind reconstructionKind, bool requiresBytePerfectReconstruction, IEnumerable<string> memberEntryPaths, byte[] recipePayload)
    {
        var backing = new ReconstructedContainerBacking
        {
            FormatId = formatId,
            ReconstructionKind = reconstructionKind,
            RecipePayload = recipePayload,
            Members = memberEntryPaths
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(x => new ContainerMemberBinding
                {
                    EntryPath = NormalizeEntryPath(x)
                })
                .ToList()
        };

        _result.OutputArtifacts.Add(new OutputArtifact
        {
            Path = input.RelativePath,
            ComponentName = input.Component.Name,
            Kind = OutputArtifactKind.File,
            RequiresBytePerfectReconstruction = requiresBytePerfectReconstruction,
            Backing = backing
        });

        _result.LogicalArtifacts.Add(new LogicalArtifact
        {
            LogicalPath = input.RelativePath,
            RelativePathWithinComponent = input.RelativePathWithinComponent,
            Component = input.Component,
            Kind = ArtifactKind.Container,
            SourcePath = input.AbsolutePath,
            FormatId = formatId,
            Length = input.Length,
            IsVirtual = false
        });
    }

    public void RegisterLogicalContainerEntry(InputItem parentInput, string containerFormatId, string entryPath, bool isDirectory, long uncompressedLength, long compressedLength)
    {
        var normalizedEntryPath = NormalizeEntryPath(entryPath);
        if (string.IsNullOrWhiteSpace(normalizedEntryPath))
            return;

        var containerLogicalPath = parentInput.RelativePath;
        var childLogicalPath = $"{containerLogicalPath}!/{normalizedEntryPath}";
        var childRelativeWithinComponent = $"{parentInput.RelativePathWithinComponent}!/{normalizedEntryPath}";

        _result.LogicalArtifacts.Add(new LogicalArtifact
        {
            LogicalPath = childLogicalPath,
            RelativePathWithinComponent = childRelativeWithinComponent,
            Component = parentInput.Component,
            Kind = isDirectory ? ArtifactKind.Directory : ArtifactKind.File,
            SourcePath = parentInput.AbsolutePath,
            FormatId = containerFormatId,
            ParentLogicalPath = containerLogicalPath,
            EntryPath = normalizedEntryPath,
            Length = isDirectory ? 0 : uncompressedLength,
            CompressedLength = isDirectory ? 0 : compressedLength,
            IsVirtual = true
        });
    }

    public void RegisterExtractedContainerMemberStorage(InputItem parentInput, string formatId, string entryPath, long length, Func<Stream> openRead)
    {
        var normalizedEntryPath = NormalizeEntryPath(entryPath);
        if (string.IsNullOrWhiteSpace(normalizedEntryPath))
            return;

        _result.StorageWorkItems.Add(new StorageWorkItem
        {
            Identity = $"member::{parentInput.RelativePath}::{normalizedEntryPath}",
            Kind = StorageWorkItemKind.ExtractedContainerMember,
            OutputArtifactPath = parentInput.RelativePath,
            SourcePath = parentInput.AbsolutePath,
            EntryPath = normalizedEntryPath,
            FormatId = formatId,
            LengthHint = length,
            OpenRead = openRead
        });
    }

    public void BindStoredContent(StorageWorkItem workItem, Hash32 hash, long length)
    {
        _result.StoredContents[hash] = new StoredContent
        {
            Hash = hash,
            Length = length,
            SourcePath = workItem.SourcePath,
            EntryPath = workItem.EntryPath,
            OutputArtifactPath = workItem.OutputArtifactPath,
            FormatId = workItem.FormatId,
            Kind = workItem.Kind
        };

        var outputArtifact = _result.OutputArtifacts.FirstOrDefault(x =>
            string.Equals(x.Path, workItem.OutputArtifactPath, StringComparison.OrdinalIgnoreCase));

        if (outputArtifact == null)
            return;

        switch (outputArtifact.Backing)
        {
            case OpaqueBlobBacking opaque when workItem.Kind == StorageWorkItemKind.OpaqueFile:
                opaque.ContentHash = hash;
                opaque.Length = length;
                break;

            case ReconstructedContainerBacking reconstructed
                when workItem.Kind == StorageWorkItemKind.ExtractedContainerMember
                     && !string.IsNullOrWhiteSpace(workItem.EntryPath):
            {
                var member = reconstructed.Members.FirstOrDefault(x =>
                    string.Equals(x.EntryPath, workItem.EntryPath, StringComparison.OrdinalIgnoreCase));

                if (member != null)
                {
                    member.ContentHash = hash;
                    member.Length = length;
                }

                break;
            }
        }
    }

    public void BindChunkMap(Hash32 hash, List<ChunkMapEntry> chunkMap)
    {
        if (_result.StoredContents.TryGetValue(hash, out var content))
        {
            content.ChunkMap = chunkMap;
        }
    }

    private static string NormalizeEntryPath(string path)
    {
        return path.Replace('\\', '/').TrimStart('/');
    }
}