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

namespace BinStash.Cli.Services.Releases;

public sealed class ReleasePackageBuilder
{
    public ReleasePackage Build(ReleaseAddOrchestrationRequest request, IngestionResult ingestionResult, Guid repositoryId)
    {
        var package = new ReleasePackage
        {
            Version = request.Version,
            Notes = request.Notes,
            RepoId = repositoryId.ToString(),
            CustomProperties = request.CustomProperties,
            OutputArtifacts = ingestionResult.OutputArtifacts,
            Chunks = [],
            Stats = new()
            {
                ComponentCount = (uint)ingestionResult.OutputArtifacts.Select(x => x.ComponentName).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                FileCount = (uint)ingestionResult.OutputArtifacts.Count(x => x.Kind == OutputArtifactKind.File)
            },
        };

        return package;
    }
}