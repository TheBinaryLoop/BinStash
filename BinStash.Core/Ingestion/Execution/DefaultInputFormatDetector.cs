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
using BinStash.Core.Ingestion.Models;

namespace BinStash.Core.Ingestion.Execution;

public sealed class DefaultInputFormatDetector : IInputFormatDetector
{
    public ValueTask<DetectedFormat> DetectAsync(InputItem input, CancellationToken ct = default)
    {
        var extension = Path.GetExtension(input.AbsolutePath);

        if (string.IsNullOrWhiteSpace(extension))
        {
            return ValueTask.FromResult(new DetectedFormat(
                FormatId: "file",
                Confidence: DetectionConfidence.Unknown,
                Traits: FormatTraits.Opaque));
        }

        extension = extension.ToLowerInvariant();

        return extension switch
        {
            ".zip" => ValueTask.FromResult(new DetectedFormat(
                FormatId: "zip",
                Confidence: DetectionConfidence.Extension,
                Traits: FormatTraits.Container)),

            ".jar" => ValueTask.FromResult(new DetectedFormat(
                FormatId: "jar",
                Confidence: DetectionConfidence.Extension,
                Traits: FormatTraits.Container)),

            ".apk" => ValueTask.FromResult(new DetectedFormat(
                FormatId: "apk",
                Confidence: DetectionConfidence.Extension,
                Traits: FormatTraits.Container)),

            ".nupkg" => ValueTask.FromResult(new DetectedFormat(
                FormatId: "nupkg",
                Confidence: DetectionConfidence.Extension,
                Traits: FormatTraits.Container)),

            ".tar" => ValueTask.FromResult(new DetectedFormat(
                FormatId: "tar",
                Confidence: DetectionConfidence.Extension,
                Traits: FormatTraits.Container)),

            ".gz" => ValueTask.FromResult(new DetectedFormat(
                FormatId: "gzip",
                Confidence: DetectionConfidence.Extension,
                Traits: FormatTraits.CompressionWrapper)),

            ".zst" => ValueTask.FromResult(new DetectedFormat(
                FormatId: "zstd",
                Confidence: DetectionConfidence.Extension,
                Traits: FormatTraits.CompressionWrapper)),

            _ => ValueTask.FromResult(new DetectedFormat(
                FormatId: "file",
                Confidence: DetectionConfidence.Extension,
                Traits: FormatTraits.Opaque))
        };
    }
}