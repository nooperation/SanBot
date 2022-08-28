﻿using SanWebApi.Json;
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

namespace VoiceBot
{
    public class VoiceBot
    {
        public Driver Driver { get; set; }
        public Dictionary<uint, SanProtocol.ClientRegion.AddUser> PersonaSessionMap { get; }
        public uint MyAgentControllerId { get; private set; }
        public ulong MyAgentComponentId { get; private set; }
        public ulong VoiceSequence { get; set; }

        public uint? CurrentlyListeningTo { get; set; } = null;
        public Dictionary<ulong, SanProtocol.AnimationComponent.CharacterTransform> ComponentPositions { get; set; } = new Dictionary<ulong, SanProtocol.AnimationComponent.CharacterTransform>();

        public Queue<SanProtocol.AnimationComponent.CharacterTransform> CharacterTransformBuffer { get; set; } = new Queue<SanProtocol.AnimationComponent.CharacterTransform>();


        public SanUUID ItemClousterResourceId { get; set; } = new SanUUID("04c2d5a7ea3d6fb47af66669cfdc9f9a"); // heart reaction thing
                                                                                                               // public SanUUID ItemClousterResourceId { get; set; } = new SanUUID("04c2d5a7ea3d6fb47af66669cfdc9f9a"); // heart reaction thing
                                                                                                               //public SanUUID ItemClousterResourceId { get; set; } = new SanUUID("b8534d067b0613a509b0155e0dacb0b2"); // fox doll


        public class AzureConfigPayload
        {
            public string key1 { get; set; }
            public string region { get; set; }
        }

        private AzureConfigPayload AzureConfig { get; set; }
        private SanBot.Database.PersonaDatabase Database { get; }


