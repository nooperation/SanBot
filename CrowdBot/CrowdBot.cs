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
using System.Net;
using SanBot.BaseBot;
using static SanProtocol.Messages;

namespace CrowdBot
{
    public class CrowdBot : SimpleBot
    {
        public event EventHandler? OnRequestRestartBot;
        public event EventHandler? OnRequestAddBot;

        public Driver Driver { get; set; }

        public HashSet<PersonaData> TargetPersonas { get; set; } = new HashSet<PersonaData>();

        public string Id { get; set; } = "";

        public bool FollowTargetMode { get; set; } = true;
        public bool IsRunning { get; set; } = false;

        public HashSet<string> OwnerHandles { get; set; } = new HashSet<string>() {
            "nop",
            "nopnop",
            "nopnopnop",
            "vitaminc-0154",
            "lgtv-user",
            "randsome",
            "eldiabloclaven",
            "nick-sansar"
        };

        public SanProtocol.AnimationComponent.CharacterTransformPersistent? SavedTransform { get; set; }
        public SanProtocol.AgentController.AgentPlayAnimation? SavedAnimation { get; set; }
        public SanProtocol.AgentController.CharacterControllerInputReliable? SavedControllerInput { get; set; }

        public string Voice { get; set; }
        public Entrypoint.GoogleTTSVoice GoogleTTSVoice { get; set; }
        public List<string> Catchphrases { get; set; }
        public bool UseCatchprase { get; set; } = false;

        public CrowdBot(string id, SanProtocol.AnimationComponent.CharacterTransformPersistent? transformToRestore, SanProtocol.AgentController.CharacterControllerInputReliable? controllerInputToRestore, SanProtocol.AgentController.AgentPlayAnimation? animationToRestore)
        {
            Id = id;
            SavedTransform = transformToRestore;
            SavedAnimation = animationToRestore;
            SavedControllerInput = controllerInputToRestore;

            Driver = new Driver();

            Driver.OnOutput += Driver_OnOutput;

           /// Driver.RegionToJoin = new RegionDetails("nop", "flat2");
           // Driver.RegionToJoin = new RegionDetails("nop", "flat");
          //  Driver.RegionToJoin = new RegionDetails("sansar-studios", "club-sansar");
           Driver.RegionToJoin = new RegionDetails("sansar-studios", "social-hub");

            Driver.AutomaticallySendClientReady = true;
            Driver.UseVoice = false;
        }

        public override void OnPacket(IPacket packet)
        {
            base.OnPacket(packet);

            switch (packet.MessageId)
            {
                case ClientKafkaMessages.RegionChat:
                    ClientKafkaMessages_OnRegionChat((SanProtocol.ClientKafka.RegionChat)packet);
                    break;
                case ClientRegionMessages.SetAgentController:
                    ClientRegionMessages_OnSetAgentController((SanProtocol.ClientRegion.SetAgentController)packet);
                    break;
                case AnimationComponentMessages.CharacterTransform:
                    AnimationComponentMessages_OnCharacterTransform((SanProtocol.AnimationComponent.CharacterTransform)packet);
                    break;
                case AnimationComponentMessages.CharacterTransformPersistent:
                    AnimationComponentMessages_OnCharacterTransformPersistent((SanProtocol.AnimationComponent.CharacterTransformPersistent)packet);
                    break;
                case AnimationComponentMessages.BehaviorStateUpdate:
                    AnimationComponentMessages_OnBehaviorStateUpdate((SanProtocol.AnimationComponent.BehaviorStateUpdate)packet);
                    break;
                case AnimationComponentMessages.PlayAnimation:
                    AnimationComponentMessages_OnPlayAnimation((SanProtocol.AnimationComponent.PlayAnimation)packet);
                    break;
                case AgentControllerMessages.CharacterControllerInput:
                    AgentControllerMessages_OnCharacterControllerInput((SanProtocol.AgentController.CharacterControllerInput)packet);
                    break;
                case AgentControllerMessages.CharacterControllerInputReliable:
                    AgentControllerMessages_OnCharacterControllerInputReliable((SanProtocol.AgentController.CharacterControllerInputReliable)packet);
                    break;
                case AgentControllerMessages.CharacterIKPose:
                    AgentControllerMessages_OnCharacterIKPose((SanProtocol.AgentController.CharacterIKPose)packet);
                    break;
                case AgentControllerMessages.CharacterIKPoseDelta:
                    AgentControllerMessages_OnCharacterIKPoseDelta((SanProtocol.AgentController.CharacterIKPoseDelta)packet);
                    break;
                case AgentControllerMessages.CharacterControlPointInput:
                    AgentControllerMessages_OnCharacterControlPointInput((SanProtocol.AgentController.CharacterControlPointInput)packet);
                    break;
                case AgentControllerMessages.CharacterControlPointInputReliable:
                    AgentControllerMessages_OnCharacterControlPointInputReliable((SanProtocol.AgentController.CharacterControlPointInputReliable)packet);
                    break;
                case WorldStateMessages.CreateClusterViaDefinition:
                    WorldStateMessages_OnCreateClusterViaDefinition((SanProtocol.WorldState.CreateClusterViaDefinition)packet);
                    break;
                case WorldStateMessages.DestroyCluster:
                    WorldStateMessages_OnDestroyCluster((SanProtocol.WorldState.DestroyCluster)packet);
                    break;
            }
        }

