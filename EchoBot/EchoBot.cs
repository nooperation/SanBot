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

        public List<byte[]> VoiceBuffer { get; set; } = new List<byte[]>();
        public DateTime? TimeStartedListeningToTarget { get; set; } = null;

        // NOTE: It's not going to echo unless you actually summon it with this reaction first
        public SanUUID ItemClousterResourceId { get; set; } = new SanUUID("8d9484518db405d954204f2bfa900d0c"); // heart reaction thing
        public RegionDetails RegionToJoin { get; set; } = new RegionDetails("sansar-studios", "sansar-park");

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

        public override async Task Start()
        {
            Driver.AutomaticallySendClientReady = true;
            Driver.UseVoice = true;
            Driver.IgnoreRegionServer = false;

            using (var timer = new Timer(new TimerCallback(CheckVoiceDump), null, 0, 1000))
            {
                await base.Start();
            }
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
            var persona = TargetPersonas
                .Where(n => n.AgentControllerId == e.AgentControllerId)
                .FirstOrDefault();
            if (persona == null)
            {
                Output("OnLocalAudioPosition: UNKNOWN -> AgentControllerId=" + e.AgentControllerId);
                return;
            }
            else
            {
                if (IgnoredPeople.Contains(persona.Handle))
                {
                    return;
                }
                Output($"OnLocalAudioPosition: Handle={persona.Handle} PersonaId={persona.PersonaId} AgentControllerId={e.AgentControllerId} Position={e.Position}");
            }
        }

        private void ClientVoiceMessages_OnLocalAudioStreamState(LocalAudioStreamState e)
        {
            var persona = TargetPersonas
                .Where(n => n.AgentControllerId == e.AgentControllerId)
                .FirstOrDefault();
            if (persona == null)
            {
                Output("OnLocalAudioStreamState: UNKNOWN -> AgentControllerId=" + e.AgentControllerId);
                return;
            }
            else
            {
                if (IgnoredPeople.Contains(persona.Handle))
                {
                    return;
                }
                Output($"OnLocalAudioStreamState: Handle={persona.Handle}  PersonaId={persona.PersonaId} AgentControllerId={e.AgentControllerId} Mute={e.Mute}");
            }
        }

        private void WorldStateMessages_OnCreateAgentController(SanProtocol.WorldState.CreateAgentController e)
        {
            var persona = TargetPersonas
                .Where(n => n.AgentControllerId == e.AgentControllerId)
                .FirstOrDefault();
            if (persona == null)
            {
                Output($"OnCreateAgentController: UNKNOWN persona -> PersonaId={e.PersonaId} CharacterObjectId={e.CharacterObjectId} SessionId={e.SessionId}");
                return;
            }

            Output(e.ToString());
        }

        private void DumpVoiceBuffer()
        {
            // This is just kept to demonstrate a way of converting the audio to a usable format. Something that can be sent to Whisper or some other api
            Directory.CreateDirectory("out_voice");

            var wavBytes = Driver.OpusToRaw(VoiceBuffer);
            File.WriteAllBytes($"./out_voice/{DateTimeOffset.Now.ToUnixTimeMilliseconds()}.wav", wavBytes);

            VoiceBuffer.Clear();
        }

        private void WorldStateMessages_OnDestroyCluster(SanProtocol.WorldState.DestroyCluster e)
        {
            var unused = TargetPersonas.RemoveAll(n => n.ClusterId == e.ClusterId);
        }

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

            var persona = TargetPersonas
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
            var unused = TargetPersonas.RemoveAll(n => n.SessionId == e.SessionId);

            // Dump the remaining buffer for this user if we're currently listening to them
            if (agentControllerIdImListeningTo != null)
            {
                var persona = Driver.PersonasBySessionId
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

        private void ClientKafkaMessages_OnPrivateChat(PrivateChat e)
        {
            Output($"(PRIVMSG) {e.FromPersonaId}: {e.Message}");
        }

        public override void OnKafkaLoginSuccess(SanProtocol.ClientKafka.LoginReply e)
        {
            Driver.JoinRegion(RegionToJoin.PersonaHandle, RegionToJoin.SceneHandle).Wait();
        }
    }
}
