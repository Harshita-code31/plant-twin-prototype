/*
 * QR code generator library (.NET)
 *
 * Copyright (c) Manuel Bleichenbacher (MIT License)
 * https://github.com/manuelbl/QrCodeGenerator
 */

namespace Net.Codecrete.QrCodeGenerator
{
    /// <summary>
    /// The two views of a single mask pattern: the mask as stored and its transpose.
    /// </summary>
    internal readonly struct MaskPair
    {
        internal MaskPair(BitMatrix rows, BitMatrix columns)
        {
            Rows = rows;
            Columns = columns;
        }

        internal BitMatrix Rows { get; }

        internal BitMatrix Columns { get; }
    }
}
