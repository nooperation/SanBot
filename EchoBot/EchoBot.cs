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

namespace EchoBot
{
    public class EchoBot
    {
        public Driver Driver { get; set; }

        public DateTime? LastTimeWeListenedToOurTarget { get; set; } = null;
        public uint? CurrentlyListeningTo { get; set; } = null;
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
        public bool FollowTargetMode { get; set; } = true;
        public bool RepeatVoice { get; set; } = true;
        public bool DistortVoice { get; set; } = true;
        public int BufferMovementAmount { get; set; } = 5;

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

            Driver.KafkaClient.ClientKafkaMessages.OnPrivateChat += ClientKafkaMessages_OnPrivateChat;
            Driver.KafkaClient.ClientKafkaMessages.OnLoginReply += ClientKafkaMessages_OnLoginReply;

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
            Driver.RegionClient.WorldStateMessages.OnDestroyCluster += WorldStateMessages_OnDestroyCluster;

            Driver.VoiceClient.ClientVoiceMessages.OnLoginReply += ClientVoiceMessages_OnLoginReply;
            Driver.VoiceClient.ClientVoiceMessages.OnLocalAudioData += ClientVoiceMessages_OnLocalAudioData;

            Driver.StartAsync(config).Wait();

            Stopwatch watch = new Stopwatch();


            while (true)
            {
                if (!Driver.Poll())
                {
                    //Thread.Sleep(10);
                }
            }
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

            var persona = TargetPersonas
                .Where(n => n.AgentComponentId == e.ComponentId)
                .FirstOrDefault();
            if (persona == null)
            {
                return;
            }

            Driver.RegionClient.SendPacket(new SanProtocol.AgentController.AgentPlayAnimation(
                Driver.MyPersonaData.AgentControllerId.Value,
                Driver.GetCurrentFrame(),
                Driver.MyPersonaData.AgentComponentId.Value,
                e.ResourceId,
                e.PlaybackSpeed,
                e.SkeletonType,
                e.AnimationType,
                e.PlaybackMode
            ));
        }

        private void AnimationComponentMessages_OnBehaviorStateUpdate(object? sender, SanProtocol.AnimationComponent.BehaviorStateUpdate e)
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

            Driver.RegionClient.SendPacket(new SanProtocol.AgentController.RequestBehaviorStateUpdate(
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

        private void AgentControllerMessages_OnCharacterControllerInputReliable(object? sender, SanProtocol.AgentController.CharacterControllerInputReliable e)
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

            Driver.RegionClient.SendPacket(new SanProtocol.AgentController.CharacterControllerInputReliable(
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
                return;
            }

            Driver.RegionClient.SendPacket(new SanProtocol.AgentController.CharacterControllerInput(
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
                return;
            }

            if(persona.LastTransform == null)
            {
                return;
            }

            if (FollowTargetMode)
            {
                Driver.SetPosition(persona.LastTransform.Position, persona.LastTransform.OrientationQuat, persona.LastTransform.GroundComponentId, true);
                Driver.SetVoicePosition(persona.LastTransform.Position, true);
            }
        }

        private void AnimationComponentMessages_OnCharacterTransform(object? sender, SanProtocol.AnimationComponent.CharacterTransform e)
        {
            var persona = TargetPersonas
                .Where(n => n.AgentComponentId == e.ComponentId)
                .FirstOrDefault();
            if (persona == null)
            {
                return;
            }

            if (persona.LastTransform == null)
            {
                return;
            }

            if (FollowTargetMode)
            {
                Driver.SetPosition(persona.LastTransform.Position, persona.LastTransform.OrientationQuat, persona.LastTransform.GroundComponentId, true);
                Driver.SetVoicePosition(persona.LastTransform.Position, true);
            }
        }

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

            if (TargetHandles.Count != 0 && persona == null)
            {
                return;
            }

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

            Driver.VoiceClient.SendPacket(new LocalAudioData(
                e.Instance,
                Driver.MyPersonaData.AgentControllerId.Value,
                new AudioData(Driver.VoiceClient.CurrentSequence, e.Data.Volume, e.Data.Data),
                new SpeechGraphicsData(Driver.VoiceClient.CurrentSequence, e.SpeechGraphicsData.Data),
                0
            ));
            Driver.VoiceClient.CurrentSequence++;
        }

        private void ClientVoiceMessages_OnLoginReply(object? sender, SanProtocol.ClientVoice.LoginReply e)
        {
            Output("Logged into voice server: " + e.ToString());
        }

        private void WorldStateMessages_OnCreateAgentController(object? sender, SanProtocol.WorldState.CreateAgentController e)
        {
            var targetPersona = TargetPersonas
                .Where(n => n.SessionId == e.SessionId)
                .FirstOrDefault();
            if (targetPersona == null)
            {
                return;
            }

            if(targetPersona.LastTransform == null)
            {
                return;
            }

            Driver.SetPosition(targetPersona.LastTransform.Position, targetPersona.LastTransform.OrientationQuat, targetPersona.LastTransform.GroundComponentId, true);
            Driver.SetVoicePosition(targetPersona.LastTransform.Position, true);
        }

        private void ClientRegionMessages_OnSetAgentController(object? sender, SanProtocol.ClientRegion.SetAgentController e)
        {
            Output("Sending to voice server: LocalAudioStreamState(1)...");
            Driver.VoiceClient.SendPacket(new SanProtocol.ClientVoice.LocalAudioStreamState(Driver.VoiceClient.InstanceId, 0, 1, 1));

            if (!FollowTargetMode)
            {
                return;
            }

            foreach (var targetPersona in TargetPersonas)
            {
                if (targetPersona.LastTransform != null)
                {
                    Output("Teleporting to our target...");
                    Driver.SetPosition(targetPersona.LastTransform.Position, targetPersona.LastTransform.OrientationQuat, targetPersona.LastTransform.GroundComponentId, true);
                    Driver.SetVoicePosition(targetPersona.LastTransform.Position, true);
                    break;
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
                Driver.WarpToPosition(e.SpawnPosition, e.SpawnRotation);
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
        }

        private void ClientRegionMessages_OnAddUser(object? sender, SanProtocol.ClientRegion.AddUser e)
        {
            var persona = Driver.PersonasBySessionId
                .Where(n => n.Key == e.SessionId)
                .Select(n => n.Value)
                .LastOrDefault();
            if(persona == null)
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
          //  Driver.JoinRegion("sansar-studios", "social-hub").Wait();
             Driver.JoinRegion("nopnopnop", "owo").Wait();
            //Driver.JoinRegion("mijekamunro", "gone-grid-city-prime-millenium").Wait();
          //  Driver.JoinRegion("nopnopnop", "aaaaaaaaaaaa").Wait();
        }
    }
}
