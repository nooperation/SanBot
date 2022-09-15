using SanBot.Core;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CrowdBot
{

    public class CrowdBotConfig
    {
        public List<BotConfig> bots { get; set; }

        public class BotConfig
        {
            public string Id { get; set; }
            public ConfigFile Credentials { get; set; }
        }
    }


    public class Entrypoint
    {
        public CrowdBotConfig BotConfigs { get; set; }
        public int CurrentBotIndex { get; set; } = 0;


        public ConcurrentDictionary<string, CrowdBot> Bots { get; set; } = new ConcurrentDictionary<string, CrowdBot>();
        public ConcurrentStack<CrowdBotConfig.BotConfig?> BotsToAdd { get; set; } = new ConcurrentStack<CrowdBotConfig.BotConfig?>();

        public Entrypoint()
        {
            var sanbotPath = Path.Join(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SanBot"
            );


            var configPath = Path.Join(sanbotPath, "CrowdBots.json");

            try
            {
                var configFileContents = File.ReadAllText(configPath);
                BotConfigs = Newtonsoft.Json.JsonConvert.DeserializeObject<CrowdBotConfig>(configFileContents);
            }
            catch (Exception ex)
            {
                throw new Exception("Missing or invalid config.json", ex);
            }

            AddBot();

            List<CrowdBot> BotsToDestroy = new List<CrowdBot>();
            while (Bots.Count > 0)
            {
                foreach (var bot in Bots.Values)
                {
                    if (!bot.Poll())
                    {
                        BotsToDestroy.Add(bot);
                    }
                }

                if(BotsToDestroy.Count > 0)
                {
                    foreach (var bot in BotsToDestroy)
                    {
                        bot.Disconnect();
                        Bots.TryRemove(bot.Id, out var removedBot);
                    }

                    BotsToDestroy.Clear();
                }

                
                while(BotsToAdd.TryPop(out var config))
                {
                    if(config == null)
                    {
                        AddBot();
                    }
                    else
                    {
                        AddBot(config);
                    }
                }

                Thread.Sleep(1);
            }
        }
        

        public void AddBot()
        {
            var config = BotConfigs.bots[CurrentBotIndex];
            CurrentBotIndex = (CurrentBotIndex + 1) % BotConfigs.bots.Count;

            AddBot(config);
        }

        public void AddBot(CrowdBotConfig.BotConfig config)
        {
            var botId = config.Id;
            var bot = new CrowdBot(botId);

            int i = 1;
            while (!Bots.TryAdd(botId, bot))
            {
                botId = $"{config.Id}_{i}";
                i++;
            }
            bot.Id = botId;

            bot.OnRequestRestartBot += Bot_OnRequestRestartBot;
            bot.OnRequestAddBot += Bot_OnRequestAddBot;
            bot.Start(config.Credentials);
        }

        private void Bot_OnRequestAddBot(object? sender, EventArgs e)
        {
            BotsToAdd.Push(null);
        }

        static void Main(string[] args)
        {
            new Entrypoint();
        }

        public void Bot_OnRequestRestartBot(object? sender, EventArgs e)
        {
            var bot = sender as CrowdBot;
            if (bot == null)
            {
                Console.WriteLine("Bot_OnRequestRestartBot: Bad bot source");
                return;
            }

            if (!Bots.TryGetValue(bot.Id, out var foundBot))
            {
                Console.WriteLine("Bot_OnRequestRestartBot: Unknown bot source");
                return;
            }

            foundBot.Disconnect();

            var existingConfig = BotConfigs.bots.FirstOrDefault(n => n.Id == foundBot.Id);
            if(existingConfig == null)
            {
                Console.WriteLine("Failed to find existing config?");
                return;
            }

            BotsToAdd.Push(existingConfig);
        }
    }
}
