// Quick seg-file diagnostic tool
// Usage: SegDump <segFilePath> <targetHashHex>

if (args.Length < 2)
{
    Console.Error.WriteLine("Usage: SegDump <segFilePath> <targetHashHex>");
    return 1;
}

var segPath    = args[0];
var targetHex  = args[1].ToLowerInvariant();
var targetBytes = Convert.FromHexString(targetHex);

var bytes = File.ReadAllBytes(segPath);
var magic = BitConverter.ToUInt32(bytes, 0);
var count = (int)BitConverter.ToUInt32(bytes, 4);

Console.WriteLine($"File       : {segPath}");
Console.WriteLine($"Magic      : 0x{magic:X8} (expected 0x58324449 = 'IDX2')");
Console.WriteLine($"EntryCount : {count}");
Console.WriteLine($"FileSize   : {bytes.Length} bytes");
Console.WriteLine($"Expected   : {8 + (long)count * 48} bytes");
Console.WriteLine($"Target     : {targetHex}");
Console.WriteLine();

if (magic != 0x58324449)
{
    Console.Error.WriteLine("ERROR: Wrong magic number!");
    return 2;
}

// Show first 10 entries raw bytes
int showCount = Math.Min(10, count);
Console.WriteLine($"First {showCount} entries (raw first 8 bytes of hash, then LE-uint64 interpretation):");
for (int i = 0; i < showCount; i++)
{
    int offset = 8 + i * 48;
    string rawHex = Convert.ToHexString(bytes, offset, 8).ToLowerInvariant();
    // Read as 4x LE uint64 (same as Hash32 constructor)
    ulong h0 = BitConverter.ToUInt64(bytes, offset);
    ulong h1 = BitConverter.ToUInt64(bytes, offset + 8);
    Console.WriteLine($"  [{i:D4}] raw={rawHex}... h0=0x{h0:X16} h1=0x{h1:X16}");
}

if (count > 10)
{
    Console.WriteLine($"  ... ({count - 10} more entries)");
    // Show last 5 too
    Console.WriteLine($"Last {Math.Min(5, count)} entries:");
    for (int i = Math.Max(0, count - 5); i < count; i++)
    {
        int offset = 8 + i * 48;
        string rawHex = Convert.ToHexString(bytes, offset, 8).ToLowerInvariant();
        ulong h0 = BitConverter.ToUInt64(bytes, offset);
        Console.WriteLine($"  [{i:D4}] raw={rawHex}... h0=0x{h0:X16}");
    }
}

Console.WriteLine();

// Linear search for target
bool found = false;
for (int i = 0; i < count; i++)
{
    int offset = 8 + i * 48;
    bool match = true;
    for (int j = 0; j < 32; j++)
    {
        if (bytes[offset + j] != targetBytes[j]) { match = false; break; }
    }
    if (match)
    {
        int fileNo  = (int)BitConverter.ToUInt32(bytes, offset + 32);
        long dataOff = BitConverter.ToInt64(bytes, offset + 36);
        int length  = (int)BitConverter.ToUInt32(bytes, offset + 44);
        Console.WriteLine($"FOUND target at entry {i}  (fileNo={fileNo}, offset={dataOff}, length={length})");
        found = true;
        break;
    }
}

if (!found)
    Console.WriteLine("Target NOT found by linear search.");

Console.WriteLine();

// Now simulate the binary search using the same comparison as Hash32.CompareTo
// Hash32 constructor: h0 = LE uint64 bytes[0..8], h1 = LE uint64 bytes[8..16], etc.
// CompareTo: compare h0 first, then h1, h2, h3
ulong targetH0 = BitConverter.ToUInt64(targetBytes, 0);
ulong targetH1 = BitConverter.ToUInt64(targetBytes, 8);
ulong targetH2 = BitConverter.ToUInt64(targetBytes, 16);
ulong targetH3 = BitConverter.ToUInt64(targetBytes, 24);

Console.WriteLine($"Target LE-uint64: h0=0x{targetH0:X16} h1=0x{targetH1:X16} h2=0x{targetH2:X16} h3=0x{targetH3:X16}");
Console.WriteLine();

// Verify sort order: are entries sorted by (h0, h1, h2, h3) ascending?
bool isSorted = true;
int firstOutOfOrder = -1;
ulong prevH0 = 0, prevH1 = 0, prevH2 = 0, prevH3 = 0;
for (int i = 0; i < count; i++)
{
    int offset = 8 + i * 48;
    ulong h0 = BitConverter.ToUInt64(bytes, offset);
    ulong h1 = BitConverter.ToUInt64(bytes, offset + 8);
    ulong h2 = BitConverter.ToUInt64(bytes, offset + 16);
    ulong h3 = BitConverter.ToUInt64(bytes, offset + 24);

    if (i > 0)
    {
        int cmp = h0.CompareTo(prevH0);
        if (cmp == 0) cmp = h1.CompareTo(prevH1);
        if (cmp == 0) cmp = h2.CompareTo(prevH2);
        if (cmp == 0) cmp = h3.CompareTo(prevH3);
        if (cmp < 0)
        {
            isSorted = false;
            firstOutOfOrder = i;
            Console.WriteLine($"SORT VIOLATION at entry {i}: current h0=0x{h0:X16} < prev h0=0x{prevH0:X16}");
            if (firstOutOfOrder > 5) break; // stop after first few violations
        }
    }
    prevH0 = h0; prevH1 = h1; prevH2 = h2; prevH3 = h3;
}

if (isSorted)
    Console.WriteLine("Sort order: CORRECT (ascending by LE-uint64 h0,h1,h2,h3)");
else
    Console.WriteLine("Sort order: INCORRECT — binary search will fail!");

Console.WriteLine();

// Simulate binary search
Console.WriteLine("Simulating binary search:");
int lo = 0, hi = count - 1;
int steps = 0;
bool bsFound = false;
while (lo <= hi)
{
    int mid = (int)(((uint)lo + (uint)hi) >> 1);
    int offset = 8 + mid * 48;
    ulong cH0 = BitConverter.ToUInt64(bytes, offset);
    ulong cH1 = BitConverter.ToUInt64(bytes, offset + 8);
    ulong cH2 = BitConverter.ToUInt64(bytes, offset + 16);
    ulong cH3 = BitConverter.ToUInt64(bytes, offset + 24);

    int cmp = targetH0.CompareTo(cH0);
    if (cmp == 0) cmp = targetH1.CompareTo(cH1);
    if (cmp == 0) cmp = targetH2.CompareTo(cH2);
    if (cmp == 0) cmp = targetH3.CompareTo(cH3);

    string rawHex = Convert.ToHexString(bytes, offset, 8).ToLowerInvariant();
    Console.WriteLine($"  step {steps}: mid={mid} (lo={lo},hi={hi}) cmp={cmp} candidate={rawHex}...");
    steps++;

    if (cmp == 0)      { bsFound = true; Console.WriteLine($"  BINARY SEARCH FOUND at entry {mid}!"); break; }
    else if (cmp < 0)  { hi = mid - 1; }
    else               { lo = mid + 1; }

    if (steps > 30) { Console.WriteLine("  (stopping at 30 steps)"); break; }
}

if (!bsFound) Console.WriteLine("  BINARY SEARCH: NOT FOUND");
return 0;
