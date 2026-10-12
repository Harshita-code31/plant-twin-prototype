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
    /// A stream of bits for encoding the QR code payload.
    /// </summary>
    internal class BitStream
    {
        private readonly byte[] _codewords;
        private readonly int _capacity;

        internal BitStream(int capacity)
        {
            Length = 0;
            _capacity = capacity;
            _codewords = new byte[capacity];
        }
        
        internal int Length { get; private set; }

        internal void AppendBits(uint value, int length)
        {
            if (length <= 0 || length > 32)
            {
                throw new ArgumentOutOfRangeException(nameof(length), "length must be between 1 and 32");
            }

            if (length < 32 && value >> length != 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "value must be in the range 0 <= value < 2^length");
            }

            var newLength = Length + length;
            if (newLength > _capacity * 8)
            {
                throw new ArgumentOutOfRangeException(nameof(length), "adding the specified length exceeds the capacity");
            }

            var valueMask = 1U << (length - 1);
            for (var i = Length; i < newLength; i += 1)
            {
                if ((value & valueMask) != 0)
                {
                    var codewordMask = (byte)(1U << (7 - (i & 7)));
                    _codewords[i >> 3] |= codewordMask;
                }
                valueMask >>= 1;
            }

            Length = newLength;
        }
        
        internal uint ExtractBits(int index, int length)
        {
            if (length <= 0 || length > 32)
            {
                throw new ArgumentOutOfRangeException(nameof(length), "length must be between 1 and 32");
            }

            if (index < 0 || index + length > Length)
            {
                throw new ArgumentOutOfRangeException(nameof(index), "'index' out of range");
            }

            var result = 0u;
            var valueMask = 1u << (length - 1);
            for (var i = index; i < index + length; i += 1)
            {
                var codewordMask = (byte)(1U << (7 - (i & 7)));
                if ((_codewords[i >> 3] & codewordMask) != 0)
                {
                    result |= valueMask;
                }
                valueMask >>= 1;
            }

            return result;
        }

        internal byte[] GetCodewords()
        {
            var resultLength = (Length + 7) >> 3;
            var result = new byte[resultLength];
            Array.Copy(_codewords, 0, result, 0, resultLength);
            return result;
        }

        internal void CopyCodewords(byte[] codewords, int index)
        {
            var resultLength = (Length + 7) >> 3;
            Array.Copy(_codewords, 0, codewords, index, resultLength);
        }
    }
}
