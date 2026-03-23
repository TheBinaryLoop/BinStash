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

using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using BinStash.Cli.Utils;

namespace BinStash.Cli.Infrastructure.Svn;

public sealed class SvnCliClient
{
    private readonly string? _username;
    private readonly string? _password;

    public SvnCliClient(string? username, string? password)
    {
        _username = username;
        _password = password;
    }

    public async Task<IReadOnlyList<SvnTagInfo>> ListTagsAsync(string svnRoot)
    {
        var entries = await ListEntriesAsync(svnRoot, recursive: false);

        return entries
            .Where(x => string.Equals(x.Kind, "dir", StringComparison.OrdinalIgnoreCase))
            .Select(x => new
            {
                Name = x.Name.TrimEnd('/'),
                x.Revision
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.Name))
            .Where(x => x.Name != "." && x.Name != "..")
            .Select(x => new SvnTagInfo(
                TagName: x.Name,
                TagUrl: BuildEncodedSvnUrl(svnRoot, x.Name),
                ListRevision: x.Revision,
                LastChangedRevision: x.Revision))
            .OrderBy(x => ExtractBuildNumber(x.TagName))
            .ToList();
    }

    public async Task<IReadOnlyList<SvnFileEntry>> ListFilesRecursiveAsync(string tagUrl)
    {
        var entries = await ListEntriesAsync(tagUrl, recursive: true);

        return entries
            .Where(x => string.Equals(x.Kind, "file", StringComparison.OrdinalIgnoreCase))
            .Select(x => new
            {
                RelativePath = x.Name.Replace('\\', '/'),
                x.Size,
                x.Revision
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.RelativePath))
            .Where(x => !ShouldSkip(x.RelativePath))
            .Select(x => new SvnFileEntry(
                RelativePath: x.RelativePath,
                FileSize: x.Size ?? 0,
                LastChangedRevision: x.Revision ?? 0,
                TagUrl: tagUrl,
                FileUrl: BuildEncodedSvnUrl(tagUrl, x.RelativePath)))
            .ToList();
    }

    public async Task CatFileToDiskAsync(string fileUrl, string destination)
    {
        await using var fs = new FileStream(destination, FileMode.Create);
        await CatFileToStreamAsync(fileUrl, fs);
    }
    
    public async Task CatFileToStreamAsync(string fileUrl, Stream destination)
    {
        var args = BuildArgs($"cat \"{fileUrl}\"");
        var psi = new ProcessStartInfo("svn", args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start svn process.");
        await process.StandardOutput.BaseStream.CopyToAsync(destination);
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode == 0)
            return;

        if (process.ExitCode != 0)
        {
            if (IsSvnPathNotFound(stderr))
                throw new SvnPathNotFoundException(fileUrl, $"svn cat path not found for '{fileUrl}': {stderr}");

            throw new InvalidOperationException($"svn cat failed for '{fileUrl}': {stderr}");
        }
    }

    public async Task PumpFileAsync(string fileUrl, Func<ReadOnlyMemory<byte>, CancellationToken, ValueTask> onChunk, CancellationToken cancellationToken = default)
    {
        var args = BuildArgs($"cat \"{fileUrl}\"");
        var psi = new ProcessStartInfo("svn", args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start svn process.");

        var buffer = new byte[128 * 1024];
        while (true)
        {
            var read = await process.StandardOutput.BaseStream.ReadAsync(buffer, cancellationToken);
            if (read <= 0)
                break;

            await onChunk(buffer.AsMemory(0, read), cancellationToken);
        }

        var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            if (IsSvnPathNotFound(stderr))
                throw new SvnPathNotFoundException(fileUrl, $"svn cat path not found for '{fileUrl}': {stderr}");

            throw new InvalidOperationException($"svn cat failed for '{fileUrl}': {stderr}");
        }
    }
    
