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

// ============================================================
//  BinStash.ChunkStoreExplorer — read-only (and selective
//  rebuild) TUI for inspecting ChunkStore pack files and
//  index structures on disk.
//
//  CONCURRENCY SAFETY:
//  All file reads use FileShare modes that coexist with a
//  running BinStash server:
//
//    .pack files  → FileShare.ReadWrite
//    .log files   → FileShare.Read
//    .seg-*.idx   → FileShare.ReadWrite|Delete
//    .bloom files → File.ReadAllBytes (no persistent server handle)
//
//  Rebuild operations (bloom / segment) write a sibling .tmp
//  file then atomically rename it into place — safe even if
//  the server is running, because the server's compaction lock
//  is not held by this process.  Rebuilds are only triggered
//  explicitly by the user.
// ============================================================

using System.Buffers.Binary;
using System.IO.Hashing;
using BinStash.Contracts.Hashing;
using BinStash.Contracts.Release;
using BinStash.Core.Serialization;
using BinStash.Core.Serialization.Utils;
using BinStash.Infrastructure.Storage.FileDefinition;
using BinStash.Infrastructure.Storage.Indexing;
using Blake3;
using Microsoft.Win32.SafeHandles;
using Spectre.Console;

// ============================================================
// Entry point
// ============================================================

if (args.Length < 1)
{
    AnsiConsole.MarkupLine("[red]Usage:[/] BinStash.ChunkStoreExplorer <storeRoot>");
    return 1;
}

var storeRoot = args[0];
if (!Directory.Exists(storeRoot))
{
    AnsiConsole.MarkupLine($"[red]ERROR:[/] Store root not found: {storeRoot}");
    return 1;
}

AnsiConsole.Clear();
await RunExplorerAsync(storeRoot);
return 0;

// ============================================================
// Explorer — main loop
// ============================================================

static async Task RunExplorerAsync(string storeRoot)
{
    // Scan bucket counts once on startup.
    Dictionary<string, long> chunkCounts   = new();
    Dictionary<string, long> fileDefCounts = new();
    Dictionary<string, long> releaseCounts = new();

    await AnsiConsole.Status()
        .Spinner(Spinner.Known.Dots)
        .StartAsync("[grey]Scanning buckets...[/]", _ =>
        {
            chunkCounts   = ScanBucketCounts(Path.Combine(storeRoot, "Chunks"),   "chunks");
            fileDefCounts = ScanBucketCounts(Path.Combine(storeRoot, "FileDefs"), "fileDefs");
            releaseCounts = ScanReleaseCounts(Path.Combine(storeRoot, "Releases"));
            return Task.CompletedTask;
        });

    // Root level — the store itself plus the three categories
    var rootLevel = new ExplorerLevel
    {
        Title = $"[bold blue]BinStash ChunkStore Explorer[/]  [grey]{Markup.Escape(storeRoot)}[/]",
        Items = BuildRootItems(storeRoot, chunkCounts, fileDefCounts, releaseCounts)
    };

    // Navigation stack
    var stack = new Stack<ExplorerLevel>();
    stack.Push(rootLevel);

    // Right-panel content (rendered after left panel)
    string rightContent = BuildRightPanel(rootLevel.Selected, storeRoot, chunkCounts, fileDefCounts, releaseCounts);

    // Scroll offset for the left list
    int scrollOffset = 0;
    const int PageSize = 20;

    while (true)
    {
        var currentLevel = stack.Peek();
        var selected     = currentLevel.Selected;

        // Clamp scroll so cursor is visible
        if (currentLevel.Cursor < scrollOffset)
            scrollOffset = currentLevel.Cursor;
        if (currentLevel.Cursor >= scrollOffset + PageSize)
            scrollOffset = currentLevel.Cursor - PageSize + 1;

        // Render
        Console.CursorVisible = false;
        AnsiConsole.Clear();
        RenderFrame(currentLevel, stack.Count, scrollOffset, PageSize, rightContent, storeRoot);

        // Status line
        AnsiConsole.MarkupLine("[grey]↑↓ Move   Enter Drill in   Backspace/Esc Back   A Action   Q Quit[/]");

        // Input
        var key = Console.ReadKey(intercept: true);

        if (key.Key == ConsoleKey.UpArrow)
        {
            if (currentLevel.Cursor > 0)
            {
                currentLevel.Cursor--;
                rightContent = BuildRightPanel(currentLevel.Selected, storeRoot, chunkCounts, fileDefCounts, releaseCounts);
            }
        }
        else if (key.Key == ConsoleKey.DownArrow)
        {
            if (currentLevel.Cursor < currentLevel.Items.Count - 1)
            {
                currentLevel.Cursor++;
                rightContent = BuildRightPanel(currentLevel.Selected, storeRoot, chunkCounts, fileDefCounts, releaseCounts);
            }
        }
        else if (key.Key == ConsoleKey.PageUp)
        {
            currentLevel.Cursor = Math.Max(0, currentLevel.Cursor - PageSize);
            rightContent = BuildRightPanel(currentLevel.Selected, storeRoot, chunkCounts, fileDefCounts, releaseCounts);
        }
        else if (key.Key == ConsoleKey.PageDown)
        {
            currentLevel.Cursor = Math.Min(currentLevel.Items.Count - 1, currentLevel.Cursor + PageSize);
            rightContent = BuildRightPanel(currentLevel.Selected, storeRoot, chunkCounts, fileDefCounts, releaseCounts);
        }
        else if (key.Key == ConsoleKey.Enter || key.Key == ConsoleKey.RightArrow)
        {
            if (selected is not null)
            {
                var child = await DrillIntoAsync(storeRoot, selected, chunkCounts, fileDefCounts, releaseCounts);
                if (child is not null)
                {
                    stack.Push(child);
                    scrollOffset = 0;
                    rightContent = BuildRightPanel(child.Selected, storeRoot, chunkCounts, fileDefCounts, releaseCounts);
                }
                else
                {
                    // Leaf action — invoke action menu
                    await RunActionMenuAsync(storeRoot, selected, chunkCounts, fileDefCounts, releaseCounts);
                    // Refresh right panel after action returns
                    rightContent = BuildRightPanel(currentLevel.Selected, storeRoot, chunkCounts, fileDefCounts, releaseCounts);
                }
            }
        }
        else if (key.Key is ConsoleKey.Backspace or ConsoleKey.Escape or ConsoleKey.LeftArrow)
        {
            if (stack.Count > 1)
            {
                stack.Pop();
                scrollOffset = 0;
                rightContent = BuildRightPanel(stack.Peek().Selected, storeRoot, chunkCounts, fileDefCounts, releaseCounts);
            }
        }
        else if (key.Key == ConsoleKey.A || key.KeyChar is 'a')
        {
            if (selected is not null)
            {
                await RunActionMenuAsync(storeRoot, selected, chunkCounts, fileDefCounts, releaseCounts);
                rightContent = BuildRightPanel(currentLevel.Selected, storeRoot, chunkCounts, fileDefCounts, releaseCounts);
            }
        }
        else if (key.Key == ConsoleKey.Q || key.KeyChar is 'q')
        {
            break;
        }
    }
}

// ============================================================
// Frame rendering
// ============================================================

static void RenderFrame(
    ExplorerLevel level,
    int stackDepth,
    int scrollOffset,
    int pageSize,
    string rightContent,
    string storeRoot)
{
    // Title bar
    AnsiConsole.MarkupLine(level.Title);
    AnsiConsole.MarkupLine(new string('─', Math.Min(Console.WindowWidth - 1, 120)));

    // Two-column layout: left = list, right = detail
    var leftLines  = BuildLeftPanel(level, scrollOffset, pageSize);
    var rightLines = rightContent.Split('\n');

    var leftWidth  = Math.Max(40, Console.WindowWidth / 2 - 2);
    var rows       = Math.Max(leftLines.Length, rightLines.Length);

    for (var i = 0; i < rows; i++)
    {
        var left  = i < leftLines.Length  ? leftLines[i]  : "";
        var right = i < rightLines.Length ? rightLines[i] : "";

        // Pad left to fixed width (strip markup length approximation)
        var leftPadded = left.PadRight(leftWidth + CountMarkupExtra(left));

        // Use a table row separator
        AnsiConsole.Markup($"{leftPadded}[grey] │ [/]{right}\n");
    }

    AnsiConsole.MarkupLine(new string('─', Math.Min(Console.WindowWidth - 1, 120)));
}

static string[] BuildLeftPanel(ExplorerLevel level, int scrollOffset, int pageSize)
{
    var lines = new List<string>();
    var end   = Math.Min(scrollOffset + pageSize, level.Items.Count);

    if (scrollOffset > 0)
        lines.Add($"[grey]  ↑ {scrollOffset} more above[/]");

    for (var i = scrollOffset; i < end; i++)
    {
        var item    = level.Items[i];
        var cursor  = i == level.Cursor ? "[bold yellow]>[/] " : "  ";
        var label   = i == level.Cursor
            ? $"[bold]{Markup.Escape(item.Label)}[/]"
            : Markup.Escape(item.Label);
        lines.Add($"{cursor}{label}  [grey]{Markup.Escape(item.Detail)}[/]");
    }

    if (end < level.Items.Count)
        lines.Add($"[grey]  ↓ {level.Items.Count - end} more below[/]");

    return lines.ToArray();
}

// Approximate extra chars added by Spectre markup tags so PadRight works acceptably.
static int CountMarkupExtra(string s)
{
    var extra = 0;
    var i = 0;
    while (i < s.Length)
    {
        if (s[i] == '[')
        {
            var end = s.IndexOf(']', i);
            if (end > i) { extra += end - i + 1; i = end + 1; continue; }
        }
        i++;
    }
    return extra;
}

// ============================================================
// Navigation — drill into a node
// Returns null if the node is a leaf (action target, not a container).
// ============================================================

static async Task<ExplorerLevel?> DrillIntoAsync(
    string storeRoot,
    ExplorerItem item,
    Dictionary<string, long> chunkCounts,
    Dictionary<string, long> fileDefCounts,
    Dictionary<string, long> releaseCounts)
{
    switch (item.Tag)
    {
        case StoreTag:
            // Store overview is an action, not a drill-in
            return null;

        case CategoryTag cat:
            return BuildCategoryLevel(cat, chunkCounts, fileDefCounts);

        case PrefixGroup1Tag g1:
        {
            var counts = g1.CatName == "Chunks" ? chunkCounts : fileDefCounts;
            return BuildPrefixGroup2Level(g1, counts);
        }

        case PrefixGroup2Tag g2:
        {
            var counts = g2.CatName == "Chunks" ? chunkCounts : fileDefCounts;
            return BuildBucketLevel(g2, counts);
        }

        case BucketTag bucket:
            return await BuildBucketFilesLevelAsync(storeRoot, bucket);

        case ReleasesTag rt:
            return BuildReleaseBucketListLevel(rt, releaseCounts);

        case RdefBucketTag rbt:
            return BuildReleaseBucketLevel(rbt);

        case RdefFileTag rdf:
            return await BuildRdefArtifactListLevelAsync(rdf);

        case RdefArtifactTag rat:
            return BuildRdefContainerMemberLevel(rat);

        case RdefFolderTag rft:
            return BuildRdefFolderLevel(rft);

        // Leaf nodes — handled via action menu, not drill-in
        case PackFileTag:
        case SegFileTag:
        case BloomFileTag:
        case LogFileTag:
        case RdefContainerMemberTag:
            return null;

        default:
            return null;
    }
}

// ============================================================
// Level builders
// ============================================================

static List<ExplorerItem> BuildRootItems(
    string storeRoot,
    Dictionary<string, long> chunkCounts,
    Dictionary<string, long> fileDefCounts,
    Dictionary<string, long> releaseCounts)
{
    var chunkTotal    = chunkCounts.Values.Sum();
    var fileDefTotal  = fileDefCounts.Values.Sum();
    var releaseTotal  = releaseCounts.Values.Sum();

    return new List<ExplorerItem>
    {
        new("Store Overview",
            "stats, volume info",
            new StoreTag()),

        new("Chunks",
            $"{chunkCounts.Count} buckets  {chunkTotal:N0} entries",
            new CategoryTag("Chunks", "chunks", Path.Combine(storeRoot, "Chunks"))),

        new("FileDefs",
            $"{fileDefCounts.Count} buckets  {fileDefTotal:N0} entries",
            new CategoryTag("FileDefs", "fileDefs", Path.Combine(storeRoot, "FileDefs"))),

        new("Releases",
            $"{releaseCounts.Count} buckets  {releaseTotal:N0} files",
            new ReleasesTag(Path.Combine(storeRoot, "Releases"))),
    };
}

static ExplorerLevel BuildCategoryLevel(
    CategoryTag cat,
    Dictionary<string, long> chunkCounts,
    Dictionary<string, long> fileDefCounts)
{
    var counts = cat.CatName == "Chunks" ? chunkCounts : fileDefCounts;
    var items  = new List<ExplorerItem>();

    for (var nibble = 0; nibble < 16; nibble++)
    {
        var digit1    = nibble.ToString("x1");
        var matching  = counts.Where(kv => kv.Key.StartsWith(digit1, StringComparison.Ordinal)).ToList();
        if (matching.Count == 0) continue;

        var totalEntries = matching.Sum(kv => kv.Value);
        items.Add(new ExplorerItem(
            $"{digit1}xx",
            $"{matching.Count} buckets  {totalEntries:N0} entries",
            new PrefixGroup1Tag(cat.CatName, cat.CatPrefix, cat.CatDir, digit1)));
    }

    return new ExplorerLevel
    {
        Title = $"[bold blue]BinStash ChunkStore Explorer[/]  [grey]/ {cat.CatName}[/]",
        Items = items
    };
}

static ExplorerLevel BuildPrefixGroup2Level(
    PrefixGroup1Tag g1,
    Dictionary<string, long> counts)
{
    var items = new List<ExplorerItem>();

    for (var nibble = 0; nibble < 16; nibble++)
    {
        var digits2   = g1.Digit1 + nibble.ToString("x1");
        var matching  = counts.Where(kv => kv.Key.StartsWith(digits2, StringComparison.Ordinal)).ToList();
        if (matching.Count == 0) continue;

        var totalEntries = matching.Sum(kv => kv.Value);
        items.Add(new ExplorerItem(
            $"{digits2}x",
            $"{matching.Count} buckets  {totalEntries:N0} entries",
            new PrefixGroup2Tag(g1.CatName, g1.CatPrefix, g1.CatDir, digits2)));
    }

    return new ExplorerLevel
    {
        Title = $"[bold blue]BinStash ChunkStore Explorer[/]  [grey]/ {g1.CatName} / {g1.Digit1}xx[/]",
        Items = items
    };
}

static ExplorerLevel BuildBucketLevel(
    PrefixGroup2Tag g2,
    Dictionary<string, long> counts)
{
    var items = counts
        .Where(kv => kv.Key.StartsWith(g2.Digits2, StringComparison.Ordinal))
        .OrderBy(kv => kv.Key)
        .Select(kv =>
        {
            var bucketDir  = Path.Combine(g2.CatDir, kv.Key[..2]);
            var packCount  = Directory.Exists(bucketDir)
                ? Directory.EnumerateFiles(bucketDir, $"{g2.CatPrefix}{kv.Key}-*.pack").Count()
                : 0;
            return new ExplorerItem(
                kv.Key,
                $"{kv.Value:N0} entries  {packCount} pack{(packCount == 1 ? "" : "s")}",
                new BucketTag(g2.CatName, g2.CatPrefix, g2.CatDir, kv.Key));
        })
        .ToList();

    return new ExplorerLevel
    {
        Title = $"[bold blue]BinStash ChunkStore Explorer[/]  [grey]/ {g2.CatName} / {g2.Digits2}x[/]",
        Items = items
    };
}

