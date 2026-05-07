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

using BinStash.Server.GraphQL.Services;

namespace BinStash.Server.GraphQL.ObjectTypes;

public sealed class ReleaseType : ObjectType<ReleaseGql>
{
    protected override void Configure(IObjectTypeDescriptor<ReleaseGql> descriptor)
    {
        descriptor.Field(x => x.CustomProperties)
            .Type<AnyType>();

        descriptor.Field("repository")
            .Authorize()
            .ResolveWith<Resolvers>(x => x.GetRepositoryAsync(null!, null!, CancellationToken.None!));
        
        descriptor.Field("metrics")
            .Authorize()
            .ResolveWith<Resolvers>(x => x.GetReleaseMetricsAsync(null!, null!, CancellationToken.None!));
    }

    private sealed class Resolvers
    {
        public Task<RepositoryGql?> GetRepositoryAsync([Parent] ReleaseGql release, [Service] RepositoryQueryService service, CancellationToken ct)
            => service.GetRepositoryByIdAsync(release.RepoId, ct);
        
        public Task<ReleaseMetricsGql?> GetReleaseMetricsAsync([Parent] ReleaseGql release, [Service] ReleaseQueryService service, CancellationToken ct)
            => service.GetReleaseMetricsForReleaseIdAsync(release.Id, ct);
    }
}