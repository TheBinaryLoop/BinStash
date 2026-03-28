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

public sealed class DefaultIngestionPlanner : IIngestionPlanner
{
    public ValueTask<IngestionPlan> CreatePlanAsync(InputItem input, DetectedFormat detectedFormat, CancellationToken ct = default)
    {
        return detectedFormat.FormatId switch
        {
            "zip" or "jar" or "apk" or "nupkg" => ValueTask.FromResult(new IngestionPlan(
                Mode: IngestionMode.Hybrid,
                PreserveOriginal: true,
                RecurseIntoChildren: false,
                Reason: $"ZIP-family format '{detectedFormat.FormatId}' is tracked as a container, but remains opaque for content processing in this phase.")),

            _ => ValueTask.FromResult(new IngestionPlan(
                Mode: IngestionMode.Opaque,
                PreserveOriginal: true,
                RecurseIntoChildren: false,
                Reason: $"Default opaque handling for format '{detectedFormat.FormatId}'."))
        };
    }
}