static async Task<ExplorerLevel?> BuildBucketFilesLevelAsync(string storeRoot, BucketTag bucket)
{
    var bucketDir = Path.Combine(bucket.CatDir, bucket.Prefix[..2]);
    if (!Directory.Exists(bucketDir)) return null;

    var items = new List<ExplorerItem>();

    // Pack files
    foreach (var pf in Directory.EnumerateFiles(bucketDir, $"{bucket.CatPrefix}{bucket.Prefix}-*.pack").OrderBy(f => f))
    {
        var fi = new FileInfo(pf);
        items.Add(new ExplorerItem(
            Path.GetFileName(pf),
            FormatBytes(fi.Length),
            new PackFileTag(pf, bucket.Prefix, bucket.CatPrefix)));
    }

    // Segment + bloom pairs
    foreach (var sf in Directory.EnumerateFiles(bucketDir, $"{bucket.CatPrefix}{bucket.Prefix}.seg-*.idx").OrderBy(f => f))
    {
        var fi        = new FileInfo(sf);
        var bloomPath = Path.ChangeExtension(sf, ".bloom");
        var level     = SortedIndexSegment.GetLevel(Path.GetFileName(sf));
        var count     = SortedIndexSegment.ReadEntryCountFromHeader(sf);
        var hasBloom  = File.Exists(bloomPath);

        items.Add(new ExplorerItem(
            Path.GetFileName(sf),
            $"L{(level >= 0 ? level.ToString() : "?")}  {count:N0} entries  {FormatBytes(fi.Length)}{(hasBloom ? "  bloom:ok" : "  no bloom")}",
            new SegFileTag(sf, bloomPath, bucket.Prefix, bucket.CatDir, bucket.CatPrefix)));

        if (hasBloom)
        {
            var bfi = new FileInfo(bloomPath);
            items.Add(new ExplorerItem(
                Path.GetFileName(bloomPath),
                FormatBytes(bfi.Length),
                new BloomFileTag(bloomPath, sf, bucket.Prefix, bucket.CatDir, bucket.CatPrefix)));
        }
    }

    // Log file
    var logPath = Path.Combine(bucketDir, $"{bucket.CatPrefix}{bucket.Prefix}.log");
    if (File.Exists(logPath))
    {
        var fi    = new FileInfo(logPath);
        var count = CountLogEntries(logPath);
        items.Add(new ExplorerItem(
            Path.GetFileName(logPath),
            $"{count:N0} entries  {FormatBytes(fi.Length)}",
            new LogFileTag(logPath, bucket.Prefix, bucket.CatPrefix)));
    }

    if (items.Count == 0) return null;

    return await Task.FromResult(new ExplorerLevel
    {
        Title = $"[bold blue]BinStash ChunkStore Explorer[/]  [grey]/ {bucket.CatName} / {bucket.Prefix}[/]",
        Items = items
    });
}

// ============================================================
// Right panel content
// ============================================================

static string BuildRightPanel(
    ExplorerItem? item,
    string storeRoot,
    Dictionary<string, long> chunkCounts,
    Dictionary<string, long> fileDefCounts,
    Dictionary<string, long> releaseCounts)
{
    if (item is null) return "[grey](nothing selected)[/]";

    return item.Tag switch
    {
        StoreTag       => BuildStoreRightPanel(storeRoot, chunkCounts, fileDefCounts),
        CategoryTag c  => BuildCategoryRightPanel(c, chunkCounts, fileDefCounts),
        PrefixGroup1Tag g1 => BuildGroupRightPanel(g1.CatName, g1.Digit1 + "xx",
            (g1.CatName == "Chunks" ? chunkCounts : fileDefCounts)
                .Where(kv => kv.Key.StartsWith(g1.Digit1, StringComparison.Ordinal))
                .ToDictionary(kv => kv.Key, kv => kv.Value)),
        PrefixGroup2Tag g2 => BuildGroupRightPanel(g2.CatName, g2.Digits2 + "x",
            (g2.CatName == "Chunks" ? chunkCounts : fileDefCounts)
                .Where(kv => kv.Key.StartsWith(g2.Digits2, StringComparison.Ordinal))
                .ToDictionary(kv => kv.Key, kv => kv.Value)),
        BucketTag b    => BuildBucketRightPanel(b),
        PackFileTag p  => BuildPackFileRightPanel(p),
        SegFileTag s   => BuildSegFileRightPanel(s),
        BloomFileTag b => BuildBloomFileRightPanel(b),
        LogFileTag l   => BuildLogFileRightPanel(l),
        ReleasesTag rt => BuildReleasesRightPanel(rt, releaseCounts),
        RdefBucketTag rbt => BuildRdefBucketRightPanel(rbt),
        RdefFileTag rf => BuildRdefFileRightPanel(rf),
        RdefArtifactTag rat => BuildRdefArtifactRightPanel(rat),
        RdefContainerMemberTag rmt => BuildRdefContainerMemberRightPanel(rmt),
        RdefFolderTag rft => BuildRdefFolderRightPanel(rft),
        _              => "[grey](unknown node)[/]"
    };
}

static string BuildStoreRightPanel(
    string storeRoot,
    Dictionary<string, long> chunkCounts,
    Dictionary<string, long> fileDefCounts)
{
    var chunkTotal   = chunkCounts.Values.Sum();
    var fileDefTotal = fileDefCounts.Values.Sum();

    var sb = new System.Text.StringBuilder();
    sb.AppendLine("[bold cyan]Store Overview[/]");
    sb.AppendLine();
    sb.AppendLine($"[grey]Chunks[/]");
    sb.AppendLine($"  Buckets:       {chunkCounts.Count:N0}");
    sb.AppendLine($"  Index entries: {chunkTotal:N0}");
    sb.AppendLine();
    sb.AppendLine($"[grey]FileDefs[/]");
    sb.AppendLine($"  Buckets:       {fileDefCounts.Count:N0}");
    sb.AppendLine($"  Index entries: {fileDefTotal:N0}");
    sb.AppendLine();
    sb.AppendLine("[grey]Press A or Enter for full store stats[/]");
    return sb.ToString().TrimEnd();
}

static string BuildCategoryRightPanel(
    CategoryTag cat,
    Dictionary<string, long> chunkCounts,
    Dictionary<string, long> fileDefCounts)
{
    var counts = cat.CatName == "Chunks" ? chunkCounts : fileDefCounts;
    var total  = counts.Values.Sum();

    var sb = new System.Text.StringBuilder();
    sb.AppendLine($"[bold cyan]{cat.CatName}[/]");
    sb.AppendLine();
    sb.AppendLine($"  Non-empty buckets: {counts.Count:N0}");
    sb.AppendLine($"  Total entries:     {total:N0}");
    sb.AppendLine();

    // Show top-5 groups by entry count
    var groups = Enumerable.Range(0, 16)
        .Select(n => n.ToString("x1"))
        .Select(d => (Digit: d,
            Entries: counts.Where(kv => kv.Key.StartsWith(d, StringComparison.Ordinal)).Sum(kv => kv.Value)))
        .Where(g => g.Entries > 0)
        .OrderByDescending(g => g.Entries)
        .Take(5)
        .ToList();

    if (groups.Count > 0)
    {
        sb.AppendLine("[grey]Top prefix groups (by entries):[/]");
        foreach (var (d, e) in groups)
            sb.AppendLine($"  {d}xx  {e:N0}");
    }

    return sb.ToString().TrimEnd();
}

static string BuildGroupRightPanel(string catName, string groupLabel, Dictionary<string, long> matchingCounts)
{
    var total   = matchingCounts.Values.Sum();
    var buckets = matchingCounts.Count;

    var sb = new System.Text.StringBuilder();
    sb.AppendLine($"[bold cyan]{catName} / {groupLabel}[/]");
    sb.AppendLine();
    sb.AppendLine($"  Non-empty buckets: {buckets:N0}");
    sb.AppendLine($"  Total entries:     {total:N0}");
    return sb.ToString().TrimEnd();
}

static string BuildBucketRightPanel(BucketTag bucket)
{
    var bucketDir = Path.Combine(bucket.CatDir, bucket.Prefix[..2]);
    var sb = new System.Text.StringBuilder();
    sb.AppendLine($"[bold cyan]{bucket.CatName} / {bucket.Prefix}[/]");
    sb.AppendLine();

    if (!Directory.Exists(bucketDir))
    {
        sb.AppendLine("[grey](bucket directory missing)[/]");
        return sb.ToString().TrimEnd();
    }

    var packs   = Directory.EnumerateFiles(bucketDir, $"{bucket.CatPrefix}{bucket.Prefix}-*.pack").OrderBy(f => f).ToList();
    var segs    = Directory.EnumerateFiles(bucketDir, $"{bucket.CatPrefix}{bucket.Prefix}.seg-*.idx").OrderBy(f => f).ToList();
    var blooms  = Directory.EnumerateFiles(bucketDir, $"{bucket.CatPrefix}{bucket.Prefix}.seg-*.bloom").OrderBy(f => f).ToList();
    var logPath = Path.Combine(bucketDir, $"{bucket.CatPrefix}{bucket.Prefix}.log");

    var packBytes = packs.Sum(f => new FileInfo(f).Length);
    var segBytes  = segs.Sum(f => new FileInfo(f).Length);

    sb.AppendLine($"  Pack files:    {packs.Count}  ({FormatBytes(packBytes)})");
    sb.AppendLine($"  Segments:      {segs.Count}  ({FormatBytes(segBytes)})");
    sb.AppendLine($"  Bloom filters: {blooms.Count}");
    sb.AppendLine($"  Log:           {(File.Exists(logPath) ? $"yes  {FormatBytes(new FileInfo(logPath).Length)}" : "no")}");
    sb.AppendLine();
    sb.AppendLine("[grey]Actions (press A):[/]");
    sb.AppendLine("  Integrity Check");
    sb.AppendLine("  Verify Pack Offsets");
    sb.AppendLine("  Rebuild Segment (from packs)");
    sb.AppendLine("  Rebuild Bloom Filters");
    if (File.Exists(logPath)) sb.AppendLine("  Dump Log");
    return sb.ToString().TrimEnd();
}

static string BuildPackFileRightPanel(PackFileTag tag)
{
    var fi = new FileInfo(tag.Path);
    var sb = new System.Text.StringBuilder();
    sb.AppendLine($"[bold cyan]Pack File[/]");
    sb.AppendLine();
    sb.AppendLine($"  {Markup.Escape(Path.GetFileName(tag.Path))}");
    sb.AppendLine($"  Size: {FormatBytes(fi.Length)}");
    sb.AppendLine();
    sb.AppendLine("[grey]Actions (press A):[/]");
    sb.AppendLine("  Inspect entries + checksum");
    return sb.ToString().TrimEnd();
}

static string BuildSegFileRightPanel(SegFileTag tag)
{
    var fi    = new FileInfo(tag.Path);
    var level = SortedIndexSegment.GetLevel(Path.GetFileName(tag.Path));
    var count = SortedIndexSegment.ReadEntryCountFromHeader(tag.Path);
    var sb    = new System.Text.StringBuilder();
    sb.AppendLine("[bold cyan]Segment File[/]");
    sb.AppendLine();
    sb.AppendLine($"  {Markup.Escape(Path.GetFileName(tag.Path))}");
    sb.AppendLine($"  Level:   {(level >= 0 ? level.ToString() : "?")}");
    sb.AppendLine($"  Entries: {count:N0}");
    sb.AppendLine($"  Size:    {FormatBytes(fi.Length)}");
    sb.AppendLine($"  Bloom:   {(File.Exists(tag.BloomPath) ? "[green]present[/]" : "[grey]missing[/]")}");
    sb.AppendLine();
    sb.AppendLine("[grey]Actions (press A):[/]");
    sb.AppendLine("  Inspect entries");
    sb.AppendLine("  Rebuild Bloom Filter for this segment");
    return sb.ToString().TrimEnd();
}

static string BuildBloomFileRightPanel(BloomFileTag tag)
{
    var fi = new FileInfo(tag.Path);
    var sb = new System.Text.StringBuilder();
    sb.AppendLine("[bold cyan]Bloom Filter[/]");
    sb.AppendLine();
    sb.AppendLine($"  {Markup.Escape(Path.GetFileName(tag.Path))}");
    sb.AppendLine($"  Size: {FormatBytes(fi.Length)}");
    sb.AppendLine();
    sb.AppendLine("[grey]Actions (press A):[/]");
    sb.AppendLine("  Inspect bloom filter");
    sb.AppendLine("  Rebuild Bloom Filter from segment");
    return sb.ToString().TrimEnd();
}

static string BuildLogFileRightPanel(LogFileTag tag)
{
    var fi    = new FileInfo(tag.Path);
    var count = CountLogEntries(tag.Path);
    var sb    = new System.Text.StringBuilder();
    sb.AppendLine("[bold cyan]Append Log[/]");
    sb.AppendLine();
    sb.AppendLine($"  {Markup.Escape(Path.GetFileName(tag.Path))}");
    sb.AppendLine($"  Entries: {count:N0}");
    sb.AppendLine($"  Size:    {FormatBytes(fi.Length)}");
    sb.AppendLine();
    sb.AppendLine("[grey]Actions (press A):[/]");
    sb.AppendLine("  Dump log entries");
    return sb.ToString().TrimEnd();
}

// ============================================================
// Action menu
// ============================================================

static async Task RunActionMenuAsync(
    string storeRoot,
    ExplorerItem item,
    Dictionary<string, long> chunkCounts,
    Dictionary<string, long> fileDefCounts,
    Dictionary<string, long> releaseCounts)
{
    switch (item.Tag)
    {
        case StoreTag:
            await ShowStoreOverviewAsync(storeRoot);
            break;

        case CategoryTag cat:
            await RunCategoryActionMenuAsync(storeRoot, cat);
            break;

        case PrefixGroup1Tag or PrefixGroup2Tag:
            AnsiConsole.Clear();
            AnsiConsole.MarkupLine("[grey]No actions available at this level. Drill into a bucket.[/]");
            Pause();
            break;

        case BucketTag bucket:
            await RunBucketActionMenuAsync(storeRoot, bucket);
            break;

        case PackFileTag pack:
            await InspectPackFileAsync(pack.Path);
            break;

        case SegFileTag seg:
            await RunSegFileActionMenuAsync(seg);
            break;

        case BloomFileTag bloom:
            await RunBloomFileActionMenuAsync(bloom);
            break;

        case LogFileTag log:
            DumpLogEntries(log.Path);
            break;

        case ReleasesTag:
            AnsiConsole.Clear();
            AnsiConsole.MarkupLine("[grey]Drill into the Releases node to browse .rdef buckets.[/]");
            Pause();
            break;

        case RdefBucketTag:
            AnsiConsole.Clear();
            AnsiConsole.MarkupLine("[grey]Drill into a bucket to browse .rdef files.[/]");
            Pause();
            break;

        case RdefFolderTag:
            AnsiConsole.Clear();
            AnsiConsole.MarkupLine("[grey]Press Enter to browse this folder.[/]");
            Pause();
            break;

        case RdefFileTag rdf:
            AnsiConsole.Clear();
            AnsiConsole.MarkupLine("[grey]Press Enter to browse artifacts inside this .rdef file.[/]");
            Pause();
            break;

        case RdefArtifactTag rat:
        {
            var a = rat.Package.OutputArtifacts[rat.ArtifactIndex];
            AnsiConsole.Clear();
            RenderHeader(storeRoot);
            AnsiConsole.MarkupLine($"[bold cyan]Artifact[/]  #{rat.ArtifactIndex}");
            AnsiConsole.MarkupLine($"  Path:      {Markup.Escape(a.Path)}");
            AnsiConsole.MarkupLine($"  Component: {Markup.Escape(a.ComponentName)}");
            AnsiConsole.MarkupLine($"  Kind:      {a.Kind}");
            switch (a.Backing)
            {
                case OpaqueBlobBacking ob:
                    AnsiConsole.MarkupLine($"  Backing:   Blob");
                    if (ob.StorageKey.HasValue)
                        AnsiConsole.MarkupLine($"  StorageKey:{Markup.Escape(ob.StorageKey.Value.ToHexString())} [grey](V5)[/]");
                    else if (ob.ContentHash.HasValue)
                        AnsiConsole.MarkupLine($"  Hash:      {Markup.Escape(ob.ContentHash.Value.ToHexString())}");
                    if (ob.Length.HasValue)
                        AnsiConsole.MarkupLine($"  Size:      {FormatBytes(ob.Length.Value)}");
                    break;
                case ReconstructedContainerBacking rc:
                    AnsiConsole.MarkupLine($"  Backing:   Container  [{Markup.Escape(rc.FormatId)}]  {rc.Members.Count} members");
                    AnsiConsole.MarkupLine("[grey]Press Enter on this item in the list to browse members.[/]");
                    break;
            }

            // Offer Verify FileDef action if a content hash or storage key is available
            if (a.Backing is OpaqueBlobBacking ob2 && (ob2.StorageKey.HasValue || ob2.ContentHash.HasValue))
            {
                AnsiConsole.WriteLine();
                var choice = AnsiConsole.Prompt(
                    new SelectionPrompt<string>()
                        .Title("Action:")
                        .AddChoices("Verify FileDef (check FileDef + chunks + content hash)", "<= Back"));
                if (choice == "Verify FileDef (check FileDef + chunks + content hash)")
                    await VerifyFileDefAsync(storeRoot, ob2.ContentHash, ob2.StorageKey);
            }
            else
            {
                Pause();
            }
            break;
        }

        case RdefContainerMemberTag rmt:
        {
            var m = rmt.Member;
            AnsiConsole.Clear();
            RenderHeader(storeRoot);
            AnsiConsole.MarkupLine($"[bold cyan]Container Member[/]  #{rmt.MemberIndex}");
            AnsiConsole.MarkupLine($"  Entry path: {Markup.Escape(m.EntryPath)}");
            if (m.Length.HasValue)
                AnsiConsole.MarkupLine($"  Size:       {FormatBytes(m.Length.Value)}");
            if (m.StorageKey.HasValue)
                AnsiConsole.MarkupLine($"  StorageKey: {Markup.Escape(m.StorageKey.Value.ToHexString())} [grey](V5)[/]");
            else if (m.ContentHash.HasValue)
                AnsiConsole.MarkupLine($"  Hash:       {Markup.Escape(m.ContentHash.Value.ToHexString())}");

            // Offer Verify FileDef action if a content hash or storage key is available
            if (m.StorageKey.HasValue || m.ContentHash.HasValue)
            {
                AnsiConsole.WriteLine();
                var choice = AnsiConsole.Prompt(
                    new SelectionPrompt<string>()
                        .Title("Action:")
                        .AddChoices("Verify FileDef (check FileDef + chunks + content hash)", "<= Back"));
                if (choice == "Verify FileDef (check FileDef + chunks + content hash)")
                    await VerifyFileDefAsync(storeRoot, m.ContentHash, m.StorageKey);
            }
            else
            {
                Pause();
            }
            break;
        }
    }
}

