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

using BinStash.Contracts.Release;
using BinStash.Core.Ingestion.Models;

namespace BinStash.Core.Ingestion.Formats.Zip;

public sealed class ZipReconstructionPlanner
{
    private readonly ZipMemberSelectionPolicy _selectionPolicy;

    public ZipReconstructionPlanner(ZipMemberSelectionPolicy selectionPolicy)
    {
        _selectionPolicy = selectionPolicy;
    }

    public ZipReconstructionPlanningResult Plan(InputItem input, IReadOnlyList<ZipArchiveEntryInfo> entries)
    {
        // Conservative first version:
        // - assume byte-perfect reconstruction is NOT available yet
        // - semantic reconstruction IS available if we can inspect and store enough members
        // - policy may later be externalized by repo/path/format rules

        var requiresBytePerfect = RequiresBytePerfectOutput(input);

        if (requiresBytePerfect)
        {
            return new ZipReconstructionPlanningResult
            {
                StoreOpaque = true,
                RequiresBytePerfect = true,
                ReconstructionKind = ReconstructionKind.None,
                Reason = "Byte-perfect format reconstruction is not implemented yet."
            };
        }

        var selected = entries.Where(x => _selectionPolicy.ShouldIngest(x.FullName, x.UncompressedLength, x.IsDirectory)).ToList();

        if (selected.Count == 0)
        {
            return new ZipReconstructionPlanningResult
            {
                StoreOpaque = true,
                RequiresBytePerfect = false,
                ReconstructionKind = ReconstructionKind.None,
                Reason = "No entries were selected for extracted storage, so opaque storage is used."
            };
        }

        return new ZipReconstructionPlanningResult
        {
            StoreOpaque = false,
            RequiresBytePerfect = false,
            ReconstructionKind = ReconstructionKind.Semantic,
            Reason = "ZIP will be stored as extracted members with semantic reconstruction.",
            SelectedEntries = selected
        };
    }

    private static bool RequiresBytePerfectOutput(InputItem input)
    {
        var ext = Path.GetExtension(input.AbsolutePath).ToLowerInvariant();

        return ext switch
        {
            ".apk" => true,
            ".jar" => true,
            ".nupkg" => true,
            _ => false
        };
    }
}