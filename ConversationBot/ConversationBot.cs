using SanWebApi.Json;
using SanProtocol.ClientKafka;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using SanProtocol;
using SanBot.Core;
using System.Web;
using SanProtocol.ClientVoice;
using SanBot.Core.MessageHandlers;
using System.Diagnostics;
using Concentus.Structs;
using NAudio.Wave;
using System.Net;
using System.Collections.Concurrent;
using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using Renci.SshNet;
using System.Net.Http.Json;
using System.Text.Json;
using Amazon.S3;
using Amazon.Runtime.CredentialManagement;
using Amazon.Runtime;
using Amazon.S3.Transfer;
using SweaterMaker;
using SignMaker;

namespace EchoBot
{
    public class ConversationBot
    {
        public Driver Driver { get; set; }

        public DateTime? LastTimeWeListenedToOurTarget { get; set; } = null;
        public uint? agentControllerIdImListeningTo { get; set; } = null;
        public List<PersonaData> TargetPersonas { get; set; } = new List<PersonaData>();


        //public SanUUID ItemClousterResourceId { get; set; } = new SanUUID("62f7bca2e04c60bc77ef3bbccbcfb61e"); // panda reaction thing
        //public SanUUID ItemClousterResourceId { get; set; } = new SanUUID("a08aa34cad4dbaea7c1e18a44e4f973c"); // toast reaction thing
        //public SanUUID ItemClousterResourceId { get; set; } = new SanUUID("df2cdee01bb4024640fb93d1c6c1bf29"); // wtf reaction thing
        public SanUUID ItemClousterResourceId_Exclamation { get; set; } = new SanUUID("beb1c1d298aa865fc5d5326dada8d2a7"); // ! reaction thing
        //public SanUUID ItemClousterResourceId { get; set; } = new SanUUID("97477c6e978aa38d20e0bb8a60e85830"); // lightning reaction thing
        public SanUUID ItemClousterResourceId { get; set; } = new SanUUID("04c2d5a7ea3d6fb47af66669cfdc9f9a"); // heart reaction thing
                                                                                                               // public SanUUID ItemClousterResourceId { get; set; } = new SanUUID("04c2d5a7ea3d6fb47af66669cfdc9f9a"); // heart reaction thing
                                                                                                               //public SanUUID ItemClousterResourceId { get; set; } = new SanUUID("b8534d067b0613a509b0155e0dacb0b2"); // fox doll
        public List<float> MarkedPosition { get; set; } = new List<float>() { 0, 0, 0 };

        public ConcurrentDictionary<uint, VoiceConversation> ConversationsByAgentControllerId { get; set; } = new ConcurrentDictionary<uint, VoiceConversation>();

        public bool ResultsShouldBeWritten { get; set; } = false;
        public bool TextToSpeechEnabled { get; set; } = false;
        public bool ResultsShouldBeSpoken { get; set; } = false;
        public bool ResultsShouldBeDrawn { get; set; } = false;


#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

        public class Rootobject
        {
            public int fn_index { get; set; }
            public List<object> data { get; set; }
            public string session_hash { get; set; }
        }

        public class PredictionResult
        {
            public List<object> data { get; set; }
            public bool is_generating { get; set; }
            public float duration { get; set; }
            public float average_duration { get; set; }
        }

        public class PredictionResultInfo
        {
            public string prompt { get; set; }
            public string[] all_prompts { get; set; }
            public string negative_prompt { get; set; }
            public long seed { get; set; }
            public long[] all_seeds { get; set; }
            public long subseed { get; set; }
            public long[] all_subseeds { get; set; }
            public int subseed_strength { get; set; }
            public int width { get; set; }
            public int height { get; set; }
            public int sampler_index { get; set; }
            public string sampler { get; set; }
            public int cfg_scale { get; set; }
            public int steps { get; set; }
            public int batch_size { get; set; }
            public bool restore_faces { get; set; }
            public object face_restoration_model { get; set; }
            public string sd_model_hash { get; set; }
            public int seed_resize_from_w { get; set; }
            public int seed_resize_from_h { get; set; }
            public object denoising_strength { get; set; }
            public Extra_Generation_Params extra_generation_params { get; set; }
            public int index_of_first_image { get; set; }
            public string[] infotexts { get; set; }
            public string[] styles { get; set; }
            public string job_timestamp { get; set; }
        }

        public class Extra_Generation_Params
        {
        }


        public class PromptResultData
        {
            public PredictionResultInfo ResultInfo { get; set; }
            public string Prompt { get; set; }
            public string SafeName { get; set; }
            public byte[] ImageBytes { get; set; }
            public string? ImagePathOnDisk { get; set; }
        }
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.


