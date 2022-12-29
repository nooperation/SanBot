using ObjectMaker;
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

namespace SignMaker
{
    public partial class SignMaker9000
    {
        public static readonly string kTempDirectory = "./SignMaker-Temp";
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

            var cluster = Path.GetFileName(filesToUpload.First(n => n.Contains("Cluster-Source.v1.manifest.v0.noVariants")));
            var clusterId = cluster.Substring(0, cluster.IndexOf('.'));


            var newMaterialPath = filesToUpload.Where(n =>
                n.Contains("Material-Resource.v1.payload.v0.noVariants") &&
                !n.EndsWith("cac0284aee0cf961bda4d59ef22130f2.Material-Resource.v1.payload.v0.noVariants") &&
                !n.EndsWith("dcb7ae1b8de01fd8b38ad4dad5e109c2.Material-Resource.v1.payload.v0.noVariants")).FirstOrDefault();
            var newMaterialId = Path.GetFileName(newMaterialPath);
            newMaterialId = newMaterialId.Substring(0, newMaterialId.IndexOf('.'));

            var newTextureResourcePath = filesToUpload.Where(n =>
                n.Contains("Texture-Resource.v3.payload.v0.noVariants") &&
                !n.EndsWith("277ce498ae90651ef3a1598ed1c3c609.Texture-Resource.v3.payload.v0.noVariants") &&
                !n.EndsWith("7b066564ef954b9219b1f43dabfb38d9.Texture-Resource.v3.payload.v0.noVariants") &&
                !n.EndsWith("01a49a3efaf5f605039b73b7439428cf.Texture-Resource.v3.payload.v0.noVariants") &&
                !n.EndsWith("4a19a59f97b345345744b8e7368c6666.Texture-Resource.v3.payload.v0.noVariants")).FirstOrDefault();
            var newTextureResourceId = Path.GetFileName(newTextureResourcePath);
            newTextureResourceId = newTextureResourceId.Substring(0, newTextureResourceId.IndexOf('.'));

            var newClusterDef = ClusterMaker.GenerateNewCluster(
                "Resources/1195311f3b8eafa3313bad401a5ba82f.Cluster-Definition.v1.payload.v0.pcClient",
                "Resources/1195311f3b8eafa3313bad401a5ba82f.Cluster-Definition.v1.manifest.v0.pcClient",
                clusterId,
                newMaterialId,
                newTextureResourceId
            );

            File.WriteAllBytes(Path.Join(kOutputDirectory, $"{clusterId}.Cluster-Definition.v1.payload.v0.pcClient"), newClusterDef.ClusterBytes);
            File.WriteAllBytes(Path.Join(kOutputDirectory, $"{clusterId}.Cluster-Definition.v1.manifest.v0.pcClient"), newClusterDef.ManifestBytes);
            //File.WriteAllBytes(Path.Join(kOutputDirectory, $"{clusterId}.Cluster-Definition.v1.payload.v0.pcClient"), File.ReadAllBytes("Resources/1195311f3b8eafa3313bad401a5ba82f.Cluster-Definition.v1.payload.v0.pcClient"));
            //File.WriteAllBytes(Path.Join(kOutputDirectory, $"{clusterId}.Cluster-Definition.v1.manifest.v0.pcClient"), File.ReadAllBytes("Resources/1195311f3b8eafa3313bad401a5ba82f.Cluster-Definition.v1.manifest.v0.pcClient"));
            filesToUpload.Add($"{clusterId}.Cluster-Definition.v1.payload.v0.pcClient");
            filesToUpload.Add($"{clusterId}.Cluster-Definition.v1.manifest.v0.pcClient");

            var myStores = await driver.WebApi.GetMyStores();
            var myStore = myStores.data.First();

            driver.SendChatMessage("Uploading results...");
            Console.WriteLine("Uploading thumbnail...");
            var thumbnailHash = Md5Sum(textureBytes);
            await UploadImageThumbnail(textureBytes, $"{thumbnailHash}.128x128.png");

            Console.WriteLine("UploadFiles...");
            await UploadFiles(filesToUpload);





            Console.WriteLine("New material id = " + newMaterialId);



            Console.WriteLine("UploadLicense...");
            var licenseAssetId = await UploadLicense("Test License", 0, clusterId, new Guid(myPersona.Id));

            Console.WriteLine("PostInventoryItemB...");
            var itemResponse = await Driver.WebApi.PostInventoryItem(AssetType.Cluster, shortName, thumbnailHash, licenseAssetId, myPersona.Id, clusterId, new List<string>()
            {
                "hasLeftGrab",
                "hasRightGrab"
            });

