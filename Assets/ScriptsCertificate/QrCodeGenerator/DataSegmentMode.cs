/*
 * QR code generator library (.NET)
 *
 * Copyright (c) Manuel Bleichenbacher (MIT License)
 * https://github.com/manuelbl/QrCodeGenerator
 */

namespace Net.Codecrete.QrCodeGenerator
{
    /// <summary>
    /// Data segment mode.
    /// </summary>
    public enum DataSegmentMode
    {
        Numeric = 1,
        Alphanumeric = 2,
        Kanji = 3,
        Binary = 4,
        ECI = 5,
        StructuredAppend = 6
    }
}
