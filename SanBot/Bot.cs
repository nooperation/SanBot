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

namespace SanBot
{
    public class Bot
    {
        private SanBot.Database.Database Database { get; }
        public Driver Driver { get; set; }
        public Dictionary<uint, SanProtocol.ClientRegion.AddUser> PersonaSessionMap { get; }

        internal async Task<string?> GetPersonaName(SanUUID personaId)
        {
            var persona = await ResolvePersonaId(personaId);
            if(persona != null)
            {
                return $"{persona.Name} ({persona.Handle})";
            }

            return personaId.Format();
        }

        internal async Task<PersonaDto?> ResolvePersonaId(SanUUID personaId)
        {
            var personaGuid = new Guid(personaId.Format());

            var persona = await Database.PersonaService.GetPersona(personaGuid);
            if(persona != null)
            {
                return persona;
            }

            var profiles = await Driver.WebApi.GetProfiles(new List<string>() {
                personaId.Format(),
            });

            PersonaDto? foundPersona = null;
            foreach (var item in profiles.Data)
            {
                if(new Guid(item.AvatarId) == personaGuid)
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

        public Bot()
        {
            ConfigFile config;
            this.PersonaSessionMap = new Dictionary<uint, SanProtocol.ClientRegion.AddUser>();
            this.Database = new Database.Database();
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

            Driver.KafkaClient.ClientKafkaMessages.OnPrivateChat += ClientKafkaMessages_OnPrivateChat;
            Driver.KafkaClient.ClientKafkaMessages.OnLoginReply += ClientKafkaMessages_OnLoginReply;
            Driver.KafkaClient.ClientKafkaMessages.OnRegionChat += ClientKafkaMessages_OnRegionChat;

            Driver.RegionClient.ClientRegionMessages.OnUserLoginReply += ClientRegionMessages_OnUserLoginReply;
            Driver.RegionClient.ClientRegionMessages.OnChatMessageToClient += ClientRegionMessages_OnChatMessageToClient;
            Driver.RegionClient.ClientRegionMessages.OnAddUser += ClientRegionMessages_OnAddUser;
            Driver.RegionClient.ClientRegionMessages.OnRemoveUser += ClientRegionMessages_OnRemoveUser;

            Driver.RegionClient.WorldStateMessages.OnCreateClusterViaDefinition += WorldStateMessages_OnCreateClusterViaDefinition;
            Driver.StartAsync(config).Wait();

            while (true)
            {
                Driver.Poll();
            }
        }

        private void WorldStateMessages_OnCreateClusterViaDefinition(object? sender, SanProtocol.WorldState.CreateClusterViaDefinition e)
        {
            //Output(e.ToString());
        }

        public void Output(string str)
        {
            var date = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            Console.WriteLine($"{date} [BOT] {str}");
        }

        private void ClientRegionMessages_OnRemoveUser(object? sender, SanProtocol.ClientRegion.RemoveUser e)
        {
            if(!PersonaSessionMap.ContainsKey(e.SessionId))
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

            ResolvePersonaId(e.PersonaId).Wait();
        }

        private void ClientRegionMessages_OnChatMessageToClient(object? sender, SanProtocol.ClientRegion.ChatMessageToClient e)
        {
            //Output($"OnChatMessageToClient: {e.Message}");
        }

        private void ClientKafkaMessages_OnRegionChat(object? sender, RegionChat e)
        {
            if(e.Message == "")
            {
                return;
            }

            var sourceName = GetPersonaName(e.FromPersonaId).Result;

            Output($"{sourceName}: {e.Message}");
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
                new List<float>() { 11.695435f, 32.8338f, 17.2235107f },
                new List<float>() { 1, 0, 0, 0 }, // upside down spin ish
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
            var sourceName = GetPersonaName(e.FromPersonaId).Result;
            Output($"(PRIVMSG) {sourceName}: {e.Message}");
        }

        private void ClientKafkaMessages_OnLoginReply(object? sender, LoginReply e)
        {
            if(!e.Success)
            {
                throw new Exception($"KafkaClient failed to login: {e.Message}");
            }

            Output("Kafka client logged in successfully");
            Driver.JoinRegion("sansar-studios", "nexus").Wait();
        }
    }
}
