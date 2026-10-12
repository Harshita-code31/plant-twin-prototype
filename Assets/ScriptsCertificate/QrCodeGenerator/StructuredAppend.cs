/*
 * QR code generator library (.NET)
 *
 * Copyright (c) Manuel Bleichenbacher (MIT License)
 * https://github.com/manuelbl/QrCodeGenerator
 */

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace Net.Codecrete.QrCodeGenerator
{
    internal static class StructuredAppend
    {
        private const int CompactionVersion = 20;

        internal static List<List<DataSegment>> BuildFromText(string text, int version, QrCode.Ecc ecl)
        {
            Objects.RequireNonNull(text, nameof(text));
            
            byte[] data;
            ECI eci;
            bool isUtf8;
            
            try
            {
                data = ECI.Latin1.GetEncoding().GetBytes(text);
                eci = ECI.None;
                isUtf8 = false;
            }
            catch (EncoderFallbackException)
            {
                data = Encoding.UTF8.GetBytes(text);
                eci = ECI.UTF8;
                isUtf8 = true;
            }

            return BuildSegments(data, version, ecl, eci, isUtf8);
        }
        
        internal static List<List<DataSegment>> BuildSegments(byte[] data, int version, QrCode.Ecc ecl, ECI eci, bool isUtf8)
        {
            Trace.Assert(!eci.Equals(ECI.Automatic));

            var qrCodes = Split(data, version, ecl, eci, isUtf8);
            AddStructuredAppendHeaders(qrCodes, data, eci);
            return qrCodes;
        }

        internal static (List<List<DataSegment>> QrCodes, int Version) BuildBalancedFromText(
            string text, int minVersion, int maxVersion, QrCode.Ecc ecl)
        {
            Objects.RequireNonNull(text, nameof(text));

            byte[] data;
            ECI eci;
            bool isUtf8;

            try
            {
                data = ECI.Latin1.GetEncoding().GetBytes(text);
                eci = ECI.None;
                isUtf8 = false;
            }
            catch (EncoderFallbackException)
            {
                data = Encoding.UTF8.GetBytes(text);
                eci = ECI.UTF8;
                isUtf8 = true;
            }

            return BuildBalancedSegments(data, minVersion, maxVersion, ecl, eci, isUtf8);
        }

        internal static (List<List<DataSegment>> QrCodes, int Version) BuildBalancedSegments(
            byte[] data, int minVersion, int maxVersion, QrCode.Ecc ecl, ECI eci, bool isUtf8)
        {
            Trace.Assert(!eci.Equals(ECI.Automatic));

            var segments = SegmentCompaction.BuildSegments(new ArraySegment<byte>(data), CompactionVersion);

            var maxCapacity = FullCapacity(maxVersion, ecl, eci);
            var numQrCodes = Split(segments, maxVersion, maxCapacity, isUtf8).Count;
            if (numQrCodes > 16 || !FitsInCodes(segments, maxVersion, maxCapacity, isUtf8, numQrCodes))
            {
                throw new DataTooLongException("The text is too long to fit into 16 QR codes");
            }

            var version = maxVersion;
            for (var v = minVersion; v <= maxVersion; v += 1)
            {
                if (FitsInCodes(segments, v, FullCapacity(v, ecl, eci), isUtf8, numQrCodes))
                {
                    version = v;
                    break;
                }
            }

            var lo = 1;
            var hi = FullCapacity(version, ecl, eci);
            while (lo < hi)
            {
                var mid = lo + (hi - lo) / 2;
                if (FitsInCodes(segments, version, mid, isUtf8, numQrCodes))
                {
                    hi = mid;
                }
                else
                {
                    lo = mid + 1;
                }
            }

            var qrCodes = Split(segments, version, lo, isUtf8);
            AddStructuredAppendHeaders(qrCodes, data, eci);
            return (qrCodes, version);
        }

        private static bool FitsInCodes(List<DataSegment> segments, int version, int cap, bool isUtf8, int maxCodes)
        {
            var qrCodes = Split(segments, version, cap, isUtf8);
            return qrCodes.Count <= maxCodes
                   && qrCodes.All(code => DataSegment.GetBitLength(code, version) <= cap);
        }

        private static void AddStructuredAppendHeaders(List<List<DataSegment>> qrCodes, byte[] data, ECI eci)
        {
            var parity = CalculateParity(data);
            var eciSegment = !Equals(eci, ECI.None) ? new DataSegmentEci(eci) : null;

            var numQrCodes = qrCodes.Count;
            for (var i = 0; i < numQrCodes; i += 1)
            {
                var segments = qrCodes[i];
                segments.Insert(0, new DataSegmentStructuredAppend(i + 1, numQrCodes, parity));
                if (eciSegment != null)
                {
                    segments.Insert(1, eciSegment);
                }
            }
        }

        private static int FullCapacity(int version, QrCode.Ecc ecl, ECI eci)
        {
            var structuredAppendLength = new DataSegmentStructuredAppend(1, 10, 0).GetTotalLength(40);
            var eciLength = !eci.Equals(ECI.None) ? new DataSegmentEci(eci).GetTotalLength(40) : 0;
            return QrCodeParameters.GetCodewordDataCapacity(version, (int)ecl) * 8
                   - structuredAppendLength - eciLength;
        }

        private static List<List<DataSegment>> Split(byte[] data, int version, QrCode.Ecc ecl, ECI eci, bool isUtf8)
        {
            var segments = SegmentCompaction.BuildSegments(new ArraySegment<byte>(data), version);
            var result = Split(segments, version, FullCapacity(version, ecl, eci), isUtf8);

            return result.Count <= 16
                ? result
                : throw new DataTooLongException("The text is too long to fit into 16 QR codes");
        }

        private static List<List<DataSegment>> Split(List<DataSegment> segments, int version, int dataCapacity, bool isUtf8)
        {
            var result = new List<List<DataSegment>>();

            var current = new List<DataSegment>();
            var bitLength = 0;

            foreach (var wholeSegment in segments)
            {
                var segment = wholeSegment;
                while (true)
                {
                    var segmentLength = segment.GetTotalLength(version);
                    if (bitLength + segmentLength <= dataCapacity)
                    {
                        current.Add(segment);
                        bitLength += segmentLength;
                        break;
                    }

                    var remainingBits = dataCapacity - bitLength;
                    var numBytes = DataSegment.GetByteCount(segment.Mode,
                        remainingBits - DataSegment.GetHeaderLength(segment.Mode, version));

                    if (isUtf8)
                    {
                        var dataBytes = segment.DataBytes;
                        while (numBytes > 0 && (dataBytes.At(numBytes) & 0xC0) == 0x80)
                        {
                            numBytes -= 1;
                        }
                    }

                    if (numBytes <= 0 && current.Count > 0)
                    {
                        result.Add(current);
                        current = new List<DataSegment>();
                        bitLength = 0;
                        continue;
                    }

                    if (numBytes <= 0)
                    {
                        numBytes = 1;
                    }

                    var splitSegments = SplitSegment(segment, numBytes);
                    current.Add(splitSegments.Item1);
                    result.Add(current);
                    current = new List<DataSegment>();
                    bitLength = 0;

                    segment = splitSegments.Item2;

                    if (segment.DataLength == 0)
                    {
                        break;
                    }
                }
            }

            result.Add(current);
            return result;
        }

        private static Tuple<DataSegment, DataSegment> SplitSegment(DataSegment segment, int numBytes)
        {
            return new Tuple<DataSegment, DataSegment>(
                DataSegment.MakeSegment(segment.Mode, segment.DataBytes.MakeSlice(0, numBytes)),
                DataSegment.MakeSegment(segment.Mode, segment.DataBytes.MakeSlice(numBytes, segment.DataLength - numBytes))
            );
        }
        
        private static byte CalculateParity(byte[] data)
        {
            return data.Aggregate<byte, byte>(0, (current, value) => (byte)(current ^ value));
        }

    }
}