static async Task RunCategoryActionMenuAsync(string storeRoot, CategoryTag cat)
{
    AnsiConsole.Clear();
    var choice = AnsiConsole.Prompt(
        new SelectionPrompt<string>()
            .Title($"[bold]{cat.CatName}[/] — actions:")
            .AddChoices(
                "Hash Lookup",
                "Integrity Check (ALL buckets)",
                "Verify Pack Offsets (ALL buckets)",
                "<= Back"));

    switch (choice)
    {
        case "Hash Lookup":
            await HashLookupAsync(storeRoot);
            break;
        case "Integrity Check (ALL buckets)":
            await IntegrityCheckAllAsync(storeRoot, cat.CatDir, cat.CatPrefix);
            break;
        case "Verify Pack Offsets (ALL buckets)":
            await VerifyPackOffsetsAllAsync(storeRoot, cat.CatDir, cat.CatPrefix);
            break;
    }
}

static async Task RunBucketActionMenuAsync(string storeRoot, BucketTag bucket)
{
    var bucketDir = Path.Combine(bucket.CatDir, bucket.Prefix[..2]);
    var logPath   = Path.Combine(bucketDir, $"{bucket.CatPrefix}{bucket.Prefix}.log");
    var segs      = Directory.Exists(bucketDir)
        ? Directory.EnumerateFiles(bucketDir, $"{bucket.CatPrefix}{bucket.Prefix}.seg-*.idx").OrderBy(f => f).ToList()
        : new List<string>();

    var choices = new List<string>
    {
        "Integrity Check (this bucket)",
        "Verify Pack Offsets (this bucket)",
        "Rebuild Segment (from pack files)",
        "Rebuild Bloom Filters (all segments in bucket)",
    };
    if (File.Exists(logPath))  choices.Add("Dump Log");
    if (segs.Count > 0)        choices.Add("Inspect a Segment File");
    choices.Add("Hash Lookup");
    choices.Add("Decode FileDef Record");
    choices.Add("<= Back");

    AnsiConsole.Clear();
    var choice = AnsiConsole.Prompt(
        new SelectionPrompt<string>()
            .Title($"[bold]{bucket.CatName}/{bucket.Prefix}[/] — actions:")
            .AddChoices(choices));

    switch (choice)
    {
        case "Integrity Check (this bucket)":
            await RunIntegrityCheckAsync(storeRoot, bucket.CatDir, bucket.CatPrefix, bucket.Prefix);
            break;
        case "Verify Pack Offsets (this bucket)":
            await RunVerifyPackOffsetsAsync(storeRoot, bucket.CatDir, bucket.CatPrefix, bucket.Prefix);
            break;
        case "Rebuild Segment (from pack files)":
            await RebuildSegmentFromPacksAsync(storeRoot, bucketDir, bucket.CatPrefix, bucket.Prefix);
            break;
        case "Rebuild Bloom Filters (all segments in bucket)":
            await RebuildAllBloomFiltersInBucketAsync(bucketDir, bucket.CatPrefix, bucket.Prefix);
            break;
        case "Dump Log":
            DumpLogEntries(logPath);
            break;
        case "Inspect a Segment File":
        {
            var sf = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("Choose segment file:")
                    .AddChoices(segs.Select(Path.GetFileName).Where(n => n is not null).Cast<string>()));
            await InspectSegmentFileAsync(Path.Combine(bucketDir, sf));
            break;
        }
        case "Hash Lookup":
            await HashLookupAsync(storeRoot);
            break;
        case "Decode FileDef Record":
            await DecodeFileDefInteractiveAsync(storeRoot);
            break;
    }
}

static async Task RunSegFileActionMenuAsync(SegFileTag seg)
{
    AnsiConsole.Clear();
    var choices = new List<string>
    {
        "Inspect entries",
        "Rebuild Bloom Filter for this segment",
        "<= Back"
    };

    var choice = AnsiConsole.Prompt(
        new SelectionPrompt<string>()
            .Title($"[bold]{Markup.Escape(Path.GetFileName(seg.Path))}[/] — actions:")
            .AddChoices(choices));

    switch (choice)
    {
        case "Inspect entries":
            await InspectSegmentFileAsync(seg.Path);
            break;
        case "Rebuild Bloom Filter for this segment":
            await RebuildBloomFilterForSegmentAsync(seg.Path, seg.BloomPath);
            break;
    }
}

static async Task RunBloomFileActionMenuAsync(BloomFileTag bloom)
{
    AnsiConsole.Clear();
    var choices = new List<string>
    {
        "Inspect bloom filter",
        "Rebuild Bloom Filter from segment",
        "<= Back"
    };

    var choice = AnsiConsole.Prompt(
        new SelectionPrompt<string>()
            .Title($"[bold]{Markup.Escape(Path.GetFileName(bloom.Path))}[/] — actions:")
            .AddChoices(choices));

    switch (choice)
    {
        case "Inspect bloom filter":
            InspectBloomFilter(bloom.Path);
            break;
        case "Rebuild Bloom Filter from segment":
            await RebuildBloomFilterForSegmentAsync(bloom.SegPath, bloom.Path);
            break;
    }
}

// ============================================================
// 1. Store Overview
// ============================================================

static async Task ShowStoreOverviewAsync(string storeRoot)
{
    AnsiConsole.Clear();
    RenderHeader(storeRoot);

    long chunkPackBytes   = 0, fileDefPackBytes = 0, releaseBytes = 0, indexBytes = 0;
    int  chunkPackFiles   = 0, fileDefPackFiles  = 0, releaseFiles = 0, indexFiles = 0;
    long totalChunks = 0, totalFileDefs = 0;

    await AnsiConsole.Status()
        .Spinner(Spinner.Known.Dots)
        .StartAsync("Scanning store...", async ctx =>
        {
            ctx.Status("Counting files and bytes...");
            await Task.Run(() =>
            {
                foreach (var filePath in Directory.EnumerateFiles(storeRoot, "*", SearchOption.AllDirectories))
                {
                    var fi   = new FileInfo(filePath);
                    var name = fi.Name;
                    var norm = filePath.Replace('\\', '/');

                    if (name.EndsWith(".idx", StringComparison.OrdinalIgnoreCase))
                    { indexBytes += fi.Length; indexFiles++; continue; }
                    if (name.EndsWith(".rdef", StringComparison.OrdinalIgnoreCase))
                    { releaseBytes += fi.Length; releaseFiles++; continue; }
                    if (name.EndsWith(".pack", StringComparison.OrdinalIgnoreCase))
                    {
                        if (norm.Contains("/Chunks/", StringComparison.OrdinalIgnoreCase))
                        { chunkPackBytes += fi.Length; chunkPackFiles++; }
                        else if (norm.Contains("/FileDefs/", StringComparison.OrdinalIgnoreCase))
                        { fileDefPackBytes += fi.Length; fileDefPackFiles++; }
                    }
                }
            });

            ctx.Status("Counting index entries (segment headers)...");
            await Task.Run(() =>
            {
                for (var i = 0; i < 4096; i++)
                {
                    var prefix    = i.ToString("x3");
                    var bucketDir = Path.Combine(storeRoot, "Chunks", prefix[..2]);
                    if (!Directory.Exists(bucketDir)) continue;

                    foreach (var seg in Directory.EnumerateFiles(bucketDir, $"chunks{prefix}.seg-*.idx"))
                        totalChunks += SortedIndexSegment.ReadEntryCountFromHeader(seg);

                    var log = Path.Combine(bucketDir, $"chunks{prefix}.log");
                    totalChunks += CountLogEntries(log);
                }

                for (var i = 0; i < 4096; i++)
                {
                    var prefix    = i.ToString("x3");
                    var bucketDir = Path.Combine(storeRoot, "FileDefs", prefix[..2]);
                    if (!Directory.Exists(bucketDir)) continue;

                    foreach (var seg in Directory.EnumerateFiles(bucketDir, $"fileDefs{prefix}.seg-*.idx"))
                        totalFileDefs += SortedIndexSegment.ReadEntryCountFromHeader(seg);

                    var log = Path.Combine(bucketDir, $"fileDefs{prefix}.log");
                    totalFileDefs += CountLogEntries(log);
                }
            });
        });

    // Drive info
    var root = Path.GetPathRoot(Path.GetFullPath(storeRoot));
    long volumeTotal = 0, volumeFree = 0;
    if (!string.IsNullOrWhiteSpace(root))
    {
        try
        {
            var di = new DriveInfo(root);
            if (di.IsReady) { volumeTotal = di.TotalSize; volumeFree = di.AvailableFreeSpace; }
        }
        catch { /* ignore */ }
    }

    var table = new Table()
        .Border(TableBorder.Rounded)
        .AddColumn("[grey]Category[/]")
        .AddColumn(new TableColumn("[grey]Value[/]").RightAligned());

    table.AddRow("[bold cyan]Chunks[/]", "");
    table.AddRow("  Index entries", $"{totalChunks:N0}");
    table.AddRow("  Pack files",    $"{chunkPackFiles:N0}");
    table.AddRow("  Pack size",     FormatBytes(chunkPackBytes));

    table.AddRow("[bold cyan]FileDefs[/]", "");
    table.AddRow("  Index entries", $"{totalFileDefs:N0}");
    table.AddRow("  Pack files",    $"{fileDefPackFiles:N0}");
    table.AddRow("  Pack size",     FormatBytes(fileDefPackBytes));

    table.AddRow("[bold cyan]Releases[/]", "");
    table.AddRow("  .rdef files",   $"{releaseFiles:N0}");
    table.AddRow("  Total size",    FormatBytes(releaseBytes));

    table.AddRow("[bold cyan]Index files[/]", "");
    table.AddRow("  Count",         $"{indexFiles:N0}");
    table.AddRow("  Size",          FormatBytes(indexBytes));

    table.AddRow("[bold cyan]Total physical[/]", $"[bold]{FormatBytes(chunkPackBytes + fileDefPackBytes + releaseBytes + indexBytes)}[/]");

    if (volumeTotal > 0)
    {
        table.AddRow("[bold cyan]Volume[/]", "");
        table.AddRow("  Total",  FormatBytes(volumeTotal));
        table.AddRow("  Free",   FormatBytes(volumeFree));
        table.AddRow("  Used %", $"{(volumeTotal - volumeFree) * 100.0 / volumeTotal:F1} %");
    }

    AnsiConsole.Write(table);
    Pause();
}

// ============================================================
// Release level builders
// ============================================================

static ExplorerLevel BuildReleaseBucketListLevel(
    ReleasesTag tag,
    Dictionary<string, long> releaseCounts)
{
    var items = releaseCounts
        .OrderBy(kv => kv.Key)
        .Select(kv =>
        {
            var bucketDir = Path.Combine(tag.ReleasesDir, kv.Key);
            return new ExplorerItem(
                kv.Key,
                $"{kv.Value:N0} file{(kv.Value == 1 ? "" : "s")}",
                new RdefBucketTag(kv.Key, bucketDir));
        })
        .ToList();

    return new ExplorerLevel
    {
        Title = "[bold blue]BinStash ChunkStore Explorer[/]  [grey]/ Releases[/]",
        Items = items
    };
}

static ExplorerLevel? BuildReleaseBucketLevel(RdefBucketTag bucket)
{
    if (!Directory.Exists(bucket.BucketDir)) return null;

    var items = Directory.EnumerateFiles(bucket.BucketDir, "*.rdef")
        .OrderBy(f => f)
        .Select(f =>
        {
            var fi = new FileInfo(f);
            return new ExplorerItem(
                Path.GetFileName(f),
                FormatBytes(fi.Length),
                new RdefFileTag(f, bucket.Prefix));
        })
        .ToList();

    if (items.Count == 0) return null;

    return new ExplorerLevel
    {
        Title = $"[bold blue]BinStash ChunkStore Explorer[/]  [grey]/ Releases / {bucket.Prefix}[/]",
        Items = items
    };
}

// ============================================================
// Release artifact level builders
// ============================================================

static async Task<ExplorerLevel?> BuildRdefArtifactListLevelAsync(RdefFileTag tag)
{
    byte[] data;
    try { data = await File.ReadAllBytesAsync(tag.Path); }
    catch { return null; }

    ReleasePackage pkg;
    try { pkg = await ReleasePackageSerializer.DeserializeAsync(data); }
    catch { return null; }

    if (pkg.OutputArtifacts.Count == 0)
        return null;

    var nameNoExt = Path.GetFileNameWithoutExtension(tag.Path);
    var title = $"[bold blue]BinStash ChunkStore Explorer[/]  [grey]/ Releases / {tag.Prefix} / {Markup.Escape(nameNoExt[..Math.Min(16, nameNoExt.Length)])}...[/]";

    // Build root folder level (FolderPath == "")
    var rootFolderTag = new RdefFolderTag(tag.Path, tag.Prefix, pkg, "", pkg.OutputArtifacts.ToList());
    var items = BuildFolderItems(rootFolderTag);

    return new ExplorerLevel { Title = title, Items = items };
}

static ExplorerLevel? BuildRdefFolderLevel(RdefFolderTag folder)
{
    var items = BuildFolderItems(folder);
    if (items.Count == 0) return null;

    var displayPath = string.IsNullOrEmpty(folder.FolderPath) ? "(root)" : folder.FolderPath;
    var nameNoExt   = Path.GetFileNameWithoutExtension(folder.RdefPath);
    var title = $"[bold blue]BinStash ChunkStore Explorer[/]  [grey]/ Releases / {folder.RdefPrefix} / {Markup.Escape(nameNoExt[..Math.Min(16, nameNoExt.Length)])}... / {Markup.Escape(displayPath)}[/]";

    return new ExplorerLevel { Title = title, Items = items };
}

// Builds the child items for a given folder prefix within the rdef artifact tree.
// Items are: virtual subdirectories (one level deep) and leaf artifacts at this depth.
static List<ExplorerItem> BuildFolderItems(RdefFolderTag folder)
{
    var allArtifacts   = folder.Package.OutputArtifacts;
    var prefixLen      = folder.FolderPath.Length;
    var subFolders     = new Dictionary<string, List<OutputArtifact>>(StringComparer.Ordinal);
    var leafArtifacts  = new List<(int Index, OutputArtifact Artifact)>();

    for (var i = 0; i < allArtifacts.Count; i++)
    {
        var a    = allArtifacts[i];
        var path = a.Path;

        // Normalize separators to forward slash
        var normalized = path.Replace('\\', '/');

        if (!normalized.StartsWith(folder.FolderPath, StringComparison.Ordinal))
            continue;

        var remainder = normalized[prefixLen..]; // path relative to current folder

        // Find the next separator in the remainder
        var sep = remainder.IndexOf('/');
        if (sep >= 0)
        {
            // There is a subdirectory component — group by that directory name
            var dirName = remainder[..(sep + 1)]; // includes trailing "/"
            var fullDir = folder.FolderPath + dirName;
            if (!subFolders.TryGetValue(fullDir, out var list))
            {
                list = new List<OutputArtifact>();
                subFolders[fullDir] = list;
            }
            list.Add(a);
        }
        else
        {
            // Leaf — no more path separator
            leafArtifacts.Add((i, a));
        }
    }

    var items = new List<ExplorerItem>();

    // Subdirectories first, sorted
    foreach (var (fullDir, children) in subFolders.OrderBy(kv => kv.Key, StringComparer.Ordinal))
    {
        var dirName = fullDir[prefixLen..]; // relative dir segment with trailing "/"
        items.Add(new ExplorerItem(
            $"[dir] {dirName}",
            $"{children.Count} item{(children.Count == 1 ? "" : "s")}",
            new RdefFolderTag(folder.RdefPath, folder.RdefPrefix, folder.Package, fullDir, folder.Artifacts)));
    }

    // Then leaf artifacts, sorted by remainder path
    foreach (var (idx, a) in leafArtifacts.OrderBy(t => t.Artifact.Path, StringComparer.Ordinal))
    {
        var normalized = a.Path.Replace('\\', '/');
        var remainder  = normalized[prefixLen..];
        var sizeStr = a.Backing switch
        {
            OpaqueBlobBacking ob             => ob.Length.HasValue ? FormatBytes(ob.Length.Value) : "blob",
            ReconstructedContainerBacking rc => $"{rc.Members.Count} member{(rc.Members.Count == 1 ? "" : "s")}",
            _                                => ""
        };
        var kindIcon = a.Backing is ReconstructedContainerBacking ? "[+]" : "   ";
        items.Add(new ExplorerItem(
            $"{kindIcon} {remainder}",
            sizeStr,
            new RdefArtifactTag(folder.RdefPath, folder.Package, idx)));
    }

    return items;
}

