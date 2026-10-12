/*
 * QR code generator library (.NET)
 *
 * Copyright (c) Manuel Bleichenbacher (MIT License)
 * https://github.com/manuelbl/QrCodeGenerator
 */

namespace Net.Codecrete.QrCodeGenerator
{
    /// <summary>
    /// Controls if the Kanji mode is used for data segments.
    /// </summary>
    public enum KanjiStrategy
    {
        Automatic,
        Enabled,
        Disabled
    }
}
