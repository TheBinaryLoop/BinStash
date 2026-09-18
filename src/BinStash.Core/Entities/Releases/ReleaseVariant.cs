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

using BinStash.Contracts.Hashing;

namespace BinStash.Core.Entities;

/// <summary>
/// One build target of a release — the Linux payload of <c>1.2.3</c>, or its Windows one.
///
/// <para>
/// A release is the version; a variant is what you actually download. Each variant carries its
/// own release definition, which is what lets a build matrix publish them independently: the
/// definition is content-addressed and immutable, so five agents finishing at five different
/// times can each contribute one without any of them rewriting a shared document.
/// </para>
///
/// <para>
/// Storing them separately costs nothing in space. The chunk store is content-addressed, so
/// everything the targets share — resources, data files, documentation — is already stored once
/// and, since <see cref="TenantStorageSnapshot"/> deduplicates within the tenant, billed once too.
/// </para>
/// </summary>
public class ReleaseVariant
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid ReleaseId { get; set; }
    public virtual Release Release { get; set; } = null!;

    /// <summary>
    /// The canonical target key, unique within the release. See
    /// <see cref="Contracts.Release.ReleaseTarget"/> for why this is an opaque string.
    /// </summary>
    public required string TargetKey { get; set; }

    /// <summary>
    /// Optional structured description of the target — <c>{"os":"linux","arch":"x64"}</c> — stored
    /// as JSON.
    /// </summary>
    /// <remarks>
    /// Kept apart from <see cref="TargetKey"/> so that matching can be structured without identity
    /// becoming structured. A client asking for "whatever runs on this machine" selects on these;
    /// a client asking for a specific build names the key. Neither constrains the other, which is
    /// what lets a target exist that is not a platform at all.
    /// </remarks>
    public string? TargetAttributes { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Hash32 ReleaseDefinitionChecksum { get; set; }

    public required byte SerializerVersion { get; set; }
}
