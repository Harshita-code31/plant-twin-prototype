/*
 * QR code generator library (.NET)
 *
 * Copyright (c) Manuel Bleichenbacher (MIT License)
 * https://github.com/manuelbl/QrCodeGenerator
 */

using System;
using System.Collections.Generic;

namespace Net.Codecrete.QrCodeGenerator
{
    /// <summary>
    /// Turns data segments into the codeword stream the matrix is filled with.
    /// </summary>
    internal static class Codewords
    {
        internal static byte[] BuildData(List<DataSegment> dataSegments, int version, int ecc)
        {
            var capacity = QrCodeParameters.GetCodewordDataCapacity(version, ecc);
            var bitStream = DataSegment.CreateBitStream(dataSegments, version, capacity);
            var bitstreamLength = bitStream.Length;

            var result = new byte[capacity];
            bitStream.CopyCodewords(result, 0);

            for (var index = (bitstreamLength + 7) / 8; index < capacity; index += 2)
            {
                result[index] = 0b1110_1100;
            }
            for (var index = (bitstreamLength + 15) / 8; index < capacity; index += 2)
            {
                result[index] = 0b0001_0001;
            }

            return result;
        }

        internal static byte[] AddErrorCorrection(byte[] codewords, int version, int ecc)
        {
            var numDataCodewords = codewords.Length;
            var numBlocks = QrCodeParameters.GetNumBlocks(version, ecc);
            var smallBlockDataLength = numDataCodewords / numBlocks;
            var eccBlockLength = (QrCodeParameters.GetCodewordCapacity(version) - numDataCodewords) / numBlocks;
            var numLargeBlocks = numDataCodewords % numBlocks;
            var numSmallBlocks = numBlocks - numLargeBlocks;

            var result = new byte[QrCodeParameters.GetCodewordCapacity(version)];
            var dataOffset = 0;
            var reedSolomon = ReedSolomon.GeneratorForCapacity(eccBlockLength);

            for (var block = 0; block < numBlocks; block += 1)
            {
                var dataLength = block < numSmallBlocks ? smallBlockDataLength : smallBlockDataLength + 1;

                var eccCodewords = reedSolomon.ComputeErrorCorrection(new ArraySegment<byte>(codewords, dataOffset, dataLength));

                for (var i = 0; i < smallBlockDataLength; i += 1)
                    result[i * numBlocks + block] = codewords[dataOffset + i];
                if (block >= numSmallBlocks)
                    result[numBlocks * smallBlockDataLength + block - numSmallBlocks] = codewords[dataOffset + dataLength - 1];
                for (var i = 0; i < eccBlockLength; i += 1)
                    result[numDataCodewords + i * numBlocks + block] = eccCodewords[i];

                dataOffset += dataLength;
            }

            return result;
        }
    }
}
