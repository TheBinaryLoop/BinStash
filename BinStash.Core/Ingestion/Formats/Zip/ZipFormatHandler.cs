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
    private readonly ZipEntryStreamFactory _entryStreamFactory;
    private readonly ZipReconstructionPlanner _reconstructionPlanner;
    private readonly ZipRecipeBuilder _recipeBuilder;

    public ZipFormatHandler(ZipArchiveInspector inspector, ZipEntryStreamFactory entryStreamFactory, ZipReconstructionPlanner reconstructionPlanner, ZipRecipeBuilder recipeBuilder)
    {
        _inspector = inspector;
        _entryStreamFactory = entryStreamFactory;
        _reconstructionPlanner = reconstructionPlanner;
        _recipeBuilder = recipeBuilder;
    }

    public IReadOnlyCollection<string> SupportedFormatIds { get; } = ["zip", "jar", "apk", "nupkg"];

    public Task HandleAsync(InputItem input, DetectedFormat detectedFormat, IngestionPlan plan, IngestionExecutionContext context, CancellationToken ct = default)
    {
        var entries = _inspector.Inspect(input.AbsolutePath);

        foreach (var entry in entries)
        {
            ct.ThrowIfCancellationRequested();

            context.RegisterLogicalContainerEntry(
                parentInput: input,
                containerFormatId: detectedFormat.FormatId,
                entryPath: entry.FullName,
                isDirectory: entry.IsDirectory,
                uncompressedLength: entry.UncompressedLength,
                compressedLength: entry.CompressedLength);
        }

        var reconstructionPlan = _reconstructionPlanner.Plan(input, entries);

        if (reconstructionPlan.StoreOpaque)
        {
            context.RegisterOpaqueOutputArtifact(
                input,
                detectedFormat.FormatId,
                reconstructionPlan.RequiresBytePerfect);

            return Task.CompletedTask;
        }

        var recipePayload = _recipeBuilder.BuildSemanticRecipe(entries);

        context.RegisterReconstructedContainerOutputArtifact(
            input: input,
            formatId: detectedFormat.FormatId,
            reconstructionKind: reconstructionPlan.ReconstructionKind,
            requiresBytePerfectReconstruction: reconstructionPlan.RequiresBytePerfect,
            memberEntryPaths: reconstructionPlan.SelectedEntries.Select(x => x.FullName),
            recipePayload: recipePayload);

        foreach (var entry in reconstructionPlan.SelectedEntries)
        {
            ct.ThrowIfCancellationRequested();

            context.RegisterExtractedContainerMemberStorage(
                parentInput: input,
                formatId: detectedFormat.FormatId,
                entryPath: entry.FullName,
                length: entry.UncompressedLength,
                openRead: _entryStreamFactory.Create(input.AbsolutePath, entry.FullName));
        }

        return Task.CompletedTask;
    }
}