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
    /// Data segment for the structured append header.
    /// </summary>
    internal class DataSegmentStructuredAppend : DataSegment
    {
        private const int StructuredAppendBitLength = 16;
        
        public override int StructuredAppendPosition { get; }

        public override int StructuredAppendTotal { get; }

        public override byte StructuredAppendParity { get; }

        internal DataSegmentStructuredAppend(int position, int total, byte parity)
            : base(DataSegmentMode.StructuredAppend, StructuredAppendBitLength)
        {
            if (total < 1 || total > 16)
            {
                throw new ArgumentOutOfRangeException(nameof(total), total.ToString(), "total must be between 1 and 16");
            }
            if (position < 1 || position > 16)
            {
                throw new ArgumentOutOfRangeException(nameof(position), position.ToString(), "position must be between 1 and 16");
            }

            if (position > total)
            {
                throw new ArgumentOutOfRangeException(nameof(position), position.ToString(), "position must be less or equal to total");
            }

            StructuredAppendPosition = position;
            StructuredAppendTotal = total;
            StructuredAppendParity = parity;
        }
        
        internal override void WriteToBitStream(BitStream bitStream)
        {
            bitStream.AppendBits((uint)StructuredAppendPosition - 1, 4);
            bitStream.AppendBits((uint)StructuredAppendTotal - 1, 4);
            bitStream.AppendBits(StructuredAppendParity, 8);
        }
    }
}
