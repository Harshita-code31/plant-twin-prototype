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
    /// Helper functions to check for valid arguments.
    /// </summary>
    internal static class Objects
    {
        internal static void RequireNonNull<T>(T arg, string paramName) where T : class
        {
            if (arg == null)
            {
                throw new ArgumentNullException(paramName);
            }
        }
    }
}
