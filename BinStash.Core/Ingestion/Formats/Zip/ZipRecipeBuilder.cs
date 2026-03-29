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

using System.Text.Json;

namespace BinStash.Core.Ingestion.Formats.Zip;

public sealed class ZipRecipeBuilder
{
    public byte[] BuildSemanticRecipe(IReadOnlyList<ZipArchiveEntryInfo> allEntries)
    {
        var dto = new ZipRecipeDto
        {
            Entries = allEntries.Select(x => new ZipRecipeEntryDto
            {
                FullName = x.FullName.Replace('\\', '/'),
                IsDirectory = x.IsDirectory
            }).ToList()
        };

        return JsonSerializer.SerializeToUtf8Bytes(dto);
    }

    private sealed class ZipRecipeDto
    {
        public List<ZipRecipeEntryDto> Entries { get; set; } = new();
    }

    private sealed class ZipRecipeEntryDto
    {
        public string FullName { get; set; } = string.Empty;
        public bool IsDirectory { get; set; }
    }
}