        public ConversationBot()
        {
            ConfigFile config;
            var sanbotPath = Path.Join(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SanBot"
            );
            var configPath = Path.Join(sanbotPath, "EchoBot.config.json");

            try
            {
                var configFileContents = File.ReadAllText(configPath);
                config = Newtonsoft.Json.JsonConvert.DeserializeObject<ConfigFile>(configFileContents);
            }
            catch (Exception ex)
            {
                throw new Exception("Missing or invalid config.json", ex);
            }

            Driver = new Driver();
            Driver.OnOutput += Driver_OnOutput;

            Driver.RegionClient.ClientRegionMessages.OnAddUser += ClientRegionMessages_OnAddUser;
            Driver.RegionClient.ClientRegionMessages.OnRemoveUser += ClientRegionMessages_OnRemoveUser;
            Driver.RegionClient.ClientRegionMessages.OnSetAgentController += ClientRegionMessages_OnSetAgentController;

            Driver.RegionClient.WorldStateMessages.OnCreateClusterViaDefinition += WorldStateMessages_OnCreateClusterViaDefinition;
            Driver.RegionClient.WorldStateMessages.OnDestroyCluster += WorldStateMessages_OnDestroyCluster;

            Driver.VoiceClient.ClientVoiceMessages.OnLocalAudioData += ClientVoiceMessages_OnLocalAudioData;

            Driver.KafkaClient.ClientKafkaMessages.OnRegionChat += ClientKafkaMessages_OnRegionChat;

            Driver.RegionClient.ClientRegionMessages.OnClientRuntimeInventoryUpdatedNotification += ClientRegionMessages_OnClientRuntimeInventoryUpdatedNotification;
            //
           //  Driver.RegionToJoin = new RegionDetails("nop", "script-sandbox");
          //  Driver.RegionToJoin = new RegionDetails("anuamun", "bamboo-central");
         //   Driver.RegionToJoin = new RegionDetails("djm3n4c3-9174", "reactive-dance-demo");

            //   Driver.RegionToJoin = new RegionDetails("fayd", "android-s-dream");
            //   //  Driver.RegionToJoin = new RegionDetails("test", "base2");
            // Driver.RegionToJoin = new RegionDetails("sansar-studios", "r-d-starter-inventory-collection");
            // Driver.RegionToJoin = new RegionDetails("sansar-studios", "r-d-starter-inventory-collection");
            //  Driver.RegionToJoin = new RegionDetails("solasnagealai", "once-upon-a-midnight-dream");
                Driver.RegionToJoin = new RegionDetails("sansar-studios", "social-hub");
            //    Driver.RegionToJoin = new RegionDetails("turtle-4332", "turtles-campfire");

            Driver.AutomaticallySendClientReady = true;
            Driver.UseVoice = false;
            Driver.StartAsync(config).Wait();

            Stopwatch watch = new Stopwatch();

            _IsConversationThreadRunning = false;
            //ConversationThread = new Thread(new ThreadStart(ConversationThreadEntrypoint));
            //ConversationThread.Start();

            while (true)
            {
                if (!Driver.Poll())
                {
                    foreach (var conversation in ConversationsByAgentControllerId)
                    {
                        conversation.Value.Poll();
                    }
                    //Thread.Sleep(10);
                }
            }

            _IsConversationThreadRunning = false;
            ConversationThread.Join();

        }

        static AmazonS3Client GetClient()
        {
            var chain = new CredentialProfileStoreChain();
            AWSCredentials awsCredentials;
            if (chain.TryGetAWSCredentials("nopbox", out awsCredentials))
            {
                return new AmazonS3Client(awsCredentials);
            }

            throw new Exception("Failed to get AWS Credentials");
        }

