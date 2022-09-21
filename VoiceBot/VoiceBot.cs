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
        public bool TryToAvoidInterruptingPeople { get; set; } = false;
        public bool TextToSpeechEnabled { get; set; } = true;

        public SanUUID ItemClousterResourceId { get; set; } = new SanUUID("04c2d5a7ea3d6fb47af66669cfdc9f9a"); // heart reaction thing
                                                                                                               // public SanUUID ItemClousterResourceId { get; set; } = new SanUUID("04c2d5a7ea3d6fb47af66669cfdc9f9a"); // heart reaction thing
                                                                                                               //public SanUUID ItemClousterResourceId { get; set; } = new SanUUID("b8534d067b0613a509b0155e0dacb0b2"); // fox doll
        public List<float> MarkedPosition { get; set; } = new List<float>() { 0, 0, 0};

        public class AzureConfigPayload
        {
            public string key1 { get; set; }
            public string region { get; set; }
        }

        private AzureConfigPayload AzureConfig { get; set; }
        public DateTime LastTimeSomeoneSpoke { get; set; } = DateTime.Now;


        public VoiceBot()
        {

            ConfigFile config;
            var sanbotPath = Path.Join(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SanBot"
            );
            var configPath = Path.Join(sanbotPath, "VoiceBot.config.json");
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
            Driver.RegionClient.ClientRegionMessages.OnSetAgentController += ClientRegionMessages_OnSetAgentController;

            Driver.RegionClient.ClientRegionMessages.OnClientSetRegionBroadcasted += ClientRegionMessages_OnClientSetRegionBroadcasted;

            Driver.RegionClient.WorldStateMessages.OnCreateClusterViaDefinition += WorldStateMessages_OnCreateClusterViaDefinition;

            Driver.VoiceClient.ClientVoiceMessages.OnLocalAudioData += ClientVoiceMessages_OnLocalAudioData;

            AudioThread = new VoiceAudioThread(Driver.VoiceClient.SendRaw, TryToAvoidInterruptingPeople);

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
            public DateTime LastTimeSomeoneSpoke { get; set; }
            public bool TryToAvoidInterruptingPeople { get; set; }

            public VoiceAudioThread(Action<byte[]> callback, bool tryToAvoidInterruptingPeople)
            {
                Callback = callback;

                ConsumerThread = new Thread(new ThreadStart(Consumer));
                TryToAvoidInterruptingPeople = tryToAvoidInterruptingPeople;
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
                    if ((DateTime.Now - LastTimeSomeoneSpoke).TotalSeconds < 1)
                    {
                        Thread.Yield();
                        continue;
                    }

                    if (AudioDataQueue.TryDequeue(out List<byte[]> rawAudioPackets))
                    {
                        Console.WriteLine($"VoiceAudioThread: Audio payload found. Sending it... rawAudioPackets={rawAudioPackets.Count}");

                        for (int i = 0; i < rawAudioPackets.Count; i++)
                        {
                            Callback(rawAudioPackets[i]);

                            if (TryToAvoidInterruptingPeople && (DateTime.Now - LastTimeSomeoneSpoke).TotalSeconds < 1)
                            {
                                // This is not safe in any safe in any sort of manner :D
                                Console.WriteLine($"We are being interrupted. Pausing speech for now");
                                var newQueue = new ConcurrentQueue<List<byte[]>>();

                                // NOTE: We cannot skip back to previously played samples because each sample has the
                                //       sequence id already baked into it. We'd need to construct the packets here
                                //       instead of elsewhere, which I don't really want to do.
                                newQueue.Enqueue(rawAudioPackets.Skip(i).ToList());
                                foreach (var item in AudioDataQueue)
                                {
                                    newQueue.Enqueue(item);
                                }
                                AudioDataQueue = newQueue;
                                break;
                            }

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
                    Driver.MyPersonaData.AgentControllerId.Value,
                    new AudioData(Driver.VoiceClient.CurrentSequence, 50, compressedBytes.Take(written).ToArray()),
                    new SpeechGraphicsData(Driver.VoiceClient.CurrentSequence, new byte[] { }),
                    0
                ).GetBytes();
                Driver.VoiceClient.CurrentSequence++;

                messages.Add(packetBytes);
            }

            AudioThread.EnqueueData(messages);
        }

        public HashSet<string> PreviousMessages { get; set; } = new HashSet<string>();
        public void Speak(string message, bool allowRepeating = false)
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

            if (!allowRepeating && PreviousMessages.Contains(message))
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
                    // var speechSynthesisResult = speechSynthesizer.SpeakSsmlAsync(@"" + message).Result;
                    var speechSynthesisResult = speechSynthesizer.SpeakSsmlAsync($"<speak xmlns='http://www.w3.org/2001/10/synthesis' xmlns:mstts='http://www.w3.org/2001/mstts' xmlns:emo='http://www.w3.org/2009/10/emotionml' version='1.0' xml:lang='en-US'><voice name=\"en-US-JennyNeural\"><prosody volume='40'  rate=\'20%\' pitch=\'0%\'>{message}</prosody></voice></speak>").Result;
                    OutputSpeechSynthesisResult(speechSynthesisResult);
                }
            }

            LastSpoke = DateTime.Now;
        }

        DateTime LastSpoke = DateTime.Now;
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
                    Thread.Sleep(10);
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
                    Thread.Sleep(10);
                }

                return;
            }
            if (e.Message == "/jump!")
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
                    Thread.Sleep(10);
                }

                Thread.Sleep(500);

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
                    //if (persona.Name.ToLower() == "nop")
                    //{
                    //    Speak($"nawp: {e.Message}");
                    //}
                    //else
                    //{
                    //    Speak($"{persona.Name}: {e.Message}");
                    //}


                    if (TextToSpeechEnabled)
                    {
                        Speak($"{e.Message}");
                    }
                    //
                }
            }

            Output($"{persona.Name} [{persona.Handle}]: {e.Message}");
        }
        

        private void ClientRegionMessages_OnSetAgentController(object? sender, SanProtocol.ClientRegion.SetAgentController e)
        {
            Output("Sending to voice server: LocalAudioStreamState(1)...");
            Driver.VoiceClient.SendPacket(new LocalAudioStreamState(Driver.VoiceClient.InstanceId, 0, 1, 1));

            Output("Sending to voice server: LocalAudioPosition(0,0,0)...");
            Driver.SetVoicePosition(new List<float>() { 0, 0, 0 }, true);

            AudioThread.Start();
        }

        private void ClientVoiceMessages_OnLocalAudioData(object? sender, SanProtocol.ClientVoice.LocalAudioData e)
        {
            if (Driver.MyPersonaData == null || Driver.MyPersonaData.AgentControllerId == null)
            {
                return;
            }

            if (e.AgentControllerId == Driver.MyPersonaData.AgentControllerId)
            {
                return;
            }

            if (e.Data.Volume >= 400)
            {
                Output($"Someone is speaking [{e.Data.Volume}]: " + e.AgentControllerId);
                if (AudioThread != null)
                {
                    AudioThread.LastTimeSomeoneSpoke = DateTime.Now;
                }
            }
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
                Driver.WarpToPosition(e.SpawnPosition, e.SpawnRotation);

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
                new List<float>() { 1, 0, 0, 0 },
                new SanUUID(Driver.MyPersonaDetails!.Id),
                "",
                0,
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
    //    https://atlas.sansar.com/experiences/mijekamunro/bingo-oracle
            // default bot
            // Driver.WebApi.SetAvatarIdAsync(Driver.MyPersonaDetails.Id, "43668ab727c00fd7d33a5af1085493dd").Wait();

            // Driver.JoinRegion("djm3n4c3-9174", "dj-s-outside-fun2").Wait();
            //   Driver.JoinRegion("sansar-studios", "social-hub").Wait();
            //  Driver.JoinRegion("sansar-studios", "social-hub").Wait();
            //  Driver.JoinRegion("lozhyde", "sxc").Wait();
           // Driver.JoinRegion("mijekamunro", "bingo-oracle").Wait();
              Driver.JoinRegion("nopnopnop", "owo").Wait();
            //Driver.JoinRegion("nop", "rce-poc").Wait();
            // Driver.JoinRegion("princesspea-0197", "wanderlust").Wait();
        }
    }
}
