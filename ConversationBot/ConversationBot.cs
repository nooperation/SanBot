using System.Collections.Concurrent;
using System.Diagnostics;
using NAudio.Wave;
using ObjectMaker;
using SanBot.BaseBot;
using SanBot.Core;
using SanProtocol;
using SanProtocol.ClientKafka;
using SanProtocol.ClientVoice;
using SweaterMaker;
using static ConversationBot.ImageGenerator;
using static ConversationBot.OpenAiChat;
using static ConversationBot.VoiceConversation;
using static SanBot.Core.Driver;
using static SanProtocol.Messages;

namespace ConversationBot
{
    public class ConversationBot : SimpleBot
    {
        public DateTime? LastTimeWeListenedToOurTarget { get; set; } = null;
        public uint? agentControllerIdImListeningTo { get; set; } = null;
        public List<PersonaData> TargetPersonas { get; set; } = new List<PersonaData>();


        public SanUUID ItemClousterResourceId_Exclamation { get; set; } = new SanUUID("beb1c1d298aa865fc5d5326dada8d2a7"); // ! reaction thing
        public SanUUID ItemClousterResourceId { get; set; } = new SanUUID("8d9484518db405d954204f2bfa900d0c"); // heart reaction thing
                                                                                                               // public SanUUID ItemClousterResourceId { get; set; } = new SanUUID("8d9484518db405d954204f2bfa900d0c"); // heart reaction thing
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

        public RegionDetails RegionToJoin { get; set; } = new RegionDetails("sansar-studios", "sansar-park");

        private AzureApi? _azureApi = null;

        public override Task Start()
        {
            ConfigFile config;
            var sanbotPath = GetSanbotConfigPath();
            var configPath = Path.Join(sanbotPath, "EchoBot.config.json");

            try
            {
                config = ConfigFile.FromJsonFile(configPath);
            }
            catch (Exception ex)
            {
                throw new Exception("Missing or invalid config.json", ex);
            }

            var azureConfigPath = Path.Join(sanbotPath, "azure.json");
            if (File.Exists(azureConfigPath))
            {
                _azureApi = new AzureApi(azureConfigPath, Driver.Speak);
            }

            Driver.RegionToJoin = RegionToJoin;
            Driver.AutomaticallySendClientReady = true;
            Driver.UseVoice = true;
            Driver.StartAsync(config.Username, config.Password).Wait();

            var watch = new Stopwatch();

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

            //_IsConversationThreadRunning = false;
            //ConversationThread.Join();

            //return Task.CompletedTask;
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
                    ClientVoiceMessages_OnLocalAudioData((LocalAudioData)packet);
                    break;
                case ClientKafkaMessages.RegionChat:
                    ClientKafkaMessages_OnRegionChat((RegionChat)packet);
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
            return (float)Math.Sqrt(((x1 - x2) * (x1 - x2)) + ((y1 - y2) * (y1 - y2)) + ((z1 - z2) * (z1 - z2)));
        }

        private void ClientRegionMessages_OnClientRuntimeInventoryUpdatedNotification(SanProtocol.ClientRegion.ClientRuntimeInventoryUpdatedNotification e)
        {
            Console.WriteLine("ClientRegionMessages_OnClientRuntimeInventoryUpdatedNotification: " + e.Message);
        }

        #region Sweater_Maker_9000
        public string GenerateSweaterPrompt(string prompt, string creatorName)
        {
            var isTiled = false;
            var size = 512;

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

Created by {creatorName} on {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}
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
            var isTiled = false;
            var size = 512;

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

Created by {creatorName} on {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}
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
            var isTiled = false;

            prompt = prompt.Trim();
            if (prompt.ToLower().StartsWith("tiled "))
            {
                isTiled = true;
                prompt = prompt.Substring("tiled ".Length).Trim();
            }

            var truncatedPrompt = prompt.Substring(0, Math.Min(128, prompt.Length));
            var promptResult = GetImage(truncatedPrompt, isTiled).Result;

            var url = AWSUtils.UploadBytes(promptResult).Result;

            return url;
        }

        private readonly List<string> LastMessages = new();

        private void ClientKafkaMessages_OnRegionChat(RegionChat e)
        {
            var persona = Driver.ResolvePersonaId(e.FromPersonaId).Result ?? new SanBot.Database.Services.PersonaService.PersonaDto()
            {
                Handle = ".UNKNOWN",
                Id = new Guid(),
                Name = ".UNKNOWN"
            };
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
                var playbackSpeed = 1.0f;
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
                var rand = new Random();
                foreach (var item in Driver.PersonasBySessionId)
                {
                    if (item.Value.AgentControllerId == null)
                    {
                        continue;
                    }

                    Driver.RegionClient.SendPacket(new SanProtocol.AgentController.WarpCharacter(
                        Driver.GetCurrentFrame(),
                        item.Value.AgentControllerId.Value,
                        MarkedPosition[0] + (1.5f - (rand.NextSingle() * 3)),
                        MarkedPosition[1] + (1.5f - (rand.NextSingle() * 3)),
                        MarkedPosition[2] + (1.5f - (rand.NextSingle() * 3)),
                        0,
                        0,
                        0,
                        0
                    ));
                    Thread.Sleep(100);
                }

                return;
            }
            if (e.Message.ToLower().StartsWith("/speak "))
            {
                ChatbotPrompt = e.Message.Substring("/speak ".Length).Trim();
                _azureApi?.SpeakAzure(ChatbotPrompt, true);
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
                for (var i = 0; i < 255; i++)
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
                        _azureApi?.SpeakAzure($"{e.Message}");
                    }
                }
            }

            if (ChatbotEnabled)
            {
                //RunChatQuery(persona.Name, persona.Handle, e.Message);
            }

            Output($"{persona.Name} [{persona.Handle}]: {e.Message}");
        }

        public void SetTypingIndicator(bool showIndicator)
        {
            Driver.KafkaClient.SendPacket(new RegionChat(
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
            if (Driver.IsSpeaking)
            {
                Console.WriteLine($"Ignored request because we are currently speaking: " + query);
                return;
            }

            if (PlayTypingIndicator)
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

            var result = RunPrompt(ChatbotPrompt, query, personaName, previousHistory).Result;
            Output("AI RESULT: " + result);

            ConversationHistoriesByPersonaHandle[personaHandle].Add(new ConversationData()
            {
                Query = query,
                Response = result
            });

            var historyCount = ConversationHistoriesByPersonaHandle[personaHandle].Count;
            if (historyCount > NumHistoriesToKeep)
            {
                ConversationHistoriesByPersonaHandle[personaHandle].RemoveRange(0, historyCount - NumHistoriesToKeep);
            }

            if (ChatbotSayResult)
            {
                Driver.SendChatMessage(result);
            }
            if (ChatbotSpeakResult)
            {
                _azureApi?.SpeakAzure(result, true);
            }

            if (PlayTypingIndicator)
            {
                SetTypingIndicator(false);
            }
        }
        public Thread? ConversationThread { get; set; }

        private volatile bool _IsConversationThreadRunning = false;
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



        private void ClientVoiceMessages_OnLocalAudioData(LocalAudioData e)
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
                ConversationsByAgentControllerId[e.AgentControllerId] = new VoiceConversation(persona, this, _azureApi);
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
                if (Driver.MyPersonaData != null)
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

            var translated = _azureApi?.ToEnglishAzure(result.Text);
            if (translated == null)
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
            Driver.VoiceClient.SendPacket(new LocalAudioStreamState(Driver.VoiceClient.InstanceId, 0, 1, 1));

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
            if (e.ResourceId == ItemClousterResourceId)
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
