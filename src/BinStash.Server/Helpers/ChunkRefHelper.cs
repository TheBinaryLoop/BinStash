using BinStash.Contracts.Release;

namespace BinStash.Server.Helpers;

public static class ChunkRefHelper
{
    public static List<ChunkRef> ConvertDeltaToChunkRefs(List<DeltaChunkRef> deltaRefs)
    {
        var refs = new List<ChunkRef>();
        var currentIndex = 0u;

        foreach (var delta in deltaRefs)
        {
            currentIndex += delta.DeltaIndex;
            refs.Add(new ChunkRef
            {
                Index = Convert.ToInt32(currentIndex),
                Offset = Convert.ToInt64(delta.Offset),
                Length = Convert.ToInt32(delta.Length)
            });
        }

        return refs.OrderBy(x => x.Offset).ToList();
    }

}