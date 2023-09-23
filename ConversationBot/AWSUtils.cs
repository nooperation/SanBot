using Amazon.Runtime.CredentialManagement;
using Amazon.S3;
using Amazon.S3.Transfer;
using static ConversationBot.ImageGenerator;

namespace ConversationBot
{
    internal class AWSUtils
    {
        private static readonly string _profile = "nopbox";
        private static readonly string _bucketName = "nopbox-public";

        public static AmazonS3Client GetS3Client()
        {
            var chain = new CredentialProfileStoreChain();
            if (chain.TryGetAWSCredentials(_profile, out var awsCredentials))
            {
                return new AmazonS3Client(awsCredentials);
            }

            throw new Exception("Failed to get AWS Credentials");
        }

        public static async Task<string> UploadBytes(PromptResultData data)
        {
            if (data.ImageBytes.Length >= 4 && data.ImageBytes[0] != '%' && data.ImageBytes[1] != 'P' && data.ImageBytes[2] != 'N' && data.ImageBytes[3] != 'G')
            {
                throw new Exception("Attempted to upload bad file");
            }

            return await AWSUtils.UploadImage(data.ImageBytes, data.SafeName, data.Prompt) ?? "Error";
        }

        public static async Task<string?> UploadImage(byte[] data, string name, string fullPrompt)
        {
            var s3Client = GetS3Client();
            var keyName = $"Prompts/{name}.png";

            using (var memoryStream = new MemoryStream(data))
            {
                try
                {
                    var fileTransferUtility = new TransferUtility(s3Client);

                    var fileTransferUtilityRequest = new TransferUtilityUploadRequest
                    {
                        BucketName = _bucketName,
                        InputStream = memoryStream,
                        StorageClass = S3StorageClass.Standard,
                        PartSize = 6291456, // 6 MB.
                        Key = keyName,
                        ContentType = "image/png",
                        CannedACL = S3CannedACL.PublicRead
                    };
                    fileTransferUtilityRequest.Metadata.Add("prompt", fullPrompt);

                    await fileTransferUtility.UploadAsync(fileTransferUtilityRequest);

                    return $"https://nopbox-public.s3.amazonaws.com/{keyName}";
                }
                catch (AmazonS3Exception e)
                {
                    Console.WriteLine("Error encountered on server. Message:'{0}' when writing an object", e.Message);
                }
                catch (Exception e)
                {
                    Console.WriteLine("Unknown encountered on server. Message:'{0}' when writing an object", e.Message);
                }

                return null;
            }
        }
    }
}
