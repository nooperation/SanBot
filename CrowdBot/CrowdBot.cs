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
using SanBot.Core.MessageHandlers;
using System.Diagnostics;
using SanProtocol.WorldState;

namespace CrowdBot
{
    public class CrowdBot
    {
        public Driver Driver { get; set; }
        public Dictionary<uint, SanProtocol.ClientRegion.AddUser> PersonaSessionMap { get; } = new Dictionary<uint, SanProtocol.ClientRegion.AddUser>();
        public uint MyAgentControllerId { get; private set; }
        public ulong MyAgentComponentId { get; private set; }

        public HashSet<ulong> TargetAgentControllerIds { get; set; } = new HashSet<ulong>();
        public HashSet<ulong> TargetAgentComponentIds { get; set; } = new HashSet<ulong>();

        public long LastTimestampTicks { get; set; } = 0;
        public ulong LastTimestampFrame { get; set; } = 0;
        public long InitialTimestamp { get; set; } = 0;
        public string MyName { get; set; } = "";

        Dictionary<uint, CreateAgentController> AgentControllersBySessionId { get; set; } = new System.Collections.Generic.Dictionary<uint, CreateAgentController>();
        public Dictionary<ulong, SanProtocol.AnimationComponent.CharacterTransform> ComponentPositionsByComponentId { get; set; } = new Dictionary<ulong, SanProtocol.AnimationComponent.CharacterTransform>();

        public Queue<SanProtocol.AnimationComponent.CharacterTransform> CharacterTransformBuffer { get; set; } = new Queue<SanProtocol.AnimationComponent.CharacterTransform>();


        public bool FollowTargetMode { get; set; } = true;
        public int BufferMovementAmount { get; set; } = 5;



        public HashSet<SanUUID> OwnerPersonaIDs { get; set; } = new HashSet<SanUUID>();
        public HashSet<string> OwnerHandles { get; set; } = new HashSet<string>() {
            "nop",
            "nopnop",
            "nopnopnop",
            "vitaminc-0154"
        };

        public CrowdBot(string name)
        {
            MyName = name;

            ConfigFile config;
            this.PersonaSessionMap = new Dictionary<uint, SanProtocol.ClientRegion.AddUser>();
            var sanbotPath = Path.Join(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SanBot"
            );
            var configPath = Path.Join(sanbotPath, "CrowdBot.config.json");

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

            Driver.RegionClient.WorldStateMessages.OnCreateAgentController += WorldStateMessages_OnCreateAgentController;
            Driver.RegionClient.WorldStateMessages.OnDestroyAgentController += WorldStateMessages_OnDestroyAgentController;
            Driver.RegionClient.WorldStateMessages.OnDestroyCluster += WorldStateMessages_OnDestroyCluster;

            Driver.RegionClient.SimulationMessages.OnTimestamp += SimulationMessages_OnTimestamp;
            Driver.RegionClient.SimulationMessages.OnInitialTimestamp += SimulationMessages_OnInitialTimestamp;

            Driver.StartAsync(config).Wait();

            while (true)
            {
                if (!Driver.Poll())
                {
                    Thread.Sleep(1);
                }
            }
        }


