using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace DLNAServer.Helpers.Database
{
    public static class MemoryExtensions
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static TResult[] AsArray<TResult>(this ReadOnlyMemory<TResult> memory)
        {
            if (MemoryMarshal.TryGetArray(memory, out ArraySegment<TResult> segment)
                && segment.Array is TResult[] array)
            {
                if (segment.Offset == 0 && segment.Count == segment.Array.Length)
                {
                    return array;
                }

                var result = new TResult[segment.Count];
                segment.AsSpan().CopyTo(result);
                return result;
            }
            return memory.IsEmpty ? Array.Empty<TResult>() : memory.ToArray();
        }
        public static TResult[] Sort<TResult>(this ReadOnlyMemory<TResult> memory, Comparison<TResult> comparison)
        {
            TResult[] array = memory.AsArray();
            Array.Sort(array, comparison);
            return array;
        }
    }
}
