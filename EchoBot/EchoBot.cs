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

namespace EchoBot
{
    public class EchoBot
    {
        public Driver Driver { get; set; }

        public DateTime? LastTimeWeListenedToOurTarget { get; set; } = null;
        public uint? agentControllerIdImListeningTo { get; set; } = null;
        public List<PersonaData> TargetPersonas { get; set; } = new List<PersonaData>();


        //public SanUUID ItemClousterResourceId { get; set; } = new SanUUID("62f7bca2e04c60bc77ef3bbccbcfb61e"); // panda reaction thing
        //public SanUUID ItemClousterResourceId { get; set; } = new SanUUID("a08aa34cad4dbaea7c1e18a44e4f973c"); // toast reaction thing
        //public SanUUID ItemClousterResourceId { get; set; } = new SanUUID("df2cdee01bb4024640fb93d1c6c1bf29"); // wtf reaction thing
        public SanUUID ItemClousterResourceId_Exclamation { get; set; } = new SanUUID("beb1c1d298aa865fc5d5326dada8d2a7"); // ! reaction thing
        /// public SanUUID ItemClousterResourceId_Exclamation { get; set; } = new SanUUID("beb1c1d298aa865fc5d5326dada8d2a7"); // ! reaction thing
        //public SanUUID ItemClousterResourceId { get; set; } = new SanUUID("97477c6e978aa38d20e0bb8a60e85830"); // lightning reaction thing
        public SanUUID ItemClousterResourceId { get; set; } = new SanUUID("04c2d5a7ea3d6fb47af66669cfdc9f9a"); // heart reaction thing
                                                                                                               // public SanUUID ItemClousterResourceId { get; set; } = new SanUUID("04c2d5a7ea3d6fb47af66669cfdc9f9a"); // heart reaction thing
                                                                                                               //public SanUUID ItemClousterResourceId { get; set; } = new SanUUID("b8534d067b0613a509b0155e0dacb0b2"); // fox doll
        public bool FollowTargetMode { get; set; } = false;
        public bool RepeatVoice { get; set; } = false;

        public HashSet<string> TargetHandles { get; set; } = new HashSet<string>() {
            //"entity0x",
            //"metaverseking-3934",
        //    "rosekai-9021",
           //"rosekai-9021",
            //"medhue",
            //"fkat",
            //"rainkinglw-6445",
            //"vitaminc-0154",
           // "mark344573-2324",
           //"jkchan91-5005"
          // "ninki"
            //"dnaelite-6543"
          //  "nopnopnop",
        };

        public EchoBot()
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


            Driver.RegionClient.ClientRegionMessages.OnAddUser += ClientRegionMessages_OnAddUser;
            Driver.RegionClient.ClientRegionMessages.OnRemoveUser += ClientRegionMessages_OnRemoveUser;

            Driver.RegionClient.AnimationComponentMessages.OnCharacterTransform += AnimationComponentMessages_OnCharacterTransform;
            Driver.RegionClient.AnimationComponentMessages.OnCharacterTransformPersistent += AnimationComponentMessages_OnCharacterTransformPersistent;
            Driver.RegionClient.AnimationComponentMessages.OnBehaviorStateUpdate += AnimationComponentMessages_OnBehaviorStateUpdate;
            Driver.RegionClient.AnimationComponentMessages.OnPlayAnimation += AnimationComponentMessages_OnPlayAnimation; ;

            Driver.RegionClient.AgentControllerMessages.OnCharacterControllerInput += AgentControllerMessages_OnCharacterControllerInput;
            Driver.RegionClient.AgentControllerMessages.OnCharacterControllerInputReliable += AgentControllerMessages_OnCharacterControllerInputReliable;

            Driver.RegionClient.AgentControllerMessages.OnCharacterIKPose += AgentControllerMessages_OnCharacterIKPose;
            Driver.RegionClient.AgentControllerMessages.OnCharacterIKPoseDelta += AgentControllerMessages_OnCharacterIKPoseDelta;
            Driver.RegionClient.AgentControllerMessages.OnCharacterControlPointInput += AgentControllerMessages_OnCharacterControlPointInput;
            Driver.RegionClient.AgentControllerMessages.OnCharacterControlPointInputReliable += AgentControllerMessages_OnCharacterControlPointInputReliable;

            Driver.RegionClient.WorldStateMessages.OnCreateClusterViaDefinition += WorldStateMessages_OnCreateClusterViaDefinition;
            Driver.RegionClient.WorldStateMessages.OnDestroyCluster += WorldStateMessages_OnDestroyCluster;

