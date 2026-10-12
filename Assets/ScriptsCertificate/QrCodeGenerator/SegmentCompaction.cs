/*
 * QR code generator library (.NET)
 *
 * Copyright (c) Manuel Bleichenbacher (MIT License)
 * https://github.com/manuelbl/QrCodeGenerator
 */

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Net.Codecrete.QrCodeGenerator
{
    /// <summary>
    /// Functions for building a list of segments with the shortest bit stream.
    /// </summary>
    internal static class SegmentCompaction
    {
        #region Optimal Segments

        internal static List<DataSegment> BuildSegments(ArraySegment<byte> bytes, int version = 20,
            bool considerKanjiMode = false)
        {
            var blocks = BuildBlocks(bytes, considerKanjiMode);

            MergeBlocks(blocks, version, DataSegmentMode.Alphanumeric,
                (mode0, mode1, mode2) => mode0 == DataSegmentMode.Alphanumeric
                                         && mode1 == DataSegmentMode.Numeric && mode2 == mode0,
                (mode0, mode1) => (mode0 == DataSegmentMode.Alphanumeric && mode1 == DataSegmentMode.Numeric)
                                  || (mode0 == DataSegmentMode.Numeric && mode1 == DataSegmentMode.Alphanumeric)
            );
            MergeBlocks(blocks, version, DataSegmentMode.Binary,
                (mode0, mode1, mode2) => mode1 != DataSegmentMode.Binary && mode2 == mode0,
                (mode0, mode1) => (mode0 == DataSegmentMode.Binary && mode1 != DataSegmentMode.Binary)
                                  || (mode0 != DataSegmentMode.Binary && mode1 == DataSegmentMode.Binary)
            );

            var offset = 0;
            return blocks.ConvertAll(block =>
            {
                var blockBytes = bytes.MakeSlice(offset, block.Length);
                offset += block.Length;
                return DataSegment.MakeSegment(block.Mode, blockBytes);
            });
        }

        [SuppressMessage("csharpsquid", "S3776")]
        private static void MergeBlocks(List<Block> blocks, int version, DataSegmentMode mergedMode,
            Func<DataSegmentMode, DataSegmentMode, DataSegmentMode, bool> merge3Condition,
            Func<DataSegmentMode, DataSegmentMode, bool> merge2Condition)
        {
            var previousCount = -1;
            while (blocks.Count > 1 && previousCount != blocks.Count)
            {
                previousCount = blocks.Count;

                var index = blocks.Count - 1;
                while (index > 0)
                {
                    var mode0 = blocks[index].Mode;
                    var mode1 = blocks[index - 1].Mode;
                    DataSegmentMode? mode2 = index >= 2 ? blocks[index - 2].Mode : (DataSegmentMode?) null;

                    if (mode2 != null && merge3Condition(mode0, mode1, mode2.Value))
                    {
                        var mergedPayloadLength =
                            blocks[index - 2].Length + blocks[index - 1].Length + blocks[index].Length;
                        var mergedBlock = new Block { Mode = mergedMode, Length = mergedPayloadLength };
                        var mergedLength = mergedBlock.GetSegmentLength(version);
                        var separateLength = blocks[index - 2].GetSegmentLength(version)
                                             + blocks[index - 1].GetSegmentLength(version)
                                             + blocks[index].GetSegmentLength(version);
                        if (mergedLength <= separateLength)
                        {
                            blocks[index - 2] = mergedBlock;
                            blocks.RemoveRange(index - 1, 2);
                            index -= 1;
                        }
                    }
                    else if (merge2Condition(mode0, mode1))
                    {
                        var mergedBlock = new Block
                            { Mode = mergedMode, Length = blocks[index - 1].Length + blocks[index].Length };
                        var mergedLength = mergedBlock.GetSegmentLength(version);
                        var separateLength = blocks[index - 1].GetSegmentLength(version) +
                                             blocks[index].GetSegmentLength(version);
                        if (mergedLength <= separateLength)
                        {
                            blocks[index - 1] = mergedBlock;
                            blocks.RemoveAt(index);
                        }
                    }

                    index -= 1;
                }
            }
        }

        private static List<Block> BuildBlocks(ArraySegment<byte> bytes, bool useKanji)
        {
            if (bytes.Count == 0)
            {
                return new List<Block>();
            }

            var modes = CalcCompactionMode(bytes, useKanji);

            var blockList = new List<Block>();
            var blockStartIndex = 0;
            var previousMode = modes[0];
            for (var i = 0; i < modes.Length; i += 1)
            {
                var currentMode = modes[i];
                if (currentMode == previousMode)
                {
                    continue;
                }

                blockList.Add(new Block { Mode = previousMode, Length = i - blockStartIndex });
                previousMode = currentMode;
                blockStartIndex = i;
            }

            blockList.Add(new Block { Mode = previousMode, Length = modes.Length - blockStartIndex });

            return blockList;
        }

        private static DataSegmentMode[] CalcCompactionMode(ArraySegment<byte> bytes, bool useKanji)
        {
            var len = bytes.Count;
            var modes = new DataSegmentMode[len];
            var index = 0;
            while (index < len)
            {
                var b1 = bytes.At(index);
                if (DataSegmentNumeric.IsNumeric(b1))
                {
                    modes[index] = DataSegmentMode.Numeric;
                }
                else if (DataSegmentAlphanumeric.IsAlphanumeric(b1))
                {
                    modes[index] = DataSegmentMode.Alphanumeric;
                }
                else if (useKanji && index < len - 1 && DataSegmentKanji.IsShiftJisDoubleByte(b1, bytes.At(index + 1)))
                {
                    modes[index] = DataSegmentMode.Kanji;
                    index += 1;
                    modes[index] = DataSegmentMode.Kanji;
                }
                else
                {
                    modes[index] = DataSegmentMode.Binary;
                }

                index += 1;
            }

            return modes;
        }

        #endregion

        #region Block

        private struct Block
        {
            internal DataSegmentMode Mode;

            internal int Length;

            internal int GetSegmentLength(int version)
            {
                return DataSegment.GetBitLength(Mode, Length, version);
            }

            public override string ToString()
            {
                return $"{Mode}: {Length} bytes";
            }
        }

        #endregion
    }
}
