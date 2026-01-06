// Copyright (C) 2025  Lukas Eßmann
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

namespace BinStash.Core.Entities;

public class StorageClass
{
    public string Name { get; set; } = null!;           // PK: "standard"
    public string DisplayName { get; set; } = null!;    // "Standard"
    public string? Description { get; set; }
    public bool IsDeprecated { get; set; }

    // Optional policy knobs - if null, no limit
    public int MaxChunkBytes { get; set; } = 16 * 1024 * 1024;
    //public int? MaxBatchItems { get; set; } = 2048;
}
