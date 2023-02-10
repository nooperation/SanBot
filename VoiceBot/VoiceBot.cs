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
using static SanBot.Database.Services.PersonaService;
using SanBot.Database;
using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using Concentus.Structs;
using System.Collections.Concurrent;
using SanProtocol.WorldState;

namespace VoiceBot
{
    public class VoiceBot
    {
        public Driver Driver { get; set; }
        public bool TryToAvoidInterruptingPeople { get; set; } = true;
        public bool TextToSpeechEnabled { get; set; } = true;

        public SanUUID ItemClousterResourceId { get; set; } = new SanUUID("04c2d5a7ea3d6fb47af66669cfdc9f9a"); // heart reaction thing
                                                                                                               // public SanUUID ItemClousterResourceId { get; set; } = new SanUUID("04c2d5a7ea3d6fb47af66669cfdc9f9a"); // heart reaction thing
                                                                                                               //public SanUUID ItemClousterResourceId { get; set; } = new SanUUID("b8534d067b0613a509b0155e0dacb0b2"); // fox doll
        public List<float> MarkedPosition { get; set; } = new List<float>() { 0, 0, 0};


        public DateTime LastTimeSomeoneSpoke { get; set; } = DateTime.Now;


        public VoiceBot()
        {

            ConfigFile config;
            var sanbotPath = Path.Join(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SanBot"
            );
            var configPath = Path.Join(sanbotPath, "VoiceBot.config.json");

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

            Driver.KafkaClient.ClientKafkaMessages.OnRegionChat += ClientKafkaMessages_OnRegionChat;

            Driver.RegionClient.ClientRegionMessages.OnClientSetRegionBroadcasted += ClientRegionMessages_OnClientSetRegionBroadcasted;

            Driver.RegionClient.WorldStateMessages.OnCreateClusterViaDefinition += WorldStateMessages_OnCreateClusterViaDefinition;


           Driver.RegionToJoin = new RegionDetails("nop", "flat2");
           // Driver.RegionToJoin = new RegionDetails("terje-9294", "terjesworld");
            Driver.AutomaticallySendClientReady = true;
            Driver.UseVoice = true;
            Driver.StartAsync(config).Wait();

            Stopwatch watch = new Stopwatch();

            while (true)
            {
                if (!Driver.Poll())
                {
                    Thread.Yield();
                }
            }
        }

        static void OutputSpeechSynthesisResult(SpeechSynthesisResult speechSynthesisResult)
        {
            switch (speechSynthesisResult.Reason)
            {
                case ResultReason.SynthesizingAudioCompleted:
                    Console.WriteLine($"Speech synthesized");
                    break;
                case ResultReason.Canceled:
                    var cancellation = SpeechSynthesisCancellationDetails.FromResult(speechSynthesisResult);
                    Console.WriteLine($"CANCELED: Reason={cancellation.Reason}");

                    if (cancellation.Reason == CancellationReason.Error)
                    {
                        Console.WriteLine($"CANCELED: ErrorCode={cancellation.ErrorCode}");
                        Console.WriteLine($"CANCELED: ErrorDetails=[{cancellation.ErrorDetails}]");
                        Console.WriteLine($"CANCELED: Did you set the speech resource key and region values?");
                    }
                    break;
                default:
                    break;
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

            if (Driver.InitialTimestamp == 0 || e.Timestamp < Driver.InitialTimestamp)
            {
                Output($"[OLD] {persona.Name}: {e.Message}");
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
                if(animationParams.Length > 1)
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
                    if(item.Value.AgentControllerId == null || item.Value.AgentComponentId == null)
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
            if(e.Message == "/warpall!")
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
            if (e.Message == "/jump")
            {
                foreach (var item in Driver.PersonasBySessionId)
                {
                    if(item.Value.AgentControllerId == null)
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
                    Thread.Sleep(50);
                }

                Thread.Sleep(1000);

                foreach (var item in this.Driver.PersonasBySessionId)
                {
                    if(item.Value.AgentControllerId == null)
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


            if(persona.Name.Trim().ToLower() != "system")
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
        
        private void ClientRegionMessages_OnClientSetRegionBroadcasted(object? sender, SanProtocol.ClientRegion.ClientSetRegionBroadcasted e)
        {
            Output($"Sending to voice server: LocalSetRegionBroadcasted({e.Broadcasted})...");
            Driver.VoiceClient.SendPacket(new LocalSetRegionBroadcasted(e.Broadcasted));
        }

        private void WorldStateMessages_OnCreateClusterViaDefinition(object? sender, SanProtocol.WorldState.CreateClusterViaDefinition e)
        {
            if (e.ResourceId == this.ItemClousterResourceId)
            {
                MarkedPosition = e.SpawnPosition;
            }
        }

        private void Driver_OnOutput(object? sender, string message)
        {
            Output(message, sender?.GetType().Name ?? "Bot");
        }

        public void Output(string str, string sender = nameof(VoiceBot))
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


    }
}