static ExplorerLevel? BuildRdefContainerMemberLevel(RdefArtifactTag tag)
{
    var artifact = tag.Package.OutputArtifacts[tag.ArtifactIndex];
    if (artifact.Backing is not ReconstructedContainerBacking rc || rc.Members.Count == 0)
        return null;

    var items = rc.Members
        .Select((m, i) =>
        {
            var sizeStr = m.Length.HasValue ? FormatBytes(m.Length.Value) : "";
            return new ExplorerItem(
                m.EntryPath,
                sizeStr,
                new RdefContainerMemberTag(artifact.Path, m, i));
        })
        .ToList();

    return new ExplorerLevel
    {
        Title = $"[bold blue]BinStash ChunkStore Explorer[/]  [grey]/ Releases / ... / {Markup.Escape(artifact.Path)}[/]",
        Items = items
    };
}

// ============================================================
// Release right-panel builders
// ============================================================

static string BuildReleasesRightPanel(ReleasesTag tag, Dictionary<string, long> releaseCounts)
{
    var total = releaseCounts.Values.Sum();
    var sb    = new System.Text.StringBuilder();
    sb.AppendLine("[bold cyan]Releases[/]");
    sb.AppendLine();
    sb.AppendLine($"  Buckets:     {releaseCounts.Count:N0}");
    sb.AppendLine($"  Total files: {total:N0}");
    sb.AppendLine();

    var top5 = releaseCounts.OrderByDescending(kv => kv.Value).Take(5).ToList();
    if (top5.Count > 0)
    {
        sb.AppendLine("[grey]Top buckets (by file count):[/]");
        foreach (var (k, v) in top5)
            sb.AppendLine($"  {k}  {v:N0}");
    }

    return sb.ToString().TrimEnd();
}

static string BuildRdefBucketRightPanel(RdefBucketTag tag)
{
    var sb = new System.Text.StringBuilder();
    sb.AppendLine($"[bold cyan]Releases / {tag.Prefix}[/]");
    sb.AppendLine();

    if (!Directory.Exists(tag.BucketDir))
    {
        sb.AppendLine("[grey](bucket directory missing)[/]");
        return sb.ToString().TrimEnd();
    }

    var files = Directory.EnumerateFiles(tag.BucketDir, "*.rdef").ToList();
    var totalBytes = files.Sum(f => new FileInfo(f).Length);
    sb.AppendLine($"  Files: {files.Count:N0}");
    sb.AppendLine($"  Size:  {FormatBytes(totalBytes)}");
    return sb.ToString().TrimEnd();
}

static string BuildRdefFileRightPanel(RdefFileTag tag)
{
    var sb = new System.Text.StringBuilder();
    sb.AppendLine("[bold cyan].rdef File[/]");
    sb.AppendLine();
    sb.AppendLine($"  {Markup.Escape(Path.GetFileName(tag.Path))}");

    if (File.Exists(tag.Path))
    {
        var fi = new FileInfo(tag.Path);
        sb.AppendLine($"  Size:   {FormatBytes(fi.Length)}");
        // The filename (without extension) is the full 64-hex BLAKE3 hash
        var nameNoExt = Path.GetFileNameWithoutExtension(tag.Path);
        sb.AppendLine($"  Hash:   {Markup.Escape(nameNoExt[..Math.Min(16, nameNoExt.Length)])}...");
    }

    sb.AppendLine();
    sb.AppendLine("[grey]Press Enter to browse artifacts[/]");
    return sb.ToString().TrimEnd();
}

static string BuildRdefArtifactRightPanel(RdefArtifactTag tag)
{
    var a  = tag.Package.OutputArtifacts[tag.ArtifactIndex];
    var sb = new System.Text.StringBuilder();
    sb.AppendLine("[bold cyan]Artifact[/]");
    sb.AppendLine();
    sb.AppendLine($"  Path:      {Markup.Escape(a.Path)}");
    sb.AppendLine($"  Component: {Markup.Escape(a.ComponentName)}");
    sb.AppendLine($"  Kind:      {a.Kind}");
    sb.AppendLine();

    switch (a.Backing)
    {
        case OpaqueBlobBacking ob:
            sb.AppendLine("  Backing:   [cyan]Blob[/]");
            if (ob.StorageKey.HasValue)
            {
                sb.AppendLine($"  StorageKey:{Markup.Escape(ob.StorageKey.Value.ToHexString()[..16])}... [grey](V5)[/]");
                sb.AppendLine();
                sb.AppendLine("[grey]Press A → Verify FileDef[/]");
            }
            else if (ob.ContentHash.HasValue)
            {
                sb.AppendLine($"  Hash:      {Markup.Escape(ob.ContentHash.Value.ToHexString()[..16])}...");
                sb.AppendLine();
                sb.AppendLine("[grey]Press A → Verify FileDef[/]");
            }
            if (ob.Length.HasValue)
                sb.AppendLine($"  Size:      {FormatBytes(ob.Length.Value)}");
            break;

        case ReconstructedContainerBacking rc:
            sb.AppendLine($"  Backing:   [cyan]Container[/]  ({Markup.Escape(rc.FormatId)})");
            sb.AppendLine($"  Reconstruct: {rc.ReconstructionKind}");
            sb.AppendLine($"  Members:   {rc.Members.Count:N0}");
            var containerBytes = rc.Members.Where(m => m.Length.HasValue).Sum(m => m.Length!.Value);
            if (containerBytes > 0)
                sb.AppendLine($"  Total size:{FormatBytes(containerBytes)}");
            sb.AppendLine();
            sb.AppendLine("[grey]Press Enter to browse members[/]");
            break;

        default:
            sb.AppendLine("  Backing:   [grey]unknown[/]");
            break;
    }

    return sb.ToString().TrimEnd();
}

static string BuildRdefContainerMemberRightPanel(RdefContainerMemberTag tag)
{
    var m  = tag.Member;
    var sb = new System.Text.StringBuilder();
    sb.AppendLine("[bold cyan]Container Member[/]");
    sb.AppendLine();
    sb.AppendLine($"  Entry path: {Markup.Escape(m.EntryPath)}");
    if (m.Length.HasValue)
        sb.AppendLine($"  Size:       {FormatBytes(m.Length.Value)}");
    if (m.StorageKey.HasValue)
    {
        var hex = m.StorageKey.Value.ToHexString();
        sb.AppendLine($"  StorageKey: {Markup.Escape(hex[..16])}... [grey](V5)[/]");
        sb.AppendLine($"  Full key:   {Markup.Escape(hex)}");
        sb.AppendLine();
        sb.AppendLine("[grey]Press A → Verify FileDef[/]");
    }
    else if (m.ContentHash.HasValue)
    {
        var hex = m.ContentHash.Value.ToHexString();
        sb.AppendLine($"  Hash:       {Markup.Escape(hex[..16])}...");
        sb.AppendLine($"  Full hash:  {Markup.Escape(hex)}");
        sb.AppendLine();
        sb.AppendLine("[grey]Press A → Verify FileDef[/]");
    }
    else
    {
        sb.AppendLine("  Hash:       [grey](none)[/]");
    }
    return sb.ToString().TrimEnd();
}

static string BuildRdefFolderRightPanel(RdefFolderTag folder)
{
    var sb = new System.Text.StringBuilder();
    var displayPath = string.IsNullOrEmpty(folder.FolderPath) ? "(root)" : folder.FolderPath;
    sb.AppendLine($"[bold cyan]Folder[/]  {Markup.Escape(displayPath)}");
    sb.AppendLine();

    // Count direct children
    var prefixLen = folder.FolderPath.Length;
    var subFolderSet = new HashSet<string>(StringComparer.Ordinal);
    var leafCount    = 0;

    foreach (var a in folder.Package.OutputArtifacts)
    {
        var normalized = a.Path.Replace('\\', '/');
        if (!normalized.StartsWith(folder.FolderPath, StringComparison.Ordinal)) continue;
        var remainder = normalized[prefixLen..];
        var sep = remainder.IndexOf('/');
        if (sep >= 0)
            subFolderSet.Add(remainder[..(sep + 1)]);
        else
            leafCount++;
    }

    sb.AppendLine($"  Subfolders: {subFolderSet.Count:N0}");
    sb.AppendLine($"  Files:      {leafCount:N0}");
    sb.AppendLine($"  Total in subtree: {folder.Package.OutputArtifacts.Count(a => a.Path.Replace('\\', '/').StartsWith(folder.FolderPath, StringComparison.Ordinal)):N0}");
    sb.AppendLine();
    sb.AppendLine("[grey]Press Enter to browse[/]");
    return sb.ToString().TrimEnd();
}

// ============================================================
// ScanBucketCounts
// ============================================================

static Dictionary<string, long> ScanBucketCounts(string catDir, string catPrefix)
{
    var result = new Dictionary<string, long>(4096);
    for (var i = 0; i < 4096; i++)
    {
        var prefix    = i.ToString("x3");
        var bucketDir = Path.Combine(catDir, prefix[..2]);
        if (!Directory.Exists(bucketDir)) continue;
        if (!Directory.EnumerateFiles(bucketDir, $"{catPrefix}{prefix}*").Any()) continue;

        long count = 0;
        foreach (var seg in Directory.EnumerateFiles(bucketDir, $"{catPrefix}{prefix}.seg-*.idx"))
            count += SortedIndexSegment.ReadEntryCountFromHeader(seg);

        var logPath = Path.Combine(bucketDir, $"{catPrefix}{prefix}.log");
        count += CountLogEntries(logPath);

        result[prefix] = count;
    }
    return result;
}

// ============================================================
// ScanReleaseCounts — counts .rdef files per 3-hex bucket
// ============================================================

static Dictionary<string, long> ScanReleaseCounts(string releasesDir)
{
    var result = new Dictionary<string, long>();
    if (!Directory.Exists(releasesDir)) return result;

    foreach (var bucketDir in Directory.EnumerateDirectories(releasesDir))
    {
        var prefix = Path.GetFileName(bucketDir);
        if (prefix is null || prefix.Length != 3) continue;
        var count = Directory.EnumerateFiles(bucketDir, "*.rdef").LongCount();
        if (count > 0) result[prefix] = count;
    }
    return result;
}

// ============================================================
// Inspect Pack File
// ============================================================

static async Task InspectPackFileAsync(string packPath)
{
    AnsiConsole.Clear();
    AnsiConsole.MarkupLine($"[bold]Pack file:[/] {Markup.Escape(packPath)}");
    AnsiConsole.WriteLine();

    const int MaxShow = 200;

    var entries = new List<(long Offset, int HeaderPlusCompressed, int UncompressedLen, ulong Checksum, ulong ActualChecksum, bool Valid)>();

    await AnsiConsole.Progress()
        .AutoRefresh(true)
        .Columns(new TaskDescriptionColumn(), new ProgressBarColumn(), new PercentageColumn(), new SpinnerColumn())
        .StartAsync(async ctx =>
        {
            var task = ctx.AddTask("Reading pack entries...", maxValue: new FileInfo(packPath).Length);

            await using var fs = new FileStream(
                packPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
                bufferSize: 128 * 1024, options: FileOptions.Asynchronous | FileOptions.SequentialScan);

            const int  HeaderSize = 21;
            const uint Magic      = 0x4B505342;

            while (fs.Position < fs.Length)
            {
                var entryStart = fs.Position;
                var header     = new byte[HeaderSize];
                var read       = await fs.ReadAsync(header.AsMemory(0, HeaderSize));
                if (read < HeaderSize) break;

                var magic           = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(0, 4));
                var uncompressedLen = (int)BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(5, 4));
                var compressedLen   = (int)BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(9, 4));
                var expectedChksum  = BinaryPrimitives.ReadUInt64LittleEndian(header.AsSpan(13, 8));

                if (magic != Magic)
                {
                    AnsiConsole.MarkupLine($"[red]Bad magic at offset {entryStart}: 0x{magic:X8}[/]");
                    break;
                }

                var compressed = new byte[compressedLen];
                var totalRead  = 0;
                while (totalRead < compressedLen)
                {
                    var r = await fs.ReadAsync(compressed.AsMemory(totalRead, compressedLen - totalRead));
                    if (r == 0) break;
                    totalRead += r;
                }

                var actualChksum = XxHash3.HashToUInt64(compressed);
                var valid        = actualChksum == expectedChksum && magic == Magic;

                entries.Add((entryStart, HeaderSize + compressedLen, uncompressedLen, expectedChksum, actualChksum, valid));
                task.Value = fs.Position;
            }
        });

    var goodCount = entries.Count(e => e.Valid);
    var badCount  = entries.Count - goodCount;

    AnsiConsole.MarkupLine($"Total entries: [bold]{entries.Count:N0}[/]  " +
                           $"[green]OK: {goodCount:N0}[/]  " +
                           $"[red]Corrupt: {badCount:N0}[/]");
    AnsiConsole.WriteLine();

    var table = new Table()
        .Border(TableBorder.Simple)
        .AddColumn(new TableColumn("#").RightAligned())
        .AddColumn(new TableColumn("Offset").RightAligned())
        .AddColumn(new TableColumn("Total bytes").RightAligned())
        .AddColumn(new TableColumn("Uncompressed").RightAligned())
        .AddColumn("Checksum OK");

    var showCount = Math.Min(MaxShow, entries.Count);
    for (var i = 0; i < showCount; i++)
    {
        var e = entries[i];
        table.AddRow(
            $"{i}",
            $"0x{e.Offset:X}",
            $"{e.HeaderPlusCompressed:N0}",
            FormatBytes(e.UncompressedLen),
            e.Valid ? "[green]✓[/]" : "[red]✗[/]");
    }

    AnsiConsole.Write(table);
    if (entries.Count > MaxShow)
        AnsiConsole.MarkupLine($"[grey]... {entries.Count - MaxShow} more entries not shown[/]");

    Pause();
}

// ============================================================
// Inspect Segment File
// ============================================================

static async Task InspectSegmentFileAsync(string segPath)
{
    AnsiConsole.Clear();
    AnsiConsole.MarkupLine($"[bold]Segment file:[/] {Markup.Escape(segPath)}");
    AnsiConsole.WriteLine();

    const int MaxShow = 100;

    byte[] data;
    try
    {
        await using var fs = new FileStream(
            segPath, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 64 * 1024, options: FileOptions.Asynchronous | FileOptions.SequentialScan);
        data = new byte[fs.Length];
        await fs.ReadExactlyAsync(data);
    }
    catch (Exception ex)
    {
        AnsiConsole.MarkupLine($"[red]Failed to read segment: {Markup.Escape(ex.Message)}[/]");
        Pause();
        return;
    }

    const uint ExpectedMagic = 0x58324449;
    const int  HeaderSize    = 8;
    const int  EntrySize     = 48;

    if (data.Length < HeaderSize)
    {
        AnsiConsole.MarkupLine("[red]File too short to contain a valid header.[/]");
        Pause();
        return;
    }

    var magic      = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(0, 4));
    var entryCount = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(4, 4));
    var level      = SortedIndexSegment.GetLevel(Path.GetFileName(segPath));

    AnsiConsole.MarkupLine($"Magic:       0x{magic:X8} {(magic == ExpectedMagic ? "[green](OK)[/]" : "[red](BAD)[/]")}");
    AnsiConsole.MarkupLine($"Entry count: [bold]{entryCount:N0}[/]");
    AnsiConsole.MarkupLine($"Level:       {(level >= 0 ? level.ToString() : "[grey]unknown[/]")}");
    AnsiConsole.MarkupLine($"File size:   {FormatBytes(data.Length)}  (expected {FormatBytes(HeaderSize + (long)entryCount * EntrySize)})");
    AnsiConsole.WriteLine();

    if (magic != ExpectedMagic || data.Length < HeaderSize + (long)entryCount * EntrySize)
    {
        AnsiConsole.MarkupLine("[red]File appears corrupt.[/]");
        Pause();
        return;
    }

    var sortOk   = true;
    var firstVio = -1;
    for (var i = 1; i < entryCount; i++)
    {
        var prevOff = HeaderSize + (long)(i - 1) * EntrySize;
        var currOff = HeaderSize + (long)i * EntrySize;
        var prev    = new Hash32(data.AsSpan((int)prevOff, 32));
        var curr    = new Hash32(data.AsSpan((int)currOff, 32));
        if (curr.CompareTo(prev) < 0) { sortOk = false; firstVio = i; break; }
    }

    AnsiConsole.MarkupLine(sortOk
        ? "[green]Sort order: correct (ascending by Hash32)[/]"
        : $"[red]Sort order: VIOLATION at entry {firstVio}[/]");
    AnsiConsole.WriteLine();

    var showCount = Math.Min(MaxShow, entryCount);
    var table = new Table()
        .Border(TableBorder.Simple)
        .AddColumn(new TableColumn("#").RightAligned())
        .AddColumn("Hash (first 16 hex chars)")
        .AddColumn(new TableColumn("FileNo").RightAligned())
        .AddColumn(new TableColumn("Offset").RightAligned())
        .AddColumn(new TableColumn("Length").RightAligned());

    for (var i = 0; i < showCount; i++)
    {
        var off    = (int)(HeaderSize + (long)i * EntrySize);
        var hash   = Convert.ToHexString(data, off, 8).ToLowerInvariant();
        var fileNo = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(off + 32, 4));
        var offset = (long)BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan(off + 36, 8));
        var length = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(off + 44, 4));
        table.AddRow($"{i}", $"{hash}...", $"{fileNo}", $"0x{offset:X}", FormatBytes(length));
    }

    AnsiConsole.Write(table);
    if (entryCount > MaxShow)
        AnsiConsole.MarkupLine($"[grey]... {entryCount - MaxShow} more entries not shown[/]");

    if (AnsiConsole.Confirm("Binary-search for a specific hash?", defaultValue: false))
    {
        var hexInput = AnsiConsole.Ask<string>("Enter BLAKE3 hash (hex, 64 chars):");
        hexInput = hexInput.Trim().ToLowerInvariant();
        if (hexInput.Length == 64)
        {
            var targetBytes = Convert.FromHexString(hexInput);
            var target      = new Hash32(targetBytes);
            var (found, idx) = BinarySearch(data, entryCount, target);
            if (found)
            {
                var off    = (int)(HeaderSize + (long)idx * EntrySize);
                var fileNo = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(off + 32, 4));
                var offset = (long)BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan(off + 36, 8));
                var length = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(off + 44, 4));
                AnsiConsole.MarkupLine($"[green]FOUND[/] at entry {idx}: fileNo={fileNo}, offset=0x{offset:X}, length={FormatBytes(length)}");
            }
            else AnsiConsole.MarkupLine("[yellow]NOT FOUND[/] in this segment.");
        }
        else AnsiConsole.MarkupLine("[red]Invalid hex length (expected 64).[/]");
    }

    Pause();
}

