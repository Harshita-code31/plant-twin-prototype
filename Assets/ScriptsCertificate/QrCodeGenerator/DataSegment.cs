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
    /// <summary>
    /// A chunk of payload data to be carried by the QR code.
    /// <para>
    /// The payload data carried by a QR code is a sequence of data segments.
    /// Each segment is a sequence of bytes encoded with a specific encoding
    /// mode: numeric, alphanumeric, Kanji, or binary.
    /// Additionally, a segment can contain an extended character set
    /// indicator (ECI) or a Structured Append header.
    /// </para>
    /// <para>
    /// Instances of this class represent such a data segment.
    /// They retain the data in the original unencoded form until
    /// the bit stream for the QR code is created.
    /// </para>
    /// </summary>
    public abstract class DataSegment
    {
        #region Properties
        
        /// <summary>The data segment mode.</summary>
        /// <value>The data segment mode.</value>
        public DataSegmentMode Mode { get; }

        /// <summary>The data bytes.</summary>
        /// <value>The data bytes (if any).</value>
        public ArraySegment<byte> DataBytes { get; protected set; }
        
        /// <summary>The data length, in bytes.</summary>
        /// <value>The data length, in bytes.</value>
        public int DataLength => DataBytes.Count;

        /// <summary>
        /// The value written into the character count indicator of this segment.
        /// <para>
        /// The indicator counts the characters the segment encodes. That is the number of data
        /// bytes for all modes but Kanji, where a character occupies two bytes.
        /// </para>
        /// </summary>
        internal virtual int CharacterCount => DataBytes.Count;

        /// <summary>
        /// The length of the encoded segment, in bits.
        /// <para>
        /// The length is without the header (mode indicator and count indicator).
        /// </para>
        /// </summary>
        public int EncodedLength { get; }
        
        /// <summary>
        /// Gets the encoded segment length, in bits.
        /// <para>
        /// The length includes the mode indicator, the count indicator, and the data bits.
        /// It depends on the QR code version.
        /// </para>
        /// </summary>
        /// <param name="version">The QR code version.</param>
        /// <returns>The segment length, in bits.</returns>
        public int GetTotalLength(int version) => GetHeaderLength(Mode, version) + EncodedLength;

        /// <summary>
        /// Gets the extended character set indicator (ECI).
        /// <para>
        /// This value is only valid if this segment uses the segment mode <see cref="DataSegmentMode.ECI"/>.
        /// </para>
        /// </summary>
        public virtual ECI EciDesignator => ECI.None;

        /// <summary>
        /// The position of the QR code within a structured append message.
        /// <para>
        /// Valid positions are between 1 and 16.
        /// The value is only valid if this segment uses the segment mode <see cref="DataSegmentMode.StructuredAppend"/>.
        /// </para>
        /// </summary>
        public virtual int StructuredAppendPosition => 0;

        /// <summary>
        /// The total number of QR codes used for the structured append message.
        /// <para>
        /// Valid numbers are between 1 and 16.
        /// The value is only valid if this segment uses the segment mode <see cref="DataSegmentMode.StructuredAppend"/>.
        /// </para>
        /// </summary>
        public virtual int StructuredAppendTotal => 0;

        /// <summary>
        /// The data parity in the structured append messages.
        /// <para>
        /// The value is only valid if this segment uses the segment mode <see cref="DataSegmentMode.StructuredAppend"/>.
        /// </para>
        /// </summary>
        public virtual byte StructuredAppendParity => 0;

        #endregion

        #region Factory methods

        /// <summary>
        /// Creates optimal data segments for the specified text string.
        /// </summary>
        /// <param name="text">The text to encode.</param>
        /// <param name="eci">The extended character set indicator segment to add.</param>
        /// <param name="encoding">The character set encoding to use, or <c>null</c> if it should be derived from the ECI.</param>
        /// <param name="version">The QR code version.</param>
        /// <param name="kanjiStrategy">Controls if Kanji mode is used for data segments.</param>
        /// <returns>List of QR data segments representing the text.</returns>
        public static List<DataSegment> FromText(
            string text,
            ECI eci = null,
            Encoding encoding = null,
            int version = 20,
            KanjiStrategy kanjiStrategy = KanjiStrategy.Automatic
        )
        {
            Objects.RequireNonNull(text, nameof(text));
            eci = eci ?? ECI.Automatic;
            byte[] data = null;
            
            switch (eci.Value)
            {
                case ECI.AutomaticValue:
                    // try ISO-8859-1 encoding
                    try
                    {
                        data = ECI.Latin1.GetEncoding().GetBytes(text);
                        eci = ECI.None;
                    }
                    catch (EncoderFallbackException)
                    {
                        // Cannot encode as ISO-8859-1 without loss, use UTF-8
                        eci = ECI.UTF8;
                        encoding = ECI.UTF8.GetEncoding();
                    }
                    break;
                
                case ECI.NoneValue:
                {
                    if (encoding == null)
                    {
                        throw new ArgumentException("Encoding must be specified for eci == ECI.None", nameof(encoding));
                    }
                    break;
                }
                
                default:
                    encoding = encoding ?? eci.GetEncoding();
                    break;
            }

            data = data ?? encoding.GetBytes(text);

            bool considerKanjiMode;
            switch (kanjiStrategy)
            {
                case KanjiStrategy.Enabled:
                    considerKanjiMode = true;
                    break;
                case KanjiStrategy.Disabled:
                    considerKanjiMode = false;
                    break;
                default:
                    considerKanjiMode = Equals(eci, ECI.ShiftJIS);
                    break;
            }
            
            return FromBinaryData(data, eci, version, considerKanjiMode);
        }

        /// <summary>
        /// Creates optimal data segments for the specified binary data.
        /// </summary>
        /// <param name="data">The binary data to encode.</param>
        /// <param name="eci">The extended character set indicator segment to add.</param>
        /// <param name="version">The QR code version.</param>
        /// <param name="considerKanjiMode">Controls if Kanji mode is considered for data segments.</param>
        /// <returns>A list of QR data segments representing the data.</returns>
        public static List<DataSegment> FromBinaryData(
            byte[] data,
            ECI eci = null,
            int version = 20,
            bool considerKanjiMode = false
        )
        {
            Objects.RequireNonNull(data, nameof(data));
            
            var dataSegments = SegmentCompaction.BuildSegments(new ArraySegment<byte>(data), version, considerKanjiMode);
            if (!Equals(eci, ECI.None))
            {
                dataSegments.Insert(0, new DataSegmentEci(eci ?? ECI.BinaryData));
            }
            
            return dataSegments;
        }

        #endregion
        
        #region Bit Stream

        /// <summary>
        /// Creates a bit stream for the specified data segments.
        /// </summary>
        /// <param name="segments">The segments to encode in the bit stream.</param>
        /// <param name="version">The QR code version.</param>
        /// <param name="capacity">The capacity for the bit stream, in bytes.</param>
        /// <returns>The bit stream with the encoded segments.</returns>
        internal static BitStream CreateBitStream(List<DataSegment> segments, int version, int capacity)
        {
            var bitStream = new BitStream(capacity);
            foreach (var segment in segments)
            {
                var modeInfo = DataSegmentModeInfo.For(segment.Mode);
                // mode indicator
                bitStream.AppendBits((uint)modeInfo.ModeIndicator, 4);
                // character count indicator
                if (modeInfo.HasCountIndicator)
                {
                    bitStream.AppendBits((uint)segment.CharacterCount,
                        modeInfo.GetCountIndicatorLength(version));
                }
                // data bit stream
                segment.WriteToBitStream(bitStream);
            }
            
            Trace.Assert(bitStream.Length <= 8 * capacity);
            
            // terminator
            var terminatorLength = Math.Min(4, capacity * 8 - bitStream.Length);
            if (terminatorLength > 0)
            {
                bitStream.AppendBits(0, terminatorLength);
            }

            return bitStream;
        }

        /// <summary>
        /// Writes the encoded segment data to the specified bit stream.
        /// </summary>
        /// <param name="bitStream">The bit stream.</param>
        internal abstract void WriteToBitStream(BitStream bitStream);

        internal static uint GetModeIndicator(DataSegmentMode mode)
        {
            return (uint)DataSegmentModeInfo.For(mode).ModeIndicator;
        }
        
        #endregion
        

        #region Constructor

        /// <summary>
        /// Creates a new instance with the given data segment mode and encoded data length.
        /// </summary>
        /// <param name="mode">The data segment mode.</param>
        /// <param name="encodedLength">The length of the encoded data, without mode and count indicator, in bits.</param>
        protected DataSegment(DataSegmentMode mode, int encodedLength)
        {
            Mode = mode;
            EncodedLength = encodedLength;
        }

        /// <summary>
        /// Creates a new data segment for encoding the specified data.
        /// </summary>
        /// <param name="dataSegmentMode">The data segment mode.</param>
        /// <param name="dataBytes">The character or binary data.</param>
        /// <returns>The new data segment.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown if an unsupported data segment mode is specified.</exception>
        /// <exception cref="ArgumentNullException">Thrown if <c>dataBytes.Array</c> is <c>null</c>.</exception>
        public static DataSegment MakeSegment(DataSegmentMode dataSegmentMode, ArraySegment<byte> dataBytes)
        {
            Objects.RequireNonNull(dataBytes.Array, "dataBytes.Array");

            var create = DataSegmentModeInfo.For(dataSegmentMode).Create;
            Trace.Assert(create != null);
            return create(dataBytes);
        }

        /// <summary>
        /// Creates a new ECI data segment.
        /// </summary>
        /// <param name="designator">The ECI designator.</param>
        /// <returns>An ECI data segment.</returns>
        /// <seealso cref="ECI"/>
        public static DataSegment MakeECISegment(ECI designator)
        {
            return new DataSegmentEci(designator);
        }

        /// <summary>
        /// Creates a structured append data segment.
        /// </summary>
        /// <param name="position">The position of the QR code within the message (1 to 16).</param>
        /// <param name="total">The total number of QR code used for the message (1 to 16).</param>
        /// <param name="parity">The message parity.</param>
        /// <returns>A structured append data segment.</returns>
        public static DataSegment MakeStructuredAppend(int position, int total, byte parity)
        {
            return new DataSegmentStructuredAppend(position, total, parity);
        }
        
        #endregion
        
        #region Length Calculation

        /// <summary>
        /// Calculates the segment length.
        /// </summary>
        /// <param name="mode">The encoding mode.</param>
        /// <param name="length">The number of bytes to encode.</param>
        /// <param name="version">The QR code version.</param>
        /// <returns>The segment length, in bits.</returns>
        internal static int GetBitLength(DataSegmentMode mode, int length, int version)
        {
            var info = DataSegmentModeInfo.For(mode);
            Trace.Assert(info.EncodedBitLength != null);
            return info.GetHeaderLength(version) + info.EncodedBitLength(length);
        }

        internal static int GetBitLength(IEnumerable<DataSegment> segments, int version)
        {
            return segments.Sum(segment => segment.GetTotalLength(version));
        }

        /// <summary>
        /// Gets the number of bytes that will fit in a segment with the given bit length.
        /// </summary>
        /// <param name="mode">The data segment mode.</param>
        /// <param name="bitLength">The encoded data length, in bits.</param>
        /// <returns>The number of bytes or characters.</returns>
        internal static int GetByteCount(DataSegmentMode mode, int bitLength)
        {
            var info = DataSegmentModeInfo.For(mode);
            Trace.Assert(info.ByteCount != null);
            return info.ByteCount(bitLength);
        }
        
        /// <summary>
        /// Calculates the length of the header (mode indicator and count indicator).
        /// </summary>
        /// <param name="mode">The encoding mode.</param>
        /// <param name="version">The QR code version.</param>
        /// <returns>The header length, in bits.</returns>
        internal static int GetHeaderLength(DataSegmentMode mode, int version)
        {
            return DataSegmentModeInfo.For(mode).GetHeaderLength(version);
        }
        
        #endregion
        

        #region Text

        /// <summary>
        /// Gets the text encoded in the given sequence of segments.
        /// </summary>
        /// <param name="segments">The segments.</param>
        /// <returns>The text.</returns>
        public static string GetText(IEnumerable<DataSegment> segments)
        {
            var encoding = ECI.Latin1.GetEncoding();
            var text = new StringBuilder();
            foreach (var segment in segments)
            {
                if (segment is DataSegmentEci eci)
                {
                    encoding = eci.EciDesignator.GetEncoding();
                }
                else if (segment is DataSegmentStructuredAppend)
                {
                    // ignore
                }
                else
                {
                    var dataBytes = segment.DataBytes;
                    Trace.Assert(dataBytes.Array != null);
                    text.Append(encoding.GetString(dataBytes.Array, dataBytes.Offset, dataBytes.Count));
                }
            }
            return text.ToString();
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return $"[Mode: {Mode}; Data length: {DataLength}]";
        }
        
        #endregion
    }
}
