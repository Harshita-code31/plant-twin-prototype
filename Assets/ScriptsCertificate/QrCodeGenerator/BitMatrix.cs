/*
 * QR code generator library (.NET)
 *
 * Copyright (c) Manuel Bleichenbacher (MIT License)
 * https://github.com/manuelbl/QrCodeGenerator
 */

using System;
using System.Diagnostics.CodeAnalysis;

namespace Net.Codecrete.QrCodeGenerator
{
    /// <summary>
    /// Square matrix of binary pixels.
    /// </summary>
    internal readonly struct BitMatrix
    {
        internal int Size => Raw.Length >> 2;

        internal BitMatrix(int size)
        {
            Raw = new ulong[4 * size];
        }

        private BitMatrix(ulong[] bits)
        {
            Raw = bits;
        }

        internal static BitMatrix FromBits(ulong[] bits)
        {
            if (bits.Length % 4 != 0)
            {
                throw new ArgumentException("The bits array must have 4 elements for each row", nameof(bits));
            }
            
            return new BitMatrix(bits);
        }

        internal bool Get(int x, int y)
        {
            var bitMask = 1ul << (x & 0x3f);
            var index = 4 * y + (x >> 6);
            return (Raw[index] & bitMask) != 0;
        }

        internal void Set(int x, int y, bool bit)
        {
            var bitMask = 1ul << (x & 0x3f);
            var index = 4 * y + (x >> 6);
            if (bit)
            {
                Raw[index] |= bitMask;
            }
            else
            {
                Raw[index] &= ~bitMask;
            }
        }

        internal void FillRect(int x, int y, int width, int height)
        {
            if (width <= 0 || height <= 0)
            {
                return;
            }

            var startWord = x >> 6;
            var endX = x + width - 1;
            var endWord = endX >> 6;
            var startBit = x & 0x3f;
            var endBit = endX & 0x3f;

            var startMask = ulong.MaxValue << startBit;
            var endMask = ulong.MaxValue >> (63 - endBit);

            var rowBase = 4 * y;
            var rowEnd = rowBase + 4 * height;

            if (startWord == endWord)
            {
                var mask = startMask & endMask;
                for (var idx = rowBase + startWord; idx < rowEnd; idx += 4)
                {
                    Raw[idx] |= mask;
                }
            }
            else
            {
                for (var row = rowBase; row < rowEnd; row += 4)
                {
                    Raw[row + startWord] |= startMask;
                    for (var w = startWord + 1; w < endWord; w += 1)
                    {
                        Raw[row + w] = ulong.MaxValue;
                    }
                    Raw[row + endWord] |= endMask;
                }
            }
        }

        internal void Invert()
        {
            var size = Size;
            var lastBit = size - 1;
            var lastWord = lastBit >> 6;
            var lastMask = ulong.MaxValue >> (63 - (lastBit & 0x3F));

            for (var y = 0; y < size; y += 1)
            {
                var rowBase = 4 * y;
                for (var w = 0; w < lastWord; w += 1)
                {
                    Raw[rowBase + w] = ~Raw[rowBase + w];
                }
                Raw[rowBase + lastWord] = ~Raw[rowBase + lastWord] & lastMask;
                for (var w = lastWord + 1; w < 4; w += 1)
                {
                    Raw[rowBase + w] = 0;
                }
            }
        }

        [SuppressMessage("csharpsquid", "S2234")]
        internal void Transpose()
        {
            var size = Size;
            if (size <= 1)
            {
                return;
            }

            var nBlocks = (size + 63) >> 6;
            var blockA = new ulong[64];
            var blockB = new ulong[64];

            for (var br = 0; br < nBlocks; br += 1)
            {
                GatherBlock(Raw, blockA, br, br, size);
                Transpose64X64(blockA);
                ScatterBlock(Raw, blockA, br, br, size);

                for (var bc = br + 1; bc < nBlocks; bc += 1)
                {
                    GatherBlock(Raw, blockA, br, bc, size);
                    GatherBlock(Raw, blockB, bc, br, size);
                    Transpose64X64(blockA);
                    Transpose64X64(blockB);
                    ScatterBlock(Raw, blockA, bc, br, size);
                    ScatterBlock(Raw, blockB, br, bc, size);
                }
            }
        }

        private static void GatherBlock(ulong[] bits, ulong[] dest, int br, int bc, int size)
        {
            var rowStart = br << 6;
            var rows = Math.Min(size - rowStart, 64);
            for (var i = 0; i < rows; i += 1)
            {
                dest[i] = bits[((rowStart + i) << 2) + bc];
            }
            for (var i = rows; i < 64; i += 1)
            {
                dest[i] = 0;
            }
        }

        private static void ScatterBlock(ulong[] bits, ulong[] src, int br, int bc, int size)
        {
            var rowStart = br << 6;
            var rows = Math.Min(size - rowStart, 64);
            for (var i = 0; i < rows; i += 1)
            {
                bits[((rowStart + i) << 2) + bc] = src[i];
            }
        }

        private static void Transpose64X64(ulong[] a)
        {
            DeltaSwap(a, 32, 0x00000000FFFFFFFFul);
            DeltaSwap(a, 16, 0x0000FFFF0000FFFFul);
            DeltaSwap(a,  8, 0x00FF00FF00FF00FFul);
            DeltaSwap(a,  4, 0x0F0F0F0F0F0F0F0Ful);
            DeltaSwap(a,  2, 0x3333333333333333ul);
            DeltaSwap(a,  1, 0x5555555555555555ul);
        }

        private static void DeltaSwap(ulong[] a, int j, ulong m)
        {
            for (var k = 0; k < 64; k = (k + j + 1) & ~j)
            {
                var t = ((a[k] >> j) ^ a[k + j]) & m;
                a[k + j] ^= t;
                a[k] ^= t << j;
            }
        }

        internal void And(BitMatrix other)
        {
            if (other.Raw.Length != Raw.Length)
            {
                throw new ArgumentException("The matrices must have the same size", nameof(other));
            }

            for (var i = 0; i < Raw.Length; i += 1)
            {
                Raw[i] &= other.Raw[i];
            }
        }

        internal void Xor(BitMatrix other)
        {
            if (other.Raw.Length != Raw.Length)
            {
                throw new ArgumentException("The matrices must have the same size", nameof(other));
            }

            for (var i = 0; i < Raw.Length; i += 1)
            {
                Raw[i] ^= other.Raw[i];
            }
        }

        internal BitMatrix Copy()
        {
            var copy = new BitMatrix(Size);
            Array.Copy(Raw, copy.Raw, Raw.Length);
            return copy;
        }

        internal ulong[] Raw { get; }

        internal bool[,] ToBoolArray()
        {
            var array = new bool[Size, Size];
            for (var y = 0; y < Size; y += 1)
            {
                for (var x = 0; x < Size; x += 1)
                {
                    array[y, x] = Get(x, y);
                }
            }
            
            return array;
        }

        internal int PopCount()
        {
            var sum = 0;
            foreach (var v in Raw)
            {
                sum += PopCount(v);
            }
            return sum;
        }
        
        internal static int PopCount(ulong i)
        {
            i = i - ((i >> 1) & 0x5555555555555555UL);
            i = (i & 0x3333333333333333UL) + ((i >> 2) & 0x3333333333333333UL);
            return (int)(unchecked(((i + (i >> 4)) & 0xF0F0F0F0F0F0F0FUL) * 0x101010101010101UL) >> 56);
        }
    }
}