static (bool Found, int Index) BinarySearch(byte[] data, int entryCount, Hash32 target)
{
    const int HeaderSize = 8;
    const int EntrySize  = 48;
    var lo = 0;
    var hi = entryCount - 1;
    while (lo <= hi)
    {
        var mid       = (int)(((uint)lo + (uint)hi) >> 1);
        var off       = (int)(HeaderSize + (long)mid * EntrySize);
        var candidate = new Hash32(data.AsSpan(off, 32));
        var cmp       = target.CompareTo(candidate);
        if (cmp == 0) return (true, mid);
        if (cmp < 0)  hi = mid - 1;
        else          lo = mid + 1;
    }
    return (false, -1);
}

// ============================================================
// Inspect Bloom Filter
// ============================================================

static void InspectBloomFilter(string bloomPath)
{
    AnsiConsole.Clear();
    AnsiConsole.MarkupLine($"[bold]Bloom filter:[/] {Markup.Escape(bloomPath)}");
    AnsiConsole.WriteLine();

    byte[] data;
    try { data = File.ReadAllBytes(bloomPath); }
    catch (Exception ex)
    {
        AnsiConsole.MarkupLine($"[red]Failed to read bloom filter: {Markup.Escape(ex.Message)}[/]");
        Pause();
        return;
    }

    if (data.Length < 8)
    {
        AnsiConsole.MarkupLine("[red]File too short (< 8 bytes).[/]");
        Pause();
        return;
    }

    var bitCount  = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(0, 4));
    var hashCount = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(4, 4));
    var byteCount = bitCount / 8;
    var expected  = 8 + bitCount / 8;

    AnsiConsole.MarkupLine($"Bit count:  {bitCount:N0}  ({FormatBytes(byteCount)})");
    AnsiConsole.MarkupLine($"Hash count: {hashCount}");
    AnsiConsole.MarkupLine($"File size:  {FormatBytes(data.Length)}  (expected {FormatBytes(expected)})");
    AnsiConsole.MarkupLine(data.Length >= expected ? "[green]File size: OK[/]" : "[red]File size: truncated[/]");

    if (data.Length >= 8 && bitCount > 0)
    {
        long setBits = 0;
        for (var i = 8; i < Math.Min(data.Length, 8 + byteCount); i++)
            setBits += System.Numerics.BitOperations.PopCount(data[i]);
        var fillRatio = (double)setBits / bitCount * 100.0;
        AnsiConsole.MarkupLine($"Set bits:   {setBits:N0} / {bitCount:N0}  ({fillRatio:F1}% fill)");
    }

    Pause();
}

// ============================================================
// Decode FileDef Record
// ============================================================

static async Task DecodeFileDefInteractiveAsync(string storeRoot)
{
    AnsiConsole.Clear();
    RenderHeader(storeRoot);

    var hexKey = AnsiConsole.Ask<string>("Enter storage key (BLAKE3 hex, 64 chars):");
    hexKey = hexKey.Trim().ToLowerInvariant();
    if (hexKey.Length != 64)
    {
        AnsiConsole.MarkupLine("[red]Invalid key length (expected 64 hex chars).[/]");
        Pause();
        return;
    }

    Hash32 storageKey;
    try { storageKey = Hash32.FromHexString(hexKey); }
    catch { AnsiConsole.MarkupLine("[red]Invalid hex string.[/]"); Pause(); return; }

    var prefix    = hexKey[..3];
    var bucketDir = Path.Combine(storeRoot, "FileDefs", prefix[..2]);

    byte[]? blob = null;
    await AnsiConsole.Status().Spinner(Spinner.Known.Dots)
        .StartAsync("Looking up storage key...", async _ =>
        { blob = await ReadRawBlobAsync(bucketDir, "fileDefs", prefix, storageKey); });

    if (blob is null)
    {
        AnsiConsole.MarkupLine("[red]Storage key not found in FileDefs store.[/]");
        Pause();
        return;
    }

    AnsiConsole.MarkupLine($"[green]Found blob:[/] {FormatBytes(blob.Length)}");
    AnsiConsole.WriteLine();

    try
    {
        var record = FileDefinitionRecord.Deserialize(blob);
        var table  = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("[grey]Field[/]")
            .AddColumn("[grey]Value[/]");

        table.AddRow("FileHash",   record.FileHash.ToHexString());
        table.AddRow("FileLength", FormatBytes(record.FileLength));
        table.AddRow("ChunkCount", $"{record.ChunkHashes.Count:N0}");
        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();

        const int MaxChunks = 50;
        var showChunks = Math.Min(MaxChunks, record.ChunkHashes.Count);
        AnsiConsole.MarkupLine($"[bold]First {showChunks} chunk hashes:[/]");
        for (var i = 0; i < showChunks; i++)
            AnsiConsole.WriteLine($"  [{i:D4}] {record.ChunkHashes[i].ToHexString()}");
        if (record.ChunkHashes.Count > MaxChunks)
            AnsiConsole.MarkupLine($"  [grey]... {record.ChunkHashes.Count - MaxChunks} more[/]");
    }
    catch (Exception ex)
    {
        AnsiConsole.MarkupLine($"[red]Failed to deserialize FileDefinitionRecord: {Markup.Escape(ex.Message)}[/]");
    }

    Pause();
}

// ============================================================
// Verify FileDef (V5: by storageKey; V4 legacy: by contentHash)
// ============================================================

// Verify a FileDef:
//   V5 path (storageKey known):
//     1a. Fast path: ReadRawBlobAsync using storageKey as index key.
//     1b. Fallback: scan storageKey-prefix bucket, match BLAKE3(blob) == storageKey.
//   V4 legacy path (only contentHash known):
//     1c. Scan contentHash-prefix bucket, match rec.FileHash == contentHash.
//   2. Probe each chunk hash in the Chunks store (lightweight, no blob read).
//   3. Optional content-hash verification: read all chunk blobs, compute BLAKE3, compare.
static async Task VerifyFileDefAsync(string storeRoot, Hash32? contentHash, Hash32? storageKey)
{
    AnsiConsole.Clear();
    RenderHeader(storeRoot);
    AnsiConsole.MarkupLine($"[bold cyan]Verify FileDef[/]");
    if (storageKey.HasValue)
        AnsiConsole.MarkupLine($"  Storage key : {Markup.Escape(storageKey.Value.ToHexString())}  [grey](V5)[/]");
    if (contentHash.HasValue)
        AnsiConsole.MarkupLine($"  Content hash: {Markup.Escape(contentHash.Value.ToHexString())}");
    AnsiConsole.WriteLine();

    if (!storageKey.HasValue && !contentHash.HasValue)
    {
        AnsiConsole.MarkupLine("[red]No key provided — cannot look up FileDef.[/]");
        Pause();
        return;
    }

    FileDefinitionRecord? foundRecord = null;

    // ── V5: storageKey known ─────────────────────────────────────────────────
    if (storageKey.HasValue)
    {
        var sk        = storageKey.Value;
        var hexSk     = sk.ToHexString();
        var prefix    = hexSk[..3];
        var bucketDir = Path.Combine(storeRoot, "FileDefs", prefix[..2]);

        AnsiConsole.MarkupLine("[grey]V5: trying fast-path lookup via storageKey index...[/]");
        var fastBlob = await ReadRawBlobAsync(bucketDir, "fileDefs", prefix, sk);
        if (fastBlob is not null)
        {
            try
            {
                foundRecord = FileDefinitionRecord.Deserialize(fastBlob);
                AnsiConsole.MarkupLine("[green]Fast-path hit.[/]");
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[yellow]Fast-path blob deserialization failed: {Markup.Escape(ex.Message)}[/]");
            }
        }

        if (foundRecord is null)
        {
            AnsiConsole.MarkupLine("[grey]Fast-path missed — scanning bucket for BLAKE3(blob) == storageKey...[/]");

            if (!Directory.Exists(bucketDir))
            {
                AnsiConsole.MarkupLine("[red]FileDef bucket directory does not exist.[/]");
                Pause();
                return;
            }

            var packFiles = Directory.EnumerateFiles(bucketDir, $"fileDefs{prefix}-*.pack").OrderBy(f => f).ToList();
            var sw2       = System.Diagnostics.Stopwatch.StartNew();
            var scanned2  = 0;

            await AnsiConsole.Status().Spinner(Spinner.Known.Dots).StartAsync("Scanning FileDef packs (V5 fallback)...", async _ =>
            {
                foreach (var packPath in packFiles)
                {
                    if (foundRecord is not null) break;
                    try
                    {
                        await using var fs = new FileStream(packPath, FileMode.Open, FileAccess.Read,
                            FileShare.ReadWrite, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);

                        const int  HeaderSize = 21;
                        const uint Magic      = 0x4B505342;

                        while (fs.Position < fs.Length && foundRecord is null)
                        {
                            var header = new byte[HeaderSize];
                            var read   = await fs.ReadAsync(header.AsMemory(0, HeaderSize));
                            if (read < HeaderSize) break;

                            var magic           = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(0, 4));
                            var uncompressedLen = (int)BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(5, 4));
                            var compressedLen   = (int)BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(9, 4));
                            var expectedChksum  = BinaryPrimitives.ReadUInt64LittleEndian(header.AsSpan(13, 8));

                            if (magic != Magic) break;

                            var compressed = new byte[compressedLen];
                            var totalRead  = 0;
                            while (totalRead < compressedLen)
                            {
                                var r = await fs.ReadAsync(compressed.AsMemory(totalRead, compressedLen - totalRead));
                                if (r == 0) goto nextPackV5;
                                totalRead += r;
                            }

                            if (XxHash3.HashToUInt64(compressed) != expectedChksum) { scanned2++; continue; }

                            byte[] blob2;
                            try
                            {
                                using var dec = new ZstdNet.Decompressor();
                                blob2 = dec.Unwrap(compressed, uncompressedLen);
                            }
                            catch { scanned2++; continue; }

                            scanned2++;
                            var computedKey = new Hash32(Blake3.Hasher.Hash(blob2).AsSpan());
                            if (computedKey == sk)
                            {
                                try { foundRecord = FileDefinitionRecord.Deserialize(blob2); }
                                catch { /* skip */ }
                            }
                        }
                        nextPackV5:;
                    }
                    catch { /* skip corrupt pack */ }
                }
            });
            sw2.Stop();
            AnsiConsole.MarkupLine($"Scanned [bold]{scanned2:N0}[/] record{(scanned2 == 1 ? "" : "s")} in {FormatElapsed(sw2.Elapsed)}.");
            AnsiConsole.WriteLine();
        }
    }

    // ── V4 legacy: only contentHash known ───────────────────────────────────
    if (foundRecord is null && contentHash.HasValue)
    {
        var ch        = contentHash.Value;
        var hexCh     = ch.ToHexString();
        var prefix    = hexCh[..3];
        var bucketDir = Path.Combine(storeRoot, "FileDefs", prefix[..2]);

        AnsiConsole.MarkupLine("[grey]V4 legacy: scanning FileDef bucket for matching FileHash...[/]");

        if (!Directory.Exists(bucketDir))
        {
            AnsiConsole.MarkupLine("[red]FileDef bucket directory does not exist.[/]");
            Pause();
            return;
        }

        var packFiles = Directory.EnumerateFiles(bucketDir, $"fileDefs{prefix}-*.pack").OrderBy(f => f).ToList();
        var sw        = System.Diagnostics.Stopwatch.StartNew();
        var scanned   = 0;

        await AnsiConsole.Status().Spinner(Spinner.Known.Dots).StartAsync("Scanning FileDef packs (V4 legacy)...", async _ =>
        {
            foreach (var packPath in packFiles)
            {
                if (foundRecord is not null) break;
                try
                {
                    await using var fs = new FileStream(packPath, FileMode.Open, FileAccess.Read,
                        FileShare.ReadWrite, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);

                    const int  HeaderSize = 21;
                    const uint Magic      = 0x4B505342;

                    while (fs.Position < fs.Length && foundRecord is null)
                    {
                        var header = new byte[HeaderSize];
                        var read   = await fs.ReadAsync(header.AsMemory(0, HeaderSize));
                        if (read < HeaderSize) break;

                        var magic           = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(0, 4));
                        var uncompressedLen = (int)BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(5, 4));
                        var compressedLen   = (int)BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(9, 4));
                        var expectedChksum  = BinaryPrimitives.ReadUInt64LittleEndian(header.AsSpan(13, 8));

                        if (magic != Magic) break;

                        var compressed = new byte[compressedLen];
                        var totalRead  = 0;
                        while (totalRead < compressedLen)
                        {
                            var r = await fs.ReadAsync(compressed.AsMemory(totalRead, compressedLen - totalRead));
                            if (r == 0) goto nextPackV4;
                            totalRead += r;
                        }

                        if (XxHash3.HashToUInt64(compressed) != expectedChksum) { scanned++; continue; }

                        byte[] blobV4;
                        try
                        {
                            using var dec = new ZstdNet.Decompressor();
                            blobV4 = dec.Unwrap(compressed, uncompressedLen);
                        }
                        catch { scanned++; continue; }

                        try
                        {
                            var rec = FileDefinitionRecord.Deserialize(blobV4);
                            scanned++;
                            if (rec.FileHash == ch) foundRecord = rec;
                        }
                        catch { scanned++; }
                    }
                    nextPackV4:;
                }
                catch { /* skip corrupt pack */ }
            }
        });
        sw.Stop();
        AnsiConsole.MarkupLine($"Scanned [bold]{scanned:N0}[/] record{(scanned == 1 ? "" : "s")} in {FormatElapsed(sw.Elapsed)}.");
        AnsiConsole.WriteLine();
        if (foundRecord is null)
            AnsiConsole.MarkupLine("[grey]Note: V4 lookup may fail for stores migrated to V5 (blobs stored under storageKey prefix).[/]");
    }

    // ── Result ───────────────────────────────────────────────────────────────
    if (foundRecord is null)
    {
        AnsiConsole.MarkupLine("[red]FileDef NOT FOUND.[/]");
        Pause();
        return;
    }

    AnsiConsole.MarkupLine($"[green]FileDef FOUND[/]  FileLength: {FormatBytes(foundRecord.FileLength)}  Chunks: {foundRecord.ChunkHashes.Count:N0}");
    AnsiConsole.WriteLine();

    // ── Step 2: Probe chunk existence ────────────────────────────────────────
    AnsiConsole.MarkupLine("[bold]Probing chunks...[/]");
    var missingChunks = new List<(int Index, Hash32 Hash)>();
    var totalChunks   = foundRecord.ChunkHashes.Count;

    var chunkSw = System.Diagnostics.Stopwatch.StartNew();
    await AnsiConsole.Progress()
        .AutoRefresh(true)
        .Columns(new TaskDescriptionColumn(), new ProgressBarColumn(), new PercentageColumn(), new SpinnerColumn())
        .StartAsync(async ctx =>
        {
            var task = ctx.AddTask("Checking chunks", maxValue: totalChunks);
            for (var i = 0; i < totalChunks; i++)
            {
                var chunkHash = foundRecord.ChunkHashes[i];
                var exists    = await ProbeHashExistsAsync(storeRoot, "Chunks", "chunks", chunkHash);
                if (!exists) missingChunks.Add((i, chunkHash));
                task.Increment(1);
            }
        });
    chunkSw.Stop();

    AnsiConsole.WriteLine();
    AnsiConsole.MarkupLine($"Chunk probe complete in {FormatElapsed(chunkSw.Elapsed)}.");
    AnsiConsole.WriteLine();

    if (missingChunks.Count == 0)
    {
        AnsiConsole.MarkupLine($"[green]All {totalChunks:N0} chunk{(totalChunks == 1 ? "" : "s")} present.[/]");
    }
    else
    {
        AnsiConsole.MarkupLine($"[red]MISSING {missingChunks.Count:N0} / {totalChunks:N0} chunk{(missingChunks.Count == 1 ? "" : "s")}![/]");
        AnsiConsole.WriteLine();
        const int MaxShow = 30;
        var show = Math.Min(MaxShow, missingChunks.Count);
        AnsiConsole.MarkupLine($"[bold]First {show} missing chunk{(show == 1 ? "" : "s")}:[/]");
        for (var j = 0; j < show; j++)
        {
            var (idx, hash) = missingChunks[j];
            AnsiConsole.MarkupLine($"  [{idx:D4}] {Markup.Escape(hash.ToHexString())}");
        }
        if (missingChunks.Count > MaxShow)
            AnsiConsole.MarkupLine($"  [grey]... {missingChunks.Count - MaxShow} more not shown[/]");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[yellow]FileDef is INCOMPLETE — missing chunks detected.[/]");
        Pause();
        return;
    }

    // ── Step 3: Optional content-hash verification ───────────────────────────
    AnsiConsole.WriteLine();
    var proceed = AnsiConsole.Prompt(
        new SelectionPrompt<string>()
            .Title("[bold]Proceed with content-hash verification?[/] (reads all chunk blobs)")
            .AddChoices("Yes — compute BLAKE3 over all chunk data", "No — skip"));

    if (proceed.StartsWith("No"))
    {
        Pause();
        return;
    }

    AnsiConsole.MarkupLine("[grey]Reading all chunks and computing BLAKE3...[/]");
    var verifySw      = System.Diagnostics.Stopwatch.StartNew();
    Hash32? computedFileHash = null;
    var verifyError   = false;

    await AnsiConsole.Progress()
        .AutoRefresh(true)
        .Columns(new TaskDescriptionColumn(), new ProgressBarColumn(), new PercentageColumn(), new SpinnerColumn())
        .StartAsync(async ctx =>
        {
            var task = ctx.AddTask("Reading chunks", maxValue: totalChunks);
            using var hasher = Blake3.Hasher.New();
            for (var i = 0; i < totalChunks; i++)
            {
                var chunkHash   = foundRecord.ChunkHashes[i];
                var hexChunk    = chunkHash.ToHexString();
                var chunkPrefix = hexChunk[..3];
                var chunkBucket = Path.Combine(storeRoot, "Chunks", chunkPrefix[..2]);
                var chunkBlob   = await ReadRawBlobAsync(chunkBucket, "chunks", chunkPrefix, chunkHash);
                if (chunkBlob is null)
                {
                    AnsiConsole.MarkupLine($"[red]Cannot read chunk [{i:D4}] {Markup.Escape(hexChunk[..16])}...[/]");
                    verifyError = true;
                    return;
                }
                hasher.Update(chunkBlob);
                task.Increment(1);
            }
            computedFileHash = new Hash32(hasher.Finalize().AsSpan());
        });
    verifySw.Stop();

    AnsiConsole.WriteLine();
    AnsiConsole.MarkupLine($"Content-hash verification complete in {FormatElapsed(verifySw.Elapsed)}.");
    AnsiConsole.WriteLine();

    if (verifyError)
    {
        AnsiConsole.MarkupLine("[red]Verification aborted — could not read one or more chunks.[/]");
    }
    else if (computedFileHash.HasValue)
    {
        if (computedFileHash.Value == foundRecord.FileHash)
            AnsiConsole.MarkupLine($"[green]Content hash MATCH[/]  {Markup.Escape(foundRecord.FileHash.ToHexString())}");
        else
        {
            AnsiConsole.MarkupLine("[red]Content hash MISMATCH[/]");
            AnsiConsole.MarkupLine($"  Expected : {Markup.Escape(foundRecord.FileHash.ToHexString())}");
            AnsiConsole.MarkupLine($"  Computed : {Markup.Escape(computedFileHash.Value.ToHexString())}");
        }
    }

    Pause();
}

