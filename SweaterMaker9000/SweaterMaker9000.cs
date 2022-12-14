using SanBot.Core;
using SanWebApi;
using SanWebApi.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using static SanWebApi.WebApiClient;

namespace SweaterMaker
{
    public partial class SweaterMaker9000
    {
        public static readonly string kTempDirectory = "./SweatShop-Temp";
        public static readonly string kOutputDirectory = Path.GetFullPath(Path.Join(kTempDirectory, "Import", "BF", "RF"));
        public static readonly Regex SafeFilenamePattern = new Regex("^[a-zA-Z0-9-_\\.]+$");

        public int SessionId { get; set; } = 12345;


        public Driver Driver { get; set; } = default!;

        struct PromptData
        {

        }
        public async Task<string> Start(Driver driver, string shortName, string description, string texturePath, byte[] textureBytes)
        {
            shortName = shortName.Substring(0, Math.Min(64, shortName.Length));

            this.Driver = driver;
            var myPersona = driver.MyPersonaDetails;
            if(myPersona == null)
            {
                throw new Exception("myPersona is null :(");
            }

            // Clear our temp directory
            if (Directory.Exists(kTempDirectory))
            {
                Directory.Delete(kTempDirectory, true);
            }

            Directory.CreateDirectory(kTempDirectory + "/Import/BF/material-override/");
            Directory.CreateDirectory(kTempDirectory + "/Import/BF/material-editor-save/" + SessionId);

            WriteMaterialOverride(texturePath);
            driver.SendChatMessage("Building item...");
            await BuildResources();
            var filesToUpload = GetFilesToUpload();

            var myStores = await driver.WebApi.GetMyStores();
            var myStore = myStores.data.First();

            driver.SendChatMessage("Uploading results...");
            Console.WriteLine("Uploading thumbnail...");
            var thumbnailHash = Md5Sum(textureBytes);
            await UploadImageThumbnail(textureBytes, $"{thumbnailHash}.128x128.png");

            Console.WriteLine("UploadFiles...");
            await UploadFiles(filesToUpload);

            var cluster = Path.GetFileName(filesToUpload.First(n => n.Contains("Cluster-Source.v1.manifest.v0.noVariants")));
            var clusterId = cluster.Substring(0, cluster.IndexOf('.'));

            Console.WriteLine("UploadLicense...");
            var licenseAssetId = await UploadLicense("Test License", 0, clusterId, new Guid(myPersona.Id));

            Console.WriteLine("PostInventoryItemB...");
            var itemResponse = await Driver.WebApi.PostInventoryItem(AssetType.Cluster, shortName, thumbnailHash, licenseAssetId, myPersona.Id, clusterId, new List<string>()
            {
                "characterMdClothing",
                "characterClothing",
                "R41_2CharacterAsset",
                "avatar2CharacterAsset",
                "characterSexMale"
            });

            driver.SendChatMessage("Creating listing...");
            Console.WriteLine("CreateListing...");
            var listingResponse = await Driver.WebApi.CreateListing(
                new SanWebApi.Json.CreateListingRequest(
                    itemResponse.id,
                    shortName,
                    description,
                    myStore.id,
                    "001cefbb-b2b6-4abc-9695-e5191043f9fe"
                )
            );

            driver.SendChatMessage("Adding listing images...");
            Console.WriteLine("CreateProductImageUrl...");
            var productImageBytes = textureBytes;
            var newImageId = await CreateProductImageUrl(productImageBytes);

            Console.WriteLine("AddProductImage...");
            var setProductImageResponse = await Driver.WebApi.AddProductImage(new SanWebApi.Json.AddProductImageRequest(newImageId, listingResponse.data.id));

            Console.WriteLine("SetListingImage...");
            var setImageResponse = await Driver.WebApi.SetListingImage(listingResponse.data.id, new SanWebApi.Json.SetListingImageRequest(setProductImageResponse.data.id));

            return listingResponse.data.id;
        }

        private string FixPath(string path)
        {
            var fixedPath = $"!{path.Remove(2, 1)}".Replace('\\', '/');
            return fixedPath;
        }

