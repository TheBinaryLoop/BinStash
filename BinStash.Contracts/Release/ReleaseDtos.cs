// Copyright (C) 2025-2026  Lukas Eßmann
// 
//     This program is free software: you can redistribute it and/or modify
//     it under the terms of the GNU Affero General Public License as published
//     by the Free Software Foundation, either version 3 of the License, or
//     (at your option) any later version.
// 
//     This program is distributed in the hope that it will be useful,
//     but WITHOUT ANY WARRANTY; without even the implied warranty of
//     MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//     GNU Affero General Public License for more details.
// 
//     You should have received a copy of the GNU Affero General Public License
//     along with this program.  If not, see <https://www.gnu.org/licenses/>.

using BinStash.Contracts.Repos;

namespace BinStash.Contracts.Release;

public class ReleaseSummaryDto
{
    public required Guid Id { get; set; }
    public required string Version { get; set; }
    public required DateTimeOffset CreatedAt { get; set; }
    public string? Notes { get; set; }
    
    public RepositorySummaryDto Repository { get; set; } = null!;
}