// Lightweight probe: returns true if the given hash exists in the specified category store.
// Uses log → bloom → segment binary search, but does NOT read the blob payload.
static async Task<bool> ProbeHashExistsAsync(string storeRoot, string catName, string catPrefix, Hash32 hash)
{
    var hexHash   = hash.ToHexString();
    var prefix    = hexHash[..3];
    var bucketDir = Path.Combine(storeRoot, catName, prefix[..2]);

    if (!Directory.Exists(bucketDir)) return false;

    // Tier 0: log
    var logPath = Path.Combine(bucketDir, $"{catPrefix}{prefix}.log");
    if (File.Exists(logPath) && SearchLog(logPath, hash).HasValue) return true;

    // Tier 1+: bloom + segment binary search
    foreach (var sf in Directory.EnumerateFiles(bucketDir, $"{catPrefix}{prefix}.seg-*.idx")
                 .OrderByDescending(f => f))
    {
        var bloomPath = Path.ChangeExtension(sf, ".bloom");
        if (File.Exists(bloomPath))
        {
            try
            {
                var bd    = File.ReadAllBytes(bloomPath);
                var bloom = PackIndexBloomFilter.Deserialize(bd);
                if (!bloom.MightContain(hash)) continue;
            }
            catch { /* proceed to segment */ }
        }

        try
        {
            await using var sfs = new FileStream(sf, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, 64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var segData = new byte[sfs.Length];
            await sfs.ReadExactlyAsync(segData);

            var ec       = (int)BinaryPrimitives.ReadUInt32LittleEndian(segData.AsSpan(4, 4));
            var (found, _) = BinarySearch(segData, ec, hash);
            if (found) return true;
        }
        catch { /* skip corrupt segment */ }
    }

    return false;
}

// ============================================================
// Hash Lookup
// ============================================================

static async Task HashLookupAsync(string storeRoot)
{
    AnsiConsole.Clear();
    RenderHeader(storeRoot);

    var hexHash = AnsiConsole.Ask<string>("Enter BLAKE3 hash to find (64 hex chars):");
    hexHash = hexHash.Trim().ToLowerInvariant();
    if (hexHash.Length != 64)
    {
        AnsiConsole.MarkupLine("[red]Invalid length (expected 64 hex chars).[/]");
        Pause();
        return;
    }

    Hash32 hash;
    try { hash = Hash32.FromHexString(hexHash); }
    catch { AnsiConsole.MarkupLine("[red]Invalid hex string.[/]"); Pause(); return; }

    var prefix = hexHash[..3];

    var categoryChoice = AnsiConsole.Prompt(
        new SelectionPrompt<string>()
            .Title("Search in:")
            .AddChoices("Chunks", "FileDefs", "Both"));

    var categories = categoryChoice switch
    {
        "Chunks"   => new[] { ("Chunks",   "chunks") },
        "FileDefs" => new[] { ("FileDefs", "fileDefs") },
        _          => new[] { ("Chunks",   "chunks"), ("FileDefs", "fileDefs") }
    };

    AnsiConsole.WriteLine();

    var steps      = new List<LookupStep>();
    var totalSw    = System.Diagnostics.Stopwatch.StartNew();
    bool anyFound  = false;

    foreach (var (catName, catPrefix) in categories)
    {
        AnsiConsole.MarkupLine($"[bold]Searching in {catName}...[/]");
        var bucketDir = Path.Combine(storeRoot, catName, prefix[..2]);

        if (!Directory.Exists(bucketDir))
        {
            AnsiConsole.MarkupLine("  [grey]Bucket directory does not exist.[/]");
            steps.Add(new LookupStep($"{catName} / bucket dir", false, "missing", TimeSpan.Zero));
            continue;
        }

        // ── Tier 0: log ─────────────────────────────────────
        var logPath = Path.Combine(bucketDir, $"{catPrefix}{prefix}.log");
        if (File.Exists(logPath))
        {
            var sw       = System.Diagnostics.Stopwatch.StartNew();
            var logEntry = SearchLog(logPath, hash);
            sw.Stop();

            if (logEntry.HasValue)
            {
                var (fn, off, len) = logEntry.Value;
                var detail = $"fileNo={fn}, offset=0x{off:X}, length={FormatBytes(len)}";
                AnsiConsole.MarkupLine($"  [green]FOUND in log:[/] {detail}");
                steps.Add(new LookupStep($"{catName} / log", true, detail, sw.Elapsed));
                anyFound = true;
                continue;
            }
            else
            {
                AnsiConsole.MarkupLine("  [grey]Not in log.[/]");
                steps.Add(new LookupStep($"{catName} / log", false, "not found", sw.Elapsed));
            }
        }

        // ── Tier 1+: bloom + segment ─────────────────────────
        var segFiles   = Directory.EnumerateFiles(bucketDir, $"{catPrefix}{prefix}.seg-*.idx")
            .OrderByDescending(f => f).ToList();
        var foundInSeg = false;

        foreach (var sf in segFiles)
        {
            var segName   = Path.GetFileName(sf);
            var bloomPath = Path.ChangeExtension(sf, ".bloom");

            // Bloom probe
            if (File.Exists(bloomPath))
            {
                var bsw = System.Diagnostics.Stopwatch.StartNew();
                var skip = false;
                try
                {
                    var bloomData = File.ReadAllBytes(bloomPath);
                    var bloom     = PackIndexBloomFilter.Deserialize(bloomData);
                    skip          = !bloom.MightContain(hash);
                }
                catch { /* bloom unreadable — proceed to segment */ }
                bsw.Stop();

                if (skip)
                {
                    AnsiConsole.MarkupLine($"  [grey]Bloom says NOT in {segName}[/]");
                    steps.Add(new LookupStep($"{catName} / bloom({segName})", false, "rejected by bloom", bsw.Elapsed));
                    continue;
                }
                steps.Add(new LookupStep($"{catName} / bloom({segName})", false, "passed bloom", bsw.Elapsed));
            }

            // Segment binary search
            var ssw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                byte[] segData;
                await using var fs = new FileStream(sf, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete, 64 * 1024,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                segData = new byte[fs.Length];
                await fs.ReadExactlyAsync(segData);
                ssw.Stop();

                var readElapsed = ssw.Elapsed;
                var bsw2        = System.Diagnostics.Stopwatch.StartNew();
                var ec          = (int)BinaryPrimitives.ReadUInt32LittleEndian(segData.AsSpan(4, 4));
                var (found, idx) = BinarySearch(segData, ec, hash);
                bsw2.Stop();

                steps.Add(new LookupStep($"{catName} / seg read({segName})", false, $"{FormatBytes(segData.Length)} read", readElapsed));

                if (found)
                {
                    const int H = 8, E = 48;
                    var o      = (int)(H + (long)idx * E);
                    var fileNo = (int)BinaryPrimitives.ReadUInt32LittleEndian(segData.AsSpan(o + 32, 4));
                    var offset = (long)BinaryPrimitives.ReadUInt64LittleEndian(segData.AsSpan(o + 36, 8));
                    var length = (int)BinaryPrimitives.ReadUInt32LittleEndian(segData.AsSpan(o + 44, 4));
                    var detail = $"fileNo={fileNo}, offset=0x{offset:X}, length={FormatBytes(length)}";
                    AnsiConsole.MarkupLine($"  [green]FOUND in {segName}[/] entry {idx}: {detail}");
                    steps.Add(new LookupStep($"{catName} / bsearch({segName})", true, detail, bsw2.Elapsed));
                    foundInSeg = true;
                    anyFound   = true;
                    break;
                }
                else
                {
                    AnsiConsole.MarkupLine($"  [grey]Not in {segName}[/]");
                    steps.Add(new LookupStep($"{catName} / bsearch({segName})", false, "not found", bsw2.Elapsed));
                }
            }
            catch (Exception ex)
            {
                ssw.Stop();
                AnsiConsole.MarkupLine($"  [red]Error reading {segName}: {Markup.Escape(ex.Message)}[/]");
                steps.Add(new LookupStep($"{catName} / seg read({segName})", false, $"ERROR: {ex.Message}", ssw.Elapsed));
            }
        }

        if (!foundInSeg)
        {
            var verdict = File.Exists(logPath) ? "NOT FOUND in segments" : "NOT FOUND";
            AnsiConsole.MarkupLine($"  [yellow]{verdict} in {catName}[/]");
        }
    }

    totalSw.Stop();

    // ── Timing summary ────────────────────────────────────────
    AnsiConsole.WriteLine();
    AnsiConsole.MarkupLine("[bold cyan]Timing summary[/]");

    var timingTable = new Table()
        .Border(TableBorder.Simple)
        .AddColumn("Step")
        .AddColumn(new TableColumn("Result").Centered())
        .AddColumn("Detail")
        .AddColumn(new TableColumn("Elapsed").RightAligned());

    foreach (var s in steps)
    {
        var resultMarkup = s.Found ? "[green]FOUND[/]" : "[grey]—[/]";
        timingTable.AddRow(
            Markup.Escape(s.Label),
            resultMarkup,
            Markup.Escape(s.Detail),
            FormatElapsed(s.Elapsed));
    }

    AnsiConsole.Write(timingTable);

    var totalColor = anyFound ? "green" : "yellow";
    AnsiConsole.MarkupLine($"\n[bold]Total:[/] [{totalColor}]{FormatElapsed(totalSw.Elapsed)}[/]  " +
                           (anyFound ? "[green]Hash found.[/]" : "[yellow]Hash not found.[/]"));

    Pause();
}

static string FormatElapsed(TimeSpan t) =>
    t.TotalSeconds >= 1
        ? $"{t.TotalSeconds:F3} s"
        : t.TotalMilliseconds >= 1
            ? $"{t.TotalMilliseconds:F2} ms"
            : $"{t.TotalMicroseconds:F0} µs";

// ============================================================
// Integrity Check
// ============================================================

static async Task IntegrityCheckAllAsync(string storeRoot, string catDir, string catPrefix)
{
    AnsiConsole.Clear();
    RenderHeader(storeRoot);
    long total = 0, corrupt = 0;
    await AnsiConsole.Progress()
        .Columns(new TaskDescriptionColumn(), new ProgressBarColumn(), new PercentageColumn(), new SpinnerColumn())
        .StartAsync(async ctx =>
        {
            var task = ctx.AddTask("Checking all buckets...", maxValue: 4096);
            for (var i = 0; i < 4096; i++)
            {
                var p  = i.ToString("x3");
                var bd = Path.Combine(catDir, p[..2]);
                if (Directory.Exists(bd))
                {
                    var (t, c) = await CheckBucketAsync(bd, catPrefix, p, reportErrors: false);
                    total   += t;
                    corrupt += c;
                }
                task.Increment(1);
            }
        });

    AnsiConsole.MarkupLine($"\nTotal entries checked: [bold]{total:N0}[/]");
    AnsiConsole.MarkupLine(corrupt == 0 ? "[green]No corrupt entries found.[/]" : $"[red]Corrupt entries: {corrupt:N0}[/]");
    Pause();
}

static async Task RunIntegrityCheckAsync(string storeRoot, string catDir, string catPrefix, string prefix)
{
    AnsiConsole.Clear();
    RenderHeader(storeRoot);
    AnsiConsole.MarkupLine($"[bold]Integrity check:[/] {catPrefix}/{prefix}");
    AnsiConsole.WriteLine();

    var bucketDir = Path.Combine(catDir, prefix[..2]);
    if (!Directory.Exists(bucketDir))
    {
        AnsiConsole.MarkupLine("[yellow]Bucket directory does not exist.[/]");
        Pause();
        return;
    }

    long totalEntries = 0, corruptEntries = 0;
    await AnsiConsole.Status().Spinner(Spinner.Known.Dots)
        .StartAsync("Checking pack entries...", async _ =>
        { (totalEntries, corruptEntries) = await CheckBucketAsync(bucketDir, catPrefix, prefix, reportErrors: true); });

    AnsiConsole.MarkupLine($"\nEntries checked: [bold]{totalEntries:N0}[/]");
    AnsiConsole.MarkupLine(corruptEntries == 0 ? "[green]All entries OK.[/]" : $"[red]{corruptEntries} corrupt entries found.[/]");
    Pause();
}

static async Task<(long total, long corrupt)> CheckBucketAsync(
    string bucketDir, string catPrefix, string prefix, bool reportErrors)
{
    long total = 0, corrupt = 0;
    const int  HeaderSize = 21;
    const uint Magic      = 0x4B505342;

    foreach (var pf in Directory.EnumerateFiles(bucketDir, $"{catPrefix}{prefix}-*.pack").OrderBy(f => f))
    {
        try
        {
            await using var fs = new FileStream(pf, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);

            while (fs.Position < fs.Length)
            {
                var header = new byte[HeaderSize];
                var read   = await fs.ReadAsync(header.AsMemory());
                if (read < HeaderSize) break;

                var magic          = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(0, 4));
                var compressedLen  = (int)BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(9, 4));
                var expectedChksum = BinaryPrimitives.ReadUInt64LittleEndian(header.AsSpan(13, 8));

                if (magic != Magic)
                {
                    if (reportErrors) AnsiConsole.MarkupLine($"[red]Bad magic in {Path.GetFileName(pf)} at offset {fs.Position - HeaderSize}[/]");
                    corrupt++; break;
                }

                var compressed = new byte[compressedLen];
                var totalRead  = 0;
                while (totalRead < compressedLen)
                {
                    var r = await fs.ReadAsync(compressed.AsMemory(totalRead, compressedLen - totalRead));
                    if (r == 0) break;
                    totalRead += r;
                }

                var actualChksum = XxHash3.HashToUInt64(compressed);
                if (actualChksum != expectedChksum)
                {
                    if (reportErrors) AnsiConsole.MarkupLine($"[red]Checksum mismatch in {Path.GetFileName(pf)}[/]");
                    corrupt++;
                }
                total++;
            }
        }
        catch (Exception ex)
        {
            if (reportErrors) AnsiConsole.MarkupLine($"[red]Error reading {Path.GetFileName(pf)}: {Markup.Escape(ex.Message)}[/]");
        }
    }
    return (total, corrupt);
}

