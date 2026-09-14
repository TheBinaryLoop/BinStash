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

namespace BinStash.Server.Configuration;

/// <summary>
/// Ceilings on how much a single request may send. Bind to the <c>RequestLimits</c> section.
/// </summary>
/// <remarks>
/// Kestrel and gRPC both have framework defaults here (30 MB and 4 MB respectively), so this is
/// less about introducing a limit than about making it explicit, tunable, and reviewable — an
/// inherited default is one nobody has decided on, and both of these are load-bearing for an
/// ingest path that streams large artifacts.
/// </remarks>
public sealed class RequestLimitSettings
{
    public const string SectionName = "RequestLimits";

    /// <summary>
    /// Kestrel's ceiling on a request body, in bytes. Applies to the REST surface, which includes
    /// batched chunk upload and the multipart release finalize — raising it is legitimate, but it
    /// should be a decision rather than a discovery.
    /// </summary>
    public long MaxRequestBodyBytes { get; set; } = 32L * 1024 * 1024;

    /// <summary>
    /// Ceiling on a single inbound gRPC message, in bytes. This is the chunk upload path; a
    /// message carries one chunk plus framing, and FastCDC chunks are far smaller than this.
    /// </summary>
    public int MaxGrpcReceiveBytes { get; set; } = 16 * 1024 * 1024;

    /// <summary>
    /// Ceiling applied to the small JSON endpoints — authentication, account management — that
    /// have no business receiving a large body. Without it they inherit
    /// <see cref="MaxRequestBodyBytes"/>, which lets an unauthenticated caller make the server
    /// read tens of megabytes before it has established who they are.
    /// </summary>
    public long MaxSmallRequestBodyBytes { get; set; } = 64 * 1024;
}
