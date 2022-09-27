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

        public ConcurrentDictionary<uint, VoiceConversation> ConversationsByAgentControllerId { get; set; } = new ConcurrentDictionary<uint, VoiceConversation>();

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

            Driver.KafkaClient.ClientKafkaMessages.OnPrivateChat += ClientKafkaMessages_OnPrivateChat;
            Driver.KafkaClient.ClientKafkaMessages.OnLoginReply += ClientKafkaMessages_OnLoginReply;

            Driver.RegionClient.ClientRegionMessages.OnUserLoginReply += ClientRegionMessages_OnUserLoginReply;
            Driver.RegionClient.ClientRegionMessages.OnAddUser += ClientRegionMessages_OnAddUser;
            Driver.RegionClient.ClientRegionMessages.OnRemoveUser += ClientRegionMessages_OnRemoveUser;
            Driver.RegionClient.ClientRegionMessages.OnSetAgentController += ClientRegionMessages_OnSetAgentController;

            Driver.RegionClient.ClientRegionMessages.OnClientSetRegionBroadcasted += ClientRegionMessages_OnClientSetRegionBroadcasted;

            Driver.RegionClient.WorldStateMessages.OnCreateClusterViaDefinition += WorldStateMessages_OnCreateClusterViaDefinition;
            Driver.RegionClient.WorldStateMessages.OnDestroyCluster += WorldStateMessages_OnDestroyCluster;

            Driver.VoiceClient.ClientVoiceMessages.OnLoginReply += ClientVoiceMessages_OnLoginReply;
            Driver.VoiceClient.ClientVoiceMessages.OnLocalAudioData += ClientVoiceMessages_OnLocalAudioData;

            Driver.StartAsync(config).Wait();

            Stopwatch watch = new Stopwatch();

            _IsConversationThreadRunning = true;
            ConversationThread = new Thread(new ThreadStart(ConversationThreadEntrypoint));
            ConversationThread.Start();

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

        public Thread ConversationThread { get; set; }
        volatile bool _IsConversationThreadRunning = false;
        public void ConversationThreadEntrypoint()
        {
            while(_IsConversationThreadRunning)
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
            public Driver Driver { get; set; }

            public int Id { get; set; }
            public DateTime? TimeWeStartedListeningToTarget { get; set; } = null;
            public DateTime? LastTimeWeListened { get; set; } = null;

            public List<byte[]> VoiceBuffer { get; set; } = new List<byte[]>();
            public ConcurrentQueue<List<byte[]>> VoiceBufferQueue = new ConcurrentQueue<List<byte[]>>();

            public VoiceConversation(PersonaData persona, Driver driver)
            {
                this.Persona = persona;
                this.Driver = driver;
            }

            public void AddVoiceData(AudioData data)
            {
                if (TimeWeStartedListeningToTarget == null)
                {
                    Console.WriteLine($"Started buffering voice for {Persona.UserName} ({Persona.Handle})");
                    TimeWeStartedListeningToTarget = DateTime.Now;
                }

                if (data.Volume > 300)
                {
                    LastTimeWeListened = DateTime.Now;
                }

                VoiceBuffer.Add(data.Data);
            }

            public void Poll()
            {
                if(VoiceBuffer.Count == 0)
                {
                    return;
                }

                if (LastTimeWeListened != null)
                {
                    if ((DateTime.Now - LastTimeWeListened.Value).TotalMilliseconds > 500)
                    {
                        VoiceBufferQueue.Enqueue(new List<byte[]>(VoiceBuffer.AsEnumerable()));
                        VoiceBuffer.Clear();

                        TimeWeStartedListeningToTarget = null;
                        LastTimeWeListened = null;
                    }
                }

                if (TimeWeStartedListeningToTarget != null)
                {
                    if ((DateTime.Now - TimeWeStartedListeningToTarget.Value).TotalMilliseconds > 29500)
                    {
                        VoiceBufferQueue.Enqueue(new List<byte[]> (VoiceBuffer.AsEnumerable()));
                        VoiceBuffer.Clear();

                        TimeWeStartedListeningToTarget = null;
                        LastTimeWeListened = null;
                    }
                }
            }

            public HashSet<string> Blacklist { get; set; } = new HashSet<string>()
            {
                "entity0x",
            };

            public bool ProcessVoiceBufferQueue()
            {
                while(VoiceBufferQueue.TryDequeue(out List<byte[]> voiceBuffer))
                {
                    if(Blacklist.Contains(Persona.Handle.ToLower()))
                    {
                        return true;
                    }

                    Console.WriteLine($"Dumping voice buffer for {Persona.UserName} ({Persona.Handle})");
                    const int kFrameSize = 960;
                    const int kFrequency = 48000;

                    byte[] wavBytes;
                    using (MemoryStream ms = new MemoryStream())
                    {
                        var decoder = OpusDecoder.Create(kFrequency, 1);
                        var decompressedBuffer = new short[kFrameSize * 2];

                        foreach (var item in voiceBuffer)
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

                            Driver.SendChatMessage($"{Persona.UserName} ({Persona.Handle}): {jsonResult.Text}");
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
            if(persona == null)
            {
                return;
            }

            if(!ConversationsByAgentControllerId.ContainsKey(e.AgentControllerId))
            {
                ConversationsByAgentControllerId[e.AgentControllerId] = new VoiceConversation(persona, Driver);
            }
            var conversation = ConversationsByAgentControllerId[e.AgentControllerId];

            conversation.AddVoiceData(e.Data);
        }

        private void ClientVoiceMessages_OnLoginReply(object? sender, SanProtocol.ClientVoice.LoginReply e)
        {
            Output("Logged into voice server: " + e.ToString());
        }

        private void ClientRegionMessages_OnSetAgentController(object? sender, SanProtocol.ClientRegion.SetAgentController e)
        {
            Output("Sending to voice server: LocalAudioStreamState(1)...");
            Driver.VoiceClient.SendPacket(new SanProtocol.ClientVoice.LocalAudioStreamState(Driver.VoiceClient.InstanceId, 0, 1, 1));

            HaveIBeenCreatedYet = true;
            if(Driver.MyPersonaData != null && Driver.MyPersonaData.ClusterId != null)
            {
                if(InitialClusterPositions.ContainsKey(Driver.MyPersonaData.ClusterId.Value))
                {
                    Output("Found my initial cluster position. Updating my voice position");
                    Driver.SetVoicePosition(InitialClusterPositions[Driver.MyPersonaData.ClusterId.Value], true);
                }
            }
        }

        private void ClientRegionMessages_OnClientSetRegionBroadcasted(object? sender, SanProtocol.ClientRegion.ClientSetRegionBroadcasted e)
        {
            Output($"Sending to voice server: LocalSetRegionBroadcasted({e.Broadcasted})...");
            Driver.VoiceClient.SendPacket(new LocalSetRegionBroadcasted(e.Broadcasted));
        }

        public bool HaveIBeenCreatedYet { get; set; } = false;
        public Dictionary<uint, List<float>> InitialClusterPositions { get; set; } = new Dictionary<uint, List<float>>();

        private void WorldStateMessages_OnCreateClusterViaDefinition(object? sender, SanProtocol.WorldState.CreateClusterViaDefinition e)
        {
            if (e.ResourceId == this.ItemClousterResourceId)
            {
                Driver.WarpToPosition(e.SpawnPosition, e.SpawnRotation);
                Driver.SetVoicePosition(e.SpawnPosition, true);
            }

            if(!HaveIBeenCreatedYet)
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
                //new List<float>() { (float)(radius*Math.Sin(angleRads)), (float)(radius * Math.Cos(angleRads)), 5.0f },
                new List<float>() { 0, 0, 0 },
                new List<float>() { -1, 0, 0, 0 }, // upside down spin ish
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

            // mark
            //Driver.WebApi.SetAvatarIdAsync(Driver.MyPersonaDetails.Id, "0fd910bd763fa45580de460cb6f76c57").Wait();

            // dnaelite
            //Driver.WebApi.SetAvatarIdAsync(Driver.MyPersonaDetails.Id, "404e7e026b53ce8a8721d2fc3657f37f").Wait();

            // default bot
            Driver.WebApi.SetAvatarIdAsync(Driver.MyPersonaDetails.Id, "43668ab727c00fd7d33a5af1085493dd").Wait();

            // Driver.JoinRegion("djm3n4c3-9174", "dj-s-outside-fun2").Wait();
              Driver.JoinRegion("sansar-studios", "social-hub").Wait();
        //   Driver.JoinRegion("nopnopnop", "owo").Wait();
            //Driver.JoinRegion("mijekamunro", "gone-grid-city-prime-millenium").Wait();
            //  Driver.JoinRegion("nopnopnop", "aaaaaaaaaaaa").Wait();
        }
    }
}
