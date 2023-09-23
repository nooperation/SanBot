using System.Security;
using SanBot.Core;
using SanBot.Database;
using SanProtocol;
using SanProtocol.ClientKafka;

namespace SanBot.BaseBot
{
    public class SimpleBot
    {
        private PersonaDatabase Database { get; }
        public Driver Driver { get; set; }

        public SimpleBot()
        {
            Driver = new Driver();
            Driver.OnOutput += Driver_OnOutput;
            Driver.OnPacket = OnPacket;

            Database = new PersonaDatabase();
        }

        public virtual void OnPacket(IPacket packet)
        {
            switch (packet.MessageId)
            {
                case SanProtocol.Messages.ClientKafkaMessages.LoginReply:
                    ClientKafkaMessages_OnLoginReply((SanProtocol.ClientKafka.LoginReply)packet);
                    break;
                case SanProtocol.Messages.ClientRegionMessages.UserLoginReply:
                    ClientRegionMessages_OnUserLoginReply((SanProtocol.ClientRegion.UserLoginReply)packet);
                    break;
            }
        }

        public virtual async Task Start()
        {
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

            await Start(config.Username, config.Password);
        }

        public virtual async Task Start(SecureString username, SecureString password)
        {
            await Driver.StartAsync(username, password);

            while (true)
            {
                Driver.Poll();
            }
        }

        public void SpawnItemAt(List<float> position, List<float> offset, SanUUID itemClusterResourceId)
        {
            if (Driver.MyPersonaData == null || Driver.MyPersonaData.AgentControllerId == null)
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


        private void Driver_OnOutput(object? sender, string message)
        {
            Output(message, sender?.GetType().Name ?? "Bot");
        }

        private void Output(string str, string sender = nameof(SimpleBot))
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

        private void ClientRegionMessages_OnUserLoginReply(SanProtocol.ClientRegion.UserLoginReply e)
        {
            if (!e.Success)
            {
                throw new Exception("Failed to enter region");
            }

            Output("Logged into region: " + e.ToString());

            OnRegionLoginSuccess(e);
        }

        public virtual void OnRegionLoginSuccess(SanProtocol.ClientRegion.UserLoginReply e)
        {

        }

        private void ClientKafkaMessages_OnLoginReply(LoginReply e)
        {
            if (!e.Success)
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

            OnKafkaLoginSuccess(e);
        }

        public virtual void OnKafkaLoginSuccess(LoginReply e)
        {
            Console.WriteLine("SimpleBot::OnKafkaLoginSuccess");
        }
    }
}
