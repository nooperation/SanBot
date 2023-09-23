using SanBot.BaseBot;
using SanBot.Core;
using SanProtocol;
using SanProtocol.ClientKafka;
using SanProtocol.ClientRegion;
using static SanProtocol.Messages;

namespace SanBot
{
    public class Bot : SimpleBot
    {
        public enum RunMode
        {
            Hotfeet,
            Shitlisted,
        }

        public List<PersonaData> TargetPersonas { get; set; } = new List<PersonaData>();
        public DateTime LastSpawn { get; set; } = DateTime.Now;
        public System.Numerics.Vector3 PreviousPosition { get; set; }
        public float DistanceSinceLastSpawn { get; set; } = 0.0f;

        private readonly HashSet<ulong> OurSpawnedComponentIds = new();

        public SanUUID ItemClousterResourceId { get; set; }
        public SanUUID ItemClousterResourceIdBig { get; set; }
        public int MaxSpawnRateMs { get; set; }
        public float DistancedRequiredBeforeSpawningMore { get; set; }
        public List<float> SpawnOffset { get; set; } = new List<float> { 0.0f, 0.0f, 0.0f };

        public RunMode CurrentRunMode { get; set; } = RunMode.Hotfeet;

        public HashSet<string> TargetHandles { get; set; } = new HashSet<string>()
        {
            "fakename-12345678"
        };

        public override Task Init()
        {
            var unused = base.Init();

            Driver.AutomaticallySendClientReady = true;
            Driver.OnOutput += Driver_OnOutput;

            return Task.CompletedTask;
        }

        public override void OnPacket(IPacket packet)
        {
            base.OnPacket(packet);

            switch (packet.MessageId)
            {
                case ClientRegionMessages.AddUser:
                    ClientRegionMessages_OnAddUser((AddUser)packet);
                    break;
                case ClientRegionMessages.RemoveUser:
                    ClientRegionMessages_OnRemoveUser((RemoveUser)packet);
                    break;
                case AnimationComponentMessages.CharacterTransform:
                    AnimationComponentMessages_OnCharacterTransform((SanProtocol.AnimationComponent.CharacterTransform)packet);
                    break;
                case AnimationComponentMessages.CharacterTransformPersistent:
                    AnimationComponentMessages_OnCharacterTransform((SanProtocol.AnimationComponent.CharacterTransformPersistent)packet);
                    break;
                case WorldStateMessages.CreateClusterViaDefinition:
                    WorldStateMessages_OnCreateClusterViaDefinition((SanProtocol.WorldState.CreateClusterViaDefinition)packet);
                    break;
                case WorldStateMessages.DestroyCluster:
                    WorldStateMessages_OnDestroyCluster((SanProtocol.WorldState.DestroyCluster)packet);
                    break;
            }
        }

        public Bot()
        {
            CurrentRunMode = RunMode.Hotfeet;

            if (CurrentRunMode == RunMode.Shitlisted)
            {
                ItemClousterResourceId = Driver.Clusterbutt("593fbd143678551813d813c51d9fca2a");
                ItemClousterResourceIdBig = Driver.Clusterbutt("c196ada06a5b6d85357e5d08d6b6a6df");
                MaxSpawnRateMs = 100;
                DistancedRequiredBeforeSpawningMore = 0.5f;
                SpawnOffset = new List<float> { 0.0f, 0.0f, 0.0f };
            }
            else
            {
                TargetHandles = new HashSet<string>();
                ItemClousterResourceId = Driver.Clusterbutt("771e941bbea30bef600e9ef74c3f270a"); // flame
                ItemClousterResourceIdBig = Driver.Clusterbutt("771e941bbea30bef600e9ef74c3f270a");
                MaxSpawnRateMs = 10;
                DistancedRequiredBeforeSpawningMore = 0.0f;
                SpawnOffset = new List<float> { 0.0f, 0.0f, 0.0f };
            }

            ConfigFile config;
            var sanbotPath = Path.Join(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SanBot"
            );
            var configPath = Path.Join(sanbotPath, "SanBot.config.json");

            try
            {
                config = ConfigFile.FromJsonFile(configPath);
            }
            catch (Exception ex)
            {
                throw new Exception("Missing or invalid config.json", ex);
            }

            Start(config.Username, config.Password).Wait();
        }