            driver.SendChatMessage("Creating listing...");
            Console.WriteLine("CreateListing...");
            var listingResponse = await Driver.WebApi.CreateListing(
                new SanWebApi.Json.CreateListingRequest(
                    itemResponse.id,
                    shortName,
                    description,
                    myStore.id,
                    "5c816075-f251-416b-adc1-0f00d9f2d735"
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
name = ""Material""
shader = ""OpaqueSingleLayer""
useType = ""Both""

[[file]]
name = ""Albedo""
value = ""C:\\Program Files\\Sansar\\Client\\Graphics\\Textures\\Default\\albedo.png""

[[file]]
name = ""Normal""
value = ""C:\\Program Files\\Sansar\\Client\\Graphics\\Textures\\Default\\normal.png""

[[file]]
name = ""Roughness""
value = ""C:\\Program Files\\Sansar\\Client\\Graphics\\Textures\\Default\\roughness.png""

[[file]]
name = ""Metalness""
value = ""C:\\Program Files\\Sansar\\Client\\Graphics\\Textures\\Default\\metalness.png""

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
value = ""0.80000007, 0.80000007, 0.80000007, 1.00000000""

[[number]]
name = ""UvScale""
value = ""1.0""
";
            File.WriteAllText($"{kTempDirectory}/Import/BF/material-editor-save/{SessionId}/bc5c7c605a871aefe330a7da4e95d14a", fileContents);
            File.WriteAllText($"{kTempDirectory}/Import/BF/material-override/bc5c7c605a871aefe330a7da4e95d14a", fileContents);

            fileContents = $@"
name = ""Handle""
shader = ""OpaqueSingleLayer""
useType = ""Both""

[[file]]
name = ""Albedo""
value = ""{FixPath(Path.Join(currentDirectory, "Resources", "Handle_Albedo.jpg"))}""

[[file]]
name = ""Normal""
value = ""C:\\Program Files\\Sansar\\Client\\Graphics\\Textures\\Default\\normal.png""

[[file]]
name = ""Roughness""
value = ""C:\\Program Files\\Sansar\\Client\\Graphics\\Textures\\Default\\roughness.png""

[[file]]
name = ""Metalness""
value = ""C:\\Program Files\\Sansar\\Client\\Graphics\\Textures\\Default\\metalness.png""

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

            File.WriteAllText($"{kTempDirectory}/Import/BF/material-editor-save/{SessionId}/3c31b51a5fb62eb8a232fc1053a75bbe", fileContents);
            File.WriteAllText($"{kTempDirectory}/Import/BF/material-override/3c31b51a5fb62eb8a232fc1053a75bbe", fileContents);
            
            fileContents = $@"
name = ""Material.001""
shader = ""OpaqueSingleLayer""
useType = ""Both""

[[file]]
name = ""Albedo""
value = ""{FixPath(absolutePathToAlbedoTextureFile)}""

[[file]]
name = ""Normal""
value = ""C:\\Program Files\\Sansar\\Client\\Graphics\\Textures\\Default\\normal.png""

[[file]]
name = ""Roughness""
value = ""C:\\Program Files\\Sansar\\Client\\Graphics\\Textures\\Default\\roughness.png""

[[file]]
name = ""Metalness""
value = ""C:\\Program Files\\Sansar\\Client\\Graphics\\Textures\\Default\\metalness.png""

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

            File.WriteAllText($"{kTempDirectory}/Import/BF/material-editor-save/{SessionId}/32b6034201095615ead9488197e993e4", fileContents);
            File.WriteAllText($"{kTempDirectory}/Import/BF/material-override/32b6034201095615ead9488197e993e4", fileContents);
        }

        private async Task BuildResources()
        {
            // Build our resources
            var commandLine = @$"-singleInstance true " +
                              @$"-memoryTrackingLevel none " +
                              @$"-recordResourceWriting false " +
                              @$"-textOutput {kTempDirectory}/logs/730cbac54aa6476caa8f75166a3fd953_log.txt " +
                              @$"-inputFile !D:models/sign2.fbx !D:models/sign2.fbx " +
                              @$"-outputFile {kTempDirectory}/Import/9567287f60dd4c0b93a40d602033907f_source.bag {kTempDirectory}/Import/9567287f60dd4c0b93a40d602033907f_import.bag {kTempDirectory}/Import/9567287f60dd4c0b93a40d602033907f_runtime.bag " +
                              @$"-buildTargets RUNTIME:ContentTools/targets/Mesh.toml " +
                              $@"-buildFolder {kTempDirectory}/Import/BF/ " +
                              @$"-buildTarget ComposeBlueprintWithPhysics " +
                              @$"-postBuild FinalizeBuild " +
                              @$"-sessionId {SessionId} " +
                              @$"-bodyMotionType dynamic " +
                              @$"-rigFilePath """" " +
                              @$"-generatePreviewTexture false " +
                              @$"-doCombineMeshes false " +
                              @$"-importItemType Mesh " +
                              @$"-validateSkeletonAabb true " +
                              @$"-useReferenceSkeleton false " +
                              @$"-referenceMaleSkeletonFilePath """" " +
                              @$"-referenceFemaleSkeletonFilePath """" " +
                              @$"-validateTransformsFlag 7 " +
                              @$"-application.console.visible true " +
                              @$"-application.console.title Sansar " +
                              @$"-application.logging.timeStamps utc " +
                              @$"-application.logging.logAllTagged true " +
                              @$"-application.logging.disableTags ComponentManager MeshDuplicateCheck AssetSystem EventQueue ResourceLoader TextureStreamingManager " +
                              @$"-application.logging.logFilePath C:\Users\Nop\AppData\Local\LindenLab\SansarClient\Log\2022_12_15-06_13_15_SansarClient.log ";
            
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
