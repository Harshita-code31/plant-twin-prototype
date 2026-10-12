/*
 * QR code generator library (.NET)
 *
 * Copyright (c) Manuel Bleichenbacher (MIT License)
 * https://github.com/manuelbl/QrCodeGenerator
 */

namespace Net.Codecrete.QrCodeGenerator
{
    /// <summary>
    /// Information about the penalty score of a data mask pattern.
    /// </summary>
    public struct PenaltyScore
    {
        public int HorizontalStreaks { get; set; }
        public int VerticalStreaks { get; set; }
        public int Blocks { get; set; }
        public int HorizontalFinderPatterns { get; set; }
        public int VerticalFinderPatterns { get; set; }
        public int ColorBalance { get; set; }
        public int Total { get; set; }
    }
}
