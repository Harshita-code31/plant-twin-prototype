/*
 * QR code generator library (.NET)
 *
 * Copyright (c) Manuel Bleichenbacher (MIT License)
 * https://github.com/manuelbl/QrCodeGenerator
 */

using System.Collections.Concurrent;

namespace Net.Codecrete.QrCodeGenerator
{
    /// <summary>
    /// Single source of truth for the fixed-pattern geometry of a QR code version.
    /// </summary>
    internal static class FixedPatterns
    {
        #region Caches

        private static readonly ConcurrentDictionary<int, (BitMatrix Drawn, BitMatrix PayloadAreaMap)> Cache
            = new ConcurrentDictionary<int, (BitMatrix, BitMatrix)>();

        private static (BitMatrix Drawn, BitMatrix PayloadAreaMap) GetCached(int version)
        {
            return Cache.GetOrAdd(version, ComputeCached);
        }

        private static (BitMatrix Drawn, BitMatrix PayloadAreaMap) ComputeCached(int version)
        {
            var (drawn, reserved) = BuildFixedPatterns(version);
            reserved.Invert();
            return (drawn, reserved);
        }

        #endregion

        #region Accessors

        internal static BitMatrix CreateWithFixedPatterns(int version)
        {
            return GetCached(version).Drawn.Copy();
        }

        internal static BitMatrix GetPayloadAreaMap(int version)
        {
            return GetCached(version).PayloadAreaMap;
        }

        #endregion

        #region Single walk

        internal static (BitMatrix Drawn, BitMatrix Reserved) BuildFixedPatterns(int version)
        {
            var size = QrCodeParameters.GetSize(version);
            var drawn = new BitMatrix(size);
            var reserved = new BitMatrix(size);

            drawn.Set(8, size - 8, true);
            reserved.Set(8, size - 8, true);

            ReserveFormatInformation(reserved);

            DrawVersionInformation(drawn, version);
            ReserveVersionInformation(reserved, version);

            DrawFinderPattern(drawn, 0, 0);
            DrawFinderPattern(drawn, size - 7, 0);
            DrawFinderPattern(drawn, 0, size - 7);
            reserved.FillRect(0, 0, 8, 8);
            reserved.FillRect(size - 8, 0, 8, 8);
            reserved.FillRect(0, size - 8, 8, 8);

            DrawTimingPattern(drawn);
            reserved.FillRect(8, 6, size - 16, 1);
            reserved.FillRect(6, 8, 1, size - 16);

            DrawAndReserveAlignmentPatterns(drawn, reserved, version);

            return (drawn, reserved);
        }

        #endregion

        #region Finder patterns

        private static void DrawFinderPattern(BitMatrix modules, int x, int y)
        {
            for (var i = 0; i < 7; i += 1)
            {
                modules.Set(x + i, y,     true);
                modules.Set(x + i, y + 6, true);
            }

            for (var i = 1; i < 6; i += 1)
            {
                modules.Set(x,     y + i, true);
                modules.Set(x + 1, y + i, false);
                modules.Set(x + 5, y + i, false);
                modules.Set(x + 6, y + i, true);
            }

            for (var i = 2; i < 5; i += 1)
            {
                modules.Set(x + i, y + 1, false);
                modules.Set(x + i, y + 5, false);
            }

            for (var i = 2; i < 5; i += 1)
            {
                modules.Set(x + i, y + 2, true);
                modules.Set(x + i, y + 3, true);
                modules.Set(x + i, y + 4, true);
            }
        }

        #endregion

        #region Alignment patterns

        private static void DrawAndReserveAlignmentPatterns(BitMatrix drawn, BitMatrix reserved, int version)
        {
            if (version == 1)
            {
                return;
            }

            var positions = QrCodeParameters.GetAlignmentPatternPosition(version);
            var numPositions = positions.Length;

            for (var x = 0; x < numPositions; x += 1)
            {
                for (var y = 0; y < numPositions; y += 1)
                {
                    if ((x == 0 && y == 0) || (x == numPositions - 1 && y == 0) || (x == 0 && y == numPositions - 1))
                    {
                        continue;
                    }

                    reserved.FillRect(positions[x] - 2, positions[y] - 2, 5, 5);
                    DrawAlignmentPattern(drawn, positions[x], positions[y]);
                }
            }
        }

        private static void DrawAlignmentPattern(BitMatrix modules, int x, int y)
        {
            for (var i = -2; i <= 2; i += 1)
            {
                modules.Set(x + i, y - 2, true);
                modules.Set(x + i, y + 2, true);
            }

            for (var i = -1; i <= 1; i += 1)
            {
                modules.Set(x - 2, y + i, true);
                modules.Set(x + 2, y + i, true);
            }

            modules.Set(x, y, true);
        }

        #endregion

        #region Timing patterns

        private static void DrawTimingPattern(BitMatrix modules)
        {
            var size = modules.Size;
            for (var x = 8; x < size - 8; x += 1)
            {
                var isDark = ((x + 1) & 1) != 0;
                modules.Set(x, 6, isDark);
                modules.Set(6, x, isDark);
            }
        }

        #endregion

        #region Version information

        private static void ReserveVersionInformation(BitMatrix reserved, int version)
        {
            if (version < 7)
            {
                return;
            }

            var size = reserved.Size;
            reserved.FillRect(0, size - 11, 6, 3);
            reserved.FillRect(size - 11, 0, 3, 6);
        }

        private static void DrawVersionInformation(BitMatrix modules, int version)
        {
            if (version < 7)
            {
                return;
            }

            var size = modules.Size;
            var bits = QrCodeParameters.GetVersionInformationBits(version);

            for (var bit = 0; bit < 18; bit += 1)
            {
                var isDark = (bits & (1 << bit)) != 0;
                var x = bit / 3;
                var y = bit % 3;

                modules.Set(x, size - 11 + y, isDark);
                modules.Set(size - 11 + y, x, isDark);
            }
        }

        #endregion

        #region Format information

        private static void ReserveFormatInformation(BitMatrix reserved)
        {
            reserved.FillRect(8, 0, 1, 9);
            reserved.FillRect(0, 8, 8, 1);
            reserved.FillRect(reserved.Size - 8, 8, 8, 1);
            reserved.FillRect(8, reserved.Size - 7, 1, 7);
        }

        #endregion
    }
}