// ============================================================
// Verify Pack Offsets
// ============================================================

static async Task VerifyPackOffsetsAllAsync(string storeRoot, string catDir, string catPrefix)
{
    AnsiConsole.Clear();
    RenderHeader(storeRoot);
    long totalChecked = 0, totalFailed = 0;
    await AnsiConsole.Progress()
        .Columns(new TaskDescriptionColumn(), new ProgressBarColumn(), new PercentageColumn(), new SpinnerColumn())
        .StartAsync(async ctx =>
        {
            var task = ctx.AddTask("Verifying all buckets...", maxValue: 4096);
            for (var i = 0; i < 4096; i++)
            {
                var p  = i.ToString("x3");
                var bd = Path.Combine(catDir, p[..2]);
                if (Directory.Exists(bd))
                {
                    var (chk, fail) = await VerifyBucketOffsetAsync(bd, catPrefix, p, reportDetails: false);
                    totalChecked += chk;
                    totalFailed  += fail;
                }
                task.Increment(1);
            }
        });

    AnsiConsole.MarkupLine($"\nTotal index entries checked: [bold]{totalChecked:N0}[/]");
    AnsiConsole.MarkupLine(totalFailed == 0
        ? "[green]All index pointers verified — no mismatches found.[/]"
        : $"[red]Mismatches found: {totalFailed:N0}[/]");
    Pause();
}

static async Task RunVerifyPackOffsetsAsync(string storeRoot, string catDir, string catPrefix, string prefix)
{
    AnsiConsole.Clear();
    RenderHeader(storeRoot);
    AnsiConsole.MarkupLine($"[bold]Verify pack offsets:[/] {catPrefix}/{prefix}");
    AnsiConsole.WriteLine();

    var bucketDir = Path.Combine(catDir, prefix[..2]);
    if (!Directory.Exists(bucketDir))
    {
        AnsiConsole.MarkupLine("[yellow]Bucket directory does not exist.[/]");
        Pause();
        return;
    }

    long numChecked = 0, numFailed = 0;
    await AnsiConsole.Status().Spinner(Spinner.Known.Dots)
        .StartAsync("Verifying index pointers...", async _ =>
        { (numChecked, numFailed) = await VerifyBucketOffsetAsync(bucketDir, catPrefix, prefix, reportDetails: true); });

    AnsiConsole.MarkupLine($"\nIndex entries checked: [bold]{numChecked:N0}[/]");
    AnsiConsole.MarkupLine(numFailed == 0
        ? "[green]All index pointers verified — no mismatches found.[/]"
        : $"[red]Mismatches found: {numFailed:N0}[/]");
    Pause();
}

static async Task<(long total, long failed)> VerifyBucketOffsetAsync(
    string bucketDir, string catPrefix, string prefix, bool reportDetails)
{
    long totalChecked = 0, totalFailed = 0;

    const int  PackHeaderSize = 21;
    const uint PackMagic      = 0x4B505342;
    const byte PackVersion    = 1;

    var entries = new List<(string Source, string HashHex, int FileNo, long Offset, int Length)>();

    var logPath = Path.Combine(bucketDir, $"{catPrefix}{prefix}.log");
    if (File.Exists(logPath))
    {
        try
        {
            using var fs     = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024);
            using var reader = new BinaryReader(fs, System.Text.Encoding.UTF8, leaveOpen: true);
            var srcLabel     = Path.GetFileName(logPath);
            while (fs.Position < fs.Length)
            {
                var hashBytes = reader.ReadBytes(32);
                if (hashBytes.Length < 32) break;
                var fileNo = VarIntUtils.ReadVarInt<int>(reader);
                var offset = VarIntUtils.ReadVarInt<long>(reader);
                var length = VarIntUtils.ReadVarInt<int>(reader);
                entries.Add((srcLabel, Convert.ToHexString(hashBytes, 0, 8).ToLowerInvariant(), fileNo, offset, length));
            }
        }
        catch (Exception ex)
        {
            if (reportDetails) AnsiConsole.MarkupLine($"[red]Error reading log: {Markup.Escape(ex.Message)}[/]");
        }
    }

    foreach (var sf in Directory.EnumerateFiles(bucketDir, $"{catPrefix}{prefix}.seg-*.idx").OrderBy(f => f))
    {
        try
        {
            await using var sfs = new FileStream(sf, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, 64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var segData = new byte[sfs.Length];
            await sfs.ReadExactlyAsync(segData);

            const uint SegMagic     = 0x58324449;
            const int  SegHdrSize   = 8;
            const int  SegEntrySize = 48;

            if (segData.Length < SegHdrSize) continue;
            var magic = BinaryPrimitives.ReadUInt32LittleEndian(segData.AsSpan(0, 4));
            if (magic != SegMagic) continue;

            var entryCount = (int)BinaryPrimitives.ReadUInt32LittleEndian(segData.AsSpan(4, 4));
            var srcLabel   = Path.GetFileName(sf);

            for (var i = 0; i < entryCount; i++)
            {
                var o = SegHdrSize + i * SegEntrySize;
                if (o + SegEntrySize > segData.Length) break;
                var hashHex = Convert.ToHexString(segData, o, 8).ToLowerInvariant();
                var fileNo  = (int)BinaryPrimitives.ReadUInt32LittleEndian(segData.AsSpan(o + 32, 4));
                var offset  = (long)BinaryPrimitives.ReadUInt64LittleEndian(segData.AsSpan(o + 36, 8));
                var length  = (int)BinaryPrimitives.ReadUInt32LittleEndian(segData.AsSpan(o + 44, 4));
                entries.Add((srcLabel, hashHex, fileNo, offset, length));
            }
        }
        catch (Exception ex)
        {
            if (reportDetails) AnsiConsole.MarkupLine($"[red]Error reading segment {Path.GetFileName(sf)}: {Markup.Escape(ex.Message)}[/]");
        }
    }

    var packHandles = new Dictionary<int, (FileStream Stream, SafeFileHandle Handle)>();
    try
    {
        foreach (var (source, hashHex, fileNo, offset, indexLength) in entries)
        {
            totalChecked++;

            if (!packHandles.TryGetValue(fileNo, out var pf))
            {
                var packPath = Path.Combine(bucketDir, $"{catPrefix}{prefix}-{fileNo}.pack");
                if (!File.Exists(packPath))
                {
                    if (reportDetails)
                        AnsiConsole.MarkupLine($"[red]MISS pack[/] [{source}] hash={hashHex}... fileNo={fileNo} — pack file not found");
                    totalFailed++; continue;
                }
                try
                {
                    var stream = new FileStream(packPath, FileMode.Open, FileAccess.Read,
                        FileShare.ReadWrite, 128 * 1024, FileOptions.Asynchronous | FileOptions.RandomAccess);
                    packHandles[fileNo] = (stream, stream.SafeFileHandle);
                    pf = packHandles[fileNo];
                }
                catch (Exception ex)
                {
                    if (reportDetails)
                        AnsiConsole.MarkupLine($"[red]OPEN ERR[/] [{source}] hash={hashHex}... fileNo={fileNo}: {Markup.Escape(ex.Message)}");
                    totalFailed++; continue;
                }
            }

            var headerBuf = new byte[PackHeaderSize];
            try
            {
                var totalRead = 0;
                while (totalRead < PackHeaderSize)
                {
                    var r = await RandomAccess.ReadAsync(pf.Handle, headerBuf.AsMemory(totalRead), offset + totalRead);
                    if (r == 0) break;
                    totalRead += r;
                }
                if (totalRead < PackHeaderSize)
                {
                    if (reportDetails)
                        AnsiConsole.MarkupLine($"[red]TRUNC[/] [{source}] hash={hashHex}... fileNo={fileNo} offset=0x{offset:X}");
                    totalFailed++; continue;
                }
            }
            catch (Exception ex)
            {
                if (reportDetails)
                    AnsiConsole.MarkupLine($"[red]READ ERR[/] [{source}] offset=0x{offset:X}: {Markup.Escape(ex.Message)}");
                totalFailed++; continue;
            }

            var magic = BinaryPrimitives.ReadUInt32LittleEndian(headerBuf.AsSpan(0, 4));
            if (magic != PackMagic)
            {
                if (reportDetails)
                    AnsiConsole.MarkupLine($"[red]BAD MAGIC[/] [{source}] hash={hashHex}... offset=0x{offset:X}");
                totalFailed++; continue;
            }

            var version = headerBuf[4];
            if (version != PackVersion)
            {
                if (reportDetails)
                    AnsiConsole.MarkupLine($"[red]BAD VERSION[/] [{source}] hash={hashHex}... offset=0x{offset:X} got {version}");
                totalFailed++; continue;
            }

            var compressedLen  = (int)BinaryPrimitives.ReadUInt32LittleEndian(headerBuf.AsSpan(9, 4));
            var storedChecksum = BinaryPrimitives.ReadUInt64LittleEndian(headerBuf.AsSpan(13, 8));
            var expectedLength = PackHeaderSize + compressedLen;

            if (indexLength != expectedLength)
            {
                if (reportDetails)
                    AnsiConsole.MarkupLine($"[red]LEN MISMATCH[/] [{source}] hash={hashHex}... index.Length={indexLength}, expected {expectedLength}");
                totalFailed++; continue;
            }

            var compressed = new byte[compressedLen];
            try
            {
                var totalRead = 0;
                while (totalRead < compressedLen)
                {
                    var r = await RandomAccess.ReadAsync(pf.Handle, compressed.AsMemory(totalRead), offset + PackHeaderSize + totalRead);
                    if (r == 0) break;
                    totalRead += r;
                }
                if (totalRead < compressedLen)
                {
                    if (reportDetails)
                        AnsiConsole.MarkupLine($"[red]BODY TRUNC[/] [{source}] hash={hashHex}... offset=0x{offset:X}");
                    totalFailed++; continue;
                }
            }
            catch (Exception ex)
            {
                if (reportDetails)
                    AnsiConsole.MarkupLine($"[red]BODY READ ERR[/] [{source}]: {Markup.Escape(ex.Message)}");
                totalFailed++; continue;
            }

            var actualChecksum = XxHash3.HashToUInt64(compressed);
            if (actualChecksum != storedChecksum)
            {
                if (reportDetails)
                    AnsiConsole.MarkupLine($"[red]CHKSUM MISMATCH[/] [{source}] hash={hashHex}... offset=0x{offset:X}");
                totalFailed++;
            }
        }
    }
    finally
    {
        foreach (var (stream, _) in packHandles.Values)
            await stream.DisposeAsync();
    }

    return (totalChecked, totalFailed);
}

// ============================================================
// Rebuild Segment (from pack files)
// ============================================================

/// <summary>
/// Rebuilds a fresh sorted segment file by scanning all pack entries in the
/// bucket sequentially, computing the BLAKE3 hash of each decompressed blob,
/// and writing a new <c>seg-000.idx</c> at level 0.
///
/// WARNING: This modifies files on disk.  The user must confirm before proceeding.
/// A safe window to run this is when the server is not actively writing new chunks
/// to this bucket, but the operation is idempotent and will not lose data.
/// </summary>
static async Task RebuildSegmentFromPacksAsync(
    string storeRoot, string bucketDir, string catPrefix, string prefix)
{
    AnsiConsole.Clear();
    RenderHeader(storeRoot);
    AnsiConsole.MarkupLine($"[bold yellow]Rebuild Segment[/]  {catPrefix}/{prefix}");
    AnsiConsole.WriteLine();
    AnsiConsole.MarkupLine("[yellow]WARNING:[/] This will overwrite the existing segment files for this bucket.");
    AnsiConsole.MarkupLine("All segment files will be replaced by a single fresh level-0 segment");
    AnsiConsole.MarkupLine("rebuilt by scanning pack files and hashing each decompressed entry.");
    AnsiConsole.MarkupLine("Bloom filters and log file are NOT modified.");
    AnsiConsole.WriteLine();

    if (!AnsiConsole.Confirm("Proceed?", defaultValue: false))
    {
        AnsiConsole.MarkupLine("[grey]Cancelled.[/]");
        Pause();
        return;
    }

    if (!Directory.Exists(bucketDir))
    {
        AnsiConsole.MarkupLine("[yellow]Bucket directory does not exist.[/]");
        Pause();
        return;
    }

    var packFiles = Directory.EnumerateFiles(bucketDir, $"{catPrefix}{prefix}-*.pack")
        .OrderBy(f => f).ToList();

    if (packFiles.Count == 0)
    {
        AnsiConsole.MarkupLine("[yellow]No pack files found in this bucket.[/]");
        Pause();
        return;
    }

    // Collect (hash, IndexEntry) from all pack entries
    var allEntries = new List<(Hash32 Hash, IndexEntry Entry)>();
    long packBytesTotal = packFiles.Sum(f => new FileInfo(f).Length);

    await AnsiConsole.Progress()
        .AutoRefresh(true)
        .Columns(new TaskDescriptionColumn(), new ProgressBarColumn(), new PercentageColumn(), new SpinnerColumn())
        .StartAsync(async ctx =>
        {
            var task = ctx.AddTask("Scanning pack files...", maxValue: packBytesTotal);

            const int  HeaderSize = 21;
            const uint Magic      = 0x4B505342;

            for (var fileNo = 0; fileNo < packFiles.Count; fileNo++)
            {
                var pf = packFiles[fileNo];
                // Extract fileNo from filename: "{catPrefix}{prefix}-{fileNo}.pack"
                var fname  = Path.GetFileNameWithoutExtension(pf);
                var dashIdx = fname.LastIndexOf('-');
                var packNo  = dashIdx >= 0 && int.TryParse(fname[(dashIdx + 1)..], out var n) ? n : fileNo;

                await using var fs = new FileStream(pf, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite, 128 * 1024,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);

                while (fs.Position < fs.Length)
                {
                    var entryOffset = fs.Position;
                    var header      = new byte[HeaderSize];
                    var read        = await fs.ReadAsync(header.AsMemory());
                    if (read < HeaderSize) break;

                    var magic           = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(0, 4));
                    var uncompressedLen = (int)BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(5, 4));
                    var compressedLen   = (int)BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(9, 4));

                    if (magic != Magic) break;

                    var compressed = new byte[compressedLen];
                    var totalRead  = 0;
                    while (totalRead < compressedLen)
                    {
                        var r = await fs.ReadAsync(compressed.AsMemory(totalRead, compressedLen - totalRead));
                        if (r == 0) break;
                        totalRead += r;
                    }
                    if (totalRead < compressedLen) break;

                    // Decompress and hash with BLAKE3 to get the content key
                    byte[] decompressed;
                    try
                    {
                        using var decompressor = new ZstdNet.Decompressor();
                        decompressed = decompressor.Unwrap(compressed, uncompressedLen);
                    }
                    catch { task.Value = fs.Position; continue; }

                    var hashBytes = Hasher.Hash(decompressed).AsSpan();
                    var hash      = new Hash32(hashBytes);
                    var length    = HeaderSize + compressedLen;

                    allEntries.Add((hash, new IndexEntry(packNo, entryOffset, length)));
                    task.Value = fs.Position;
                }
            }
        });

    if (allEntries.Count == 0)
    {
        AnsiConsole.MarkupLine("[yellow]No valid entries found in pack files.[/]");
        Pause();
        return;
    }

    AnsiConsole.MarkupLine($"Found [bold]{allEntries.Count:N0}[/] entries.");

    // Sort by hash
    await AnsiConsole.Status().Spinner(Spinner.Known.Dots)
        .StartAsync("Sorting entries...", _ =>
        {
            allEntries.Sort((a, b) => a.Hash.CompareTo(b.Hash));
            return Task.CompletedTask;
        });

    // Deduplicate (keep last-seen entry for each hash, like the server does)
    var deduped = new List<(Hash32 Hash, IndexEntry Entry)>(allEntries.Count);
    for (var i = 0; i < allEntries.Count; i++)
    {
        if (i + 1 < allEntries.Count && allEntries[i].Hash == allEntries[i + 1].Hash)
            continue; // skip older duplicate
        deduped.Add(allEntries[i]);
    }

    AnsiConsole.MarkupLine($"After dedup: [bold]{deduped.Count:N0}[/] unique entries.");

    // Delete existing segment files (but NOT log and bloom files)
    var existingSegs = Directory.EnumerateFiles(bucketDir, $"{catPrefix}{prefix}.seg-*.idx").ToList();
    foreach (var s in existingSegs) { try { File.Delete(s); } catch { /* ignore */ } }

    // Write new level-0 segment
    var segPath = Path.Combine(bucketDir, $"{catPrefix}{prefix}.seg-000.idx");
    await AnsiConsole.Status().Spinner(Spinner.Known.Dots)
        .StartAsync($"Writing {segPath}...", async _ =>
        {
            await SortedIndexSegment.WriteAsync(segPath, deduped);
        });

    AnsiConsole.MarkupLine($"[green]Segment rebuilt:[/] {Path.GetFileName(segPath)}  {deduped.Count:N0} entries");
    AnsiConsole.MarkupLine("[grey]Tip: Run 'Rebuild Bloom Filters' to regenerate the bloom filter for the new segment.[/]");
    Pause();
}

