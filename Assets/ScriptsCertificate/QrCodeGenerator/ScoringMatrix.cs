/*
 * QR code generator library (.NET)
 *
 * Copyright (c) Manuel Bleichenbacher (MIT License)
 * https://github.com/manuelbl/QrCodeGenerator
 */

using System.Diagnostics.CodeAnalysis;

namespace Net.Codecrete.QrCodeGenerator
{
    /// <summary>
    /// A module matrix paired with its transpose, kept in sync, used while selecting the mask pattern.
    /// </summary>
    internal readonly struct ScoringMatrix
    {
        private ScoringMatrix(BitMatrix rows, BitMatrix columns)
        {
            Rows = rows;
            Columns = columns;
        }

        internal static ScoringMatrix From(BitMatrix modules)
        {
            var columns = modules.Copy();
            columns.Transpose();
            return new ScoringMatrix(modules, columns);
        }

        internal BitMatrix Rows { get; }

        internal BitMatrix Columns { get; }

        internal int Size => Rows.Size;

        internal void Xor(MaskPair mask)
        {
            Rows.Xor(mask.Rows);
            Columns.Xor(mask.Columns);
        }

        [SuppressMessage("csharpsquid", "S2234")]
        internal void SetFormatBit(int x, int y, bool value)
        {
            Rows.Set(x, y, value);
            Columns.Set(y, x, value);
        }

        internal BitMatrix Finish(MaskPair mask)
        {
            Rows.Xor(mask.Rows);
            return Rows;
        }
    }
}
