﻿using Concentus.Structs;
using NAudio.Wave;
using SanBot.BaseBot;
using SanBot.Core;
using SanProtocol;
using SanProtocol.ClientKafka;
using SanProtocol.ClientVoice;
using static SanProtocol.Messages;

namespace EchoBot
{
    public class EchoBot : SimpleBot
    {
        public DateTime? LastTimeWeListenedToOurTarget { get; set; } = null;
        public uint? agentControllerIdImListeningTo { get; set; } = null;
        public List<PersonaData> TargetPersonas { get; set; } = new List<PersonaData>();

        public SanUUID ItemClousterResourceId { get; set; } = new SanUUID("8d9484518db405d954204f2bfa900d0c"); // heart reaction thing

        public HashSet<string> TargetHandles { get; set; } = new HashSet<string>()
        {
            "nop",
        };

        public HashSet<string> IgnoredPeople { get; set; } = new HashSet<string>()
        {
            "VitaminC-0154",
            "TwoInTheBusch-6219",
            "yash-0879",
            "Bigtod-6269",
            "Ravioli",
            "olympusMons",
        };

        private void CheckVoiceDump(object? target)
        {
            Console.WriteLine("Check voice dump");

            if (LastTimeWeListenedToOurTarget != null)
            {
                if ((DateTime.Now - LastTimeWeListenedToOurTarget.Value).TotalMilliseconds > 500)
                {
                    DumpVoiceBuffer();

                    TimeStartedListeningToTarget = null;
                    LastTimeWeListenedToOurTarget = null;
                    agentControllerIdImListeningTo = null;
                }
            }

            if (TimeStartedListeningToTarget != null)
            {
                if ((DateTime.Now - TimeStartedListeningToTarget.Value).TotalMilliseconds > 29500)
                {
                    DumpVoiceBuffer();

                    TimeStartedListeningToTarget = null;
                    LastTimeWeListenedToOurTarget = null;
                    agentControllerIdImListeningTo = null;
                }
            }
        }

        private readonly Timer? voiceDumpTimer = null;
        public EchoBot()
        {
            ConfigFile config;
            string sanbotPath = Path.Join(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SanBot"
            );
            string configPath = Path.Join(sanbotPath, "SanBot.config.json");

            try
            {
                config = ConfigFile.FromJsonFile(configPath);
            }
            catch (Exception ex)
            {
                throw new Exception("Missing or invalid config.json", ex);
            }

            Start(config.Username, config.Password).Wait();

            voiceDumpTimer = new Timer(new TimerCallback(CheckVoiceDump), null, 0, 1000);
        }

        public override Task Init()
        {
            Task unused = base.Init();

            Driver.AutomaticallySendClientReady = true;
            Driver.UseVoice = true;
            Driver.OnOutput += Driver_OnOutput;

            return Task.CompletedTask;
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
                case WorldStateMessages.CreateClusterViaDefinition:
                    WorldStateMessages_OnCreateClusterViaDefinition((SanProtocol.WorldState.CreateClusterViaDefinition)packet);
                    break;
                case WorldStateMessages.DestroyCluster:
                    WorldStateMessages_OnDestroyCluster((SanProtocol.WorldState.DestroyCluster)packet);
                    break;
                case WorldStateMessages.CreateAgentController:
                    WorldStateMessages_OnCreateAgentController((SanProtocol.WorldState.CreateAgentController)packet);
                    break;
                case ClientVoiceMessages.LocalAudioData:
                    ClientVoiceMessages_OnLocalAudioData((SanProtocol.ClientVoice.LocalAudioData)packet);
                    break;
                case ClientVoiceMessages.LocalAudioStreamState:
                    ClientVoiceMessages_OnLocalAudioStreamState((SanProtocol.ClientVoice.LocalAudioStreamState)packet);
                    break;
                case ClientVoiceMessages.LocalAudioPosition:
                    ClientVoiceMessages_OnLocalAudioPosition((SanProtocol.ClientVoice.LocalAudioPosition)packet);
                    break;
            }
        }