        public VoiceBot()
        {
            this.Database = new PersonaDatabase();

            ConfigFile config;
            this.PersonaSessionMap = new Dictionary<uint, SanProtocol.ClientRegion.AddUser>();
            var sanbotPath = Path.Join(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SanBot"
            );
            var configPath = Path.Join(sanbotPath, "EchoBot.config.json");
            var azureConfigPath = Path.Join(sanbotPath, "azure.json");

            try
            {
                var configFileContents = File.ReadAllText(configPath);
                config = Newtonsoft.Json.JsonConvert.DeserializeObject<ConfigFile>(configFileContents);
            }
            catch (Exception ex)
            {
                throw new Exception("Missing or invalid config.json", ex);
            }


            try
            {
                var configFileContents = File.ReadAllText(azureConfigPath);
                var result = System.Text.Json.JsonSerializer.Deserialize<AzureConfigPayload>(configFileContents);
                if (result == null || result.key1.Length == 0 || result.region.Length == 0)
                {
                    throw new Exception("Invalid azure config");
                }

                AzureConfig = result;
            }
            catch (Exception ex)
            {
                throw new Exception("Missing or invalid azure config", ex);
            }


            Driver = new Driver();
            Driver.OnOutput += Driver_OnOutput;

            Driver.KafkaClient.ClientKafkaMessages.OnPrivateChat += ClientKafkaMessages_OnPrivateChat;
            Driver.KafkaClient.ClientKafkaMessages.OnLoginReply += ClientKafkaMessages_OnLoginReply;
            Driver.KafkaClient.ClientKafkaMessages.OnRegionChat += ClientKafkaMessages_OnRegionChat;

            Driver.RegionClient.ClientRegionMessages.OnUserLoginReply += ClientRegionMessages_OnUserLoginReply;
            Driver.RegionClient.ClientRegionMessages.OnAddUser += ClientRegionMessages_OnAddUser;
            Driver.RegionClient.ClientRegionMessages.OnRemoveUser += ClientRegionMessages_OnRemoveUser;
            Driver.RegionClient.ClientRegionMessages.OnSetAgentController += ClientRegionMessages_OnSetAgentController;

            Driver.RegionClient.ClientRegionMessages.OnClientSetRegionBroadcasted += ClientRegionMessages_OnClientSetRegionBroadcasted;

            Driver.RegionClient.WorldStateMessages.OnCreateClusterViaDefinition += WorldStateMessages_OnCreateClusterViaDefinition;
            Driver.RegionClient.WorldStateMessages.OnCreateAgentController += WorldStateMessages_OnCreateAgentController;

            Driver.RegionClient.SimulationMessages.OnTimestamp += SimulationMessages_OnTimestamp;
            Driver.RegionClient.SimulationMessages.OnInitialTimestamp += SimulationMessages_OnInitialTimestamp;

            Driver.VoiceClient.ClientVoiceMessages.OnLoginReply += ClientVoiceMessages_OnLoginReply;

            AudioThread = new VoiceAudioThread(Driver.VoiceClient.SendRaw);

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

        internal async Task<string?> GetPersonaName(SanUUID personaId)
        {
            var persona = await ResolvePersonaId(personaId);
            if (persona != null)
            {
                return $"{persona.Name} ({persona.Handle})";
            }

            return personaId.Format();
        }

        internal async Task<PersonaDto?> ResolvePersonaId(SanUUID personaId)
        {
            var personaGuid = new Guid(personaId.Format());

            var persona = await Database.PersonaService.GetPersona(personaGuid);
            if (persona != null)
            {
                return persona;
            }

            var profiles = await Driver.WebApi.GetProfiles(new List<string>() {
                personaId.Format(),
            });

            PersonaDto? foundPersona = null;
            foreach (var item in profiles.Data)
            {
                if (new Guid(item.AvatarId) == personaGuid)
                {
                    foundPersona = new PersonaDto
                    {
                        Id = personaGuid,
                        Handle = item.AvatarHandle,
                        Name = item.AvatarName
                    };
                }

                await Database.PersonaService.UpdatePersonaAsync(new Guid(item.AvatarId), item.AvatarHandle, item.AvatarName);
            }

            return foundPersona;
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

        public class AudioStreamHandler : PushAudioOutputStreamCallback
        {
            public List<byte[]> CollectedBytes { get; set; } = new List<byte[]>();

            public VoiceBot Bot { get; set; }
            public AudioStreamHandler(VoiceBot bot)
            {
                this.Bot = bot;
            }

            public override uint Write(byte[] dataBuffer)
            {
                Bot.Output($"Write() - Added {dataBuffer.Length} bytes to the buffer");
                CollectedBytes.Add(dataBuffer);

                return (uint)dataBuffer.Length;
            }

            public override void Close()
            {
                Bot.Output("Audio data is ready to be consumed");

                long totalSize = 0;
                foreach (var item in CollectedBytes)
                {
                    totalSize += item.Length;
                }

                var buffer = new byte[totalSize];
                var bufferOffset = 0;

                foreach (var item in CollectedBytes)
                {
                    item.CopyTo(buffer, bufferOffset);
                    bufferOffset += item.Length;
                }

                Bot.Speak(buffer);
                base.Close();
            }
        }

        public VoiceAudioThread AudioThread { get; set; }

        public class VoiceAudioThread
        {
            private Action<byte[]> Callback { get; set; }
            private ConcurrentQueue<List<byte[]>> AudioDataQueue { get; set; } = new ConcurrentQueue<List<byte[]>>();
            private Thread ConsumerThread { get; set; }

            private volatile bool _isRunning = false;

            public VoiceAudioThread(Action<byte[]> callback)
            {
                Callback = callback;

                ConsumerThread = new Thread(new ThreadStart(Consumer));
            }

            public void Start()
            {
                _isRunning = true;
                ConsumerThread.Start();
            }
            public void Stop()
            {
                _isRunning = false;
                ConsumerThread.Join();
            }

            public void EnqueueData(List<byte[]> audioData)
            {
                AudioDataQueue.Enqueue(audioData);
            }

            public void Consumer()
            {
                long previousTickCount = 0;

                while (_isRunning)
                {
                    if (AudioDataQueue.TryDequeue(out List<byte[]> rawAudioPackets))
                    {
                        Console.WriteLine("VoiceAudioThread: Audio payload found. Sending it...");

                        foreach (var packetBytes in rawAudioPackets)
                        {
                            Callback(packetBytes);
                            while ((DateTimeOffset.Now.Ticks - previousTickCount) < 200000)
                            {
                                // nothing
                            }

                            previousTickCount = DateTimeOffset.Now.Ticks;
                        }
                    }
                    else
                    {
                        Thread.Yield();
                    }
                }
            }
        }

        public void Speak(byte[] rawPcmBytes)
        {
            const int kFrameSize = 960;
            const int kFrequency = 48000;

            var pcmSamples = new short[rawPcmBytes.Length / 2];
            Buffer.BlockCopy(rawPcmBytes, 0, pcmSamples, 0, rawPcmBytes.Length);

            OpusEncoder encoder = OpusEncoder.Create(kFrequency, 1, Concentus.Enums.OpusApplication.OPUS_APPLICATION_VOIP);

            var totalFrames = pcmSamples.Length / 960;

            List<byte[]> messages = new List<byte[]>();
            for (int i = 0; i < totalFrames; i++)
            {
                var compressedBytes = new byte[1276];
                var written = encoder.Encode(pcmSamples, kFrameSize * i, kFrameSize, compressedBytes, 0, compressedBytes.Length);

                var packetBytes = new LocalAudioData(
                    Driver.CurrentInstanceId,
                    MyAgentControllerId,
                    new AudioData(VoiceSequence, 1, compressedBytes.Take(written).ToArray()),
                    new SpeechGraphicsData(VoiceSequence, new byte[] { }),
                    1
                ).GetBytes();
                VoiceSequence++;

                messages.Add(packetBytes);
            }

            AudioThread.EnqueueData(messages);
        }

        public HashSet<string> PreviousMessages { get; set; } = new HashSet<string>();
        public void Speak(string message)
        {
            if (message.Length >= 256)
            {
                Output($"Ignored, too long ${message.Length}");
                return;
            }

            //if ((DateTime.Now - LastSpoke).TotalSeconds <= 1)
            //{
            //    Output($"Ignored, only {(DateTime.Now - LastSpoke).TotalSeconds} since last speaking");
            //    return;
            //}

            if (PreviousMessages.Contains(message))
            {
                return;
            }
            PreviousMessages.Add(message);


            //var audioCallbackHandler = new AudioStreamHandler(this);
            // audioCallbackHandler.Write(File.ReadAllBytes(@test2.pcm"));

            var speechConfig = SpeechConfig.FromSubscription(AzureConfig.key1, AzureConfig.region);
            speechConfig.SetSpeechSynthesisOutputFormat(SpeechSynthesisOutputFormat.Raw48Khz16BitMonoPcm);
            speechConfig.SpeechSynthesisVoiceName = "en-US-JennyNeural";
            //speechConfig.SpeechSynthesisVoiceName = "en-US-AnaNeural";
          
            var audioCallbackHandler = new AudioStreamHandler(this);
            using (var audioConfig = AudioConfig.FromStreamOutput(audioCallbackHandler))
            {
                using (var speechSynthesizer = new SpeechSynthesizer(speechConfig, audioConfig))
                {
                    var speechSynthesisResult = speechSynthesizer.SpeakTextAsync(message).Result;
                    OutputSpeechSynthesisResult(speechSynthesisResult);
                }
            }

            LastSpoke = DateTime.Now;
        }

        DateTime LastSpoke = DateTime.Now;
        private void ClientKafkaMessages_OnRegionChat(object? sender, RegionChat e)
        {
            var persona = ResolvePersonaId(e.FromPersonaId).Result;
            if (e.Message == "")
            {
                return;
            }

            if (e.Typing != 0)
            {
                return;
            }

            if (InitialTimestamp == 0 || e.Timestamp < InitialTimestamp)
            {
                Output($"[OLD] {persona.Name}: {e.Message}");
                return;
            }

            if (!e.Message.Contains("://") && !e.Message.StartsWith('/'))
            {
                //if (persona.Name.ToLower() == "nop")
                //{
                //    Speak($"nawp: {e.Message}");
                //}
                //else
                //{
                //    Speak($"{persona.Name}: {e.Message}");
                //}
                Speak($"{e.Message}");
            }
            Output($"{persona.Name} [{persona.Handle}]: {e.Message}");
        }

        void WarpToPosition(List<float> position3, List<float> rotation4)
        {
            if (position3.Count != 3)
            {
                throw new Exception($"{nameof(WarpToPosition)} Expected float3 position, got float{position3.Count}");
            }
            if (rotation4.Count != 4)
            {
                throw new Exception($"{nameof(rotation4)} Expected float4 rotation, got float{rotation4.Count}");
            }

            Driver.RegionClient.SendPacket(new SanProtocol.AgentController.WarpCharacter(
                GetCurrentFrame(),
                MyAgentControllerId,
                position3[0],
                position3[1],
                position3[2],
                rotation4[0],
                rotation4[1],
                rotation4[2],
                rotation4[3]
            ));

            Driver.VoiceClient.SendPacket(new LocalAudioPosition(
                (uint)VoiceSequence++,
                Driver.VoiceClient.InstanceId,
                new List<float>()
                {
                    position3[0],
                    position3[1],
                    position3[2],
                },
                MyAgentControllerId
            ));
        }

        private void ClientVoiceMessages_OnLoginReply(object? sender, SanProtocol.ClientVoice.LoginReply e)
        {
            Output("Logged into voice server: " + e.ToString());
        }

        private void WorldStateMessages_OnCreateAgentController(object? sender, SanProtocol.WorldState.CreateAgentController e)
        {
            if (e.PersonaId == Driver.MyPersonaDetails!.Id)
            {
                Output($"My AgentComponentId is {e.CharacterObjectId * 0x100000000ul}");
                this.MyAgentComponentId = e.CharacterObjectId * 0x100000000ul;
            }
        }

        private void ClientRegionMessages_OnSetAgentController(object? sender, SanProtocol.ClientRegion.SetAgentController e)
        {
            Output($"Agent controller has been set to {e.AgentControllerId}");
            this.MyAgentControllerId = e.AgentControllerId;

            Output("Sending to voice server: LocalAudioStreamState(1)...");
            Driver.VoiceClient.SendPacket(new LocalAudioStreamState(Driver.VoiceClient.InstanceId, 0, 1, 1));

            Output("Sending to voice server: LocalAudioPosition(0,0,0)...");
            Driver.VoiceClient.SendPacket(new LocalAudioPosition((uint)VoiceSequence++, Driver.VoiceClient.InstanceId, new List<float>() { 0, 0, 0 }, MyAgentControllerId));

            AudioThread.Start();
        }

        public long LastTimestampTicks { get; set; } = 0;
        public ulong LastTimestampFrame { get; set; } = 0;
        public long InitialTimestamp { get; set; } = 0;

        private ulong GetCurrentFrame()
        {
            if (LastTimestampTicks == 0)
            {
                return LastTimestampFrame;
            }

            const float kFrameFrequency = 1000.0f / 90.0f;

            var millisecondsSinceLastTimestamp = ((DateTime.Now.Ticks - LastTimestampTicks)) / 10000;
            var totalFramesSinceLastTimestamp = millisecondsSinceLastTimestamp / kFrameFrequency;

            return LastTimestampFrame + (ulong)totalFramesSinceLastTimestamp;
        }

        private void SimulationMessages_OnInitialTimestamp(object? sender, SanProtocol.Simulation.InitialTimestamp e)
        {
            // Output($"InitialTimestamp {e.Frame} | {e.Nanoseconds}}");

            LastTimestampFrame = e.Frame;
            LastTimestampTicks = DateTime.Now.Ticks;
            InitialTimestamp = DateTimeOffset.Now.ToUnixTimeMilliseconds();
        }

        private void SimulationMessages_OnTimestamp(object? sender, SanProtocol.Simulation.Timestamp e)
        {
            //Output($"OnTimestamp {e.Frame} | {e.Nanoseconds}");

            LastTimestampFrame = e.Frame;
            LastTimestampTicks = DateTime.Now.Ticks;
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
                Output($"<{string.Join(',', e.SpawnRotation)}>");
                WarpToPosition(e.SpawnPosition, e.SpawnRotation);
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

        private void ClientRegionMessages_OnRemoveUser(object? sender, SanProtocol.ClientRegion.RemoveUser e)
        {
            if (!PersonaSessionMap.ContainsKey(e.SessionId))
            {
                Output($"<session {e.SessionId}> Left the region");
            }
            else
            {
                var source = PersonaSessionMap[e.SessionId];
                Output($"{source.UserName} ({source.Handle}) Left the region");
                PersonaSessionMap.Remove(e.SessionId);
            }
        }

        private void ClientRegionMessages_OnAddUser(object? sender, SanProtocol.ClientRegion.AddUser e)
        {
            PersonaSessionMap[e.SessionId] = e;
            Output($"{e.UserName} ({e.Handle}) Entered the region");
        }


        private void ClientRegionMessages_OnUserLoginReply(object? sender, SanProtocol.ClientRegion.UserLoginReply e)
        {
            if (!e.Success)
            {
                throw new Exception("Failed to enter region");
            }

            Output("Logged into region: " + e.ToString());

            var regionAddress = Driver.CurrentInstanceId!.Format();
            Driver.KafkaClient.SendPacket(new SanProtocol.ClientKafka.EnterRegion(
                regionAddress
            ));

            Driver.RegionClient.SendPacket(new SanProtocol.ClientRegion.ClientDynamicReady(
                new List<float>() { 0, 0, 0 },
                new List<float>() { -1, 0, 0, 0 },
                new SanUUID(Driver.MyPersonaDetails!.Id),
                "",
                1,
                1
            ));

            Driver.RegionClient.SendPacket(new SanProtocol.ClientRegion.ClientStaticReady(
                1
            ));
        }

        private void ClientKafkaMessages_OnPrivateChat(object? sender, PrivateChat e)
        {
            Output($"(PRIVMSG) {e.FromPersonaId}: {e.Message}");
        }

        private void ClientKafkaMessages_OnLoginReply(object? sender, SanProtocol.ClientKafka.LoginReply e)
        {
            if (!e.Success)
            {
                throw new Exception($"KafkaClient failed to login: {e.Message}");
            }

            Output("Kafka client logged in successfully");

            // default bot
           // Driver.WebApi.SetAvatarIdAsync(Driver.MyPersonaDetails.Id, "43668ab727c00fd7d33a5af1085493dd").Wait();

            // Driver.JoinRegion("djm3n4c3-9174", "dj-s-outside-fun2").Wait();
            // Driver.JoinRegion("sansar-studios", "social-hub").Wait();
            // Driver.JoinRegion("nopnop", "unit").Wait();
            //Driver.JoinRegion("mijekamunro", "gone-grid-city-prime-millenium").Wait();
            Driver.JoinRegion("nopnopnop", "aaaaaaaaaaaa").Wait();
            // Driver.JoinRegion("princesspea-0197", "wanderlust").Wait();
        }
    }
}
