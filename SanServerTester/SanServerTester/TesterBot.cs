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
using System.Web;

namespace SanServerTester
{
    public class TesterBot
    {
        public Driver Driver { get; set; }
        public Dictionary<uint, SanProtocol.ClientRegion.AddUser> PersonaSessionMap { get; }
        private string testMessage = Guid.NewGuid().ToString();

        public TesterBot()
        {
            ConfigFile config;
            this.PersonaSessionMap = new Dictionary<uint, SanProtocol.ClientRegion.AddUser>();
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

            Driver.KafkaClient.ClientKafkaMessages.OnPrivateChat += ClientKafkaMessages_OnPrivateChat;
            Driver.KafkaClient.ClientKafkaMessages.OnLoginReply += ClientKafkaMessages_OnLoginReply;
            Driver.KafkaClient.ClientKafkaMessages.OnRegionChat += ClientKafkaMessages_OnRegionChat;

            Driver.RegionClient.ClientRegionMessages.OnUserLoginReply += ClientRegionMessages_OnUserLoginReply;
            Driver.RegionClient.ClientRegionMessages.OnAddUser += ClientRegionMessages_OnAddUser;
            Driver.RegionClient.ClientRegionMessages.OnRemoveUser += ClientRegionMessages_OnRemoveUser;

            Driver.StartAsync(config).Wait();

            while (true)
            {
                Driver.Poll();
            }
        }

        private void Driver_OnOutput(object? sender, string message)
        {
            Output(message, sender?.GetType().Name ?? "Bot");
        }

        public void Output(string str, string sender=nameof(TesterBot))
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

        public void OutputSuccess(string message)
        {
            Console.WriteLine($"[SUCCESS] {message}");
        }

        public void OutputFailure(string message)
        {
            Console.WriteLine($"[FAIL] {message}");
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

            if(e.PersonaId.Format() == Driver.MyPersonaDetails.Id)
            {
                Output("Sending test message...");
                Driver.SendChatMessage(testMessage);
            }
        }

        private void ClientKafkaMessages_OnRegionChat(object? sender, RegionChat e)
        {
            if(e.Message == "")
            {
                return;
            }

            Output($"{e.FromPersonaId}: {e.Message}");

            if (e.FromPersonaId.Format() == Driver.MyPersonaDetails.Id && e.Message == testMessage)
            {
                Output("Chat seems to work");
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
            
            Driver.RegionClient.SendPacket(new SanProtocol.ClientRegion.ClientDynamicReady(
                //new List<float>() { (float)(radius*Math.Sin(angleRads)), (float)(radius * Math.Cos(angleRads)), 5.0f },
                new List<float>() { 11.695435f, 32.8338f, 17.2235107f },
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

        private void ClientKafkaMessages_OnLoginReply(object? sender, LoginReply e)
        {
            if(!e.Success)
            {
                throw new Exception($"KafkaClient failed to login: {e.Message}");
            }


            Output("Getting account confugration...");
            var accountConfiguration = Driver.WebApi.GetAccountConfiguration(Driver.MyUserInfo!.AccountId).Result;
            Output("OK");

            Output("Getting subscriptions...");
            var subscriptions = Driver.WebApi.GetSubscriptions().Result;
            Output("OK");

            Output("Getting business rules...");
            var businessRules = Driver.WebApi.GetBusinessRules().Result;
            Output("OK");

            Output("Getting library...");
            var library = Driver.WebApi.GetLibrary().Result;
            Output($"  {library.Items.Count} items fetched");
            Output("OK");

            Output("Checking categories...");
            var marketplaceCategories = Driver.WebApi.GetMarketplaceCategoriesAsync().Result;
            Output($"Categories = " + marketplaceCategories);

            Output("Checking balance...");
            var balanceResponse = Driver.WebApi.GetBalanceAsync().Result;
            Output($"Balance = {balanceResponse.Data.Balance} {balanceResponse.Data.Currency} (Earned={balanceResponse.Data.Earned} General={balanceResponse.Data.General})");

            Output("Kafka client logged in successfully");
            Driver.JoinRegion("sansar-studios", "nexus").Wait();
        }
    }
}