        private void WriteMaterialOverride(string absolutePathToAlbedoTextureFile)
        {
            var currentDirectory = Environment.CurrentDirectory;

            var fileContents = $@"
name = ""Sansar Xmas Sweater Template""
shader = ""TwoSidedOpaqueSingleLayer""
useType = ""Both""

[[file]]
name = ""Albedo""
value = ""{FixPath(absolutePathToAlbedoTextureFile)}""

[[file]]
name = ""Normal""
value = ""{FixPath(Path.Join(currentDirectory, "Resources", "Normal.png"))}""

[[file]]
name = ""Roughness""
value = ""{FixPath(Path.Join(currentDirectory, "Resources", "Roughness.png"))}""

[[file]]
name = ""Metalness""
value = ""{FixPath(Path.Join(currentDirectory, "Resources", "Metallic.png"))}""

[[layer]]
name = ""Albedo""
value = 1

[[layer]]
name = ""Normal""
value = 1

[[layer]]
name = ""Roughness""
value = 1

[[layer]]
name = ""Metalness""
value = 1

[[color]]
name = ""Tint""
value = ""1.00000000, 1.00000000, 1.00000000, 1.00000000""

[[number]]
name = ""UvScale""
value = ""1.0""
";
            File.WriteAllText($"{kTempDirectory}/Import/BF/material-editor-save/{SessionId}/66cf7801166c21319e3f51875571fb4f", fileContents);
            File.WriteAllText($"{kTempDirectory}/Import/BF/material-override/56f64b17558d79aa346bc00b7a9c6a1d", fileContents);
        }

        private async Task BuildResources()
        {
            // Build our resources
            var commandLine = @$"-singleInstance true " +
                              @$"-logging.logUntagged -logging.logAllTagged " +
                              @$"-memoryTrackingLevel none " +
                              @$"-recordResourceWriting false " +
                              @$"-textOutput {kTempDirectory}/logs/730cbac54aa6476caa8f75166a3fd953_log.txt " +
                              @$"-inputFile ""!R:dec/new_sansar_dec/sweater/uglyxmassweatertemplate/Sansar Xmas Sweater Template.samd"" " +
                              @$"-outputFile {kTempDirectory}/Import/9567287f60dd4c0b93a40d602033907f_source.bag {kTempDirectory}/Import/9567287f60dd4c0b93a40d602033907f_import.bag {kTempDirectory}/Import/9567287f60dd4c0b93a40d602033907f_runtime.bag " +
                              @$"-buildTargets RUNTIME:ContentTools/targets/Clothing.toml " +
                              $@"-buildFolder {kTempDirectory}/Import/BF/ " +
                              $@"-buildTarget ComposeClothingBlueprint " +
                              @$"-postBuild FinalizeBuild " +
                              @$"-sessionId {SessionId} " +
                              @$"-bodyMotionType static " +
                              @$"-rigFilePath RUNTIME:ContentTools/customImportInput/def_male_skel_lod.txt " +
                              @$"-generatePreviewTexture false " +
                              $@"-doCombineMeshes true " +
                              $@"-importItemType Clothing " +
                              @$"-validateSkeletonAabb true " +
                              @$"-useReferenceSkeleton true " +
                              @$"-referenceMaleSkeletonFilePath RUNTIME:ContentTools/customImportInput/base_male_2.hkt " +
                              @$"-referenceFemaleSkeletonFilePath RUNTIME:ContentTools/customImportInput/base_female_2.hkt " +
                              @$"-validateTransformsFlag 6 " +
                              @$"-application.console.visible true " +
                              @$"-application.console.title Sansar " +
                              @$"-application.logging.timeStamps utc " +
                              @$"-application.logging.logFilePath {kTempDirectory}/Logs/2022_12_13-18_23_38_SansarClient.log";
            var process = Process.Start(@"C:\Program Files\Sansar\Client\ImportContent.exe", commandLine);
            await process.WaitForExitAsync();
        }

        private List<string> GetFilesToUpload()
        {
            // Get files to upload
            List<string> filesToUpload = new List<string>();
            var buildFiles = Directory.GetFiles(kOutputDirectory);
            foreach (var file in buildFiles)
            {
                if (!file.EndsWith(".v0.noVariants") || file.Contains(".debug."))
                {
                    continue;
                }

                if (!file.Contains(".TextureMip-Resource.") &&
                    !file.Contains(".Texture-Resource.") &&
                    !file.Contains(".Texture-Source.") &&
                    !file.Contains(".Clothing-Source.") &&
                    !file.Contains(".Material-Resource.") &&
                    !file.Contains(".Cluster-Source.") &&
                    !file.Contains(".Blueprint-Resource."))
                {
                    continue;
                }

                filesToUpload.Add(Path.GetFullPath(file));
            }

            return filesToUpload;
        }