    public static string BuildEncodedSvnUrl(string baseUrl, string relativePath)
    {
        var encodedSegments = relativePath
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(Uri.EscapeDataString);

        return $"{baseUrl.TrimEnd('/')}/{string.Join("/", encodedSegments)}";
    }
    
    private async Task<IReadOnlyList<SvnListEntry>> ListEntriesAsync(string url, bool recursive)
    {
        var recursiveFlag = recursive ? "-R " : string.Empty;
        var output = await RunSvnAsync($"list --xml {recursiveFlag}\"{url}\"");

        var doc = System.Xml.Linq.XDocument.Parse(output);
        var result = new List<SvnListEntry>();

        foreach (var entry in doc.Descendants("entry"))
        {
            var kind = entry.Attribute("kind")?.Value;
            var name = entry.Element("name")?.Value;

            if (string.IsNullOrWhiteSpace(kind) || string.IsNullOrWhiteSpace(name))
                continue;

            long? size = null;
            var sizeElement = entry.Element("size");
            if (sizeElement != null && long.TryParse(sizeElement.Value, out var parsedSize))
                size = parsedSize;

            long? revision = null;
            var commitElement = entry.Element("commit");
            var revisionAttr = commitElement?.Attribute("revision")?.Value;
            if (!string.IsNullOrWhiteSpace(revisionAttr) && long.TryParse(revisionAttr, out var parsedRevision))
                revision = parsedRevision;

            result.Add(new SvnListEntry(
                Kind: kind,
                Name: name,
                Size: size,
                Revision: revision));
        }

        return result;
    }

    
    
    private static bool ShouldSkip(string relativePath)
    {
        var p = relativePath.Replace('\\', '/');

        return p.Contains("/.svn/", StringComparison.OrdinalIgnoreCase)
               || p.Contains("/.git/", StringComparison.OrdinalIgnoreCase)
               || p.Contains("/.hg/", StringComparison.OrdinalIgnoreCase)
               || p.StartsWith(".svn/", StringComparison.OrdinalIgnoreCase)
               || p.StartsWith(".git/", StringComparison.OrdinalIgnoreCase)
               || p.StartsWith(".hg/", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<string> RunSvnAsync(string commandArgs)
    {
        var args = BuildArgs(commandArgs);
        var psi = new ProcessStartInfo("svn", args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start svn process.");
        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
            throw new InvalidOperationException($"svn command failed: svn {args}\n{stderr}");

        return stdout;
    }

    private string BuildArgs(string commandArgs)
    {
        var parts = new List<string>
        {
            "--non-interactive",
            "--trust-server-cert-failures=unknown-ca,cn-mismatch,expired,not-yet-valid,other"
        };

        if (!string.IsNullOrWhiteSpace(_username))
        {
            parts.Add("--username");
            parts.Add($"\"{_username}\"");
        }

        if (!string.IsNullOrWhiteSpace(_password))
        {
            parts.Add("--password");
            parts.Add($"\"{_password}\"");
            parts.Add("--no-auth-cache");
        }

        parts.Add(commandArgs);
        return string.Join(" ", parts);
    }

    private static IEnumerable<string> SplitLines(string text) =>
        text.Split(["\r\n", "\n"], StringSplitOptions.None);

    private static int ExtractBuildNumber(string tagName)
    {
        var m = Regex.Match(tagName, @"-(\d+)$");
        return m.Success ? int.Parse(m.Groups[1].Value) : int.MaxValue;
    }

    private static bool IsSvnPathNotFound(string stderr)
    {
        return stderr.Contains("W160013", StringComparison.OrdinalIgnoreCase) ||
               stderr.Contains("path not found", StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class SvnPathNotFoundException : Exception
{
    public string FileUrl { get; }

    public SvnPathNotFoundException(string fileUrl, string message) : base(message)
    {
        FileUrl = fileUrl;
    }
}

internal sealed record SvnListEntry(string Kind, string Name, long? Size, long? Revision);