        private void WorldStateMessages_OnDestroyCluster(SanProtocol.WorldState.DestroyCluster e)
        {
            _ = OurSpawnedComponentIds.Remove((ulong)e.ClusterId * 0x100000000);
        }

        private void AnimationComponentMessages_OnCharacterTransformPersistent(SanProtocol.AnimationComponent.CharacterTransformPersistent e)
        {
            if (CurrentRunMode != RunMode.Shitlisted)
            {
                var persona = TargetPersonas
                    .Where(n => n.AgentComponentId == e.ComponentId)
                    .FirstOrDefault();
                if (persona == null)
                {
                    return;
                }

                if (!OurSpawnedComponentIds.Contains(e.GroundComponentId))
                {
                    return;
                }
            }

            Output($"Spawn item at our stationary target: <{string.Join(",", e.Position)}>");

            SpawnItemAt(e.Position, SpawnOffset, ItemClousterResourceId);
            LastSpawn = DateTime.Now;
            PreviousPosition = new System.Numerics.Vector3(e.Position[0], e.Position[1], e.Position[2]);
        }

        private void AnimationComponentMessages_OnCharacterTransform(SanProtocol.AnimationComponent.CharacterTransform e)
        {
            if (CurrentRunMode != RunMode.Shitlisted)
            {
                var persona = TargetPersonas
                    .Where(n => n.AgentComponentId == e.ComponentId)
                    .FirstOrDefault();
                if (persona == null)
                {
                    return;
                }

                if (OurSpawnedComponentIds.Contains(e.GroundComponentId))
                {
                    return;
                }
            }

            System.Numerics.Vector3 newPosition = new(e.Position[0], e.Position[1], e.Position[2]);

            if ((DateTime.Now - LastSpawn).TotalMilliseconds > MaxSpawnRateMs)
            {
                var xyDistance = (float)Math.Sqrt(Math.Pow(2, newPosition.X - PreviousPosition.X) + Math.Pow(2, newPosition.Y - PreviousPosition.Y));
                var distance = (newPosition - PreviousPosition).Length();
                DistanceSinceLastSpawn += distance;

                if (DistanceSinceLastSpawn >= DistancedRequiredBeforeSpawningMore)
                {
                    Output($"Spawn item at our moving target: [{e.GroundComponentId}] Dist={xyDistance} | <{string.Join(",", e.Position)}>");

                    SpawnItemAt(e.Position, SpawnOffset, ItemClousterResourceIdBig);
                    LastSpawn = DateTime.Now;
                }
            }

            PreviousPosition = newPosition;
        }

        private void WorldStateMessages_OnCreateClusterViaDefinition(SanProtocol.WorldState.CreateClusterViaDefinition e)
        {
            if (e.ResourceId == ItemClousterResourceId || e.ResourceId == ItemClousterResourceIdBig)
            {
                _ = OurSpawnedComponentIds.Add((ulong)e.StartingObjectId * 0x100000000);
            }
        }

        private void Driver_OnOutput(object? sender, string message)
        {
            Output(message, sender?.GetType().Name ?? "Bot");
        }

        private void Output(string str, string sender = nameof(Bot))
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

            if (TargetHandles.Count == 0 || TargetHandles.Contains(e.Handle.ToLower()))
            {
                Output($"Target found. SessionID = {e.SessionId}");
                TargetPersonas.Add(persona);
            }
        }

        public override void OnRegionLoginSuccess(UserLoginReply e)
        {
            Output("Logged into region: " + e.ToString());
        }

        public override void OnKafkaLoginSuccess(LoginReply e)
        {
            Console.WriteLine("Bot::OnKafkaLoginSuccess");

            //Driver.JoinRegion("sansar-studios", "sansar-park").Wait();
            Driver.JoinRegion("nop", "flat2").Wait();
        }
    }
}