        private static void DumpJson(object obj)
        {
            var options = new System.Text.Json.JsonSerializerOptions(new System.Text.Json.JsonSerializerDefaults());
            options.WriteIndented = true;

            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(obj, options));
        }

        private async Task<string> UploadLicense(string name, int price, string clusterId, Guid myPersonaId)
        {
            var licenseAssetId = GenerateRandomHash();
            var licenseId = Guid.NewGuid();
            var license = new ItemLicense(licenseAssetId, myPersonaId, licenseId, name, price, "Cluster-Source", clusterId);

            var getUploadUrlsPayload = new GetUploadUrlsRequest();
            getUploadUrlsPayload.AddRaw($"{licenseAssetId}.License-Resource.v1.payload.v0.noVariants");

            var uploadUrls = await Driver.WebApi.GetUploadUrls(getUploadUrlsPayload);
            if(uploadUrls.assets.Length == 0)
            {
                throw new Exception("We didn't get any urls back from the signing endpoint");
            }
            if (uploadUrls.assets[0].exists)
            {
                throw new Exception("Asset already exists :(");
            }

            var licenseJson = System.Text.Json.JsonSerializer.Serialize(license);
            var licenseJsonBytes = Encoding.UTF8.GetBytes(licenseJson);

            Console.WriteLine($"Uploading https://sansar-asset-production.s3.us-west-2.amazonaws.com/{uploadUrls.assets[0].asset_name}");
            await Driver.WebApi.UploadAsset(uploadUrls.assets[0].url, uploadUrls.assets[0].headers, licenseJsonBytes);

            return licenseAssetId;
        }

        private async Task UploadImageThumbnail(byte[] imageBytes, string imageName)
        {
            var getUploadUrlsPayload = new GetUploadUrlsRequest();
            getUploadUrlsPayload.AddRaw(imageName);

            var uploadUrls = await Driver.WebApi.GetUploadUrls(getUploadUrlsPayload);

            foreach (var item in uploadUrls.assets)
            {
                if (item.exists)
                {
                    continue;
                }

                Console.WriteLine($"Uploading https://sansar-asset-production.s3.us-west-2.amazonaws.com/{imageName}");
                await Driver.WebApi.UploadAsset(item.url, item.headers, imageBytes);
            }
        }

        private async Task UploadFiles(List<string> filesToUpload)
        {
            var getUploadUrlsPayload = new GetUploadUrlsRequest();
            foreach (var item in filesToUpload)
            {
                getUploadUrlsPayload.AddRaw(Path.GetFileName(item));
            }

            var uploadUrls = await Driver.WebApi.GetUploadUrls(getUploadUrlsPayload);

            foreach (var item in uploadUrls.assets)
            {
                if (item.exists)
                {
                    continue;
                }

                if (!SafeFilenamePattern.IsMatch(item.asset_name))
                {
                    throw new Exception("Malformed asset name: " + item.asset_name);
                }

                var fileToUpload = Path.GetFullPath(Path.Join(kOutputDirectory, Path.GetFileName(item.asset_name)));
                if (!fileToUpload.StartsWith(Path.GetFullPath(kTempDirectory)))
                {
                    throw new Exception("Directory traversal from asset name: " + item.asset_name + " --> resulted in path=" + fileToUpload);
                }

                Console.WriteLine($"Uploading https://sansar-asset-production.s3.us-west-2.amazonaws.com/{item.asset_name}");
                await Driver.WebApi.UploadAsset(item.url, item.headers, File.ReadAllBytes(fileToUpload));
            }
        }

        private async Task<string> CreateProductImageUrl(byte[] imageBytes)
        {
            var imageUploadUrl = await Driver.WebApi.CreateProductImageUrl();
            await Driver.WebApi.UploadProductImage(imageUploadUrl.signedRequest, imageBytes);

            return Path.GetFileNameWithoutExtension(imageUploadUrl.url);
        }

        private string GenerateRandomHash()
        {
            var randomBytes = RandomNumberGenerator.GetBytes(16);

            return BitConverter.ToString(randomBytes).Replace("-", "").ToLower();
        }
        private string Md5Sum(byte[] data)
        {
            using (var md5 = MD5.Create())
            {
                byte[] hashValue = md5.ComputeHash(data);

                return BitConverter.ToString(hashValue).Replace("-", "").ToLower();
            }
        }
    }
}
