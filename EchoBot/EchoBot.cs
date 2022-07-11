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

namespace EchoBot
{
    public class EchoBot
    {
        public Driver Driver { get; set; }
        public Dictionary<uint, SanProtocol.ClientRegion.AddUser> PersonaSessionMap { get; }
        public uint MyAgentControllerId { get; private set; }
        public ulong MyAgentComponentId { get; private set; }
        public ulong VoiceSequence { get; set; }
        public ulong CurrentFrame { get; set; } = 0;
        public DateTime? LastTimeWeListenedToOurTarget { get; set; } = null;
        public uint? CurrentlyListeningTo { get; set; } = null;
        public HashSet<ulong> TargetSessionIds { get; set; } = new HashSet<ulong>();
        public HashSet<ulong> TargetAgentControllerIds { get; set; } = new HashSet<ulong>();
        public HashSet<ulong> TargetAgentComponentIds { get; set; } = new HashSet<ulong>();
        public Dictionary<ulong, SanProtocol.AnimationComponent.CharacterTransform> ComponentPositions { get; set; } = new Dictionary<ulong, SanProtocol.AnimationComponent.CharacterTransform>();
        public List<float>? OurLastVoicePosition { get; set; } = null;


        //public SanUUID ItemClousterResourceId { get; set; } = new SanUUID("62f7bca2e04c60bc77ef3bbccbcfb61e"); // panda reaction thing
        //public SanUUID ItemClousterResourceId { get; set; } = new SanUUID("a08aa34cad4dbaea7c1e18a44e4f973c"); // toast reaction thing
        //public SanUUID ItemClousterResourceId { get; set; } = new SanUUID("df2cdee01bb4024640fb93d1c6c1bf29"); // wtf reaction thing
        //public SanUUID ItemClousterResourceId { get; set; } = new SanUUID("beb1c1d298aa865fc5d5326dada8d2a7"); // ! reaction thing
        //public SanUUID ItemClousterResourceId { get; set; } = new SanUUID("97477c6e978aa38d20e0bb8a60e85830"); // lightning reaction thing
        public SanUUID ItemClousterResourceId { get; set; } = new SanUUID("04c2d5a7ea3d6fb47af66669cfdc9f9a"); // heart reaction thing
        //public SanUUID ItemClousterResourceId { get; set; } = new SanUUID("b8534d067b0613a509b0155e0dacb0b2"); // fox doll
        public bool RandomCircleOffsetMode { get; set; } = false;
        public bool FollowTargetMode { get; set; } = true;
        public HashSet<string> TargetHandles { get; set; } = new HashSet<string>() {
            "nopnop",
        };

        public EchoBot()
        {
            ConfigFile config;
            this.PersonaSessionMap = new Dictionary<uint, SanProtocol.ClientRegion.AddUser>();
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
            Driver.KafkaClient.ClientKafkaMessages.OnRegionChat += ClientKafkaMessages_OnRegionChat;

            Driver.RegionClient.ClientRegionMessages.OnUserLoginReply += ClientRegionMessages_OnUserLoginReply;
            Driver.RegionClient.ClientRegionMessages.OnAddUser += ClientRegionMessages_OnAddUser;
            Driver.RegionClient.ClientRegionMessages.OnRemoveUser += ClientRegionMessages_OnRemoveUser;
            Driver.RegionClient.ClientRegionMessages.OnSetAgentController += ClientRegionMessages_OnSetAgentController;

            Driver.RegionClient.ClientRegionMessages.OnClientSetRegionBroadcasted += ClientRegionMessages_OnClientSetRegionBroadcasted;

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
            Driver.RegionClient.WorldStateMessages.OnCreateAgentController += WorldStateMessages_OnCreateAgentController;
            Driver.RegionClient.WorldStateMessages.OnDestroyAgentController += WorldStateMessages_OnDestroyAgentController;
            Driver.RegionClient.WorldStateMessages.OnDestroyCluster += WorldStateMessages_OnDestroyCluster;

            Driver.RegionClient.SimulationMessages.OnTimestamp += SimulationMessages_OnTimestamp;
            Driver.RegionClient.SimulationMessages.OnInitialTimestamp += SimulationMessages_OnInitialTimestamp;

            Driver.VoiceClient.ClientVoiceMessages.OnLoginReply += ClientVoiceMessages_OnLoginReply;
            Driver.VoiceClient.ClientVoiceMessages.OnLocalAudioData += ClientVoiceMessages_OnLocalAudioData;

            Driver.StartAsync(config).Wait();

            while (true)
            {
                Driver.Poll();
            }
        }

