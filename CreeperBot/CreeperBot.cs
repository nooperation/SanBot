using System.Collections.Concurrent;
using System.Diagnostics;
using SanBot.BaseBot;
using SanBot.Core;
using SanProtocol;
using SanProtocol.ClientKafka;
using SanProtocol.ClientVoice;
using static CreeperBot.VoiceConversation;
using static SanProtocol.Messages;

namespace CreeperBot
{
    public class CreeperBot : SimpleBot
    {
        public ConcurrentDictionary<uint, VoiceConversation> ConversationsByAgentControllerId { get; set; } = new ConcurrentDictionary<uint, VoiceConversation>();

        public Thread? ConversationThread { get; set; }

        private bool _IsConversationThreadRunning = false;

        public override Task Start()
        {
            ConfigFile config;
            var sanbotPath = Driver.GetSanbotConfigPath();
            var configPath = Path.Join(sanbotPath, "CreeperBot.config.json");

            try
            {
                config = ConfigFile.FromJsonFile(configPath);
            }
            catch (Exception ex)
            {
                throw new Exception("Missing or invalid config.json", ex);
            }

            Driver.RegionToJoin = new RegionDetails("nop", "flat");
            //Driver.RegionToJoin = new RegionDetails("sansar-studios", "sansar-park");
            Driver.AutomaticallySendClientReady = false;
            Driver.IgnoreRegionServer = true;
            Driver.UseVoice = false;
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
                    Thread.Yield();
                }
            }
        }


        public override void OnPacket(IPacket packet)
        {
            base.OnPacket(packet);

            switch (packet.MessageId)
            {
                case ClientKafkaMessages.RegionChat:
                    ClientKafkaMessages_OnRegionChat((RegionChat)packet);
                    break;
                case ClientKafkaMessages.RelationshipTable:
                    ClientKafkaMessages_OnRelationshipTable((RelationshipTable)packet);
                    break;
                case ClientKafkaMessages.PrivateChat:
                    ClientKafkaMessages_OnPrivateChat((PrivateChat)packet);
                    break;
                case ClientVoiceMessages.LocalAudioData:
                    ClientVoiceMessages_OnLocalAudioData((LocalAudioData)packet);
                    break;
                case ClientVoiceMessages.LocalAudioStreamState:
                    ClientVoiceMessages_OnLocalAudioStreamState((LocalAudioStreamState)packet);
                    break;
            }
        }

        public bool IsInitialized { get; set; } = false;

        private void ClientVoiceMessages_OnLocalAudioStreamState(LocalAudioStreamState e)
        {
            if (!IsInitialized)
            {
                SetVoicePosition(0, 0, 0);
                IsInitialized = true;
            }

            if (e.Mute == 1)
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
            Driver.VoiceClient.SendPacket(new LocalAudioPosition(
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

        private void ClientKafkaMessages_OnPrivateChat(PrivateChat e)
        {
            if (!Driver.VoiceClient.GotVersionPacket)
            {
                Output("[Old] Private chat: " + e.Message);
                return;
            }

            if (e.FromPersonaId != "1c3aad2b02584c90a6040da35a9743f9")
            {
                return;
            }

            if (e.Message == "init")
            {
                SetVoicePosition(-1, 93, 12);
            }
            if (e.Message.StartsWith("say"))
            {
                var message = e.Message[3..].Trim();
                Driver.SendChatMessage(message);
            }
            if (e.Message.StartsWith("goto "))
            {
                var message = e.Message[4..].Trim();
                var parts = message.Split(' ');
                if (parts.Length == 3)
                {
                    var x = float.Parse(parts[0]);
                    var y = float.Parse(parts[1]);
                    var z = float.Parse(parts[2]);

                    SetVoicePosition(x, y, z);
                }
                Driver.SendChatMessage(message);
            }
        }

        private void ClientKafkaMessages_OnRelationshipTable(RelationshipTable e)
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
            if (e.FromOther == 1 && e.Status == 2)
            {
                Console.WriteLine("Unblocking user " + e.Other);
                Driver.KafkaClient.SendPacket(new RelationshipOperation(
                    e.Other, 3
                ));
            }
            else if (e.FromOther == 1 && e.Status == 0)
            {
                Console.WriteLine("Accepting invite from " + e.Other);
                Driver.KafkaClient.SendPacket(new RelationshipOperation(
                    e.Other, 0
                ));
            }
        }

        public Dictionary<uint, string> AgentControllerToNameMap { get; set; } = new Dictionary<uint, string>();

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


            if (!AgentControllerToNameMap.ContainsKey(e.AgentControllerId))
            {
                if (persona != null)
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
                Output($"[OLD] {persona!.Name}: {e.Message}");
                return;
            }

            Output($"{persona!.Name} [{persona.Handle}]: {e.Message}");
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

        private void ClientVoiceMessages_OnLocalAudioData(LocalAudioData e)
        {
            // MAIN THREAD
            if (!ConversationsByAgentControllerId.ContainsKey(e.AgentControllerId))
            {
                ConversationsByAgentControllerId[e.AgentControllerId] = new VoiceConversation(new VoicePersona() { AgentControllerId = e.AgentControllerId }, this);
            }
            var conversation = ConversationsByAgentControllerId[e.AgentControllerId];

            conversation.AddVoiceData(e.Data);
        }
    }
}
