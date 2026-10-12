using System;
using System.Diagnostics;

namespace Net.Codecrete.QrCodeGenerator
{
    /// <summary>
    /// Extension members for <see cref="ArraySegment{T}"/> (kept for .NET Standard 2.0 compatibility).
    /// </summary>
    internal static class ArraySegmentExtensions
    {
        internal static T At<T>(this ArraySegment<T> segment, int index)
        {
            Trace.Assert(segment.Array != null);
            return segment.Array[segment.Offset + index];
        }

        internal static ArraySegment<T> MakeSlice<T>(this ArraySegment<T> segment, int startIndex, int length)
        {
            Trace.Assert(segment.Array != null);
            return new ArraySegment<T>(segment.Array, segment.Offset + startIndex, length);
        }
    }
}
