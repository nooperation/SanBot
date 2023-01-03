using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace CrowdBot
{
    public class Utils
    {
        public static bool AvatarAssetIdExists(string avatarAssetId)
        {
            try
            {
                var assetUri = @$"https://sansar-asset-production.s3-us-west-2.amazonaws.com/{avatarAssetId}.Cluster-Source.v1.manifest.v0.noVariants";
                using (var client = new WebClient())
                {
                    using (var readStream = client.OpenRead(assetUri))
                    {
                        readStream.ReadByte();
                    }
                }
            }
            catch
            {
                return false;
            }

            return true;
        }

        public static float Distance(float x1, float x2, float y1, float y2, float z1, float z2)
        {
            return (float)Math.Sqrt((x1 - x2) * (x1 - x2) + (y1 - y2) * (y1 - y2) + (z1 - z2) * (z1 - z2));
        }
    }
}