        private void AgentControllerMessages_OnCharacterControlPointInputReliable(object? sender, SanProtocol.AgentController.CharacterControlPointInputReliable e)
        {
            if (e.AgentControllerId != MyAgentControllerId)
            {
                Driver.RegionClient.SendPacket(new SanProtocol.AgentController.CharacterControlPointInputReliable(
                    GetCurrentFrame(),
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
                Driver.RegionClient.SendPacket(new SanProtocol.AgentController.CharacterControlPointInput(
                    GetCurrentFrame(),
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
                Driver.RegionClient.SendPacket(new SanProtocol.AgentController.CharacterIKPoseDelta(
                    MyAgentControllerId,
                    GetCurrentFrame(),
                    e.BoneRotations,
                    e.RootBoneTranslationDelta
                ));
            }
        }

        private void AgentControllerMessages_OnCharacterIKPose(object? sender, SanProtocol.AgentController.CharacterIKPose e)
        {
            if (false && e.AgentControllerId != MyAgentControllerId)
            {
                Driver.RegionClient.SendPacket(new SanProtocol.AgentController.CharacterIKPose(
                    MyAgentControllerId,
                    GetCurrentFrame(),
                    e.BoneRotations,
                    e.RootBoneTranslation
                ));
            }
        }

        private void AnimationComponentMessages_OnPlayAnimation(object? sender, SanProtocol.AnimationComponent.PlayAnimation e)
        {
            if (TargetAgentComponentIds.Contains(e.ComponentId))
            {
                Driver.RegionClient.SendPacket(new SanProtocol.AgentController.AgentPlayAnimation(
                    MyAgentControllerId,
                    GetCurrentFrame(),
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
                Driver.RegionClient.SendPacket(new SanProtocol.AgentController.RequestBehaviorStateUpdate(
                    GetCurrentFrame(),
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
                Driver.RegionClient.SendPacket(new SanProtocol.AgentController.CharacterControllerInputReliable(
                    GetCurrentFrame(),
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
                Driver.RegionClient.SendPacket(new SanProtocol.AgentController.CharacterControllerInput(
                    GetCurrentFrame(),
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

        void SetPosition(List<float> position, Quaternion quat, ulong groundComponentId, bool ignoreDistanceCheck, bool isPersistent)
        {
            if (position.Count != 3)
            {
                throw new Exception($"{nameof(SetPosition)} Expected float3 position, got float{position.Count}");
            }

            if (isPersistent)
            {
                Output("CharacterTransformPersistent");
                Driver.RegionClient.SendPacket(new SanProtocol.AnimationComponent.CharacterTransformPersistent(
                    MyAgentComponentId,
                    GetCurrentFrame(),
                    groundComponentId,
                    new List<float>()
                    {
                      position[0],
                      position[1],
                      position[2],
                    },
                    quat
                ));
            }
            else
            {
                Output("CharacterTransform");
                Driver.RegionClient.SendPacket(new SanProtocol.AnimationComponent.CharacterTransform(
                    MyAgentComponentId,
                    GetCurrentFrame(),
                    groundComponentId,
                    new List<float>()
                    {
                        position[0],
                        position[1],
                        position[2],
                    },
                    quat
                ));
            }
        }

        void WarpToPosition(List<float> position3, List<float> rotation4)
        {
            if (position3.Count != 3)
            {
                throw new Exception($"{nameof(SetPosition)} Expected float3 position, got float{position3.Count}");
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
        }

        private void AnimationComponentMessages_OnCharacterTransformPersistent(object? sender, SanProtocol.AnimationComponent.CharacterTransformPersistent e)
        {
            ComponentPositionsByComponentId[e.ComponentId] = e;
            Output($"OnCharacterTransformPersistent {e.ComponentId} {String.Join(',', e.Position)}");

            if (TargetAgentComponentIds.Contains(e.ComponentId))
            {
                if (FollowTargetMode)
                {
                    CharacterTransformBuffer.Enqueue(e);

                    if (CharacterTransformBuffer.Count >= this.BufferMovementAmount)
                    {
                        var transform = CharacterTransformBuffer.Dequeue();
                        SetPosition(transform.Position, transform.OrientationQuat, transform.GroundComponentId, true, true);
                    }
                }
            }
        }

        private void AnimationComponentMessages_OnCharacterTransform(object? sender, SanProtocol.AnimationComponent.CharacterTransform e)
        {
            ComponentPositionsByComponentId[e.ComponentId] = e;

            if (TargetAgentComponentIds.Contains(e.ComponentId))
            {
                if (FollowTargetMode)
                {
                    CharacterTransformBuffer.Enqueue(e);

                    if (CharacterTransformBuffer.Count >= this.BufferMovementAmount)
                    {
                        var transform = CharacterTransformBuffer.Dequeue();
                        SetPosition(transform.Position, transform.OrientationQuat, transform.GroundComponentId, false, false);
                    }
                }
            }
        }

        private void WorldStateMessages_OnCreateAgentController(object? sender, SanProtocol.WorldState.CreateAgentController e)
        {
            var componentId = e.CharacterObjectId * 0x100000000ul;

            if (e.PersonaId == Driver.MyPersonaDetails!.Id)
            {
                Say($"Hello I am {MyName}");
                Output($"My AgentComponentId is {componentId}");
                this.MyAgentComponentId = componentId;
            }

            Output($"CreateAgentController: ControllerId={e.AgentControllerId} personaId={e.PersonaId} sessionId={e.SessionId} componentId={componentId}");
            AgentControllersBySessionId[e.SessionId] = e;
        }

        private void WorldStateMessages_OnDestroyAgentController(object? sender, SanProtocol.WorldState.DestroyAgentController e)
        {
            TargetAgentControllerIds.Remove(e.AgentControllerId);
        }

        private void ClientRegionMessages_OnSetAgentController(object? sender, SanProtocol.ClientRegion.SetAgentController e)
        {
            Output($"Agent controller has been set to {e.AgentControllerId}");
            this.MyAgentControllerId = e.AgentControllerId;
        }


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
            Output($"InitialTimestamp {e.Frame} | {e.Nanoseconds}");

            LastTimestampFrame = e.Frame;
            LastTimestampTicks = DateTime.Now.Ticks;
            InitialTimestamp = DateTimeOffset.Now.ToUnixTimeMilliseconds();
        }

        private void SimulationMessages_OnTimestamp(object? sender, SanProtocol.Simulation.Timestamp e)
        {
            //Output($"Server frame: {e.Frame} Client frame: {GetCurrentFrame()} | Diff={(long)e.Frame - (long)GetCurrentFrame()}");
            LastTimestampFrame = e.Frame;
            LastTimestampTicks = DateTime.Now.Ticks;
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

            if (OwnerHandles.Contains(e.Handle.ToLower())) {
                OwnerPersonaIDs.Add(e.PersonaId);
            }

            Output($"{e.UserName} ({e.Handle} | {e.PersonaId}) Entered the region");
        }

        private void ClientKafkaMessages_OnRegionChat(object? sender, RegionChat e)
        {
            if (e.Message == "")
            {
                return;
            }

            if(!OwnerPersonaIDs.Contains(e.FromPersonaId))
            {
                return;
            }

            if (InitialTimestamp == 0 || e.Timestamp < InitialTimestamp)
            {
                return;
            }

            Output($"Owner Message: {e.Message}");

            var firstSpace = e.Message.IndexOf(' ');
            if(firstSpace == -1)
            {
                return;
            }

            var commandDestination = e.Message.Substring(0, firstSpace).ToLower().Trim();
            var command = e.Message.Substring(firstSpace + 1).ToLower().Trim();

            Output($"Owner Message: commandDestination={commandDestination} Command={command}");

            if (commandDestination.ToLower() != MyName)
            {
                return;
            }

            if (command == "follow")
            {
                var persona = PersonaSessionMap
                    .Where(n => n.Value.PersonaId == e.FromPersonaId)
                    .Select(n => n.Value)
                    .OrderBy(n => n.SessionId)
                    .LastOrDefault();
                if(persona == null)
                {
                    Say($"Could not find session details for persona ID {e.FromPersonaId}");
                    return;
                }

                Output($"Start following {persona.Handle}...");
                StartFollowing(persona.SessionId);
            }
            if (command.StartsWith("follow "))
            {
                var followTargetHandle = command.Substring(6).Trim();

                var persona = PersonaSessionMap
                    .Where(n => n.Value.Handle.ToLower() == followTargetHandle)
                    .Select(n => n.Value)
                    .OrderBy(n => n.SessionId)
                    .LastOrDefault();
                if (persona == null)
                {
                    Say($"Could not find session details for persona ID {e.FromPersonaId}");
                    return;
                }

                Output($"Start following {persona.UserName} ({persona.Handle})...");
                StartFollowing(persona.SessionId);
            }
            else if(command == "stop")
            {
                Output($"Stop following");
                StopFollowing();
            }
            else if(command == "exit")
            {

                StopFollowing();
            }
        }

        public void StartFollowing(uint sessionId)
        {
            if(!PersonaSessionMap.TryGetValue(sessionId, out var session))
            {
                Say("Invalid session id");
                return;
            }

            if(!AgentControllersBySessionId.TryGetValue(sessionId, out var agentController))
            {
                Say($"I can't find {session.UserName}'s controller");
                return;
            }

            var componentId = agentController.CharacterObjectId * 0x100000000ul;

            TargetAgentControllerIds.Clear();
            TargetAgentControllerIds.Add(agentController.AgentControllerId);

            TargetAgentComponentIds.Clear();
            TargetAgentComponentIds.Add(componentId);

            if(ComponentPositionsByComponentId.TryGetValue(componentId, out var lastTransform))
            {
                SetPosition(lastTransform.Position, lastTransform.OrientationQuat, lastTransform.GroundComponentId, true, true);
            }

            Say($"Started following {session.UserName} ({session.Handle})");
        }

        public void StopFollowing()
        {
            Say("Ok");
            TargetAgentControllerIds.Clear();
            TargetAgentComponentIds.Clear();
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
        
        private void Driver_OnOutput(object? sender, string message)
        {
            Output(message, sender?.GetType().Name ?? "Bot");
        }

        public void Output(string str, string sender = nameof(CrowdBot))
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

        public void Say(string str)
        {
            this.Driver.SendChatMessage(str);
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
         //   Driver.WebApi.SetAvatarIdAsync(Driver.MyPersonaDetails.Id, "43668ab727c00fd7d33a5af1085493dd").Wait();

           // Driver.JoinRegion("djm3n4c3-9174", "dj-s-outside-fun2").Wait();
          ///  Driver.JoinRegion("sansar-studios", "social-hub").Wait();
            // Driver.JoinRegion("nopnop", "unit").Wait();
            //Driver.JoinRegion("mijekamunro", "gone-grid-city-prime-millenium").Wait();
            Driver.JoinRegion("nopnopnop", "owo").Wait();
        }
    }
}
