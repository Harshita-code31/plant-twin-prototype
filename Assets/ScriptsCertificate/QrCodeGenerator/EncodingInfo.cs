using System.Collections.Generic;

namespace Net.Codecrete.QrCodeGenerator
{
    /// <summary>
    /// Details about the QR code encoding, collected for analysis purposes (not used internally).
    /// </summary>
    public class EncodingInfo
    {
        public PenaltyScore[] Penalties { get; } = new PenaltyScore[8];
        public List<DataSegment> DataSegments { get; set; }
        public int ForcedDataMask { get; set; } = -1;
    }
}
