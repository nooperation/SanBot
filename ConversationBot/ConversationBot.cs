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
using static SanBot.Core.Driver;
using Microsoft.CognitiveServices.Speech.Translation;
using NAudio.Utils;
using SanBot.Database.Models;
using static EchoBot.VoiceConversation;
using NAudio;
using ConversationBot;
using static ConversationBot.ImageGenerator;
using static ConversationBot.OpenAiChat;
using static SanWebApi.Json.ExtractionResponse;
using NAudio.Codecs;
using Renci.SshNet.Messages;
using SanBot.BaseBot;
using static SanProtocol.Messages;

namespace EchoBot
{
    public class ConversationBot : SimpleBot
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

        public bool SpeechToTextSayEnabled { get; set; } = false;

        public bool TextToSpeechEnabled { get; set; } = false;
        public bool DrawEnabled { get; set; } = false;
        public bool SpeechToTextDrawEnabled { get; set; } = false;
        public bool SweaterMakerEnabled { get; set; } = false;
        public bool SignMakerEnabled { get; set; } = false;

        public bool ChatbotEnabled { get; set; } = false;
        public bool ChatbotSayResult { get; set; } = false;
        public bool ChatbotSpeakResult { get; set; } = true;
      //  public string ChatbotPrompt { get; set; } = "You are a bot that is playing sansar and talking in the social hub. Your name is bot. There aren't many people around and you're bored. Do not respond in long sentences";
   //     public string ChatbotPrompt { get; set; } = "You are a gamer. You only talk in gamer and twitch memes.";
     
        
        public string ChatbotPrompt { get; set; } = "prompt You are playing sansar. You are open to talk about anything. You must only respond with personal attacks";
      //  public string ChatbotPrompt { get; set; } = "I want you to act as a bored player that is playing sansar and talking in the social hub. You will only speak in short responses. You do not respond in multiple sentences";
       //ds public string ChatbotPrompt { get; set; } = "I want you to act as a normal player that is playing sansar and talking in the social hub. Your name is bot. There aren't many people around and you're bored. Do not respond in long sentences";
      //  public string ChatbotPrompt { get; set; } = "prompt I want you to be a cringy furry squirrel who dances with pompoms. You say things like \"1 2 3 power pompom\". you always make fun of people's name. You talk in very short sentences. You do not greet people.";
        public int NumHistoriesToKeep { get; set; } = 4;
        public double ChatbotActiveConversationDurationSeconds { get; set; } = 0;
        public Dictionary<string, List<ConversationData>> ConversationHistoriesByPersonaHandle { get; set; } = new Dictionary<string, List<ConversationData>>();
        public Dictionary<string, DateTime> ChatbotLastConversationTimeByPersonaHandle { get; set; } = new Dictionary<string, DateTime>();
        public bool MustContainKeyword { get; set; } = true;
        public bool PlayTypingIndicator { get; set; } = true;
        public bool PlaySoundIndicator { get; set; } = true;
        public float MaxListenDistance { get; set; } = 7.0f;
        public float AutoConversationDistance { get; set; } = 1.5f;

