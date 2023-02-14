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
using static EchoBot.VoiceConversation;

namespace EchoBot
{
    public class CreeperBot
    {
        public Driver Driver { get; set; }

        public CreeperBot()
        {
            ConfigFile config;
            var sanbotPath = Path.Join(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SanBot"
            );
            var configPath = Path.Join(sanbotPath, "CreeperBot.config.json");

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
            Driver.KafkaClient.ClientKafkaMessages.OnRelationshipTable += ClientKafkaMessages_OnRelationshipTable;
            Driver.KafkaClient.ClientKafkaMessages.OnPrivateChat += ClientKafkaMessages_OnPrivateChat;

            Driver.VoiceClient.ClientVoiceMessages.OnLocalAudioData += ClientVoiceMessages_OnLocalAudioData;
            Driver.VoiceClient.ClientVoiceMessages.OnLocalAudioStreamState += ClientVoiceMessages_OnLocalAudioStreamState;

           // Driver.RegionToJoin = new RegionDetails("nop", "flat2");
            Driver.RegionToJoin = new RegionDetails("sansar-studios", "sansar-park");
            Driver.AutomaticallySendClientReady = false;
            Driver.IgnoreRegionServer = true;
            Driver.UseVoice = false;
            Driver.StartAsync(config).Wait();

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
                    Thread.Yield();
                }
            }

            _IsConversationThreadRunning = false;
            ConversationThread.Join();
        }

        public bool IsInitialized { get; set; } = false;

        private void ClientVoiceMessages_OnLocalAudioStreamState(object? sender, LocalAudioStreamState e)
        {
            if(!IsInitialized)
            {
                SetVoicePosition(-1, 93, 12);
                IsInitialized = true;
            }

            if(e.Mute == 1)
            {
                Console.WriteLine($"{e.AgentControllerId} is active");
            }
            else
            {
                Console.WriteLine($"{e.AgentControllerId} is mute");
            }
        }

        private void SetVoicePosition(float x, float y, float z)
        {
            Console.WriteLine($"SetVoicePosition: {x}, {y}, {z}");
            Driver.VoiceClient.SendPacket(new SanProtocol.ClientVoice.LocalAudioPosition(
                Driver.VoiceClient.CurrentSequence++,
                Driver.VoiceClient.InstanceId,
                new List<float>()
                {
                    x,
                    y,
                    z,
                },
                0
            ));
        }

        private void ClientKafkaMessages_OnPrivateChat(object? sender, PrivateChat e)
        {
            if(!Driver.VoiceClient.GotVersionPacket)
            {
                Output("[Old] Private chat: " + e.Message);
                return;
            }

            if(e.FromPersonaId != "1c3aad2b02584c90a6040da35a9743f9")
            {
                return;
            }

            if(e.Message == "init")
            {
                SetVoicePosition(-1, 93, 12);
            }
            if (e.Message.StartsWith("say"))
            {
                var message = e.Message[3..].Trim();
                this.Driver.SendChatMessage(message);
            }
            if (e.Message.StartsWith("goto "))
            {
                var message = e.Message[4..].Trim();
                var parts = message.Split(' ');
                if(parts.Length == 3)
                {
                    var x = float.Parse(parts[0]);
                    var y = float.Parse(parts[1]);
                    var z = float.Parse(parts[2]);

                    SetVoicePosition(x, y, z);
                }
                this.Driver.SendChatMessage(message);
            }
        }

        private void ClientKafkaMessages_OnRelationshipTable(object? sender, RelationshipTable e)
        {
            if (!Driver.VoiceClient.GotVersionPacket)
            {
                return;
            }

            // Lol this resets the instance because I sent a bad packet
            // Output(e.ToString());
            // if(e.FromOther == 1 && e.Status == 0)
            // {
            //     Console.WriteLine("Sending status 1");
            //     Driver.VoiceClient.SendPacket(new SanProtocol.ClientKafka.RelationshipOperation(
            //         e.Other, 1
            //     ));
            // }

            Output(e.ToString());
            if(e.FromOther == 1 && e.Status == 2)
            {
                Console.WriteLine("Unblocking user " + e.Other);
                Driver.KafkaClient.SendPacket(new SanProtocol.ClientKafka.RelationshipOperation(
                    e.Other, 3
                ));
            }
            else if (e.FromOther == 1 && e.Status == 0)
            {
                Console.WriteLine("Accepting invite from " + e.Other);
                Driver.KafkaClient.SendPacket(new SanProtocol.ClientKafka.RelationshipOperation(
                    e.Other, 0
                ));
            }
        }

        public Dictionary<uint, string> AgentControllerToNameMap { get; set; } = new Dictionary<uint, string>();

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


            if(!AgentControllerToNameMap.ContainsKey(e.AgentControllerId))
            {
                if(persona != null)
                {
                    AgentControllerToNameMap.Add(e.AgentControllerId, $"{persona.Name} ({persona.Handle})");
                }
                else
                {
                    AgentControllerToNameMap.Add(e.AgentControllerId, e.FromPersonaId.Format());
                }
            }

            if (!Driver.VoiceClient.GotVersionPacket)
            {
                Output($"[OLD] {persona.Name}: {e.Message}");
                return;
            }

            Output($"{persona.Name} [{persona.Handle}]: {e.Message}");
        }

        private void Driver_OnOutput(object? sender, string message)
        {
            Output(message, sender?.GetType().Name ?? "Bot");
        }

        public void Output(string str, string sender = nameof(CreeperBot))
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


        public ConcurrentDictionary<uint, VoiceConversation> ConversationsByAgentControllerId { get; set; } = new ConcurrentDictionary<uint, VoiceConversation>();

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

        public static float Distance(float x1, float x2, float y1, float y2, float z1, float z2)
        {
            return (float)Math.Sqrt((x1 - x2) * (x1 - x2) + (y1 - y2) * (y1 - y2) + (z1 - z2) * (z1 - z2));
        }

        private void ClientVoiceMessages_OnLocalAudioData(object? sender, SanProtocol.ClientVoice.LocalAudioData e)
        {
            // MAIN THREAD
            if (!ConversationsByAgentControllerId.ContainsKey(e.AgentControllerId))
            {
                ConversationsByAgentControllerId[e.AgentControllerId] = new VoiceConversation(new VoiceConversation.VoicePersona() { AgentControllerId = e.AgentControllerId }, this);
                ConversationsByAgentControllerId[e.AgentControllerId].OnSpeechToText += ConversationBot_OnSpeechToText;
            }
            var conversation = ConversationsByAgentControllerId[e.AgentControllerId];

            conversation.AddVoiceData(e.Data);
        }

        private void ConversationBot_OnSpeechToText(object? sender, SpeechToTextItem result)
        {
            // MAIN THREAD
            if (result.Text.Length == 0)
            {
                return;
            }

            if(result.Persona.AgentControllerId != null && AgentControllerToNameMap.ContainsKey(result.Persona.AgentControllerId.Value))
            {
                Console.WriteLine($"{AgentControllerToNameMap[result.Persona.AgentControllerId.Value]}): {result.Text}");
            }
            else
            {
                Console.WriteLine($"{result.Persona.AgentControllerId}): {result.Text}");
            }
        }
    }
}