            Driver.RegionClient.WorldStateMessages.OnCreateAgentController += WorldStateMessages_OnCreateAgentController;

            Driver.VoiceClient.ClientVoiceMessages.OnLocalAudioData += ClientVoiceMessages_OnLocalAudioData;
            Driver.VoiceClient.ClientVoiceMessages.OnLocalAudioStreamState += ClientVoiceMessages_OnLocalAudioStreamState;
            Driver.VoiceClient.ClientVoiceMessages.OnLocalAudioPosition += ClientVoiceMessages_OnLocalAudioPosition;

        // https://atlas.sansar.com/experiences/sansar-studios/social-hub
        //  Driver.RegionToJoin = new RegionDetails("nopnopnop", "stadium-swim");
        Driver.RegionToJoin = new RegionDetails("sansar-studios", "social-hub");
          //  Driver.RegionToJoin = new RegionDetails("djm3n4c3-9174", "reactive-dance-demo");

            //  https://atlas.sansar.com/experiences/loz-sansar/stadium-swim
            // Driver.RegionToJoin = new RegionDetails("nopnopnop", "owo");
            Driver.AutomaticallySendClientReady = true;
            Driver.UseVoice = true;
            Driver.StartAsync(config).Wait();

            Stopwatch watch = new Stopwatch();