        private void ClientVoiceMessages_OnLocalAudioPosition(LocalAudioPosition e)
        {
            PersonaData? persona = TargetPersonas
                .Where(n => n.AgentControllerId == e.AgentControllerId)
                .FirstOrDefault();
            if (persona == null)
            {
                Console.WriteLine("OnLocalAudioPosition: UNKNOWN -> AgentControllerId=" + e.AgentControllerId);
                return;
            }
            else
            {
                if (IgnoredPeople.Contains(persona.Handle))
                {
                    return;
                }
                Console.WriteLine($"OnLocalAudioPosition: Handle={persona.Handle} PersonaId={persona.PersonaId} AgentControllerId={e.AgentControllerId} Position={e.Position}");
            }
        }

        private void ClientVoiceMessages_OnLocalAudioStreamState(LocalAudioStreamState e)
        {
            PersonaData? persona = TargetPersonas
                .Where(n => n.AgentControllerId == e.AgentControllerId)
                .FirstOrDefault();
            if (persona == null)
            {
                Console.WriteLine("OnLocalAudioStreamState: UNKNOWN -> AgentControllerId=" + e.AgentControllerId);
                return;
            }
            else
            {
                if (IgnoredPeople.Contains(persona.Handle))
                {
                    return;
                }
                Console.WriteLine($"OnLocalAudioStreamState: Handle={persona.Handle}  PersonaId={persona.PersonaId} AgentControllerId={e.AgentControllerId} Mute={e.Mute}");
            }
        }

        private void WorldStateMessages_OnCreateAgentController(SanProtocol.WorldState.CreateAgentController e)
        {
            PersonaData? persona = TargetPersonas
                .Where(n => n.AgentControllerId == e.AgentControllerId)
                .FirstOrDefault();
            if (persona == null)
            {
                Console.WriteLine("OnLocalAudioStreamState: Persona={e.PersonaId} CharacterObjectId={e.CharacterObjectId} SessionId={e.SessionId}");
                return;
            }

            Console.WriteLine($"CreateAgentController: Handle={e.PersonaId} Persona={e.PersonaId} CharacterObjectId={e.CharacterObjectId} SessionId={e.SessionId}");
            Console.WriteLine(e.ToString());
        }

        private void DumpVoiceBuffer()
        {
            Output("Dumping voice buffer...");

            byte[] wavBytes;
            using (MemoryStream ms = new())
            {
                const int kFrameSize = 960;
                const int kFrequency = 48000;

                OpusDecoder decoder = OpusDecoder.Create(kFrequency, 1);
                short[] decompressedBuffer = new short[kFrameSize * 2];

                foreach (byte[] item in VoiceBuffer)
                {
                    int numSamples = OpusPacketInfo.GetNumSamples(decoder, item, 0, item.Length);
                    int result = decoder.Decode(item, 0, item.Length, decompressedBuffer, 0, numSamples);

                    byte[] decompressedBufferBytes = new byte[result * 2];
                    Buffer.BlockCopy(decompressedBuffer, 0, decompressedBufferBytes, 0, result * 2);

                    ms.Write(decompressedBufferBytes);
                }

                long unused = ms.Seek(0, SeekOrigin.Begin);
                using RawSourceWaveStream rs = new(ms, new WaveFormat(kFrequency, 16, 1));
                using MemoryStream wavStream = new();
                WaveFileWriter.WriteWavFileToStream(wavStream, rs);
                wavBytes = wavStream.ToArray();
            }

            ++VoiceBufferIndex;
            VoiceBuffer.Clear();
        }

        private void WorldStateMessages_OnDestroyCluster(SanProtocol.WorldState.DestroyCluster e)
        {
            int unused = TargetPersonas.RemoveAll(n => n.ClusterId == e.ClusterId);
        }

        public List<byte[]> VoiceBuffer { get; set; } = new List<byte[]>();
        public DateTime? TimeStartedListeningToTarget { get; set; } = null;
        public int VoiceBufferIndex { get; set; } = 0;

