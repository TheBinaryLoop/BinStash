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

using BinStash.Cli.Utils;
using Microsoft.Data.Sqlite;

namespace BinStash.Cli.Infrastructure.Svn;

public sealed class ImportStateStore : IAsyncDisposable
{
    private readonly string _connectionString;

    public ImportStateStore(string sqliteFile)
    {
        var cs = new SqliteConnectionStringBuilder
        {
            DataSource = sqliteFile,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();

        _connectionString = cs;
    }

    private async Task<SqliteConnection> OpenConnectionAsync()
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        return connection;
    }
    
    public async Task InitializeAsync()
    {
        await using var connection = await OpenConnectionAsync();
        
        var sql = """
                  create table if not exists import_source (
                      id integer primary key autoincrement,
                      svn_root text not null unique,
                      tenant_slug text not null,
                      repo_name text not null,
                      created_at text not null
                  );

                  create table if not exists svn_tag (
                      id integer primary key autoincrement,
                      source_id integer not null,
                      tag_name text not null,
                      tag_url text not null unique,
                      version text not null,
                      list_revision integer null,
                      last_changed_revision integer null,
                      status integer not null default 0,
                      release_id text null,
                      last_error text null,
                      created_at text not null,
                      updated_at text not null
                  );

                  create table if not exists svn_tag_file (
                      id integer primary key autoincrement,
                      tag_id integer not null,
                      relative_path text not null,
                      file_size integer not null,
                      last_changed_revision integer not null,
                      candidate_key text not null,
                      file_hash_hex text null,
                      unique(tag_id, relative_path)
                  );

                  create index if not exists ix_svn_tag_file_candidate_key on svn_tag_file(candidate_key);
                  create index if not exists ix_svn_tag_file_file_hash_hex on svn_tag_file(file_hash_hex);

                  create table if not exists file_cache (
                      candidate_key text primary key,
                      file_hash_hex text not null,
                      file_size integer not null,
                      chunk_map_json text not null,
                      first_seen_at text not null,
                      last_used_at text not null
                  );
                  """;

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<long> GetOrCreateSourceAsync(string svnRoot, string tenantSlug, string repoName)
    {
        await using var connection = await OpenConnectionAsync();
        await using var check = connection.CreateCommand();
        check.CommandText = "select id from import_source where svn_root = $svnRoot";
        check.Parameters.AddWithValue("$svnRoot", svnRoot);
        var existing = await check.ExecuteScalarAsync();
        if (existing is long id)
            return id;

        await using var insert = connection.CreateCommand();
        insert.CommandText = """
                             insert into import_source (svn_root, tenant_slug, repo_name, created_at)
                             values ($svnRoot, $tenantSlug, $repoName, $createdAt);
                             select last_insert_rowid();
                             """;
        insert.Parameters.AddWithValue("$svnRoot", svnRoot);
        insert.Parameters.AddWithValue("$tenantSlug", tenantSlug);
        insert.Parameters.AddWithValue("$repoName", repoName);
        insert.Parameters.AddWithValue("$createdAt", DateTimeOffset.UtcNow.ToString("O"));

        var inserted = await insert.ExecuteScalarAsync();
        return (long)(inserted ?? throw new InvalidOperationException("Failed to create source."));
    }

    public async Task UpsertTagAsync(long sourceId, SvnTagInfo tag, string version)
    {
        await using var connection = await OpenConnectionAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
                          insert into svn_tag (
                              source_id, tag_name, tag_url, version, list_revision, last_changed_revision, status, created_at, updated_at
                          )
                          values (
                              $sourceId, $tagName, $tagUrl, $version, $listRevision, $lastChangedRevision, $status, $createdAt, $updatedAt
                          )
                          on conflict(tag_url) do update set
                              tag_name = excluded.tag_name,
                              version = excluded.version,
                              list_revision = excluded.list_revision,
                              last_changed_revision = excluded.last_changed_revision,
                              updated_at = excluded.updated_at;
                          """;
        cmd.Parameters.AddWithValue("$sourceId", sourceId);
        cmd.Parameters.AddWithValue("$tagName", tag.TagName);
        cmd.Parameters.AddWithValue("$tagUrl", tag.TagUrl);
        cmd.Parameters.AddWithValue("$version", version);
        cmd.Parameters.AddWithValue("$listRevision", (object?)tag.ListRevision ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$lastChangedRevision", (object?)tag.LastChangedRevision ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$status", (int)ImportTagStatus.Discovered);
        cmd.Parameters.AddWithValue("$createdAt", DateTimeOffset.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("$updatedAt", DateTimeOffset.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<List<(long Id, string TagName, string TagUrl, string Version, ImportTagStatus Status)>> GetTagsToImportAsync(long sourceId, int? limit, bool resume)
    {
        var statuses = resume
            ? new[] { (int)ImportTagStatus.Discovered, (int)ImportTagStatus.Scanned, (int)ImportTagStatus.Failed }
            : new[] { (int)ImportTagStatus.Discovered };
        
        await using var connection = await OpenConnectionAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"""
                           select id, tag_name, tag_url, version, status
                           from svn_tag
                           where source_id = $sourceId
                             and status in ({string.Join(",", statuses)})
                           order by tag_name
                           {(limit.HasValue ? $"limit {limit.Value}" : "")}
                           """;
        cmd.Parameters.AddWithValue("$sourceId", sourceId);

        var result = new List<(long, string, string, string, ImportTagStatus)>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add((
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                (ImportTagStatus)reader.GetInt32(4)
            ));
        }
        
        return result.OrderBy(t => t.Item4, NaturalStringComparer.Instance).ToList();
    }

    public async Task SaveTagFilesAsync(long tagId, IReadOnlyList<SvnFileEntry> files)
    {
        await using var connection = await OpenConnectionAsync();
        await using var tx = (SqliteTransaction)await connection.BeginTransactionAsync();

        await using (var del = connection.CreateCommand())
        {
            del.Transaction = tx;
            del.CommandText = "delete from svn_tag_file where tag_id = $tagId";
            del.Parameters.AddWithValue("$tagId", tagId);
            await del.ExecuteNonQueryAsync();
        }

        foreach (var file in files)
        {
            await using var ins = connection.CreateCommand();
            ins.Transaction = tx;
            ins.CommandText = """
                              insert into svn_tag_file (
                                  tag_id, relative_path, file_size, last_changed_revision, candidate_key, file_hash_hex
                              )
                              values ($tagId, $relativePath, $fileSize, $lastChangedRevision, $candidateKey, null)
                              """;
            ins.Parameters.AddWithValue("$tagId", tagId);
            ins.Parameters.AddWithValue("$relativePath", file.RelativePath);
            ins.Parameters.AddWithValue("$fileSize", file.FileSize);
            ins.Parameters.AddWithValue("$lastChangedRevision", file.LastChangedRevision);
            ins.Parameters.AddWithValue("$candidateKey", file.CandidateKey);
            await ins.ExecuteNonQueryAsync();
        }

        await using (var upd = connection.CreateCommand())
        {
            upd.Transaction = tx;
            upd.CommandText = """
                              update svn_tag
                              set status = $status,
                                  updated_at = $updatedAt
                              where id = $id
                              """;
            upd.Parameters.AddWithValue("$status", (int)ImportTagStatus.Scanned);
            upd.Parameters.AddWithValue("$updatedAt", DateTimeOffset.UtcNow.ToString("O"));
            upd.Parameters.AddWithValue("$id", tagId);
            await upd.ExecuteNonQueryAsync();
        }

        await tx.CommitAsync();
    }

    public async Task<List<(string RelativePath, long FileSize, long LastChangedRevision, string CandidateKey, string? FileHashHex)>> GetTagFilesAsync(long tagId)
    {
        await using var connection = await OpenConnectionAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
                          select relative_path, file_size, last_changed_revision, candidate_key, file_hash_hex
                          from svn_tag_file
                          where tag_id = $tagId
                          order by relative_path
                          """;
        cmd.Parameters.AddWithValue("$tagId", tagId);

        var result = new List<(string, long, long, string, string?)>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add((
                reader.GetString(0),
                reader.GetInt64(1),
                reader.GetInt64(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4)
            ));
        }

        return result;
    }