        public void Start(ConfigFile config)
        {
            Driver.TextToSpeechVoice = Voice;
            Driver.GoogleTTSName = GoogleTTSVoice.Name;
            Driver.GoogleTTSPitch = GoogleTTSVoice.Pitch;
            Driver.GoogleTTSRate = GoogleTTSVoice.Rate;

            IsRunning = true;
            Driver.StartAsync(config).Wait();
        }

        public bool Poll()
        {
            if(!IsRunning)
            {
                return false;
            }

            Driver.Poll();
            return IsRunning;
        }

        public SanUUID ItemClousterResourceId { get; set; } = new SanUUID("04c2d5a7ea3d6fb47af66669cfdc9f9a"); // heart reaction thing
        private void WorldStateMessages_OnCreateClusterViaDefinition(SanProtocol.WorldState.CreateClusterViaDefinition e)
        {
            if (e.ResourceId == this.ItemClousterResourceId)
            {
                Driver.WarpToPosition(e.SpawnPosition, e.SpawnRotation);
                Driver.SetVoicePosition(e.SpawnPosition, true);
            }
        }

        private void AgentControllerMessages_OnCharacterControlPointInputReliable(SanProtocol.AgentController.CharacterControlPointInputReliable e)
        {
            if(Driver.MyPersonaData == null || Driver.MyPersonaData.AgentControllerId == null)
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

            if (e.AgentControllerId != Driver.MyPersonaData.AgentControllerId)
            {
                Driver.RegionClient.EnqueuePacket(new SanProtocol.AgentController.CharacterControlPointInputReliable(
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

        private void AgentControllerMessages_OnCharacterControlPointInput(SanProtocol.AgentController.CharacterControlPointInput e)
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
                return;
            }

            if (e.AgentControllerId != Driver.MyPersonaData.AgentControllerId)
            {
                Driver.RegionClient.EnqueuePacket(new SanProtocol.AgentController.CharacterControlPointInput(
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

        private void AgentControllerMessages_OnCharacterIKPoseDelta(SanProtocol.AgentController.CharacterIKPoseDelta e)
        {
            if (Driver.MyPersonaData == null || Driver.MyPersonaData.AgentControllerId == null)
            {
                return;
            }

            if (false && e.AgentControllerId != Driver.MyPersonaData.AgentControllerId)
            {
                Driver.RegionClient.EnqueuePacket(new SanProtocol.AgentController.CharacterIKPoseDelta(
                    Driver.MyPersonaData.AgentControllerId.Value,
                    Driver.GetCurrentFrame(),
                    e.BoneRotations,
                    e.RootBoneTranslationDelta
                ));
            }
        }

        private void AgentControllerMessages_OnCharacterIKPose(SanProtocol.AgentController.CharacterIKPose e)
        {
            if (Driver.MyPersonaData == null || Driver.MyPersonaData.AgentControllerId == null)
            {
                return;
            }

            if (false && e.AgentControllerId != Driver.MyPersonaData.AgentControllerId)
            {
                Driver.RegionClient.EnqueuePacket(new SanProtocol.AgentController.CharacterIKPose(
                    Driver.MyPersonaData.AgentControllerId.Value,
                    Driver.GetCurrentFrame(),
                    e.BoneRotations,
                    e.RootBoneTranslation
                ));
            }
        }

        private void AnimationComponentMessages_OnPlayAnimation(SanProtocol.AnimationComponent.PlayAnimation e)
        {
            if (Driver.MyPersonaData == null || Driver.MyPersonaData.AgentControllerId == null || Driver.MyPersonaData.AgentComponentId == null)
            {
                return;
            }

            var persona = TargetPersonas
                .Where(n => n.AgentComponentId == e.ComponentId)
                .FirstOrDefault();
            if(persona == null)
            {
                return;
            }

            var animationPacket = new SanProtocol.AgentController.AgentPlayAnimation(
                Driver.MyPersonaData.AgentControllerId.Value,
                Driver.GetCurrentFrame(),
                Driver.MyPersonaData.AgentComponentId.Value,
                e.ResourceId,
                e.PlaybackSpeed,
                e.SkeletonType,
                e.AnimationType,
                e.PlaybackMode
            );

            Driver.RegionClient.EnqueuePacket(animationPacket);
            SavedAnimation = animationPacket;
        }

        private void AnimationComponentMessages_OnBehaviorStateUpdate(SanProtocol.AnimationComponent.BehaviorStateUpdate e)
        {
            if (Driver.MyPersonaData == null || Driver.MyPersonaData.AgentControllerId == null || Driver.MyPersonaData.AgentComponentId == null)
            {
                return;
            }

            var persona = TargetPersonas
                .Where(n => n.AgentComponentId == e.ComponentId)
                .FirstOrDefault();
            if (persona == null)
            {
                return;
            }

            Driver.RegionClient.EnqueuePacket(new SanProtocol.AgentController.RequestBehaviorStateUpdate(
                Driver.GetCurrentFrame(),
                Driver.MyPersonaData.AgentComponentId.Value,
                Driver.MyPersonaData.AgentControllerId.Value,
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

        private void AgentControllerMessages_OnCharacterControllerInputReliable(SanProtocol.AgentController.CharacterControllerInputReliable e)
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
                return;
            }

            if (Math.Abs(e.MoveForward) > 0.0001f || Math.Abs(e.MoveRight) > 0.0001f)
            {
                SavedAnimation = null;
            }

            var controllerInputPacket = new SanProtocol.AgentController.CharacterControllerInputReliable(
                Driver.GetCurrentFrame(),
                Driver.MyPersonaData.AgentControllerId.Value,
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
            );

            Driver.RegionClient.EnqueuePacket(controllerInputPacket);
            SavedControllerInput = controllerInputPacket;
        }

        private void AgentControllerMessages_OnCharacterControllerInput(SanProtocol.AgentController.CharacterControllerInput e)
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
                return;
            }

            if (Math.Abs(e.MoveForward) > 0.0001f || Math.Abs(e.MoveRight) > 0.0001f)
            {
                SavedAnimation = null;
            }
                
            Driver.RegionClient.EnqueuePacket(new SanProtocol.AgentController.CharacterControllerInput(
                Driver.GetCurrentFrame(),
                Driver.MyPersonaData.AgentControllerId.Value,
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

        private void WorldStateMessages_OnDestroyCluster(SanProtocol.WorldState.DestroyCluster e)
        {
            var persona = TargetPersonas
                .Where(n => n.ClusterId == e.ClusterId)
                .FirstOrDefault();
            if (persona == null)
            {
                return;
            }

            TargetPersonas.RemoveWhere(n => n.ClusterId == e.ClusterId);
        }

        private void AnimationComponentMessages_OnCharacterTransformPersistent(SanProtocol.AnimationComponent.CharacterTransformPersistent e)
        {
            var persona = TargetPersonas
                .Where(n => n.AgentComponentId == e.ComponentId)
                .FirstOrDefault();
            if (persona == null)
            {
                return;
            }

            if (FollowTargetMode)
            {
                if(SavedAnimation == null)
                {
                    SavedTransform = e;
                }

                Driver.SetPosition(e.Position, e.OrientationQuat, e.GroundComponentId, true);
                Driver.SetVoicePosition(e.Position, false);
            }
        }

        Dictionary<ulong, bool> spokenToAvatar = new Dictionary<ulong, bool>();

        Random rand = new Random();
        private void AnimationComponentMessages_OnCharacterTransform(SanProtocol.AnimationComponent.CharacterTransform e)
        {
            var persona = Driver.PersonasBySessionId
                .Where(n => n.Value.AgentComponentId == e.ComponentId)
                .Select(n => n.Value)
                .FirstOrDefault();
            if (persona == null)
            {
                return;
            }

            if(!spokenToAvatar.ContainsKey(e.ComponentId))
            {
                spokenToAvatar.Add(e.ComponentId, false);
            }

            if (Driver.MyPersonaData == null || persona.SessionId == Driver.MyPersonaData.SessionId)
            {
                return;
            }

            if(UseCatchprase)
            {
                var myPos = Driver.MyPersonaData.Position;
                var distToTarget = Utils.Distance(myPos[0], persona.Position[0], myPos[1], persona.Position[1], myPos[2], persona.Position[2]);
                // Console.WriteLine($"Distance to {persona.UserName} = {distToTarget}");
                if (distToTarget <= 2.0)
                {
                    if (!spokenToAvatar[e.ComponentId])
                    {
                        spokenToAvatar[e.ComponentId] = true;
                        var catchphrase = Catchphrases[rand.Next(0, Catchphrases.Count)].Replace("#NAME#", persona.UserName);
                        Driver.SpeakAzure(catchphrase, true);
                    }
                }
                else if (distToTarget >= 10)
                {
                    spokenToAvatar[e.ComponentId] = false;
                }
            }

            var targetPersona = TargetPersonas
                .Where(n => n.AgentComponentId == e.ComponentId)
                .FirstOrDefault();
            if (targetPersona == null)
            {
                return;
            }

            if (FollowTargetMode)
            {
                Driver.SetPosition(e.Position, e.OrientationQuat, e.GroundComponentId, false);
            }
        }

        private void ClientRegionMessages_OnSetAgentController(SanProtocol.ClientRegion.SetAgentController e)
        {
            /*
            if (Driver.MyPersonaData == null || Driver.MyPersonaData.AgentControllerId == null || Driver.MyPersonaData.AgentComponentId == null)
            {
                Output("Agent Controller has been set, but MyPersonaData is null or missing data?");
                return;
            }

            if(SavedTransform != null)
            {
                SavedTransform.ComponentId = Driver.MyPersonaData.AgentComponentId.Value;
                SavedTransform.ServerFrame = Driver.GetCurrentFrame();
                Driver.RegionClient.EnqueuePacket(SavedTransform);
            }

            if(SavedControllerInput != null) {
                SavedControllerInput.Frame = Driver.GetCurrentFrame();
                SavedControllerInput.AgentControllerId = Driver.MyPersonaData.AgentControllerId.Value;
                Driver.RegionClient.EnqueuePacket(SavedControllerInput);
            }

            if(SavedAnimation != null)
            {
                SavedAnimation.AgentControllerId = Driver.MyPersonaData.AgentControllerId.Value;
                SavedAnimation.Frame = Driver.GetCurrentFrame();
                SavedAnimation.ComponentId = Driver.MyPersonaData.AgentComponentId.Value;
                Driver.RegionClient.EnqueuePacket(SavedAnimation);
            }
            */
        }

        private void ClientKafkaMessages_OnRegionChat(RegionChat e)
        {
            if (e.Message == "")
            {
                return;
            }

            if (Driver.InitialTimestamp == 0 || e.Timestamp < Driver.InitialTimestamp)
            {
                return;
            }

            var sourcePersona = Driver.PersonasBySessionId
                .Where(n => n.Value.PersonaId == e.FromPersonaId)
                .Select(n => n.Value)
                .FirstOrDefault();
            if(sourcePersona == null)
            {
                return;
            }

            if(!OwnerHandles.Contains(sourcePersona.Handle.ToLower()))
            {
                return;
            }

            Output($"Owner Message: {e.Message}");
            if(e.Message.StartsWith('/'))
            {
                e.Message = e.Message[1..];
            }

            var firstSpace = e.Message.IndexOf(' ');
            if(firstSpace == -1)
            {
                return;
            }

            var commandDestination = e.Message[..firstSpace].ToLower().Trim();
            var command = e.Message[(firstSpace + 1)..].ToLower().Trim();

            Output($"Owner Message: commandDestination={commandDestination} Command={command}");

            if (commandDestination != Id && commandDestination != Driver.MyPersonaDetails?.Name && commandDestination.ToLower() != Driver.MyPersonaDetails?.Handle.ToLower())
            {
                return;
            }

            if (command == "follow")
            {
                var persona = Driver.PersonasBySessionId
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
                StartFollowing(persona);
            }
            if (command.StartsWith("follow "))
            {
                var followTargetHandle = command[6..].Trim();

                var persona = Driver.PersonasBySessionId
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
                StartFollowing(persona);
            }
            else if(command.StartsWith("clone "))
            {
                var cloneTargetHandle = command[6..].ToLower().Trim();

                var targetAvatarAssetId = "";

                if(Regex.IsMatch(cloneTargetHandle, "[0-9a-f]{32}"))
                {
                    targetAvatarAssetId = cloneTargetHandle;
                }
                else
                {
                    var persona = Driver.PersonasBySessionId
                       .Where(n => n.Value.Handle.ToLower() == cloneTargetHandle)
                       .Select(n => n.Value)
                       .OrderBy(n => n.SessionId)
                       .LastOrDefault();
                    if (persona == null)
                    {
                        Say($"Could not find session for {cloneTargetHandle}");
                        return;
                    }

                    var match = Regex.Match(persona.AvatarType, @"avatarAssetId\s*=\s*""(?<avatarAssetId>ERROR|[a-zA-Z0-9]{32})""");
                    if (!match.Success)
                    {
                        Say($"Failed to parse avatar type for {cloneTargetHandle}");
                        return;
                    }

                    targetAvatarAssetId = match.Groups["avatarAssetId"].Value;
                }

                if(!Utils.AvatarAssetIdExists(targetAvatarAssetId))
                {
                    Say($"Could not find avatar asset for {cloneTargetHandle}");
                    return;
                }

                Driver.WebApi.SetAvatarIdAsync(Driver.MyPersonaDetails.Id, targetAvatarAssetId).Wait();

                Say("Ok, changing");
                OnRequestRestartBot?.Invoke(this, new EventArgs());
                Driver.Disconnect();
            }
            else if(command == "add")
            {
                Say("Ok");
                OnRequestAddBot?.Invoke(this, new EventArgs());
            }
            else if(command == "stop")
            {
                Output($"Stop following");
                Say("Ok");
                StopFollowing();
            }
            else if(command == "exit")
            {
                Say("Bye");
                Disconnect();
            }
        }

        public void Disconnect()
        {
            StopFollowing();
            IsRunning = false;
            Driver.Disconnect();
        }



        public void StartFollowing(PersonaData persona)
        {
            if(persona.AgentControllerId == null)
            {
                Output("Could not follow because we don't know what this user's controlelr id is yet");
                return;
            }

            TargetPersonas.Clear();
            TargetPersonas.Add(persona);

            if (persona.LastTransform != null)
            {
                Driver.SetPosition(persona.LastTransform.Position, persona.LastTransform.OrientationQuat, persona.LastTransform.GroundComponentId, true);
            }

            Say($"Started following {persona.UserName} ({persona.Handle})");
        }

        public void StopFollowing()
        {
            TargetPersonas.Clear();
        }

        private void Driver_OnOutput(object? sender, string message)
        {
            Output(message, sender?.GetType().Name ?? "Bot");
        }

        public int ProcessId { get; set; } = Process.GetCurrentProcess().Id;

        public void Output(string str, string? sender = nameof(CrowdBot))
        {
            var date = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            var finalOutput = "";

            var lines = str.Replace("\r", "").Split("\n");
            foreach (var line in lines)
            {
                finalOutput += $"[{ProcessId}] {date} [{Id}] [{sender}] {line}{Environment.NewLine}";
            }

            Console.Write(finalOutput);
        }

        public void Say(string str)
        {
            Output(str);
            //this.Driver.SendChatMessage(str);
        }

    }
}
