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
    /// The per-mode rules of a <see cref="DataSegmentMode"/>.
    /// </summary>
    internal sealed class DataSegmentModeInfo
    {
        private DataSegmentModeInfo(
            int modeIndicator,
            int[] countIndicatorWidths,
            Func<int, int> encodedBitLength = null,
            Func<int, int> byteCount = null,
            Func<ArraySegment<byte>, DataSegment> create = null)
        {
            ModeIndicator = modeIndicator;
            _countIndicatorWidths = countIndicatorWidths;
            EncodedBitLength = encodedBitLength;
            ByteCount = byteCount;
            Create = create;
        }

        internal int ModeIndicator { get; }

        private readonly int[] _countIndicatorWidths;

        internal Func<int, int> EncodedBitLength { get; }

        internal Func<int, int> ByteCount { get; }

        internal Func<ArraySegment<byte>, DataSegment> Create { get; }

        internal bool HasCountIndicator => _countIndicatorWidths != null;

        internal int GetCountIndicatorLength(int version)
        {
            return HasCountIndicator ? _countIndicatorWidths[(version + 7) / 17] : 0;
        }

        internal int GetHeaderLength(int version)
        {
            return 4 + GetCountIndicatorLength(version);
        }

        internal static DataSegmentModeInfo For(DataSegmentMode mode)
        {
            return ByMode[(int)mode];
        }

        private static readonly DataSegmentModeInfo[] ByMode = BuildTable();

        private static DataSegmentModeInfo[] BuildTable()
        {
            var table = new DataSegmentModeInfo[7];
            table[(int)DataSegmentMode.Numeric] = new DataSegmentModeInfo(
                1, new[] { 10, 12, 14 },
                DataSegmentNumeric.GetNumericBitLength, DataSegmentNumeric.GetNumericByteCount,
                bytes => new DataSegmentNumeric(bytes));
            table[(int)DataSegmentMode.Alphanumeric] = new DataSegmentModeInfo(
                2, new[] { 9, 11, 13 },
                DataSegmentAlphanumeric.GetAlphanumericBitLength, DataSegmentAlphanumeric.GetAlphanumericByteCount,
                bytes => new DataSegmentAlphanumeric(bytes));
            table[(int)DataSegmentMode.Kanji] = new DataSegmentModeInfo(
                8, new[] { 8, 10, 12 },
                DataSegmentKanji.GetKanjiBitLength, DataSegmentKanji.GetKanjiByteCount,
                bytes => new DataSegmentKanji(bytes));
            table[(int)DataSegmentMode.Binary] = new DataSegmentModeInfo(
                4, new[] { 8, 16, 16 },
                DataSegmentByte.GetByteBitLength, DataSegmentByte.GetByteByteCount,
                bytes => new DataSegmentByte(bytes));
            table[(int)DataSegmentMode.ECI] = new DataSegmentModeInfo(
                7, null);
            table[(int)DataSegmentMode.StructuredAppend] = new DataSegmentModeInfo(
                3, null);
            return table;
        }
    }
}
