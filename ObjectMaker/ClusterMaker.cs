using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ObjectMaker
{
    public class ClusterMaker
    {
        private static string Clusterbutt(string text)
        {
            text = text.Replace("-", "");
            var match = Regex.Match(text, @".*([a-zA-Z0-9]{2})([a-zA-Z0-9]{2})([a-zA-Z0-9]{2})([a-zA-Z0-9]{2})([a-zA-Z0-9]{2})([a-zA-Z0-9]{2})([a-zA-Z0-9]{2})([a-zA-Z0-9]{2})([a-zA-Z0-9]{2})([a-zA-Z0-9]{2})([a-zA-Z0-9]{2})([a-zA-Z0-9]{2})([a-zA-Z0-9]{2})([a-zA-Z0-9]{2})([a-zA-Z0-9]{2})([a-zA-Z0-9]{2}).*", RegexOptions.Singleline);
            if (match.Success)
            {
                var sb = new StringBuilder();
                sb.Append(match.Groups[1 + 7]);
                sb.Append(match.Groups[1 + 6]);
                sb.Append(match.Groups[1 + 5]);
                sb.Append(match.Groups[1 + 4]);
                sb.Append(match.Groups[1 + 3]);
                sb.Append(match.Groups[1 + 2]);
                sb.Append(match.Groups[1 + 1]);
                sb.Append(match.Groups[1 + 0]);
                sb.Append(match.Groups[1 + 8 + 7]);
                sb.Append(match.Groups[1 + 8 + 6]);
                sb.Append(match.Groups[1 + 8 + 5]);
                sb.Append(match.Groups[1 + 8 + 4]);
                sb.Append(match.Groups[1 + 8 + 3]);
                sb.Append(match.Groups[1 + 8 + 2]);
                sb.Append(match.Groups[1 + 8 + 1]);
                sb.Append(match.Groups[1 + 8 + 0]);

                return sb.ToString();
            }
            else
            {
                return "ERROR";
            }
        }

        public enum OodleCompressorIds
        {
            Kraken = 8,
            Mermaid = 9,
            Selkie = 11,
            Hydra = 12,
            Leviathan = 13,
        }

        public struct GenerateNewClusterResult
        {
            public string Id { get; set; }
            public byte[] ClusterBytes { get; set; }
            public byte[] ManifestBytes { get; set; }
        }
        public static GenerateNewClusterResult GenerateNewCluster(string referenceClusterDefPayloadPath, string referenceClusterDefManifestPath, string clusterId, string newMaterialId, string newTextureResourceId)
        {
            /*
            var oodle = LibSanBag.ResourceUtils.LibOodleBase.CreateLibOodle(new LibSanBag.Providers.FileProvider());

            // Decompress resource
            byte[] originalResourceBytes;
            using (var resourceStream = File.OpenRead(referenceClusterDefPayloadPath))
            {
                originalResourceBytes = LibSanBag.ResourceUtils.Unpacker.DecompressResource(resourceStream);
            }

            var newResourceBytes = UpdateClusterMaterial(originalResourceBytes, newMaterialId, oodle);

            byte[] originalManifestBytes = File.ReadAllBytes(referenceClusterDefManifestPath);
            var newManifestBytes = UpdateClusterManifest(originalManifestBytes, newMaterialId, newTextureResourceId);

            return new GenerateNewClusterResult()
            {
                ClusterBytes = newResourceBytes,
                ManifestBytes = newManifestBytes,
                Id = clusterId
            };
            */
            return new GenerateNewClusterResult()
            {
                ClusterBytes = new byte[] { },
                ManifestBytes = new byte[] { },
                Id = clusterId
            };
        }

        public static byte[] UpdateClusterManifest(byte[] decompressedClusterDefinition, string newMaterialId, string newTextureResourceId)
        {
            var newMaterialIdBytes = Convert.FromHexString(Clusterbutt(newMaterialId));
            var newTextureResourceIdBytes = Convert.FromHexString(Clusterbutt(newTextureResourceId));

            using (var ms = new MemoryStream(decompressedClusterDefinition))
            {
                ms.Seek(0xEC, SeekOrigin.Begin);
                ms.Write(newTextureResourceIdBytes, 0, newTextureResourceIdBytes.Length);

                ms.Seek(0x1D8, SeekOrigin.Begin);
                ms.Write(newMaterialIdBytes, 0, newMaterialIdBytes.Length);


                ms.Seek(0, SeekOrigin.Begin);
                return ms.ToArray();
            }
        }

        /*
        public static byte[] UpdateClusterMaterial(byte[] decompressedClusterDefinition, string newMaterialId, LibSanBag.ResourceUtils.LibOodleBase oodle)
        {
            // Update material id
            var newMaterialIdBytes = Convert.FromHexString(Clusterbutt(newMaterialId));
            using (var ms = new MemoryStream(decompressedClusterDefinition))
            {
                ms.Seek(0xFF1, SeekOrigin.Begin);
                ms.Write(newMaterialIdBytes, 0, newMaterialIdBytes.Length);
            }

            // Compress new resource
            byte[] compressedBytes = new byte[decompressedClusterDefinition.Length];
            var compressedSize = (int)oodle.Compress((int)OodleCompressorIds.Kraken, decompressedClusterDefinition, (ulong)decompressedClusterDefinition.LongLength, compressedBytes, 7, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            if (compressedSize <= 0)
            {
                throw new Exception("Compression failed :(");
            }

            // Write new resource container
            using (MemoryStream ms = new MemoryStream())
            {
                ms.WriteByte(0xF5);
                ms.WriteByte(0x41);
                ms.WriteByte(0x11);
                ms.Write(compressedBytes, 0, (int)compressedSize);
                ms.Write(new byte[] { 0x01, 0x45, 0xEF, 0x23, 0xCD, 0x01, 0xAB, 0x67, 0x89, 0x01 });

                ms.Seek(0, SeekOrigin.Begin);
                return ms.ToArray();
            }
        }
        */
    }
}