// ============================================================
// Rebuild Bloom Filter(s)
// ============================================================

/// <summary>
/// Rebuilds the bloom filter for a single segment file.
/// Reads all hashes from the segment, constructs a fresh <see cref="PackIndexBloomFilter"/>,
/// and atomically writes it to the paired <c>.bloom</c> path.
/// </summary>
static async Task RebuildBloomFilterForSegmentAsync(string segPath, string bloomPath)
{
    AnsiConsole.Clear();
    AnsiConsole.MarkupLine($"[bold yellow]Rebuild Bloom Filter[/]");
    AnsiConsole.MarkupLine($"  Segment: {Markup.Escape(Path.GetFileName(segPath))}");
    AnsiConsole.MarkupLine($"  Bloom:   {Markup.Escape(Path.GetFileName(bloomPath))}");
    AnsiConsole.WriteLine();

    if (!File.Exists(segPath))
    {
        AnsiConsole.MarkupLine("[red]Segment file not found.[/]");
        Pause();
        return;
    }

    List<(Hash32 Hash, IndexEntry Entry)> entries;
    try
    {
        using var seg = new SortedIndexSegment(segPath);
        entries = seg.ReadAllEntries();
    }
    catch (Exception ex)
    {
        AnsiConsole.MarkupLine($"[red]Failed to read segment: {Markup.Escape(ex.Message)}[/]");
        Pause();
        return;
    }

    if (entries.Count == 0)
    {
        AnsiConsole.MarkupLine("[yellow]Segment has 0 entries — no bloom filter needed.[/]");
        Pause();
        return;
    }

    await AnsiConsole.Status().Spinner(Spinner.Known.Dots)
        .StartAsync($"Building bloom filter for {entries.Count:N0} entries...", async _ =>
        {
            var bloom = new PackIndexBloomFilter(entries.Count);
            foreach (var (hash, _) in entries)
                bloom.Add(hash);

            var bytes = bloom.Serialize();
            await FileAtomicHelper.WriteAtomicAsync(bloomPath, bytes);
        });

    var fi = new FileInfo(bloomPath);
    AnsiConsole.MarkupLine($"[green]Bloom filter rebuilt:[/] {Path.GetFileName(bloomPath)}  {FormatBytes(fi.Length)}  ({entries.Count:N0} entries)");
    Pause();
}

/// <summary>
/// Rebuilds bloom filters for ALL segment files in a bucket that are missing
/// or where the user requests a forced rebuild.
/// </summary>
static async Task RebuildAllBloomFiltersInBucketAsync(string bucketDir, string catPrefix, string prefix)
{
    AnsiConsole.Clear();
    AnsiConsole.MarkupLine($"[bold yellow]Rebuild Bloom Filters[/]  {catPrefix}/{prefix}");
    AnsiConsole.WriteLine();

    if (!Directory.Exists(bucketDir))
    {
        AnsiConsole.MarkupLine("[yellow]Bucket directory does not exist.[/]");
        Pause();
        return;
    }

    var segFiles = Directory.EnumerateFiles(bucketDir, $"{catPrefix}{prefix}.seg-*.idx")
        .OrderBy(f => f).ToList();

    if (segFiles.Count == 0)
    {
        AnsiConsole.MarkupLine("[yellow]No segment files found in this bucket.[/]");
        Pause();
        return;
    }

    var forceAll = AnsiConsole.Confirm(
        $"Rebuild bloom filters for ALL {segFiles.Count} segment(s) (including existing ones)?",
        defaultValue: false);

    int rebuilt = 0, skipped = 0, failed = 0;

    foreach (var sf in segFiles)
    {
        var bloomPath = Path.ChangeExtension(sf, ".bloom");
        var name      = Path.GetFileName(sf);

        if (!forceAll && File.Exists(bloomPath))
        {
            AnsiConsole.MarkupLine($"  [grey]SKIP[/] {name} (bloom already exists)");
            skipped++;
            continue;
        }

        try
        {
            using var seg     = new SortedIndexSegment(sf);
            var       entries = seg.ReadAllEntries();

            if (entries.Count == 0)
            {
                AnsiConsole.MarkupLine($"  [grey]SKIP[/] {name} (0 entries)");
                skipped++;
                continue;
            }

            var bloom = new PackIndexBloomFilter(entries.Count);
            foreach (var (hash, _) in entries)
                bloom.Add(hash);

            await FileAtomicHelper.WriteAtomicAsync(bloomPath, bloom.Serialize());
            AnsiConsole.MarkupLine($"  [green]OK[/] {name} → {Path.GetFileName(bloomPath)}  ({entries.Count:N0} entries)");
            rebuilt++;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"  [red]FAIL[/] {name}: {Markup.Escape(ex.Message)}");
            failed++;
        }
    }

    AnsiConsole.WriteLine();
    AnsiConsole.MarkupLine($"Rebuilt: [green]{rebuilt}[/]  Skipped: [grey]{skipped}[/]  Failed: [red]{failed}[/]");
    Pause();
}

// ============================================================
// Log entry access (raw, read-only)
// ============================================================

static void DumpLogEntries(string logPath)
{
    AnsiConsole.Clear();
    AnsiConsole.MarkupLine($"[bold]Log file:[/] {Markup.Escape(logPath)}");
    AnsiConsole.WriteLine();

    const int MaxShow = 100;

    try
    {
        using var fs     = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024);
        using var reader = new BinaryReader(fs, System.Text.Encoding.UTF8, leaveOpen: true);

        AnsiConsole.MarkupLine($"File size: {FormatBytes(fs.Length)}");
        AnsiConsole.WriteLine();

        var table = new Table()
            .Border(TableBorder.Simple)
            .AddColumn(new TableColumn("#").RightAligned())
            .AddColumn("Hash (first 16 hex chars)")
            .AddColumn(new TableColumn("FileNo").RightAligned())
            .AddColumn(new TableColumn("Offset").RightAligned())
            .AddColumn(new TableColumn("Length").RightAligned());

        var count = 0;
        while (fs.Position < fs.Length)
        {
            var hashBytes = reader.ReadBytes(32);
            if (hashBytes.Length < 32) break;

            var fileNo = VarIntUtils.ReadVarInt<int>(reader);
            var offset = VarIntUtils.ReadVarInt<long>(reader);
            var length = VarIntUtils.ReadVarInt<int>(reader);

            if (count < MaxShow)
            {
                var hex = Convert.ToHexString(hashBytes, 0, 8).ToLowerInvariant();
                table.AddRow($"{count}", $"{hex}...", $"{fileNo}", $"0x{offset:X}", FormatBytes(length));
            }
            count++;
        }

        AnsiConsole.Write(table);
        if (count > MaxShow)
            AnsiConsole.MarkupLine($"[grey]... {count - MaxShow} more entries not shown (total: {count:N0})[/]");
        else
            AnsiConsole.MarkupLine($"Total entries: {count:N0}");
    }
    catch (Exception ex)
    {
        AnsiConsole.MarkupLine($"[red]Error reading log: {Markup.Escape(ex.Message)}[/]");
    }

    Pause();
}

static (int fileNo, long offset, int length)? SearchLog(string logPath, Hash32 target)
{
    if (!File.Exists(logPath)) return null;
    try
    {
        using var fs     = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024);
        using var reader = new BinaryReader(fs, System.Text.Encoding.UTF8, leaveOpen: true);

        while (fs.Position < fs.Length)
        {
            var hashBytes = reader.ReadBytes(32);
            if (hashBytes.Length < 32) break;

            var fileNo = VarIntUtils.ReadVarInt<int>(reader);
            var offset = VarIntUtils.ReadVarInt<long>(reader);
            var length = VarIntUtils.ReadVarInt<int>(reader);

            if (new Hash32(hashBytes) == target) return (fileNo, offset, length);
        }
    }
    catch { /* ignore */ }
    return null;
}

// ============================================================
// Raw blob read (for FileDef decode)
// ============================================================

static async Task<byte[]?> ReadRawBlobAsync(string bucketDir, string catPrefix, string prefix, Hash32 hash)
{
    if (!Directory.Exists(bucketDir)) return null;

    var logPath  = Path.Combine(bucketDir, $"{catPrefix}{prefix}.log");
    var logEntry = SearchLog(logPath, hash);

    IndexEntry? indexEntry = logEntry.HasValue
        ? new IndexEntry(logEntry.Value.fileNo, logEntry.Value.offset, logEntry.Value.length)
        : null;

    if (indexEntry is null)
    {
        foreach (var sf in Directory.EnumerateFiles(bucketDir, $"{catPrefix}{prefix}.seg-*.idx")
                     .OrderByDescending(f => f))
        {
            var bloomPath = Path.ChangeExtension(sf, ".bloom");
            if (File.Exists(bloomPath))
            {
                try
                {
                    var bd    = File.ReadAllBytes(bloomPath);
                    var bloom = PackIndexBloomFilter.Deserialize(bd);
                    if (!bloom.MightContain(hash)) continue;
                }
                catch { /* proceed to segment */ }
            }

            try
            {
                await using var sfs = new FileStream(sf, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete, 64 * 1024,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                var segData = new byte[sfs.Length];
                await sfs.ReadExactlyAsync(segData);

                var ec            = (int)BinaryPrimitives.ReadUInt32LittleEndian(segData.AsSpan(4, 4));
                var (found, idx)  = BinarySearch(segData, ec, hash);
                if (!found) continue;

                const int H = 8, E = 48;
                var o      = (int)(H + (long)idx * E);
                indexEntry = new IndexEntry(
                    (int)BinaryPrimitives.ReadUInt32LittleEndian(segData.AsSpan(o + 32, 4)),
                    (long)BinaryPrimitives.ReadUInt64LittleEndian(segData.AsSpan(o + 36, 8)),
                    (int)BinaryPrimitives.ReadUInt32LittleEndian(segData.AsSpan(o + 44, 4)));
                break;
            }
            catch { /* skip corrupt segment */ }
        }
    }

    if (indexEntry is null) return null;

    var packPath = Path.Combine(bucketDir, $"{catPrefix}{prefix}-{indexEntry.Value.FileNo}.pack");
    if (!File.Exists(packPath)) return null;

    try
    {
        await using var pfs = new FileStream(packPath, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite, 128 * 1024, FileOptions.Asynchronous | FileOptions.RandomAccess);

        using var sfh = pfs.SafeFileHandle;

        const int  HeaderSize = 21;
        const uint Magic      = 0x4B505342;
        var        headerBuf  = new byte[HeaderSize];
        var        headerOff  = indexEntry.Value.Offset;

        var totalRead = 0;
        while (totalRead < HeaderSize)
        {
            var r = await RandomAccess.ReadAsync(sfh, headerBuf.AsMemory(totalRead), headerOff + totalRead);
            if (r == 0) return null;
            totalRead += r;
        }

        var magic = BinaryPrimitives.ReadUInt32LittleEndian(headerBuf.AsSpan(0, 4));
        if (magic != Magic) return null;

        var uncompressedLen = (int)BinaryPrimitives.ReadUInt32LittleEndian(headerBuf.AsSpan(5, 4));
        var compressedLen   = (int)BinaryPrimitives.ReadUInt32LittleEndian(headerBuf.AsSpan(9, 4));
        var expectedChksum  = BinaryPrimitives.ReadUInt64LittleEndian(headerBuf.AsSpan(13, 8));

        var compressed = new byte[compressedLen];
        totalRead = 0;
        while (totalRead < compressedLen)
        {
            var r = await RandomAccess.ReadAsync(sfh, compressed.AsMemory(totalRead), headerOff + HeaderSize + totalRead);
            if (r == 0) return null;
            totalRead += r;
        }

        if (XxHash3.HashToUInt64(compressed) != expectedChksum) return null;

        using var decompressor = new ZstdNet.Decompressor();
        return decompressor.Unwrap(compressed, uncompressedLen);
    }
    catch { return null; }
}

// ============================================================
// Helpers
// ============================================================

static int CountLogEntries(string logPath)
{
    if (!File.Exists(logPath)) return 0;
    try
    {
        using var fs     = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024);
        using var reader = new BinaryReader(fs, System.Text.Encoding.UTF8, leaveOpen: true);
        var count = 0;
        while (fs.Position < fs.Length)
        {
            var hashBytes = reader.ReadBytes(32);
            if (hashBytes.Length < 32) break;
            SkipVarInt(reader); SkipVarInt(reader); SkipVarInt(reader);
            count++;
        }
        return count;
    }
    catch { return 0; }
}

static void SkipVarInt(BinaryReader reader)
{
    for (var i = 0; i < 10; i++)
    {
        var b = reader.ReadByte();
        if ((b & 0x80) == 0) return;
    }
}

static string FormatBytes(long bytes) => bytes switch
{
    >= 1L << 30 => $"{bytes / (double)(1L << 30):F2} GiB",
    >= 1L << 20 => $"{bytes / (double)(1L << 20):F2} MiB",
    >= 1L << 10 => $"{bytes / (double)(1L << 10):F1} KiB",
    _           => $"{bytes} B"
};

static void RenderHeader(string storeRoot)
{
    AnsiConsole.MarkupLine("[bold blue]BinStash ChunkStore Explorer[/]  [grey](read-only)[/]");
    AnsiConsole.MarkupLine($"[grey]Store root:[/] {Markup.Escape(storeRoot)}");
    AnsiConsole.WriteLine();
}

static void Pause()
{
    AnsiConsole.MarkupLine("\n[grey]Press any key to continue...[/]");
    Console.ReadKey(intercept: true);
}

// IndexEntry is used from BinStash.Infrastructure.Storage.Indexing (accessible via InternalsVisibleTo).

// ============================================================
// Explorer — node model
// ============================================================

// Represents one level of the navigation tree.
// The explorer keeps a stack of ExplorerLevel; each level
// has a list of items and a cursor index.

record ExplorerItem(string Label, string Detail, object Tag);

// Tags (discriminated by type):
record StoreTag;
record CategoryTag(string CatName, string CatPrefix, string CatDir);
record PrefixGroup1Tag(string CatName, string CatPrefix, string CatDir, string Digit1);  // e.g. "5"
record PrefixGroup2Tag(string CatName, string CatPrefix, string CatDir, string Digits2); // e.g. "5c"
record BucketTag(string CatName, string CatPrefix, string CatDir, string Prefix);        // e.g. "5c3"
record PackFileTag(string Path, string BucketPrefix, string CatPrefix);
record SegFileTag(string Path, string BloomPath, string BucketPrefix, string CatDir, string CatPrefix);
record BloomFileTag(string Path, string SegPath, string BucketPrefix, string CatDir, string CatPrefix);
record LogFileTag(string Path, string BucketPrefix, string CatPrefix);
record ReleasesTag(string ReleasesDir);
record RdefBucketTag(string Prefix, string BucketDir);
record RdefFileTag(string Path, string Prefix);
// Deserialized release loaded once and passed down the stack so we don't re-read the file.
record RdefArtifactListTag(string RdefPath, ReleasePackage Package);
record RdefArtifactTag(string RdefPath, ReleasePackage Package, int ArtifactIndex);
record RdefContainerMemberTag(string ArtifactPath, ContainerMemberBinding Member, int MemberIndex);
// Virtual folder node within an rdef artifact tree.
// FolderPath is "" for root, "Engine/" for a top-level folder, "Engine/Binaries/" for nested.
// Artifacts contains all artifacts whose paths are rooted inside this folder.
record RdefFolderTag(string RdefPath, string RdefPrefix, ReleasePackage Package, string FolderPath, List<OutputArtifact> Artifacts);
// Timing record for one lookup step.
record LookupStep(string Label, bool Found, string Detail, TimeSpan Elapsed);

class ExplorerLevel
{
    public string Title        { get; init; } = "";
    public List<ExplorerItem> Items { get; init; } = new();
    public int Cursor          { get; set;  } = 0;

    public ExplorerItem? Selected =>
        Items.Count > 0 && Cursor >= 0 && Cursor < Items.Count
            ? Items[Cursor]
            : null;
}