        private void AgentControllerMessages_OnCharacterControlPointInputReliable(object? sender, SanProtocol.AgentController.CharacterControlPointInputReliable e)
        {

            if (e.AgentControllerId != MyAgentControllerId)
            {
                CurrentFrame = Math.Max(CurrentFrame, e.Frame);
                Driver.RegionClient.SendPacket(new SanProtocol.AgentController.CharacterControlPointInputReliable(
                    e.Frame,
                    MyAgentControllerId,
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
            if (e.AgentControllerId != MyAgentControllerId)
            {
                CurrentFrame = Math.Max(CurrentFrame, e.Frame);
                Driver.RegionClient.SendPacket(new SanProtocol.AgentController.CharacterControlPointInput(
                    e.Frame,
                    MyAgentControllerId,
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
            if (false && e.AgentControllerId != MyAgentControllerId)
            {
                CurrentFrame = Math.Max(CurrentFrame, e.Frame);
                Driver.RegionClient.SendPacket(new SanProtocol.AgentController.CharacterIKPoseDelta(
                    MyAgentControllerId,
                    e.Frame,
                    e.BoneRotations,
                    e.RootBoneTranslationDelta
                ));
            }
        }

        private void AgentControllerMessages_OnCharacterIKPose(object? sender, SanProtocol.AgentController.CharacterIKPose e)
        {
            if (false && e.AgentControllerId != MyAgentControllerId)
            {
                CurrentFrame = Math.Max(CurrentFrame, e.Frame);
                Driver.RegionClient.SendPacket(new SanProtocol.AgentController.CharacterIKPose(
                    MyAgentControllerId,
                    e.Frame,
                    e.BoneRotations,
                    e.RootBoneTranslation
                ));
            }
        }

        private void AnimationComponentMessages_OnPlayAnimation(object? sender, SanProtocol.AnimationComponent.PlayAnimation e)
        {
            if (TargetAgentComponentIds.Contains(e.ComponentId))
            {
                CurrentFrame = Math.Max(CurrentFrame, e.Frame);
                Driver.RegionClient.SendPacket(new SanProtocol.AgentController.AgentPlayAnimation(
                    MyAgentControllerId,
                    e.Frame,
                    MyAgentComponentId,
                    e.ResourceId,
                    e.PlaybackSpeed,
                    e.SkeletonType,
                    e.AnimationType,
                    e.PlaybackMode
                ));
            }
        }

        private void AnimationComponentMessages_OnBehaviorStateUpdate(object? sender, SanProtocol.AnimationComponent.BehaviorStateUpdate e)
        {
            if (TargetAgentComponentIds.Contains(e.ComponentId))
            {
                CurrentFrame = Math.Max(CurrentFrame, e.Frame);
                Driver.RegionClient.SendPacket(new SanProtocol.AgentController.RequestBehaviorStateUpdate(
                    e.Frame,
                    MyAgentComponentId,
                    MyAgentControllerId,
                    e.Floats,
                    e.Vectors,
                    e.Quaternions,
                    e.Int8s,
                    e.Bools,
                    e.InternalEventIds,
                    e.AnimationAction,
                    e.NodeLocalTimes,
                    e.NodeCropValues
                ));
            }
        }

        private void AgentControllerMessages_OnCharacterControllerInputReliable(object? sender, SanProtocol.AgentController.CharacterControllerInputReliable e)
        {
            if (TargetAgentControllerIds.Contains(e.AgentControllerId))
            {
                CurrentFrame = Math.Max(CurrentFrame, e.Frame);
                Driver.RegionClient.SendPacket(new SanProtocol.AgentController.CharacterControllerInputReliable(
                    e.Frame,
                    MyAgentControllerId,
                    e.JumpState,
                    e.JumpBtnPressed,
                    e.MoveRight,
                    e.MoveForward,
                    e.CameraYaw,
                    e.CameraPitch,
                    e.BehaviorYawDelta,
                    e.BehaviorPitchDelta,
                    e.CharacterForward,
                    e.CameraForward
                ));
            }
        }

        private void AgentControllerMessages_OnCharacterControllerInput(object? sender, SanProtocol.AgentController.CharacterControllerInput e)
        {
            if (TargetAgentControllerIds.Contains(e.AgentControllerId))
            {
                CurrentFrame = Math.Max(CurrentFrame, e.Frame);
                Driver.RegionClient.SendPacket(new SanProtocol.AgentController.CharacterControllerInput(
                    e.Frame,
                    MyAgentControllerId,
                    e.JumpState,
                    e.JumpBtnPressed,
                    e.MoveRight,
                    e.MoveForward,
                    e.CameraYaw,
                    e.CameraPitch,
                    e.BehaviorYawDelta,
                    e.BehaviorPitchDelta,
                    e.CharacterForward,
                    e.CameraForward
                ));
            }
        }

        private void WorldStateMessages_OnDestroyCluster(object? sender, SanProtocol.WorldState.DestroyCluster e)
        {
            var componentId = e.ClusterId * 0x100000000ul;

            if (TargetAgentComponentIds.Contains(componentId))
            {
                TargetAgentComponentIds.Remove(componentId);
            }
        }

        void SetPosition(List<float> position, Quaternion quat, ulong groundComponentId, bool ignoreDistanceCheck, bool isPersistent, ulong serverFrame)
        {
            if (position.Count != 3)
            {
                throw new Exception($"{nameof(SetPosition)} Expected float3 position, got float{position.Count}");
            }

            var xOffset = 0.0f;
            var yOffset = 0.0f;
            if (RandomCircleOffsetMode)
            {
                Random rand = new Random();

                var angleRads = rand.NextDouble() * (Math.PI * 2);
                var radius = 1;
                xOffset = (float)(radius * Math.Sin(angleRads));
                yOffset = (float)(radius * Math.Cos(angleRads));
            }

            if (isPersistent)
            {
                Driver.RegionClient.SendPacket(new SanProtocol.AnimationComponent.CharacterTransformPersistent(
                    MyAgentComponentId,
                    CurrentFrame,
                    groundComponentId,
                    new List<float>()
                    {
                      position[0] + xOffset,
                      position[1] + yOffset,
                      position[2],
                    },
                    quat
                ));
            }
            else
            {
                Driver.RegionClient.SendPacket(new SanProtocol.AnimationComponent.CharacterTransform(
                    MyAgentComponentId,
                    serverFrame,
                    groundComponentId,
                    new List<float>()
                    {
                        position[0] + xOffset,
                        position[1] + yOffset,
                        position[2],
                    },
                    quat
                ));
            }

            if (OurLastVoicePosition == null || ignoreDistanceCheck)
            {
                OurLastVoicePosition = position;
            }
            else
            {
                var distanceSinceFromLastVoicePosition =
                    Math.Sqrt(
                        Math.Pow(position[0] - OurLastVoicePosition[0], 2) +
                        Math.Pow(position[1] - OurLastVoicePosition[1], 2) +
                        Math.Pow(position[2] - OurLastVoicePosition[2], 2)
                    );

                if (distanceSinceFromLastVoicePosition <= 2)
                {
                    return;
                }
            }

            OurLastVoicePosition = position;
            Driver.VoiceClient.SendPacket(new LocalAudioPosition(
                (uint)VoiceSequence++,
                Driver.VoiceClient.InstanceId,
                new List<float>()
                {
                    position[0] + xOffset,
                    position[1] + yOffset,
                    position[2],
                },
                MyAgentControllerId
            ));
        }

        void WarpToPosition(List<float> position3, List<float> rotation4)
        {
            if (position3.Count != 3)
            {
                throw new Exception($"{nameof(SetPosition)} Expected float3 position, got float{position3.Count}");
            }
            if (rotation4.Count != 4)
            {
                throw new Exception($"{nameof(rotation4)} Expected float4 rotation, got float{position3.Count}");
            }

            OurLastVoicePosition = position3;

            var xOffset = 0.0f;
            var yOffset = 0.0f;
            if (RandomCircleOffsetMode)
            {
                Random rand = new Random();

                var angleRads = rand.NextDouble() * (Math.PI * 2);
                var radius = 1;
                xOffset = (float)(radius * Math.Sin(angleRads));
                yOffset = (float)(radius * Math.Cos(angleRads));
            }

            Driver.RegionClient.SendPacket(new SanProtocol.AgentController.WarpCharacter(
                CurrentFrame,
                MyAgentControllerId,
                position3[0] + xOffset,
                position3[1] + yOffset,
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
                    position3[0] + xOffset,
                    position3[1] + yOffset,
                    position3[2],
                },
                MyAgentControllerId
            ));
        }

        private void AnimationComponentMessages_OnCharacterTransformPersistent(object? sender, SanProtocol.AnimationComponent.CharacterTransformPersistent e)
        {
            ComponentPositions[e.ComponentId] = e;

            if (TargetAgentComponentIds.Contains(e.ComponentId))
            {
                if (FollowTargetMode)
                {
                    CurrentFrame = Math.Max(CurrentFrame, e.ServerFrame);
                    SetPosition(e.Position, e.OrientationQuat, e.GroundComponentId, true, true, e.ServerFrame);
                }
            }
        }

        private void AnimationComponentMessages_OnCharacterTransform(object? sender, SanProtocol.AnimationComponent.CharacterTransform e)
        {
            ComponentPositions[e.ComponentId] = e;

            if (TargetAgentComponentIds.Contains(e.ComponentId))
            {
                if (FollowTargetMode)
                {
                    CurrentFrame = Math.Max(CurrentFrame, e.ServerFrame);
                    SetPosition(e.Position, e.OrientationQuat, e.GroundComponentId, false, false, e.ServerFrame);
                }
            }
        }

        private void ClientVoiceMessages_OnLocalAudioData(object? sender, SanProtocol.ClientVoice.LocalAudioData e)
        {
            if (e.AgentControllerId == MyAgentControllerId)
            {
                return;
            }

            if (TargetHandles.Count != 0 && !TargetAgentControllerIds.Contains(e.AgentControllerId))
            {
                return;
            }
            else
            {
                if (LastTimeWeListenedToOurTarget != null)
                {
                    if ((DateTime.Now - LastTimeWeListenedToOurTarget.Value).TotalMilliseconds > 1000)
                    {
                        LastTimeWeListenedToOurTarget = null;
                        CurrentlyListeningTo = null;
                    }
                }

                if (CurrentlyListeningTo != null && e.AgentControllerId != CurrentlyListeningTo.Value)
                {
                    return;
                }

                CurrentlyListeningTo = e.AgentControllerId;
                LastTimeWeListenedToOurTarget = DateTime.Now;
            }

            Driver.VoiceClient.SendPacket(new LocalAudioData(
                e.Instance,
                MyAgentControllerId,
                new AudioData(VoiceSequence, e.Data.Volume, e.Data.Data),
                new SpeechGraphicsData(VoiceSequence, e.SpeechGraphicsData.Data),
                0
            ));
            VoiceSequence++;
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

            if (TargetSessionIds.Contains(e.SessionId))
            {
                var componentId = e.CharacterObjectId * 0x100000000ul;

                TargetAgentComponentIds.Add(componentId);
                TargetAgentControllerIds.Add(e.AgentControllerId);
                Output($"Found target agent controller ID: {e.AgentControllerId}");

                if(ComponentPositions.ContainsKey(componentId) && MyAgentComponentId != 0)
                {
                    if (FollowTargetMode)
                    {
                        Output("Teleporting to our target...");
                        var lastCharacterTransform = ComponentPositions[componentId];
                        CurrentFrame = e.Frame+10;
                        SetPosition(lastCharacterTransform.Position, lastCharacterTransform.OrientationQuat, lastCharacterTransform.GroundComponentId, true, true, e.Frame);
                    }
                }
            }
        }

        private void WorldStateMessages_OnDestroyAgentController(object? sender, SanProtocol.WorldState.DestroyAgentController e)
        {
            TargetAgentControllerIds.Remove(e.AgentControllerId);
        }

        private void ClientRegionMessages_OnSetAgentController(object? sender, SanProtocol.ClientRegion.SetAgentController e)
        {
            Output($"Agent controller has been set to {e.AgentControllerId}");
            this.MyAgentControllerId = e.AgentControllerId;

            Output("Sending to voice server: LocalAudioStreamState(1)...");
            Driver.VoiceClient.SendPacket(new LocalAudioStreamState(Driver.VoiceClient.InstanceId, 0, 1, 1));

            Output("Sending to voice server: LocalAudioPosition(0,0,0)...");
            Driver.VoiceClient.SendPacket(new LocalAudioPosition((uint)VoiceSequence++, Driver.VoiceClient.InstanceId, new List<float>() { 0, 0, 0 }, MyAgentControllerId));

            foreach (var targetAgentComponentId in TargetAgentComponentIds)
            {
                if (ComponentPositions.ContainsKey(targetAgentComponentId))
                {
                    if (FollowTargetMode)
                    {
                        Output("Teleporting to our target...");
                        var lastCharacterTransform = ComponentPositions[targetAgentComponentId];
                        CurrentFrame = e.Frame;
                        SetPosition(lastCharacterTransform.Position, lastCharacterTransform.OrientationQuat, lastCharacterTransform.GroundComponentId, true, true, e.Frame);
                    }

                    break;
                }
            }
        }

        private void SimulationMessages_OnInitialTimestamp(object? sender, SanProtocol.Simulation.InitialTimestamp e)
        {
            CurrentFrame = e.Frame;
        }

        private void SimulationMessages_OnTimestamp(object? sender, SanProtocol.Simulation.Timestamp e)
        {
            CurrentFrame = e.Frame;
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

            TargetSessionIds.Remove(e.SessionId);
        }

        private void ClientRegionMessages_OnAddUser(object? sender, SanProtocol.ClientRegion.AddUser e)
        {
            PersonaSessionMap[e.SessionId] = e;

            if (TargetHandles.Contains(e.Handle.ToLower()))
            {
                Output($"Target found. SessionID = {e.SessionId}");
                TargetSessionIds.Add(e.SessionId);
            }

            Output($"{e.UserName} ({e.Handle}) Entered the region");
        }

        private void ClientKafkaMessages_OnRegionChat(object? sender, RegionChat e)
        {
            if (e.Message == "")
            {
                return;
            }

            Output($"{e.FromPersonaId}: {e.Message}");
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
            //Driver.JoinRegion("sansar-studios", "nexus").Wait();
            Driver.JoinRegion("nopnop", "unit").Wait();
        }
    }
}
