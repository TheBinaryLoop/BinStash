// Copyright (C) Lukas Eßmann — AGPLv3 or later

using System.Security.Claims;
using BinStash.Core.Auditing;
using BinStash.Core.Auth;
using BinStash.Core.Entities;
using BinStash.Infrastructure.Data;
using BinStash.Server.Context;
using BinStash.Server.GraphQL.Features.Repositories;
using FluentAssertions;
using HotChocolate;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BinStash.Server.Tests.Repositories;

/// <summary>
/// Covers the rules that make a cross-tenant move safe: it must not strand the data in a chunk
/// store the destination cannot address, must not collide with an existing name, and must not
/// carry the source tenant's per-repository grants along with it.
/// </summary>
public class MoveRepositoryTests : IDisposable
{
    private readonly BinStashDbContext _db;

    private readonly Guid _sourceTenantId = Guid.NewGuid();
    private readonly Guid _targetTenantId = Guid.NewGuid();

    private ChunkStore _sharedStore = null!;
    private ChunkStore _otherStore = null!;
    private Repository _repo = null!;

    public MoveRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<BinStashDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new BinStashDbContext(options);
    }

    public void Dispose() => _db.Dispose();

    // -------------------------------------------------------------------------
    // Seeding
    // -------------------------------------------------------------------------

    /// <summary>
    /// Two tenants, two chunk stores. The source tenant's "standard" class is backed by the shared
    /// store; what the target tenant offers is decided per test.
    /// </summary>
    private async Task SeedAsync(bool targetSharesChunkStore, bool targetHasSameRepoName = false)
    {
        _sharedStore = new ChunkStore("shared", ChunkStoreType.Local, new LocalFolderBackendSettings { Path = "/tmp/shared" });
        _otherStore = new ChunkStore("other", ChunkStoreType.Local, new LocalFolderBackendSettings { Path = "/tmp/other" });
        _db.ChunkStores.AddRange(_sharedStore, _otherStore);

        _db.Tenants.AddRange(
            new Tenant { Id = _sourceTenantId, Name = "Source", Slug = "source" },
            new Tenant { Id = _targetTenantId, Name = "Target", Slug = "target" });

        _db.StorageClassMappings.Add(new StorageClassMapping
        {
            TenantId = _sourceTenantId,
            StorageClassName = "standard",
            ChunkStoreId = _sharedStore.Id,
            IsDefault = true
        });

        _db.StorageClassMappings.Add(new StorageClassMapping
        {
            TenantId = _targetTenantId,
            StorageClassName = "archive",
            ChunkStoreId = targetSharesChunkStore ? _sharedStore.Id : _otherStore.Id,
            IsDefault = true
        });

        _repo = new Repository
        {
            Name = "artifacts",
            ChunkStore = _sharedStore,
            ChunkStoreId = _sharedStore.Id,
            TenantId = _sourceTenantId,
            StorageClass = "standard",
            CreatedAt = DateTimeOffset.UtcNow
        };
        _db.Repositories.Add(_repo);

        if (targetHasSameRepoName)
        {
            _db.Repositories.Add(new Repository
            {
                Name = "artifacts",
                ChunkStore = _sharedStore,
                ChunkStoreId = _sharedStore.Id,
                TenantId = _targetTenantId,
                StorageClass = "archive",
                CreatedAt = DateTimeOffset.UtcNow
            });
        }

        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private RepositoryMutationService CreateSut(StubAuditWriter audit, params Guid[] adminOfTenants)
        => new(
            _db,
            new StubHttpContextAccessor(_sourceTenantId),
            new StubAuthorizationService(adminOfTenants),
            audit);

    // -------------------------------------------------------------------------
    // Tests
    // -------------------------------------------------------------------------

    [Fact]
    public async Task MoveRepository_ReassignsTenantAndRelabelsStorageClass()
    {
        await SeedAsync(targetSharesChunkStore: true);
        var audit = new StubAuditWriter();
        var sut = CreateSut(audit, _sourceTenantId, _targetTenantId);

        var result = await sut.MoveRepositoryAsync(
            new MoveRepositoryInput { RepoId = _repo.Id, TargetTenantId = _targetTenantId },
            TestContext.Current.CancellationToken);

        var moved = await _db.Repositories.AsNoTracking().SingleAsync(r => r.Id == _repo.Id, TestContext.Current.CancellationToken);

        moved.TenantId.Should().Be(_targetTenantId);
        // The target has no "standard" class, so the repository picks up the target's default…
        moved.StorageClass.Should().Be("archive");
        // …but the data itself never moves.
        moved.ChunkStoreId.Should().Be(_sharedStore.Id);
        result.StorageClass.Should().Be("archive");
    }

    [Fact]
    public async Task MoveRepository_RevokesPerRepositoryGrants()
    {
        await SeedAsync(targetSharesChunkStore: true);

        _db.RepositoryRoleAssignments.Add(new RepositoryRoleAssignment
        {
            RepositoryId = _repo.Id,
            SubjectType = SubjectType.User,
            SubjectId = Guid.NewGuid(),
            RoleName = "RepoAdmin",
            GrantedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var sut = CreateSut(new StubAuditWriter(), _sourceTenantId, _targetTenantId);

        await sut.MoveRepositoryAsync(
            new MoveRepositoryInput { RepoId = _repo.Id, TargetTenantId = _targetTenantId },
            TestContext.Current.CancellationToken);

        var remaining = await _db.RepositoryRoleAssignments
            .AsNoTracking()
            .CountAsync(a => a.RepositoryId == _repo.Id, TestContext.Current.CancellationToken);

        remaining.Should().Be(0);
    }

    [Fact]
    public async Task MoveRepository_WritesAnAuditEntryToBothTenants()
    {
        await SeedAsync(targetSharesChunkStore: true);
        var audit = new StubAuditWriter();
        var sut = CreateSut(audit, _sourceTenantId, _targetTenantId);

        await sut.MoveRepositoryAsync(
            new MoveRepositoryInput { RepoId = _repo.Id, TargetTenantId = _targetTenantId },
            TestContext.Current.CancellationToken);

        audit.Entries.Should().HaveCount(2);
        audit.Entries.Should().OnlyContain(e => e.Action == AuditActions.RepositoryMoved);
        audit.Entries.Select(e => e.TenantId).Should().BeEquivalentTo([_sourceTenantId, _targetTenantId]);
    }

    [Fact]
    public async Task MoveRepository_WhenTargetHasNoClassForTheChunkStore_Throws()
    {
        await SeedAsync(targetSharesChunkStore: false);
        var sut = CreateSut(new StubAuditWriter(), _sourceTenantId, _targetTenantId);

        var act = () => sut.MoveRepositoryAsync(
            new MoveRepositoryInput { RepoId = _repo.Id, TargetTenantId = _targetTenantId },
            TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<GraphQLException>().WithMessage("*no enabled storage class*");

        var untouched = await _db.Repositories.AsNoTracking().SingleAsync(r => r.Id == _repo.Id, TestContext.Current.CancellationToken);
        untouched.TenantId.Should().Be(_sourceTenantId);
    }

    [Fact]
    public async Task MoveRepository_WhenTargetAlreadyHasThatName_Throws()
    {
        await SeedAsync(targetSharesChunkStore: true, targetHasSameRepoName: true);
        var sut = CreateSut(new StubAuditWriter(), _sourceTenantId, _targetTenantId);

        var act = () => sut.MoveRepositoryAsync(
            new MoveRepositoryInput { RepoId = _repo.Id, TargetTenantId = _targetTenantId },
            TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<GraphQLException>().WithMessage("*already exists*");
    }

    [Fact]
    public async Task MoveRepository_WhenCallerIsNotAdminOfTheTarget_Throws()
    {
        await SeedAsync(targetSharesChunkStore: true);
        var sut = CreateSut(new StubAuditWriter(), _sourceTenantId);

        var act = () => sut.MoveRepositoryAsync(
            new MoveRepositoryInput { RepoId = _repo.Id, TargetTenantId = _targetTenantId },
            TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<GraphQLException>().WithMessage("Forbidden.");

        var untouched = await _db.Repositories.AsNoTracking().SingleAsync(r => r.Id == _repo.Id, TestContext.Current.CancellationToken);
        untouched.TenantId.Should().Be(_sourceTenantId);
    }

    [Fact]
    public async Task MoveRepository_IntoItsOwnTenant_Throws()
    {
        await SeedAsync(targetSharesChunkStore: true);
        var sut = CreateSut(new StubAuditWriter(), _sourceTenantId);

        var act = () => sut.MoveRepositoryAsync(
            new MoveRepositoryInput { RepoId = _repo.Id, TargetTenantId = _sourceTenantId },
            TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<GraphQLException>().WithMessage("*already in this workspace*");
    }

    [Fact]
    public async Task MoveRepository_WithAnExplicitStorageClassTheTargetCannotBack_Throws()
    {
        await SeedAsync(targetSharesChunkStore: true);
        var sut = CreateSut(new StubAuditWriter(), _sourceTenantId, _targetTenantId);

        var act = () => sut.MoveRepositoryAsync(
            new MoveRepositoryInput
            {
                RepoId = _repo.Id,
                TargetTenantId = _targetTenantId,
                StorageClassName = "standard"
            },
            TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<GraphQLException>().WithMessage("*is not available in workspace*");
    }

    // -------------------------------------------------------------------------
    // Stubs
    // -------------------------------------------------------------------------

    private sealed class StubAuditWriter : IAuditLogWriter
    {
        public List<AuditEntryDraft> Entries { get; } = [];

        public Task WriteAsync(AuditEntryDraft entry, CancellationToken ct = default)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }
    }

    /// <summary>Succeeds for the listed tenants only, mirroring "is a tenant admin there".</summary>
    private sealed class StubAuthorizationService(Guid[] adminOfTenants) : IAuthorizationService
    {
        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, IEnumerable<IAuthorizationRequirement> requirements)
            => Task.FromResult(AuthorizationResult.Failed());

        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, string policyName)
        {
            var tenantId = (resource as BinStash.Server.Auth.Tenant.TenantAuthResource)?.TenantId;

            return Task.FromResult(tenantId is not null && adminOfTenants.Contains(tenantId.Value)
                ? AuthorizationResult.Success()
                : AuthorizationResult.Failed());
        }
    }

    private sealed class StubHttpContextAccessor : IHttpContextAccessor
    {
        public StubHttpContextAccessor(Guid tenantId)
        {
            var services = new ServiceCollection();
            services.AddSingleton<ITenantContext>(new TenantContext
            {
                TenantId = tenantId,
                TenantSlug = "source",
                IsResolved = true
            });

            HttpContext = new DefaultHttpContext
            {
                RequestServices = services.BuildServiceProvider(),
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())],
                    "Test"))
            };
        }

        public HttpContext? HttpContext { get; set; }
    }
}