            while (true)
            {
                if (!Driver.Poll())
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

                    //Thread.Sleep(10);
                }
            }
        }

        public HashSet<string> IgnoredPeople { get; set; } = new HashSet<string>()
        {
            "Nop",
            "VitaminC-0154",
            "TwoInTheBusch-6219",
            "yash-0879",
            "Bigtod-6269",
        };

        private void ClientVoiceMessages_OnLocalAudioPosition(object? sender, LocalAudioPosition e)
        {
            var persona = TargetPersonas
                .Where(n => n.AgentControllerId == e.AgentControllerId)
                .FirstOrDefault();
            if (persona == null)
            {
                Console.WriteLine("OnLocalAudioPosition: UNKNOWN -> AgentControllerId=" + e.AgentControllerId);
                return;
            }
            else
            {
                if(IgnoredPeople.Contains(persona.Handle))
                {
                    return;
                }
                Console.WriteLine($"OnLocalAudioPosition: Handle={persona.Handle} PersonaId={persona.PersonaId} AgentControllerId={e.AgentControllerId} Position={e.Position}");
            }
        }

        private void ClientVoiceMessages_OnLocalAudioStreamState(object? sender, LocalAudioStreamState e)
        {
            var persona = TargetPersonas
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

        private void WorldStateMessages_OnCreateAgentController(object? sender, SanProtocol.WorldState.CreateAgentController e)
        {
            var persona = TargetPersonas
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

        public class SpeechToTextResult
        {
            public bool Success { get; set; }
            public string Text { get; set; }
        }

        private void DumpVoiceBuffer()
        {
            Output("Dumping voice buffer...");
            const int kFrameSize = 960;
            const int kFrequency = 48000;

            //byte[] wavBytes;
            //using (MemoryStream ms = new MemoryStream())
            //{
            //    var decoder = OpusDecoder.Create(kFrequency, 1);
            //    var decompressedBuffer = new short[kFrameSize * 2];
            //
            //    foreach (var item in VoiceBuffer)
            //    {
            //        var numSamples = OpusPacketInfo.GetNumSamples(decoder, item, 0, item.Length);
            //        var result = decoder.Decode(item, 0, item.Length, decompressedBuffer, 0, numSamples);
            //
            //        var decompressedBufferBytes = new byte[result * 2];
            //        Buffer.BlockCopy(decompressedBuffer, 0, decompressedBufferBytes, 0, result * 2);
            //
            //        ms.Write(decompressedBufferBytes);
            //    }
            //
            //    ms.Seek(0, SeekOrigin.Begin);
            //    using (var rs = new RawSourceWaveStream(ms, new WaveFormat(48000, 16, 1)))
            //    {
            //        using (var wavStream = new MemoryStream())
            //        {
            //            WaveFileWriter.WriteWavFileToStream(wavStream, rs);
            //            wavBytes = wavStream.ToArray();
            //        }
            //    }
            //}

            //using (var client = new HttpClient())
            //{
            //    var result = client.PostAsync("http://127.0.0.1:5000/speech_to_text", new ByteArrayContent(wavBytes)).Result;
            //    var resultString = result.Content.ReadAsStringAsync().Result;
            //
            //    var jsonResult = System.Text.Json.JsonSerializer.Deserialize<SpeechToTextResult>(resultString);
            //    if(jsonResult?.Success == true)
            //    {
            //        if(jsonResult.Text.Trim().Length == 0)
            //        {
            //            return;
            //        }
            //
            //        var user = Driver.PersonasBySessionId
            //            .Where(n => n.Value.AgentControllerId == agentControllerIdImListeningTo)
            //            .Select(n => n.Value)
            //            .FirstOrDefault();
            //        if(user == null)
            //        {
            //            Driver.SendChatMessage($"[unknown]: ${jsonResult.Text}");
            //        }
            //        else
            //        {
            //            Driver.SendChatMessage($"{user.UserName} ({user.Handle}): {jsonResult.Text}");
            //        }
            //    }
            //    else
            //    {
            //        Driver.SendChatMessage("Error :(");
            //    }
            //}

            ++VoiceBufferIndex;
            VoiceBuffer.Clear();
        }

        private void AgentControllerMessages_OnCharacterControlPointInputReliable(object? sender, SanProtocol.AgentController.CharacterControlPointInputReliable e)
        {
            if (Driver.MyPersonaData == null || Driver.MyPersonaData.AgentControllerId == null)
            {
                return;
            }

            if (e.AgentControllerId != Driver.MyPersonaData.AgentControllerId)
            {
                Driver.RegionClient.SendPacket(new SanProtocol.AgentController.CharacterControlPointInputReliable(
                    Driver.GetCurrentFrame(),
                    Driver.MyPersonaData.AgentControllerId.Value,
                    e.ControlPoints,
                    e.LeftIndexTrigger,
                    e.RightIndexTrigger,
                    e.LeftGripTrigger,
                    e.RightGripTrigger,
                    e.LeftTouches,
                    e.RightTouches,
                    e.IndexTriggerControlsHand,
                    e.LeftHandIsHolding,
                    e.RightHandIsHolding
                ));
            }
        }

        private void AgentControllerMessages_OnCharacterControlPointInput(object? sender, SanProtocol.AgentController.CharacterControlPointInput e)
        {
            if (Driver.MyPersonaData == null || Driver.MyPersonaData.AgentControllerId == null)
            {
                return;
            }

            if (e.AgentControllerId != Driver.MyPersonaData.AgentControllerId.Value)
            {
                Driver.RegionClient.SendPacket(new SanProtocol.AgentController.CharacterControlPointInput(
                    Driver.GetCurrentFrame(),
                    Driver.MyPersonaData.AgentControllerId.Value,
                    e.ControlPoints,
                    e.LeftIndexTrigger,
                    e.RightIndexTrigger,
                    e.LeftGripTrigger,
                    e.RightGripTrigger,
                    e.LeftTouches,
                    e.RightTouches,
                    e.IndexTriggerControlsHand,
                    e.LeftHandIsHolding,
                    e.RightHandIsHolding
                ));
            }
        }

        private void AgentControllerMessages_OnCharacterIKPoseDelta(object? sender, SanProtocol.AgentController.CharacterIKPoseDelta e)
        {
            if (Driver.MyPersonaData == null || Driver.MyPersonaData.AgentControllerId == null)
            {
                return;
            }

            if (false && e.AgentControllerId != Driver.MyPersonaData.AgentControllerId.Value)
            {
                Driver.RegionClient.SendPacket(new SanProtocol.AgentController.CharacterIKPoseDelta(
                    Driver.MyPersonaData.AgentControllerId.Value,
                    Driver.GetCurrentFrame(),
                    e.BoneRotations,
                    e.RootBoneTranslationDelta
                ));
            }
        }

        private void AgentControllerMessages_OnCharacterIKPose(object? sender, SanProtocol.AgentController.CharacterIKPose e)
        {
            if (Driver.MyPersonaData == null || Driver.MyPersonaData.AgentControllerId == null)
            {
                return;
            }

            if (false && e.AgentControllerId != Driver.MyPersonaData.AgentControllerId.Value)
            {
                Driver.RegionClient.SendPacket(new SanProtocol.AgentController.CharacterIKPose(
                    Driver.MyPersonaData.AgentControllerId.Value,
                    Driver.GetCurrentFrame(),
                    e.BoneRotations,
                    e.RootBoneTranslation
                ));
            }
        }

        private void AnimationComponentMessages_OnPlayAnimation(object? sender, SanProtocol.AnimationComponent.PlayAnimation e)
        {
            if (Driver.MyPersonaData == null || Driver.MyPersonaData.AgentControllerId == null || Driver.MyPersonaData.AgentComponentId == null)
            {
                return;
            }

            //var persona = TargetPersonas
            //    .Where(n => n.AgentComponentId == e.ComponentId)
            //    .FirstOrDefault();
            //if (persona == null)
            //{
            //    return;
            //}
            //
            //Driver.RegionClient.SendPacket(new SanProtocol.AgentController.AgentPlayAnimation(
            //    Driver.MyPersonaData.AgentControllerId.Value,
            //    Driver.GetCurrentFrame(),
            //    Driver.MyPersonaData.AgentComponentId.Value,
            //    e.ResourceId,
            //    e.PlaybackSpeed,
            //    e.SkeletonType,
            //    e.AnimationType,
            //    e.PlaybackMode
            //));
        }

        private void AnimationComponentMessages_OnBehaviorStateUpdate(object? sender, SanProtocol.AnimationComponent.BehaviorStateUpdate e)
        {
            if (Driver.MyPersonaData == null || Driver.MyPersonaData.AgentControllerId == null || Driver.MyPersonaData.AgentComponentId == null)
            {
                return;
            }

            //var persona = TargetPersonas
            //    .Where(n => n.AgentComponentId == e.ComponentId)
            //    .FirstOrDefault();
            //if (persona == null)
            //{
            //    return;
            //}
            //
            //Driver.RegionClient.SendPacket(new SanProtocol.AgentController.RequestBehaviorStateUpdate(
            //    Driver.GetCurrentFrame(),
            //    Driver.MyPersonaData.AgentComponentId.Value,
            //    Driver.MyPersonaData.AgentControllerId.Value,
            //    e.Floats,
            //    e.Vectors,
            //    e.Quaternions,
            //    e.Int8s,
            //    e.Bools,
            //    e.InternalEventIds,
            //    e.AnimationAction,
            //    e.NodeLocalTimes,
            //    e.NodeCropValues
            //));
        }

        private void AgentControllerMessages_OnCharacterControllerInputReliable(object? sender, SanProtocol.AgentController.CharacterControllerInputReliable e)
        {
            if (Driver.MyPersonaData == null || Driver.MyPersonaData.AgentControllerId == null)
            {
                return;
            }

            //var persona = TargetPersonas
            //    .Where(n => n.AgentControllerId == e.AgentControllerId)
            //    .FirstOrDefault();
            //if (persona == null)
            //{
            //    return;
            //}
            //
            //Driver.RegionClient.SendPacket(new SanProtocol.AgentController.CharacterControllerInputReliable(
            //    Driver.GetCurrentFrame(),
            //    Driver.MyPersonaData.AgentControllerId.Value,
            //    e.JumpState,
            //    e.JumpBtnPressed,
            //    e.MoveRight,
            //    e.MoveForward,
            //    e.CameraYaw,
            //    e.CameraPitch,
            //    e.BehaviorYawDelta,
            //    e.BehaviorPitchDelta,
            //    e.CharacterForward,
            //    e.CameraForward
            //));
        }

        private void AgentControllerMessages_OnCharacterControllerInput(object? sender, SanProtocol.AgentController.CharacterControllerInput e)
        {
            if (Driver.MyPersonaData == null || Driver.MyPersonaData.AgentControllerId == null)
            {
                return;
            }

            var persona = TargetPersonas
                .Where(n => n.AgentControllerId == e.AgentControllerId)
                .FirstOrDefault();
            if (persona == null)
            {
                Console.WriteLine("AgentControllerMessages_OnCharacterControllerInput: UNKNOWN -> " + e.AgentControllerId);
                return;
            }
            if (IgnoredPeople.Contains(persona.Handle))
            {
                return;
            }

            //var persona = TargetPersonas
            //    .Where(n => n.AgentControllerId == e.AgentControllerId)
            //    .FirstOrDefault();
            //if (persona == null)
            //{
            //    return;
            //}
            //
            //Driver.RegionClient.SendPacket(new SanProtocol.AgentController.CharacterControllerInput(
            //    Driver.GetCurrentFrame(),
            //    Driver.MyPersonaData.AgentControllerId.Value,
            //    e.JumpState,
            //    e.JumpBtnPressed,
            //    e.MoveRight,
            //    e.MoveForward,
            //    e.CameraYaw,
            //    e.CameraPitch,
            //    e.BehaviorYawDelta,
            //    e.BehaviorPitchDelta,
            //    e.CharacterForward,
            //    e.CameraForward
            //));
        }

        private void WorldStateMessages_OnDestroyCluster(object? sender, SanProtocol.WorldState.DestroyCluster e)
        {
            TargetPersonas.RemoveAll(n => n.ClusterId == e.ClusterId);
        }

        private void AnimationComponentMessages_OnCharacterTransformPersistent(object? sender, SanProtocol.AnimationComponent.CharacterTransformPersistent e)
        {

            var persona = TargetPersonas
                .Where(n => n.AgentComponentId == e.ComponentId)
                .FirstOrDefault();
            if (persona == null)
            {
                Console.WriteLine("AnimationComponentMessages_OnCharacterTransformPersistent: UNKNOWN " + e.ComponentId);
                return;
            }
            if (IgnoredPeople.Contains(persona.Handle))
            {
                return;
            }
            Console.WriteLine($"AnimationComponentMessages_OnCharacterTransformPersistent: Username={persona.UserName} Handle={persona.Handle} Persona={persona.PersonaId} ");

            //
            //if (persona.LastTransform == null)
            //{
            //    return;
            //}
            //
            //if (FollowTargetMode)
            //{
            //    Driver.SetPosition(persona.LastTransform.Position, persona.LastTransform.OrientationQuat, persona.LastTransform.GroundComponentId, true);
            //    Driver.SetVoicePosition(persona.LastTransform.Position, true);
            //}
        }

        private void AnimationComponentMessages_OnCharacterTransform(object? sender, SanProtocol.AnimationComponent.CharacterTransform e)
        {
            var persona = TargetPersonas
                .Where(n => n.AgentComponentId == e.ComponentId)
                .FirstOrDefault();
            if (persona == null)
            {
                Console.WriteLine($"AnimationComponentMessages_OnCharacterTransform: UNKNOWN " + e.ComponentId );
                return;
            }
            if (IgnoredPeople.Contains(persona.Handle))
            {
                return;
            }
            Console.WriteLine($"AnimationComponentMessages_OnCharacterTransform: Username={persona.UserName} Handle={persona.Handle} Persona={persona.PersonaId} ");

            //
            //if (persona.LastTransform == null)
            //{
            //    return;
            //}
            //
            //if (FollowTargetMode)
            //{
            //    Driver.SetPosition(persona.LastTransform.Position, persona.LastTransform.OrientationQuat, persona.LastTransform.GroundComponentId, true);
            //    Driver.SetVoicePosition(persona.LastTransform.Position, true);
            //}
        }

        public List<byte[]> VoiceBuffer { get; set; } = new List<byte[]>();
        public DateTime? TimeStartedListeningToTarget { get; set; } = null;
        public int VoiceBufferIndex { get; set; } = 0;

        private void ClientVoiceMessages_OnLocalAudioData(object? sender, SanProtocol.ClientVoice.LocalAudioData e)
        {
            if (Driver.MyPersonaData == null || Driver.MyPersonaData.AgentControllerId == null)
            {
                return;
            }

            if (!RepeatVoice)
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

            //if(persona.Handle != "jamal-7001")
            //{
            //    return;
            //}
            //if (TargetHandles.Count != 0 && persona == null)
            //{
            //    return;
            //}

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

        private void WorldStateMessages_OnCreateClusterViaDefinition(object? sender, SanProtocol.WorldState.CreateClusterViaDefinition e)
        {
            if (e.ResourceId == this.ItemClousterResourceId)
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

        private void ClientRegionMessages_OnRemoveUser(object? sender, SanProtocol.ClientRegion.RemoveUser e)
        {
            TargetPersonas.RemoveAll(n => n.SessionId == e.SessionId);

            if(agentControllerIdImListeningTo != null)
            {
                var persona = Driver.PersonasBySessionId
                    .Where(n => n.Key == e.SessionId)
                    .Select(n => n.Value)
                    .LastOrDefault();
                if(persona.AgentComponentId == agentControllerIdImListeningTo.Value)
                {
                    DumpVoiceBuffer();

                    TimeStartedListeningToTarget = null;
                    LastTimeWeListenedToOurTarget = null;
                    agentControllerIdImListeningTo = null;
                }
            }
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

            Console.WriteLine("ClientRegionMessages_OnAddUser: " + e.Handle);
            TargetPersonas.Add(persona);
        }

        private void ClientKafkaMessages_OnPrivateChat(object? sender, PrivateChat e)
        {
            Output($"(PRIVMSG) {e.FromPersonaId}: {e.Message}");
        }
    }
}
