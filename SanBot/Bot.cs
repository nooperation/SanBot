using SanWebApi.Json;
using SanProtocol.ClientKafka;
using SanBot.Database;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using SanProtocol;
using SanBot.Core;
using static SanBot.Database.Services.PersonaService;
using Google.Cloud.Translate.V3;
using Google.Api.Gax.ResourceNames;
using System.Web;

namespace SanBot
{
    public class Bot
    {
        public enum RunMode
        {
            Hotfeet,
            Shitlisted,
        }

        private SanBot.Database.PersonaDatabase Database { get; }
        public Driver Driver { get; set; }
        public List<PersonaData> TargetPersonas { get; set; } = new List<PersonaData>();
        public DateTime LastSpawn { get; set; } = DateTime.Now;
        public System.Numerics.Vector3 PreviousPosition { get; set; }
        public float DistanceSinceLastSpawn { get; set; } = 0.0f;
        HashSet<ulong> OurSpawnedComponentIds = new HashSet<ulong>();

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
                ItemClousterResourceIdBig = Driver.Clusterbutt("c196ada06a5b6d85357e5d08d6b6a6df");
                MaxSpawnRateMs = 10;
                DistancedRequiredBeforeSpawningMore = 0.0f;
                SpawnOffset = new List<float> { 0.0f, 0.0f, 0.0f };
            }


            ConfigFile config;
            this.Database = new Database.PersonaDatabase();
            var sanbotPath = Path.Join(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SanBot"
            );
            var configPath = Path.Join(sanbotPath, ".config.json");

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

            Driver.KafkaClient.ClientKafkaMessages.OnLoginReply += ClientKafkaMessages_OnLoginReply;

            Driver.RegionClient.ClientRegionMessages.OnUserLoginReply += ClientRegionMessages_OnUserLoginReply;
            Driver.RegionClient.ClientRegionMessages.OnAddUser += ClientRegionMessages_OnAddUser;
            Driver.RegionClient.ClientRegionMessages.OnRemoveUser += ClientRegionMessages_OnRemoveUser;

            Driver.RegionClient.AnimationComponentMessages.OnCharacterTransform += AnimationComponentMessages_OnCharacterTransform;
            Driver.RegionClient.AnimationComponentMessages.OnCharacterTransformPersistent += AnimationComponentMessages_OnCharacterTransformPersistent;

            Driver.RegionClient.WorldStateMessages.OnCreateClusterViaDefinition += WorldStateMessages_OnCreateClusterViaDefinition;
            Driver.RegionClient.WorldStateMessages.OnDestroyCluster += WorldStateMessages_OnDestroyCluster;

            Driver.StartAsync(config).Wait();

            while (true)
            {
                Driver.Poll();
            }
        }

        private void WorldStateMessages_OnDestroyCluster(object? sender, SanProtocol.WorldState.DestroyCluster e)
        {
            OurSpawnedComponentIds.Remove((ulong)e.ClusterId * 0x100000000);
        }

        private void SpawnItemAt(List<float> position, List<float> offset, SanUUID itemClusterResourceId)
        {
            if(Driver.MyPersonaData == null || Driver.MyPersonaData.AgentControllerId == null)
            {
                return;
            }

            Driver.RequestSpawnItem(
                Driver.GetCurrentFrame(),
                itemClusterResourceId,
                new List<float>(){
                    position[0] + offset[0],
                    position[1] + offset[1],
                    position[2] + offset[2],
                },
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
                },
                Driver.MyPersonaData.AgentControllerId.Value
            );
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

            if (!OurSpawnedComponentIds.Contains(e.GroundComponentId))
            {
                return;
            }

            Output($"Spawn item at our stationary target: <{String.Join(",", e.Position)}>");

            SpawnItemAt(e.Position, SpawnOffset, ItemClousterResourceId);
            LastSpawn = DateTime.Now;
            PreviousPosition = new System.Numerics.Vector3(e.Position[0], e.Position[1], e.Position[2]);
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

            if (OurSpawnedComponentIds.Contains(e.GroundComponentId))
            {
                return;
            }

            var newPosition = new System.Numerics.Vector3(e.Position[0], e.Position[1], e.Position[2]);

            if ((DateTime.Now - LastSpawn).TotalMilliseconds > MaxSpawnRateMs)
            {
                var xyDistance = (float)Math.Sqrt(Math.Pow(2, newPosition.X - PreviousPosition.X) + Math.Pow(2, newPosition.Y - PreviousPosition.Y));
                var zDistance = (float)Math.Sqrt(Math.Pow(2, newPosition.Z - PreviousPosition.Z));
                var distance = (newPosition - PreviousPosition).Length();
                DistanceSinceLastSpawn += distance;

                if (DistanceSinceLastSpawn >= DistancedRequiredBeforeSpawningMore)
                {
                    Output($"Spawn item at our moving target: [{e.GroundComponentId}] Dist={xyDistance} | {zDistance} <{String.Join(",", e.Position)}>");

                    SpawnItemAt(e.Position, SpawnOffset, ItemClousterResourceIdBig);
                    LastSpawn = DateTime.Now;
                }
            }

            PreviousPosition = newPosition;
        }

        private void WorldStateMessages_OnCreateClusterViaDefinition(object? sender, SanProtocol.WorldState.CreateClusterViaDefinition e)
        {
            if(e.ResourceId == this.ItemClousterResourceId || e.ResourceId == this.ItemClousterResourceIdBig)
            {
                OurSpawnedComponentIds.Add((ulong)e.StartingObjectId * 0x100000000);
            }
        }

        private void Driver_OnOutput(object? sender, string message)
        {
            Output(message, sender?.GetType().Name ?? "Bot");
        }

        public void Output(string str, string sender = nameof(Bot))
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

            if(TargetHandles.Count == 0 || TargetHandles.Contains(e.Handle.ToLower()))
            {
                Output($"Target found. SessionID = {e.SessionId}");
                TargetPersonas.Add(persona);
            }
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

           // Driver.SendChatMessage("Beep boop");
            Driver.RegionClient.SendPacket(new SanProtocol.ClientRegion.ClientDynamicReady(
                //new List<float>() { (float)(radius*Math.Sin(angleRads)), (float)(radius * Math.Cos(angleRads)), 5.0f },
                new List<float>() { 11.695435f, 32.8338f, 17.2235107f },
                new List<float>() { 0, 0, 0, 0 }, // upside down spin ish
                new SanUUID(Driver.MyPersonaDetails!.Id),
                "",
                1,
                1
            ));
            
            Driver.RegionClient.SendPacket(new SanProtocol.ClientRegion.ClientStaticReady(
                1
            ));
        }

        private void ClientKafkaMessages_OnLoginReply(object? sender, LoginReply e)
        {
            if(!e.Success)
            {
                throw new Exception($"KafkaClient failed to login: {e.Message}");
            }


            Output("Checking categories...");
            var marketplaceCategories = Driver.WebApi.GetMarketplaceCategoriesAsync().Result;
            Output($"Categories = " + marketplaceCategories);

            Output("Checking balance...");
            var balanceResponse = Driver.WebApi.GetBalanceAsync().Result;
            Output($"Balance = {balanceResponse.Data.Balance} {balanceResponse.Data.Currency} (Earned={balanceResponse.Data.Earned} General={balanceResponse.Data.General})");

            Output("Kafka client logged in successfully");
            //Driver.JoinRegion("sansar-studios", "nexus").Wait();
            Driver.JoinRegion("nopnopnop", "owo").Wait();
        }
    }
}