    public async Task<CachedFileResult?> TryGetCachedFileAsync(string candidateKey)
    {
        await using var connection = await OpenConnectionAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
                          select candidate_key, file_hash_hex, file_size, chunk_map_json
                          from file_cache
                          where candidate_key = $candidateKey
                          """;
        cmd.Parameters.AddWithValue("$candidateKey", candidateKey);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return null;

        return new CachedFileResult(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetInt64(2),
            reader.GetString(3));
    }

    public async Task SaveCachedFileAsync(CachedFileResult result)
    {
        await using var connection = await OpenConnectionAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
                          insert into file_cache (
                              candidate_key, file_hash_hex, file_size, chunk_map_json, first_seen_at, last_used_at
                          )
                          values ($candidateKey, $fileHashHex, $fileSize, $chunkMapJson, $now, $now)
                          on conflict(candidate_key) do update set
                              file_hash_hex = excluded.file_hash_hex,
                              file_size = excluded.file_size,
                              chunk_map_json = excluded.chunk_map_json,
                              last_used_at = excluded.last_used_at
                          """;
        cmd.Parameters.AddWithValue("$candidateKey", result.CandidateKey);
        cmd.Parameters.AddWithValue("$fileHashHex", result.FileHashHex);
        cmd.Parameters.AddWithValue("$fileSize", result.FileSize);
        cmd.Parameters.AddWithValue("$chunkMapJson", result.ChunkMapJson);
        cmd.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task SetTagImportedAsync(long tagId, string releaseId)
    {
        await using var connection = await OpenConnectionAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
                          update svn_tag
                          set status = $status,
                              release_id = $releaseId,
                              last_error = null,
                              updated_at = $updatedAt
                          where id = $id
                          """;
        cmd.Parameters.AddWithValue("$status", (int)ImportTagStatus.Imported);
        cmd.Parameters.AddWithValue("$releaseId", releaseId);
        cmd.Parameters.AddWithValue("$updatedAt", DateTimeOffset.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("$id", tagId);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task SetTagFailedAsync(long tagId, string error)
    {
        await using var connection = await OpenConnectionAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
                          update svn_tag
                          set status = $status,
                              last_error = $error,
                              updated_at = $updatedAt
                          where id = $id
                          """;
        cmd.Parameters.AddWithValue("$status", (int)ImportTagStatus.Failed);
        cmd.Parameters.AddWithValue("$error", error);
        cmd.Parameters.AddWithValue("$updatedAt", DateTimeOffset.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("$id", tagId);
        await cmd.ExecuteNonQueryAsync();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}