        private void ClientVoiceMessages_OnLocalAudioData(SanProtocol.ClientVoice.LocalAudioData e)
        {
            if (Driver.MyPersonaData?.AgentControllerId == null)
            {
                return;
            }

            if (e.AgentControllerId == Driver.MyPersonaData.AgentControllerId.Value)
            {
                return;
            }

            PersonaData? persona = TargetPersonas
                .Where(n => n.AgentControllerId == e.AgentControllerId)
                .FirstOrDefault();
            if (persona == null)
            {
                return;
            }

            if (agentControllerIdImListeningTo != null && e.AgentControllerId != agentControllerIdImListeningTo.Value)
            {
                return;
            }

            if (TimeStartedListeningToTarget == null)
            {
                TimeStartedListeningToTarget = DateTime.Now;
            }

            agentControllerIdImListeningTo = e.AgentControllerId;
            if (e.Data.Volume > 300)
            {
                LastTimeWeListenedToOurTarget = DateTime.Now;
            }
            VoiceBuffer.Add(e.Data.Data);

            Driver.VoiceClient.SendPacket(new LocalAudioData(
                e.Instance,
                Driver.MyPersonaData.AgentControllerId.Value,
                new AudioData(Driver.VoiceClient.CurrentSequence, e.Data.Volume, e.Data.Data),
                new SpeechGraphicsData(Driver.VoiceClient.CurrentSequence, e.SpeechGraphicsData.Data),
                0
            ));
            Driver.VoiceClient.CurrentSequence++;
        }

        private void WorldStateMessages_OnCreateClusterViaDefinition(SanProtocol.WorldState.CreateClusterViaDefinition e)
        {
            if (e.ResourceId == ItemClousterResourceId)
            {
                Driver.WarpToPosition(e.SpawnPosition, e.SpawnRotation);
                Driver.SetVoicePosition(e.SpawnPosition, true);
            }
        }

        private void Driver_OnOutput(object? sender, string message)
        {
            Output(message, sender?.GetType().Name ?? "Bot");
        }

        public void Output(string str, string sender = nameof(EchoBot))
        {
            string date = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            string finalOutput = "";

            string[] lines = str.Replace("\r", "").Split("\n");
            foreach (string line in lines)
            {
                finalOutput += $"{date} [{sender}] {line}{Environment.NewLine}";
            }

            Console.Write(finalOutput);
        }

        private void ClientRegionMessages_OnRemoveUser(SanProtocol.ClientRegion.RemoveUser e)
        {
            int unused = TargetPersonas.RemoveAll(n => n.SessionId == e.SessionId);

            // Dump the remaining buffer for this user if we're currently listening to them
            if (agentControllerIdImListeningTo != null)
            {
                PersonaData? persona = Driver.PersonasBySessionId
                    .Where(n => n.Key == e.SessionId)
                    .Select(n => n.Value)
                    .LastOrDefault();
                if (persona == null)
                {
                    return;
                }

                if (persona.AgentComponentId == agentControllerIdImListeningTo.Value)
                {
                    DumpVoiceBuffer();

                    TimeStartedListeningToTarget = null;
                    LastTimeWeListenedToOurTarget = null;
                    agentControllerIdImListeningTo = null;
                }
            }
        }

        private void ClientRegionMessages_OnAddUser(SanProtocol.ClientRegion.AddUser e)
        {
            PersonaData? persona = Driver.PersonasBySessionId
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

        private void ClientKafkaMessages_OnPrivateChat(PrivateChat e)
        {
            Output($"(PRIVMSG) {e.FromPersonaId}: {e.Message}");
        }

        public override void OnKafkaLoginSuccess(SanProtocol.ClientKafka.LoginReply e)
        {
            Console.WriteLine("EchoBot::OnKafkaLoginSuccess");

            Driver.JoinRegion("nop", "flat").Wait();
        }
    }
}