        static async Task<string> UploadImage(byte[] data, string name, string fullPrompt)
        {
            var s3Client = GetClient();
            var bucketName = "nopbox-public";
            var keyName = $"Prompts/{name}.png";

            using (var memoryStream = new MemoryStream(data))
            {
                try
                {
                    var fileTransferUtility = new TransferUtility(s3Client);

                    var fileTransferUtilityRequest = new TransferUtilityUploadRequest
                    {
                        BucketName = bucketName,
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

        private void ClientRegionMessages_OnClientRuntimeInventoryUpdatedNotification(object? sender, SanProtocol.ClientRegion.ClientRuntimeInventoryUpdatedNotification e)
        {
            Console.WriteLine("ClientRegionMessages_OnClientRuntimeInventoryUpdatedNotification: " + e.Message);
        }

        #region Sweater_Maker_9000
        public string GenerateSweaterPrompt(string prompt, string creatorName)
        {
            bool isTiled = false;
            int size = 512;

            prompt = prompt.Trim();
            if (prompt.ToLower().StartsWith("tiled "))
            {
                isTiled = true;
                prompt = prompt.Substring("tiled ".Length).Trim();
            }
            if (prompt.ToLower().StartsWith("big "))
            {
                size = 1024;
                prompt = prompt.Substring("big ".Length).Trim();
            }

            var truncatedPrompt = prompt.Substring(0, Math.Min(128, prompt.Length));

            Driver.SendChatMessage("Generating image...");
            var promptResult = GetImage(truncatedPrompt, isTiled, size).Result;

            if(promptResult.ImagePathOnDisk == null)
            {
                Output("Oops, image path on disk is null?");
                return "";
            }

            var description = $@"
{prompt}

Created by {creatorName} on {DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")}
-----
Sampler: {promptResult.ResultInfo.sampler}
Steps: {promptResult.ResultInfo.steps}
Cfg Scale: {promptResult.ResultInfo.cfg_scale}
Seed: {promptResult.ResultInfo.seed}
Width: {promptResult.ResultInfo.width}
Height: {promptResult.ResultInfo.height}
";

            var sweaterMaker = new SweaterMaker9000();
            var storeUrl = sweaterMaker.Start(Driver, truncatedPrompt, description, promptResult.ImagePathOnDisk, promptResult.ImageBytes).Result;

            return $"https://store.sansar.com/listings/{storeUrl}/{promptResult.SafeName}";
        }
        #endregion
        #region SignMaker_9000
        public string GenerateSignPrompt(string prompt, string creatorName)
        {
            bool isTiled = false;
            int size = 512;

            prompt = prompt.Trim();
            if (prompt.ToLower().StartsWith("tiled "))
            {
                isTiled = true;
                prompt = prompt.Substring("tiled ".Length).Trim();
            }
            if (prompt.ToLower().StartsWith("big "))
            {
                size = 1024;
                prompt = prompt.Substring("big ".Length).Trim();
            }

            var truncatedPrompt = prompt.Substring(0, Math.Min(128, prompt.Length));

            Driver.SendChatMessage("Generating image...");
            var promptResult = GetImage(truncatedPrompt, isTiled, size).Result;

            if (promptResult.ImagePathOnDisk == null)
            {
                Output("Oops, image path on disk is null?");
                return "";
            }

            var description = $@"
{prompt}

Created by {creatorName} on {DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")}
-----
Sampler: {promptResult.ResultInfo.sampler}
Steps: {promptResult.ResultInfo.steps}
Cfg Scale: {promptResult.ResultInfo.cfg_scale}
Seed: {promptResult.ResultInfo.seed}
Width: {promptResult.ResultInfo.width}
Height: {promptResult.ResultInfo.height}
";

            var signMaker = new SignMaker9000();
            var storeUrl = signMaker.Start(Driver, truncatedPrompt, description, promptResult.ImagePathOnDisk, promptResult.ImageBytes).Result;

            return $"https://store.sansar.com/listings/{storeUrl}/{promptResult.SafeName}";
        }
        #endregion
        public string GeneratePrompt(string prompt)
        {
            bool isTiled = false;

            prompt = prompt.Trim();
            if(prompt.ToLower().StartsWith("tiled "))
            {
                isTiled = true;
                prompt = prompt.Substring("tiled ".Length).Trim();
            }

            var truncatedPrompt = prompt.Substring(0, Math.Min(128, prompt.Length));
            var promptResult = GetImage(truncatedPrompt, isTiled).Result;

            var url = UploadBytes(promptResult).Result;

            return url;
        }

        public async Task<string> UploadBytes(PromptResultData data)
        {
            if (data.ImageBytes.Length >= 4 && data.ImageBytes[0] != '%' && data.ImageBytes[1] != 'P' && data.ImageBytes[2] != 'N' && data.ImageBytes[3] != 'G')
            {
                throw new Exception("Attempted to upload bad file");
            }

            return await UploadImage(data.ImageBytes, data.SafeName, data.Prompt);
            /*
            var privateKey = new PrivateKeyFile(@"C:\Users\Nop\.ssh\ovh_usa");
            using (var client = new SftpClient("vps-629dd4bd.vps.ovh.us", 422, "nop", privateKey))
            {
                client.Connect();
                client.ChangeDirectory("/var/www/nopfox.com/public/prompts/");

                using (MemoryStream ms = new MemoryStream(data.ImageBytes))
                {
                    client.UploadFile(ms, $"{data.SafeName}.png");
                }
                client.Disconnect();
            }

            return $"https://nopfox.com/prompts/{data.SafeName}.png";
            */
        }

        public class ImageResultData
        {
            public string name { get; set; }
            public object data { get; set; }
            public bool is_file { get; set; }
        }

        public async Task<PromptResultData> GetImage(string prompt, bool isTiled=false, int size=512)
        {
            Rootobject requestData = new Rootobject()
            {
                fn_index = 13,
                session_hash = "y674hs44uh9",
                data = new List<object>()
                {
                    prompt,
                    "",
                    "None",
                    "None",
                    size > 512 ? 30 : 35,
                    "Euler a",
                    false,
                    isTiled,
                    1,
                    1,
                    11,
                    -1,
                    -1,
                    0,
                    0,
                    0,
                    false,
                    size,
                    size,
                    false,
                    0.8,
                    0,
                    0,
                    "None",
                    false,
                    false,
                    null,
                    "",
                    "Seed",
                    "",
                    "Nothing",
                    "",
                    true,
                    false,
                    false,
                    null,
                    "",
                    "",
                }
            };



            using (var client = new HttpClient())
            {
                var result = await client.PostAsJsonAsync("http://127.0.0.1:7860/api/predict", requestData);
                var resultString = await result.Content.ReadAsStringAsync();

                var jsonResult = System.Text.Json.JsonSerializer.Deserialize<PredictionResult>(resultString);
                var data = (System.Text.Json.JsonElement)jsonResult.data[0];

                var imgDataArray = data.Deserialize<List<object>>();

                var jsonResult2 = System.Text.Json.JsonSerializer.Deserialize<ImageResultData>(imgDataArray[0].ToString());

                byte[] imageBytes = null;
                string imagePathOnDisk = null;
                if(jsonResult2.is_file && jsonResult2.name.StartsWith(@"C:\Users\Nop\AppData\Local\Temp\"))
                {
                    imagePathOnDisk = jsonResult2.name;

                    for (int i = 0; i < 10; i++)
                    {
                        try
                        {
                            using (var fs = new FileStream(jsonResult2.name, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                            {
                                using (var br = new BinaryReader(fs))
                                {
                                    imageBytes = br.ReadBytes((int)br.BaseStream.Length);
                                }
                            }
                            break;
                        }
                        catch (Exception)
                        {
                            Thread.Sleep(20);
                        }
                    }

                }
                else
                {
                    var imgDataB4 = imgDataArray[0].ToString();
                    var imgDataB4_2 = imgDataB4.Substring(imgDataB4.IndexOf(',') + 1);
                    imageBytes = Convert.FromBase64String(imgDataB4_2);

                }

                var data2 = ((System.Text.Json.JsonElement)jsonResult.data[1]).ToString();
                var predictionResult = JsonSerializer.Deserialize<PredictionResultInfo>(data2);

                var filename = prompt.Trim().Substring(0, Math.Min(64, prompt.Length)).Replace(" ", "-");
                filename = Regex.Replace(filename, @"[^a-zA-Z0-9\-]", string.Empty);

                if(imageBytes == null)
                {
                    return new PromptResultData()
                    {
                        ImageBytes = new byte[] { },
                        SafeName = $"{predictionResult.seed}_{filename}",
                        Prompt = prompt,
                        ImagePathOnDisk = imagePathOnDisk,
                        ResultInfo = predictionResult
                    };
                }

                return new PromptResultData()
                {
                    ImageBytes = imageBytes,
                    SafeName = $"{predictionResult.seed}_{filename}",
                    Prompt = prompt,
                    ImagePathOnDisk = imagePathOnDisk,
                    ResultInfo = predictionResult
                };
            }
        }

        private void ClientKafkaMessages_OnRegionChat(object? sender, RegionChat e)
        {
            var persona = Driver.ResolvePersonaId(e.FromPersonaId).Result;
            if (e.Message == "")
            {
                return;
            }

            if (e.Typing != 0)
            {
                return;
            }

            if (Driver.MyPersonaData != null && e.FromPersonaId == Driver.MyPersonaData.PersonaId)
            {
                return;
            }

            if (Driver.InitialTimestamp == 0 || e.Timestamp < Driver.InitialTimestamp)
            {
                Output($"[OLD] {persona.Name}: {e.Message}");
                return;
            }

            if (e.Message.StartsWith("/x "))
            {
                var args = e.Message.Split(" ");
                if (args.Length > 1)
                {
                    var target = args[1].ToLower().Trim();
                    var targetPersona = Driver.PersonasBySessionId.Where(n => n.Value.Handle.ToLower() == target).Select(n => n.Value).FirstOrDefault();
                    if (targetPersona != null)
                    {
                        Driver.SendPrivateMessage(targetPersona.PersonaId, "a\x0f\x10\x11%n%p%cBeep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boopBeep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop Beep boop");
                    }
                }
            }

            // Client crash (all):
            // /animate f8d1b2b4-41f4-3e02-5a9b-c2f8b299549b 0 1 0  (mode = 0, speed =1, type = 0) (or was this 2?)
            if (e.Message.StartsWith("/animate "))
            {
                var animationId = new SanUUID();
                byte playbackMode = 0;
                byte animationType = 1;
                float playbackSpeed = 1.0f;
                byte skeletonType = 0;

                var animationParams = e.Message.Split(" ");
                if (animationParams.Length > 1)
                {
                    try
                    {
                        animationId = new SanUUID(animationParams[1]);
                    }
                    catch (Exception)
                    {
                        Output("Animation failed. Invalid string: " + animationParams[1]);
                        return;
                    }
                }
                if (animationParams.Length > 2)
                {
                    try
                    {
                        playbackMode = byte.Parse(animationParams[2]);
                    }
                    catch (Exception)
                    {
                        Output("Animation failed. Invalid playbackMode: " + animationParams[2]);
                        return;
                    }
                }
                if (animationParams.Length > 3)
                {
                    try
                    {
                        playbackSpeed = float.Parse(animationParams[3]);
                    }
                    catch (Exception)
                    {
                        Output("Animation failed. Invalid playbackSpeed: " + animationParams[3]);
                        return;
                    }
                }
                //if (animationParams.Length > 4)
                //{
                //    try
                //    {
                //        animationType = byte.Parse(animationParams[4]);
                //    }
                //    catch (Exception)
                //    {
                //        Output("Animation failed. Invalid animationType: " + animationParams[4]);
                //        return;
                //    }
                //}


                foreach (var item in Driver.PersonasBySessionId)
                {
                    if (item.Value.AgentControllerId == null || item.Value.AgentComponentId == null)
                    {
                        continue;
                    }
                    Driver.RegionClient.SendPacket(
                        new SanProtocol.AgentController.AgentPlayAnimation(
                            item.Value.AgentControllerId.Value,
                            Driver.GetCurrentFrame(),
                            item.Value.AgentComponentId.Value,
                            animationId,
                            playbackSpeed,
                            skeletonType,
                            animationType,
                            playbackMode
                        )
                    );
                    Thread.Sleep(100);
                }

                return;
            }
            if (e.Message == "/warpall")
            {
                Random rand = new Random();
                foreach (var item in Driver.PersonasBySessionId)
                {
                    if (item.Value.AgentControllerId == null)
                    {
                        continue;
                    }

                    Driver.RegionClient.SendPacket(new SanProtocol.AgentController.WarpCharacter(
                        Driver.GetCurrentFrame(),
                        item.Value.AgentControllerId.Value,
                        MarkedPosition[0] + (1.5f - rand.NextSingle() * 3),
                        MarkedPosition[1] + (1.5f - rand.NextSingle() * 3),
                        MarkedPosition[2] + (1.5f - rand.NextSingle() * 3),
                        0,
                        0,
                        0,
                        0
                    ));
                    Thread.Sleep(100);
                }

                return;
            }
            if (e.Message.StartsWith("draw "))
            {
                var prompt = e.Message.Substring("draw ".Length).Trim();
                var result = GeneratePrompt(prompt);
                if (result != null)
                {
                    Driver.SendChatMessage(result);
                }
            }
            if (e.Message.StartsWith("sweater "))
            {
                var prompt = e.Message.Substring("sweater ".Length).Trim();
                var result = GenerateSweaterPrompt(prompt, $"{persona.Name} ({persona.Handle})");
                if (result != null)
                {
                    Driver.SendChatMessage(result);
                }
            }   
            if (e.Message.StartsWith("sign "))
            {
                var prompt = e.Message.Substring("sign ".Length).Trim();
                var result = GenerateSignPrompt(prompt, $"{persona.Name} ({persona.Handle})");
                if (result != null)
                {
                    Driver.SendChatMessage(result);
                }
            }
            //if (e.Message == "/restartthings")
            //{
            //    var testMessage = new SanProtocol.ClientRegion.ClientRuntimeInventoryUpdatedNotification("Butts");
            //    Driver.RegionClient.SendPacket(testMessage);
            //
            //    //Server crash (from spam?)
            //     for (int i = 0; i < 255; i++)
            //     {
            //         Driver.RegionClient.SendPacket(new SanProtocol.AgentController.SetCharacterNodePhysics(Driver.GetCurrentFrame(), (uint)i, (byte)i, 1, 1));
            //     }
            //}
            if (e.Message == "/jump")
            {
                foreach (var item in Driver.PersonasBySessionId)
                {
                    if (item.Value.AgentControllerId == null)
                    {
                        continue;
                    }

                    SanProtocol.AgentController.CharacterControllerInputReliable controllerInputPacket;
                    if (item.Value.LastControllerInput == null)
                    {
                        controllerInputPacket = new SanProtocol.AgentController.CharacterControllerInputReliable(
                            Driver.GetCurrentFrame(),
                            item.Value.AgentControllerId.Value,
                            1,
                            1,
                            0,
                            0,
                            0,
                            0,
                            0,
                            0,
                            0,
                            new Quaternion()
                            {
                                ModifierFlag = false,
                                UnknownA = 2,
                                UnknownB = false,
                                Values = new List<float>()
                                {
                                    0,
                                    0,
                                    0,
                                }
                            }
                        );
                    }
                    else
                    {
                        controllerInputPacket = new SanProtocol.AgentController.CharacterControllerInputReliable(
                            Driver.GetCurrentFrame(),
                            item.Value.AgentControllerId.Value,
                            1,
                            1,
                            item.Value.LastControllerInput.MoveRight,
                            item.Value.LastControllerInput.MoveForward,
                            item.Value.LastControllerInput.CameraYaw,
                            item.Value.LastControllerInput.CameraPitch,
                            item.Value.LastControllerInput.BehaviorYawDelta,
                            item.Value.LastControllerInput.BehaviorPitchDelta,
                            item.Value.LastControllerInput.CharacterForward,
                            item.Value.LastControllerInput.CameraForward
                        );
                    }

                    Driver.RegionClient.SendPacket(controllerInputPacket);
                    Thread.Sleep(10);
                }

                Thread.Sleep(1000);

                foreach (var item in this.Driver.PersonasBySessionId)
                {
                    if (item.Value.AgentControllerId == null)
                    {
                        continue;
                    }

                    SanProtocol.AgentController.CharacterControllerInputReliable controllerInputPacket;
                    if (item.Value.LastControllerInput == null)
                    {
                        controllerInputPacket = new SanProtocol.AgentController.CharacterControllerInputReliable(
                            Driver.GetCurrentFrame(),
                            item.Value.AgentControllerId.Value,
                            0,
                            0,
                            0,
                            0,
                            0,
                            0,
                            0,
                            0,
                            0,
                            new Quaternion()
                            {
                                ModifierFlag = false,
                                UnknownA = 2,
                                UnknownB = false,
                                Values = new List<float>()
                                {
                                    0,
                                    0,
                                    0,
                                }
                            }
                        );
                    }
                    else
                    {
                        controllerInputPacket = new SanProtocol.AgentController.CharacterControllerInputReliable(
                            Driver.GetCurrentFrame(),
                            item.Value.AgentControllerId.Value,
                            0,
                            0,
                            item.Value.LastControllerInput.MoveRight,
                            item.Value.LastControllerInput.MoveForward,
                            item.Value.LastControllerInput.CameraYaw,
                            item.Value.LastControllerInput.CameraPitch,
                            item.Value.LastControllerInput.BehaviorYawDelta,
                            item.Value.LastControllerInput.BehaviorPitchDelta,
                            item.Value.LastControllerInput.CharacterForward,
                            item.Value.LastControllerInput.CameraForward
                        );
                    }

                    Driver.RegionClient.SendPacket(controllerInputPacket);
                    Thread.Sleep(50);
                }

                return;
            }

            //// Server crash
            // Driver.RegionClient.SendPacket(new SanProtocol.AgentController.CharacterControllerInputReliable(GetCurrentFrame(), 2, 1, 1, 1, 0, 0, 0, 0, 0, 0, new Quaternion()));

            //// Server crash (from spam?)
            //foreach (var item in this.AllControllersBySession)
            //{
            //    for (int i = 0; i < 100; i++)
            //    {
            //        Driver.RegionClient.SendPacket(new SanProtocol.AgentController.WarpCharacterNode(item.Value.AgentControllerId, (uint)i));
            //    }
            //}

            //// Server crash (from spam?)
            // for (int i = 0; i < 255; i++)
            // {
            //     Driver.RegionClient.SendPacket(new SanProtocol.AgentController.SetCharacterNodePhysics(GetCurrentFrame(), MyAgentControllerId, (byte)i, 1, 1));
            // }


            if (persona.Name.Trim().ToLower() != "system")
            {
                if (!e.Message.Contains("://") && !e.Message.StartsWith('/'))
                {
                    if (TextToSpeechEnabled)
                    {
                        Driver.SpeakAzure($"{e.Message}");
                    }
                }
            }

            Output($"{persona.Name} [{persona.Handle}]: {e.Message}");
        }

        public Thread ConversationThread { get; set; }
        volatile bool _IsConversationThreadRunning = false;
        public void ConversationThreadEntrypoint()
        {
            while (_IsConversationThreadRunning)
            {
                foreach (var conversation in ConversationsByAgentControllerId)
                {
                    conversation.Value.ProcessVoiceBufferQueue();
                }

                Thread.Sleep(10);
            }
        }


        public class SpeechToTextResult
        {
            public bool Success { get; set; }
            public string Text { get; set; }
        }

        private void WorldStateMessages_OnDestroyCluster(object? sender, SanProtocol.WorldState.DestroyCluster e)
        {
            TargetPersonas.RemoveAll(n => n.ClusterId == e.ClusterId);
        }

        public class VoiceConversation
        {
            public PersonaData Persona { get; set; }
            public ConversationBot Bot { get; set; }

            public int Id { get; set; }
            public DateTime? TimeWeStartedListeningToTarget { get; set; } = null;
            public DateTime? LastTimeWeListened { get; set; } = null;
            public int LoudSamplesInBuffer { get; set; } = 0;

            public List<byte[]> VoiceBuffer { get; set; } = new List<byte[]>();
            public ConcurrentQueue<VoiceBufferQueueItem> VoiceBufferQueue = new ConcurrentQueue<VoiceBufferQueueItem>();

            public VoiceConversation(PersonaData persona, ConversationBot bot)
            {
                this.Persona = persona;
                this.Bot = bot;
            }

            public void AddVoiceData(AudioData data)
            {
                if (TimeWeStartedListeningToTarget == null)
                {
                    //  Console.WriteLine($"Started buffering voice for {Persona.UserName} ({Persona.Handle})");
                    TimeWeStartedListeningToTarget = DateTime.Now;
                }

                if (data.Volume > 500)
                {
                    LoudSamplesInBuffer++;
                    LastTimeWeListened = DateTime.Now;
                }

                if (data.Volume > 200)
                {
                    LastTimeWeListened = DateTime.Now;
                }

                VoiceBuffer.Add(data.Data);
            }

            public void Poll()
            {
                if (VoiceBuffer.Count == 0)
                {
                    return;
                }

                if (LastTimeWeListened != null)
                {
                    if ((DateTime.Now - LastTimeWeListened.Value).TotalMilliseconds > 1000)
                    {
                        VoiceBufferQueue.Enqueue(new VoiceBufferQueueItem()
                        {
                            VoiceBuffer = new List<byte[]>(VoiceBuffer.AsEnumerable()),
                            LoudSamplesInBuffer = LoudSamplesInBuffer
                        });

                        LoudSamplesInBuffer = 0;
                        VoiceBuffer.Clear();

                        TimeWeStartedListeningToTarget = null;
                        LastTimeWeListened = null;
                    }
                }

                if (TimeWeStartedListeningToTarget != null)
                {
                    if ((DateTime.Now - TimeWeStartedListeningToTarget.Value).TotalMilliseconds > 15000)
                    {
                        VoiceBufferQueue.Enqueue(new VoiceBufferQueueItem()
                        {
                            VoiceBuffer = new List<byte[]>(VoiceBuffer.AsEnumerable()),
                            LoudSamplesInBuffer = LoudSamplesInBuffer
                        });
                        LoudSamplesInBuffer = 0;
                        VoiceBuffer.Clear();

                        TimeWeStartedListeningToTarget = null;
                        LastTimeWeListened = null;
                    }
                }
            }

            public HashSet<string> Blacklist { get; set; } = new HashSet<string>()
            {
                "entity0x",
                "MetaverseKing-3934",
            };

            public class VoiceBufferQueueItem
            {
                public List<byte[]> VoiceBuffer { get; set; }
                public int LoudSamplesInBuffer { get; set; }
            }

            public bool ProcessVoiceBufferQueue()
            {
                while (VoiceBufferQueue.TryDequeue(out VoiceBufferQueueItem voiceBuffer))
                {
                    if (Blacklist.Contains(Persona.Handle.ToLower()))
                    {
                        return true;
                    }

                    //  Console.WriteLine(voiceBuffer.LoudSamplesInBuffer);
                    if (voiceBuffer.LoudSamplesInBuffer < 15)
                    {
                        return true;
                    }

                    //  Console.WriteLine($"Dumping voice buffer for {Persona.UserName} ({Persona.Handle})");
                    const int kFrameSize = 960;
                    const int kFrequency = 48000;

                    byte[] wavBytes;
                    using (MemoryStream ms = new MemoryStream())
                    {
                        var decoder = OpusDecoder.Create(kFrequency, 1);
                        var decompressedBuffer = new short[kFrameSize * 2];

                        foreach (var item in voiceBuffer.VoiceBuffer)
                        {
                            var numSamples = OpusPacketInfo.GetNumSamples(decoder, item, 0, item.Length);
                            var result = decoder.Decode(item, 0, item.Length, decompressedBuffer, 0, numSamples);

                            var decompressedBufferBytes = new byte[result * 2];
                            Buffer.BlockCopy(decompressedBuffer, 0, decompressedBufferBytes, 0, result * 2);

                            ms.Write(decompressedBufferBytes);
                        }

                        ms.Seek(0, SeekOrigin.Begin);
                        using (var rs = new RawSourceWaveStream(ms, new WaveFormat(48000, 16, 1)))
                        {
                            using (var wavStream = new MemoryStream())
                            {
                                WaveFileWriter.WriteWavFileToStream(wavStream, rs);
                                wavBytes = wavStream.ToArray();
                            }
                        }
                    }

                    using (var client = new HttpClient())
                    {
                        var result = client.PostAsync("http://127.0.0.1:5000/speech_to_text", new ByteArrayContent(wavBytes)).Result;
                        var resultString = result.Content.ReadAsStringAsync().Result;

                        var jsonResult = System.Text.Json.JsonSerializer.Deserialize<SpeechToTextResult>(resultString);
                        if (jsonResult?.Success == true)
                        {
                            if (jsonResult.Text.Trim().Length == 0)
                            {
                                return true;
                            }

                            Console.WriteLine($"{Persona.UserName} ({Persona.Handle}): {jsonResult.Text}");

                            if (Bot.ResultsShouldBeWritten)
                            {
                                Bot.Driver.SendChatMessage($"{Persona.UserName} ({Persona.Handle}): {jsonResult.Text}");
                            }

                            //if(Bot.ResultsShouldBeSpoken)
                            //{
                            //    Bot.Speak(jsonResult.Text);
                            //}

                            if (Bot.ResultsShouldBeDrawn)
                            {
                                var text = jsonResult.Text;
                                if (text.ToLower().Trim().StartsWith("draw "))
                                {
                                    var prompt = jsonResult.Text.Substring("draw ".Length).Trim();
                                    var promptResult = Bot.GeneratePrompt(prompt);
                                    Bot.Driver.SendChatMessage(prompt);
                                    Bot.Driver.SendChatMessage(promptResult);
                                }
                            }
                        }
                        else
                        {
                            Console.WriteLine("Speech to text failed");
                        }
                    }

                }

                return true;
            }

        }

        private void ClientVoiceMessages_OnLocalAudioData(object? sender, SanProtocol.ClientVoice.LocalAudioData e)
        {
            if (Driver.MyPersonaData == null || Driver.MyPersonaData.AgentControllerId == null)
            {
                return;
            }

            if (e.AgentControllerId == Driver.MyPersonaData.AgentControllerId.Value)
            {
                return;
            }

            var persona = Driver.PersonasBySessionId
                .Select(n => n.Value)
                .Where(n => n.AgentControllerId == e.AgentControllerId)
                .FirstOrDefault();
            if (persona == null)
            {
                return;
            }

            if (!ConversationsByAgentControllerId.ContainsKey(e.AgentControllerId))
            {
                ConversationsByAgentControllerId[e.AgentControllerId] = new VoiceConversation(persona, this);
            }
            var conversation = ConversationsByAgentControllerId[e.AgentControllerId];

            conversation.AddVoiceData(e.Data);
        }

        private void ClientRegionMessages_OnSetAgentController(object? sender, SanProtocol.ClientRegion.SetAgentController e)
        {
            Output("Sending to voice server: LocalAudioStreamState(1)...");
            Driver.VoiceClient.SendPacket(new SanProtocol.ClientVoice.LocalAudioStreamState(Driver.VoiceClient.InstanceId, 0, 1, 1));

            HaveIBeenCreatedYet = true;
            if (Driver.MyPersonaData != null && Driver.MyPersonaData.ClusterId != null)
            {
                if (InitialClusterPositions.ContainsKey(Driver.MyPersonaData.ClusterId.Value))
                {
                    Output("Found my initial cluster position. Updating my voice position");
                    Driver.SetVoicePosition(InitialClusterPositions[Driver.MyPersonaData.ClusterId.Value], true);

                    //var testMessage = new SanProtocol.ClientRegion.ClientRuntimeInventoryUpdatedNotification("Butts");
                    //Driver.RegionClient.SendPacket(testMessage);
                    //
                    ////Server crash (from spam?)
                    //for (int i = 0; i < 255; i++)
                    //{
                    //    Driver.RegionClient.SendPacket(new SanProtocol.AgentController.SetCharacterNodePhysics(Driver.GetCurrentFrame(), (uint)i, (byte)i, 1, 1));
                    //}
                }
            }
        }

        public bool HaveIBeenCreatedYet { get; set; } = false;
        public Dictionary<uint, List<float>> InitialClusterPositions { get; set; } = new Dictionary<uint, List<float>>();

        private void WorldStateMessages_OnCreateClusterViaDefinition(object? sender, SanProtocol.WorldState.CreateClusterViaDefinition e)
        {
            if (e.ResourceId == this.ItemClousterResourceId)
            {
                MarkedPosition = e.SpawnPosition;
                // Driver.WarpToPosition(e.SpawnPosition, e.SpawnRotation);
                Driver.SetVoicePosition(e.SpawnPosition, true);
            }
            else if (e.ResourceId == ItemClousterResourceId_Exclamation)
            {
                //TextToSpeechEnabled = !TextToSpeechEnabled;
                //Output("Use voice = " + TextToSpeechEnabled);
            }

            if (!HaveIBeenCreatedYet)
            {
                InitialClusterPositions[e.ClusterId] = e.SpawnPosition;
                return;
            }
        }

        private void Driver_OnOutput(object? sender, string message)
        {
            Output(message, sender?.GetType().Name ?? "Bot");
        }

        public void Output(string str, string sender = nameof(ConversationBot))
        {
            var date = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            var finalOutput = "";

            var lines = str.Replace("\r", "").Split("\n");
            foreach (var line in lines)
            {
                finalOutput += $"{date} [{sender}] {line}{Environment.NewLine}";
            }

            Console.Write(finalOutput);
        }

        private void ClientRegionMessages_OnRemoveUser(object? sender, SanProtocol.ClientRegion.RemoveUser e)
        {
            TargetPersonas.RemoveAll(n => n.SessionId == e.SessionId);
        }

        private void ClientRegionMessages_OnAddUser(object? sender, SanProtocol.ClientRegion.AddUser e)
        {
            var persona = Driver.PersonasBySessionId
                .Where(n => n.Key == e.SessionId)
                .Select(n => n.Value)
                .LastOrDefault();
            if (persona == null)
            {
                Output($"{e.UserName} ({e.Handle} | {e.PersonaId}) Entered the region, but we don't seem to be keeping track of them?");
                return;
            }

            TargetPersonas.Add(persona);
        }
    }
}
