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
    /// The exception that is thrown when the supplied data does not fit in the QR code.
    /// </summary>
    public class DataTooLongException : ArgumentException
    {
        public DataTooLongException(string message)
            : base(message)
        { }
    }
}
