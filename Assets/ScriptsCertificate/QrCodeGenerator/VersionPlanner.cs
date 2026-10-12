/*
 * QR code generator library (.NET)
 *
 * Copyright (c) Manuel Bleichenbacher (MIT License)
 * https://github.com/manuelbl/QrCodeGenerator
 */

using System.Collections.Generic;

namespace Net.Codecrete.QrCodeGenerator
{
    /// <summary>
    /// Chooses the QR code version (size) and error correction level for a set of data segments.
    /// </summary>
    internal static class VersionPlanner
    {
        internal static (int Version, int Ecc) Plan(List<DataSegment> dataSegments, int ecc,
            int minVersion = 1, int maxVersion = 40, bool boostEcc = true)
        {
            var (version, bitLength) = FindSmallestVersion(dataSegments, ecc, minVersion, maxVersion);

            if (boostEcc)
            {
                while (ecc < 3 && Fits(bitLength, version, ecc + 1))
                {
                    ecc += 1;
                }
            }

            return (version, ecc);
        }

        private static (int Version, int BitLength) FindSmallestVersion(List<DataSegment> dataSegments, int ecc, int minVersion, int maxVersion)
        {
            var bitLength = 0;
            int version;
            for (version = minVersion; version <= maxVersion; version += 1)
            {
                if (version == minVersion || version == 1 || version == 10 || version == 27)
                {
                    bitLength = DataSegment.GetBitLength(dataSegments, version);
                }

                if (Fits(bitLength, version, ecc))
                {
                    break;
                }

                if (version == maxVersion)
                {
                    if (version < 40)
                    {
                        throw new DataTooLongException(
                            $"Data is too long to fit into a QR code with version {version} and error correction level {"LMQH"[ecc]}.");
                    }

                    throw new DataTooLongException(
                        $"Data is too long to fit into a QR code with error correction level {"LMQH"[ecc]}");
                }
            }

            return (version, bitLength);
        }

        private static bool Fits(int bitLength, int version, int ecc)
        {
            return bitLength <= 8 * QrCodeParameters.GetCodewordDataCapacity(version, ecc);
        }
    }
}
