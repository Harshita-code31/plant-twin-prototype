/*
 * QR code generator library (.NET)
 *
 * Copyright (c) Manuel Bleichenbacher (MIT License)
 * https://github.com/manuelbl/QrCodeGenerator
 */

using System;

namespace Net.Codecrete.QrCodeGenerator
{
    /// <summary>
    /// Calculates the penalty for a QR code to determine the optimal mask pattern.
    /// </summary>
    internal static class Penalty
    {
        internal static int Calculate(ScoringMatrix matrix, int lowestPenaltySoFar)
        {
            var sum = Calc2By2Blocks(matrix.Rows);
            if (sum >= lowestPenaltySoFar)
            {
                return sum;
            }
            sum += CalcSameColor(matrix.Columns);
            if (sum >= lowestPenaltySoFar)
            {
                return sum;
            }
            sum += CalcSameColor(matrix.Rows);
            if (sum >= lowestPenaltySoFar)
            {
                return sum;
            }
            sum += CalcFinderPattern(matrix.Rows);
            if (sum >= lowestPenaltySoFar)
            {
                return sum;
            }
            sum += CalcFinderPattern(matrix.Columns);
            if (sum >= lowestPenaltySoFar)
            {
                return sum;
            }
            return sum + CalcColorBalance(matrix.Rows);
        }

        internal static int CalculateFully(ScoringMatrix matrix, ref PenaltyScore penaltyInfo)
        {
            penaltyInfo.Blocks = Calc2By2Blocks(matrix.Rows);
            penaltyInfo.VerticalStreaks = CalcSameColor(matrix.Columns);
            penaltyInfo.HorizontalStreaks = CalcSameColor(matrix.Rows);
            penaltyInfo.HorizontalFinderPatterns = CalcFinderPattern(matrix.Rows);
            penaltyInfo.VerticalFinderPatterns = CalcFinderPattern(matrix.Columns);
            penaltyInfo.ColorBalance = CalcColorBalance(matrix.Rows);

            penaltyInfo.Total = penaltyInfo.Blocks
                + penaltyInfo.VerticalStreaks + penaltyInfo.HorizontalStreaks
                + penaltyInfo.HorizontalFinderPatterns + penaltyInfo.VerticalFinderPatterns
                + penaltyInfo.ColorBalance;
            return penaltyInfo.Total;
        }

        internal static int CalcSameColor(BitMatrix modules)
        {
            var raw = modules.Raw;
            var size = modules.Size;
            if (size < 5)
            {
                return 0;
            }

            var edgeMask = BuildEdgeMask(size - 4);
            var fiveWindowCount = 0;
            var run5StartCount = 0;

            for (var y = 0; y < size; y += 1)
            {
                var rowOffset = 4 * y;
                var w0 = raw[rowOffset];
                var w1 = raw[rowOffset + 1];
                var w2 = raw[rowOffset + 2];
                var w3 = raw[rowOffset + 3];

                var t0 = w0 ^ ((w0 >> 1) | (w1 << 63));
                var t1 = w1 ^ ((w1 >> 1) | (w2 << 63));
                var t2 = w2 ^ ((w2 >> 1) | (w3 << 63));
                var t3 = w3 ^ (w3 >> 1);

                var tz0 = ~(t0 | ((t0 >> 1) | (t1 << 63)));
                var tz1 = ~(t1 | ((t1 >> 1) | (t2 << 63)));
                var tz2 = ~(t2 | ((t2 >> 1) | (t3 << 63)));
                var tz3 = ~(t3 | (t3 >> 1));

                var fw0 = tz0 & ((tz0 >> 2) | (tz1 << 62)) & edgeMask[0];
                var fw1 = tz1 & ((tz1 >> 2) | (tz2 << 62)) & edgeMask[1];
                var fw2 = tz2 & ((tz2 >> 2) | (tz3 << 62)) & edgeMask[2];
                var fw3 = tz3 & (tz3 >> 2) & edgeMask[3];

                var rs0 = fw0 & ((t0 << 1) | 1ul);
                var rs1 = fw1 & ((t1 << 1) | (t0 >> 63));
                var rs2 = fw2 & ((t2 << 1) | (t1 >> 63));
                var rs3 = fw3 & ((t3 << 1) | (t2 >> 63));

                fiveWindowCount += BitMatrix.PopCount(fw0) + BitMatrix.PopCount(fw1)
                                 + BitMatrix.PopCount(fw2) + BitMatrix.PopCount(fw3);
                run5StartCount += BitMatrix.PopCount(rs0) + BitMatrix.PopCount(rs1)
                                + BitMatrix.PopCount(rs2) + BitMatrix.PopCount(rs3);
            }

            return fiveWindowCount + 2 * run5StartCount - 3 * (2 * 3 + 2 * 5 + 6);
        }

        internal static int Calc2By2Blocks(BitMatrix modules)
        {
            var raw = modules.Raw;
            var size = modules.Size;
            if (size < 2)
            {
                return 0;
            }

            var edgeMask = BuildEdgeMask(size - 1);

            var count = 0;
            for (var y = 0; y < size - 1; y += 1)
            {
                var aOffset = 4 * y;
                var bOffset = aOffset + 4;
                for (var w = 0; w < 4; w += 1)
                {
                    var a = raw[aOffset + w];
                    var b = raw[bOffset + w];
                    var aNext = w < 3 ? raw[aOffset + w + 1] : 0ul;
                    var bNext = w < 3 ? raw[bOffset + w + 1] : 0ul;
                    var aShift = (a >> 1) | (aNext << 63);
                    var bShift = (b >> 1) | (bNext << 63);
                    var monochrome = ~((a ^ aShift) | (b ^ bShift) | (a ^ b)) & edgeMask[w];
                    count += BitMatrix.PopCount(monochrome);
                }
            }

            return (count - 4 * 3) * 3;
        }

        private static ulong[] BuildEdgeMask(int validBits)
        {
            var validWord = validBits >> 6;
            var validBit = validBits & 0x3F;
            var partialMask = (1ul << validBit) - 1;
            var mask = new ulong[4];
            for (var w = 0; w < 4; w += 1)
            {
                if (w < validWord)
                {
                    mask[w] = ulong.MaxValue;
                }
                else if (w == validWord)
                {
                    mask[w] = partialMask;
                }
            }
            return mask;
        }

        internal static int CalcFinderPattern(BitMatrix modules)
        {
            var raw = modules.Raw;
            var size = modules.Size;
            var count = 0;

            const ulong patternBits = 0x5D0;
            const ulong patternMask1 = 0x0FFF;
            const ulong patternMask2 = 0x7FF8;

            for (var y = 0; y < size; y += 1)
            {
                var rowOffset = 4 * y;
                var window = (raw[rowOffset] & 0x3ff) << 5;
                for (var c = 10; c < size + 4; c += 1)
                {
                    var bit = (raw[rowOffset + (c >> 6)] >> (c & 0x3F)) & 1ul;
                    window = (window >> 1) | (bit << 14);
                    if ((window & patternMask1) == patternBits || (window & patternMask2) == patternBits)
                    {
                        count += 1;
                    }
                }
            }

            return (count - 9) * 40;
        }

        internal static int CalcColorBalance(BitMatrix modules)
        {
            var darkModules = modules.PopCount();

            var size = modules.Size;
            var totalNumber = size * size;
            var deviationSteps = Math.Abs(2 * darkModules - totalNumber) * 10 / totalNumber;
            return 10 * deviationSteps;
        }

    }
}