        public List<string> ChatbotKeywords { get; set; } = new List<string>()
        {
            "okay google",
            "alexa",
            "bot",
        };

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
                config = ConfigFile.FromJsonFile(configPath);
            }
            catch (Exception ex)
            {
                throw new Exception("Missing or invalid config.json", ex);
            }

            Driver = new Driver();
            Driver.OnOutput += Driver_OnOutput;

            Driver.RegionToJoin = new RegionDetails("nop", "flat");
        //  Driver.RegionToJoin = new RegionDetails("anuamun", "bamboo-central");
        //   Driver.RegionToJoin = new RegionDetails("djm3n4c3-9174", "reactive-dance-demo");

            //   Driver.RegionToJoin = new RegionDetails("fayd", "android-s-dream");
            //   //  Driver.RegionToJoin = new RegionDetails("test", "base2");
            // Driver.RegionToJoin = new RegionDetails("sansar-studios", "r-d-starter-inventory-collection");
            // Driver.RegionToJoin = new RegionDetails("sansar-studios", "r-d-starter-inventory-collection");
            //  Driver.RegionToJoin = new RegionDetails("solasnagealai", "once-upon-a-midnight-dream");
       //        Driver.RegionToJoin = new RegionDetails("sansar-studios", "social-hub");
            //    Driver.RegionToJoin = new RegionDetails("turtle-4332", "turtles-campfire");
         //  Driver.RegionToJoin = new RegionDetails("wally-sansar", "eapycadvo");
      //  sansar://sansar.com/experience/wally-sansar/eapycadvo?instance=88faa5bd-0b86-47bb-836b-eec3eaa7989a&event=e8c62e02&target_transform=%280.0%2c%200.0%2c%200.89884615%2c%20-0.4382642%29%2c%20%28-1.9210045%2c%20-4.4623494%2c%201.2324542%2c%200.0%29

            Driver.AutomaticallySendClientReady = true;
            Driver.UseVoice = true;
            Driver.StartAsync(config.Username, config.Password).Wait();

            Stopwatch watch = new Stopwatch();

            
            _IsConversationThreadRunning = true;
            ConversationThread = new Thread(new ThreadStart(ConversationThreadEntrypoint));
            ConversationThread.Start();

            while (true)
            {
                if (!Driver.Poll())
                {
                    // MAIN THREAD
                    foreach (var conversation in ConversationsByAgentControllerId)
                    {
                        conversation.Value.Poll();
                    }
                }
            }

            _IsConversationThreadRunning = false;
            ConversationThread.Join();
        }
        
        public override void OnPacket(IPacket packet)
        {
            base.OnPacket(packet);

            switch (packet.MessageId)
            {
                case ClientRegionMessages.AddUser:
                    ClientRegionMessages_OnAddUser((SanProtocol.ClientRegion.AddUser)packet);
                    break;
                case ClientRegionMessages.RemoveUser:
                    ClientRegionMessages_OnRemoveUser((SanProtocol.ClientRegion.RemoveUser)packet);
                    break;
                case ClientRegionMessages.SetAgentController:
                    ClientRegionMessages_OnSetAgentController((SanProtocol.ClientRegion.SetAgentController)packet);
                    break;
                case WorldStateMessages.CreateClusterViaDefinition:
                    WorldStateMessages_OnCreateClusterViaDefinition((SanProtocol.WorldState.CreateClusterViaDefinition)packet);
                    break;
                case WorldStateMessages.DestroyCluster:
                    WorldStateMessages_OnDestroyCluster((SanProtocol.WorldState.DestroyCluster)packet);
                    break;
                case ClientVoiceMessages.LocalAudioData:
                    ClientVoiceMessages_OnLocalAudioData((SanProtocol.ClientVoice.LocalAudioData)packet);
                    break;
                case ClientKafkaMessages.RegionChat:
                    ClientKafkaMessages_OnRegionChat((SanProtocol.ClientKafka.RegionChat)packet);
                    break;
                case ClientRegionMessages.ClientRuntimeInventoryUpdatedNotification:
                    ClientRegionMessages_OnClientRuntimeInventoryUpdatedNotification((SanProtocol.ClientRegion.ClientRuntimeInventoryUpdatedNotification)packet);
                    break;
                case AnimationComponentMessages.CharacterTransform:
                    AnimationComponentMessages_OnCharacterTransform((SanProtocol.AnimationComponent.CharacterTransform)packet);
                    break;
            }
        }

        private void AnimationComponentMessages_OnCharacterTransform(SanProtocol.AnimationComponent.CharacterTransform e)
        {
            var persona = Driver.PersonasBySessionId
                .Where(n => n.Value.AgentComponentId == e.ComponentId)
                .Select(n => n.Value)
                .FirstOrDefault();
            if (persona == null)
            {
                return;
            }

            if (Driver.MyPersonaData == null || persona.SessionId == Driver.MyPersonaData.SessionId)
            {
                return;
            }

            var myPos = Driver.MyPersonaData.Position;
            var distToTarget = Distance(myPos[0], persona.Position[0], myPos[1], persona.Position[1], myPos[2], persona.Position[2]);

            //if (distToTarget <= 2.0)
            //{
            //    if (!ChatbotLastConversationTimeByPersonaHandle.ContainsKey(persona.Handle))
            //    {
            //        var isCurrentlyTalkingToSomeone = ChatbotLastConversationTimeByPersonaHandle.Values.Any(n => (DateTime.Now - n).TotalSeconds < 15);
            //        if(!isCurrentlyTalkingToSomeone)
            //        {
            //            if(ChatbotEnabled)
            //            {
            //                Console.WriteLine("Activating chat for " + persona.Handle);
            //                ChatbotLastConversationTimeByPersonaHandle[persona.Handle] = DateTime.Now;
            //                //Driver.SpeakAzure("Hello " + persona.Handle);
            //                RunChatQuery(persona.UserName, persona.Handle, "Hello");
            //            }
            //        }
            //    }
            //}
            //else if (distToTarget >= 10)
            //{
            //    ChatbotLastConversationTimeByPersonaHandle.Remove(persona.Handle);
            //}
        }

        public static float Distance(float x1, float x2, float y1, float y2, float z1, float z2)
        {
            return (float)Math.Sqrt((x1 - x2) * (x1 - x2) + (y1 - y2) * (y1 - y2) + (z1 - z2) * (z1 - z2));
        }

        private void ClientRegionMessages_OnClientRuntimeInventoryUpdatedNotification(SanProtocol.ClientRegion.ClientRuntimeInventoryUpdatedNotification e)
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
            var promptResult = ImageGenerator.GetImage(truncatedPrompt, isTiled, size).Result;

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
            var promptResult = ImageGenerator.GetImage(truncatedPrompt, isTiled, size).Result;

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
            var promptResult = ImageGenerator.GetImage(truncatedPrompt, isTiled).Result;

            var url = AWSUtils.UploadBytes(promptResult).Result;

            return url;
        }

        private List<string> LastMessages = new List<string>();

        private void ClientKafkaMessages_OnRegionChat(RegionChat e)
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

            if (Driver.InitialTimestamp == 0 || e.Timestamp < Driver.InitialTimestamp)
            {
                Output($"[OLD] {persona.Name}: {e.Message}");
                return;
            }

            if (Driver.MyPersonaData != null && e.FromPersonaId == Driver.MyPersonaData.PersonaId)
            {
                return;
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
            if(e.Message.ToLower().StartsWith("/speak "))
            {
                ChatbotPrompt = e.Message.Substring("/speak ".Length).Trim();
                Driver.SpeakAzure(ChatbotPrompt, true);
            }
            if (e.Message.ToLower().StartsWith("prompt "))
            {
                ChatbotPrompt = e.Message.Substring("prompt ".Length).Trim();
                ConversationHistoriesByPersonaHandle.Clear();
                ChatbotLastConversationTimeByPersonaHandle.Clear();
            }
            if (DrawEnabled && e.Message.StartsWith("draw "))
            {
                var prompt = e.Message.Substring("draw ".Length).Trim();
                var result = GeneratePrompt(prompt);
                if (result != null)
                {
                    Driver.SendChatMessage(result);
                }
            }
            if (SweaterMakerEnabled && e.Message.StartsWith("sweater "))
            {
                var prompt = e.Message.Substring("sweater ".Length).Trim();
                var result = GenerateSweaterPrompt(prompt, $"{persona.Name} ({persona.Handle})");
                if (result != null)
                {
                    Driver.SendChatMessage(result);
                }
            }   
            if (SignMakerEnabled && e.Message.StartsWith("sign "))
            {
                var prompt = e.Message.Substring("sign ".Length).Trim();
                var result = GenerateSignPrompt(prompt, $"{persona.Name} ({persona.Handle})");
                if (result != null)
                {
                    Driver.SendChatMessage(result);
                }
            }
            if (e.Message == "/restartthings")
            {
                var testMessage = new SanProtocol.ClientRegion.ClientRuntimeInventoryUpdatedNotification("Butts");
                Driver.RegionClient.SendPacket(testMessage);
            
                //Server crash (from spam?)
                 for (int i = 0; i < 255; i++)
                 {
                     Driver.RegionClient.SendPacket(new SanProtocol.AgentController.SetCharacterNodePhysics(Driver.GetCurrentFrame(), (uint)i, (byte)i, 1, 1));
                 }
            }
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

            if(ChatbotEnabled)
            {
                 //RunChatQuery(persona.Name, persona.Handle, e.Message);
            }

            Output($"{persona.Name} [{persona.Handle}]: {e.Message}");
        }

        public void SetTypingIndicator(bool showIndicator)
        {
            Driver.KafkaClient.SendPacket(new SanProtocol.ClientKafka.RegionChat(
                new SanUUID(),
                new SanUUID(),
                "",
                0,
                "",
                0,
                (byte)(showIndicator ? 1 : 0),
                0,
                0
            ));
        }

        public void PlayNotification()
        {
            var bytes = File.ReadAllBytes("siri.raw");
            Driver.Speak(bytes);
        }

        public void RunChatQuery(string personaName, string personaHandle, string query)
        {
            if(Driver.IsSpeaking)
            {
                Console.WriteLine($"Ignored request because we are currently speaking: " + query);
                return;
            }

            if(PlayTypingIndicator)
            {
                SetTypingIndicator(true);
            }
            if (PlaySoundIndicator)
            {
                PlayNotification();
            }

            if (!ConversationHistoriesByPersonaHandle.ContainsKey(personaHandle))
            {
                ChatbotLastConversationTimeByPersonaHandle.TryAdd(personaHandle, DateTime.Now);
                ConversationHistoriesByPersonaHandle.Add(personaHandle, new List<ConversationData>()
                {
                    new ConversationData
                    {
                        Query = "Hello",
                        Response = $"Hi {personaName}, Welcome to Sansar"
                    }
                });
            }

            ChatbotLastConversationTimeByPersonaHandle[personaHandle] = DateTime.Now;
            var previousHistory = ConversationHistoriesByPersonaHandle[personaHandle].Take(NumHistoriesToKeep).ToList();

            var result = OpenAiChat.RunPrompt(ChatbotPrompt, query, personaName, previousHistory).Result;
            Output("AI RESULT: " + result);

            ConversationHistoriesByPersonaHandle[personaHandle].Add(new ConversationData()
            {
                Query = query,
                Response = result
            });

            var historyCount = ConversationHistoriesByPersonaHandle[personaHandle].Count;
            if(historyCount > NumHistoriesToKeep)
            {
                ConversationHistoriesByPersonaHandle[personaHandle].RemoveRange(0, historyCount - NumHistoriesToKeep);
            }

            if(ChatbotSayResult)
            {
                Driver.SendChatMessage(result);
            }
            if(ChatbotSpeakResult)
            {
                Driver.SpeakAzure(result, true);
            }

            if(PlayTypingIndicator)
            {
                SetTypingIndicator(false);
            }
        }
        public Thread ConversationThread { get; set; }
        volatile bool _IsConversationThreadRunning = false;
        public void ConversationThreadEntrypoint()
        {
            while (_IsConversationThreadRunning)
            {
                // SECONDARY THREAD
                foreach (var conversation in ConversationsByAgentControllerId)
                {
                    conversation.Value.ProcessVoiceBufferQueue();
                }

                Thread.Sleep(10);
            }
        }

        private void WorldStateMessages_OnDestroyCluster(SanProtocol.WorldState.DestroyCluster e)
        {
            TargetPersonas.RemoveAll(n => n.ClusterId == e.ClusterId);
        }

        public class VoiceAudioStream : PullAudioInputStreamCallback
        {
            private MemoryStream ms;

            public VoiceAudioStream(byte[] data)
            {
                ms = new MemoryStream(data);
            }

            public override void Close()
            {
                ms.Close();
            }

            public override int Read(byte[] dataBuffer, uint size)
            {
                return ms.Read(dataBuffer, 0, (int)size);
            }
        }

        private void ClientVoiceMessages_OnLocalAudioData(SanProtocol.ClientVoice.LocalAudioData e)
        {
            // MAIN THREAD

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

            if (persona.Position == null || Driver.MyPersonaData == null || Driver.MyPersonaData.LastVoicePosition == null)
            {
                return;
            }

            var myPosition = Driver.MyPersonaData.LastVoicePosition;
            var distance = Distance(
                myPosition[0], persona.Position[0],
                myPosition[1], persona.Position[1],
                myPosition[2], persona.Position[2]
            );


            if (!ConversationsByAgentControllerId.ContainsKey(e.AgentControllerId))
            {
                ConversationsByAgentControllerId[e.AgentControllerId] = new VoiceConversation(persona, this);
                ConversationsByAgentControllerId[e.AgentControllerId].OnSpeechToText += ConversationBot_OnSpeechToText;
            }
            var conversation = ConversationsByAgentControllerId[e.AgentControllerId];

            if (distance > MaxListenDistance && (conversation.LastTimeWeListened == null || (DateTime.Now - conversation.LastTimeWeListened.Value).TotalSeconds > 5))
            {
                return;
            }

          //  Console.WriteLine($"Distance to {persona.UserName} = {distance}");
            conversation.AddVoiceData(e.Data);
        }

        private void ConversationBot_OnSpeechToText(object? sender, SpeechToTextItem result)
        {
            // MAIN THREAD
            if (result.Text.Length == 0)
            {
                return;
            }

            Console.WriteLine($"{result.Persona.UserName} ({result.Persona.Handle}): {result.Text}");

            if (SpeechToTextSayEnabled)
            {
                Driver.SendChatMessage($"{result.Persona.UserName} ({result.Persona.Handle}): {result.Text}");
            }

            if (DrawEnabled && SpeechToTextDrawEnabled)
            {
                var text = result.Text;
                if (text.ToLower().Trim().StartsWith("draw "))
                {
                    var prompt = result.Text.Substring("draw ".Length).Trim();
                    var promptResult = GeneratePrompt(prompt);
                    Driver.SendChatMessage(prompt);
                    Driver.SendChatMessage(promptResult);
                }
            }
            if (ChatbotEnabled)
            {
                var phrase = result.Text.ToLower();

                var isInConversationWithTargetUser = false;
                if (ChatbotLastConversationTimeByPersonaHandle.ContainsKey(result.Persona.Handle))
                {
                    var lastConversationTime = ChatbotLastConversationTimeByPersonaHandle[result.Persona.Handle];
                    var elapsedTime = (long)(DateTime.Now - lastConversationTime).TotalSeconds;

                    isInConversationWithTargetUser = elapsedTime <= ChatbotActiveConversationDurationSeconds;
                }

                var phraseContainsKeyword = false;
                foreach (var keyword in ChatbotKeywords)
                {
                    if (phrase.StartsWith(keyword))
                    {
                        phrase = phrase.Substring(keyword.Length + 1);
                        phraseContainsKeyword = true;
                        break;
                    }
                    else if (phrase.Contains(keyword))
                    {
                        phraseContainsKeyword = true;
                        break;
                    }
                }

                var distanceToPlayer = AutoConversationDistance + 1000.0f;
                if(Driver.MyPersonaData != null)
                {
                    var myPos = Driver.MyPersonaData.Position;
                    distanceToPlayer = Distance(myPos[0], result.Persona.Position[0], myPos[1], result.Persona.Position[1], myPos[2], result.Persona.Position[2]);
                }

                if (
                    (ChatbotActiveConversationDurationSeconds > 0 && isInConversationWithTargetUser) ||
                    (AutoConversationDistance > 0 && distanceToPlayer <= AutoConversationDistance) ||
                    (MustContainKeyword && phraseContainsKeyword)
                )
                {
                    Console.WriteLine("Result = " + result.Text);
                    RunChatQuery(result.Persona.UserName, result.Persona.Handle, phrase);
                }
            }

        }

        private void ConversationBot_OnSpeechToText_Translated(object? sender, SpeechToTextItem result)
        {
            // MAIN THREAD
            if (result.Text.Length == 0)
            {
                return;
            }
            var translated = ToEnglishAzure(result.Text);
            if(translated == null)
            {
                return;
            }

            Console.WriteLine($"{result.Persona.UserName} ({result.Persona.Handle}): [{translated.sourceLanguage}] {translated.translatedText}");

            if (SpeechToTextSayEnabled)
            {
                Driver.SendChatMessage($"{result.Persona.UserName} ({result.Persona.Handle}): [{translated.sourceLanguage}] {translated.translatedText}");
            }

            if (DrawEnabled && SpeechToTextDrawEnabled)
            {
                var text = result.Text;
                if (text.ToLower().Trim().StartsWith("draw "))
                {
                    var prompt = result.Text.Substring("draw ".Length).Trim();
                    var promptResult = GeneratePrompt(prompt);
                    Driver.SendChatMessage(prompt);
                    Driver.SendChatMessage(promptResult);
                }
            }

            Console.WriteLine("Result = " + result.Text);
        }

        private void ClientRegionMessages_OnSetAgentController(SanProtocol.ClientRegion.SetAgentController e)
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

        private void WorldStateMessages_OnCreateClusterViaDefinition(SanProtocol.WorldState.CreateClusterViaDefinition e)
        {
            if (e.ResourceId == this.ItemClousterResourceId)
            {
                MarkedPosition = e.SpawnPosition;
                Driver.SetPosition(
                    e.SpawnPosition,
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
                    },
                    0xFFFFFFFFFFFFFFFF, 
                    true
                );
                Driver.SetVoicePosition(e.SpawnPosition, true);
            }
            else if (e.ResourceId == ItemClousterResourceId_Exclamation)
            {
                ChatbotEnabled = !ChatbotEnabled;
                Output("ChatbotEnabled = " + ChatbotEnabled);
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

        private void ClientRegionMessages_OnRemoveUser(SanProtocol.ClientRegion.RemoveUser e)
        {
            TargetPersonas.RemoveAll(n => n.SessionId == e.SessionId);
        }

        private void ClientRegionMessages_OnAddUser(SanProtocol.ClientRegion.AddUser e)
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
