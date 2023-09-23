using System.Collections.Concurrent;
using System.Net;
using SanBot.Core;
using static SanBot.Core.ConfigFile;

namespace CrowdBot
{
    public class CrowdBotConfig
    {
        public List<BotConfig>? bots { get; set; }

        public class BotConfig
        {
            public string? Id { get; set; }
            public ConfigFileInsecure Credentials { get; set; } = default!;

            public SanProtocol.AnimationComponent.CharacterTransformPersistent? SavedTransform { get; set; }
            public SanProtocol.AgentController.AgentPlayAnimation? SavedAnimation { get; set; }
            public SanProtocol.AgentController.CharacterControllerInputReliable? SavedControllerInput { get; set; }
        }
    }

    public class Entrypoint
    {
        public CrowdBotConfig BotConfigs { get; set; }
        public int CurrentBotIndex { get; set; } = 0;

        private readonly List<string> Voices = new()
        {
            "<speak xmlns='http://www.w3.org/2001/10/synthesis' xmlns:mstts='http://www.w3.org/2001/mstts' xmlns:emo='http://www.w3.org/2009/10/emotionml' version='1.0' xml:lang='en-US'><voice name='en-US-DavisNeural'><mstts:express-as style='excited' ><prosody rate='24%' pitch='0%'>#MESSAGE#</prosody></mstts:express-as></voice></speak>",
            "<speak xmlns='http://www.w3.org/2001/10/synthesis' xmlns:mstts='http://www.w3.org/2001/mstts' xmlns:emo='http://www.w3.org/2009/10/emotionml' version='1.0' xml:lang='en-US'><voice name='en-US-AIGenerate1Neural'><prosody rate='0%' pitch='0%'>#MESSAGE#</prosody></voice></speak>",
            "<speak xmlns='http://www.w3.org/2001/10/synthesis' xmlns:mstts='http://www.w3.org/2001/mstts' xmlns:emo='http://www.w3.org/2009/10/emotionml' version='1.0' xml:lang='en-US'><voice name='en-US-SteffanNeural'><prosody rate='0%' pitch='0%'>#MESSAGE#</prosody></voice></speak>",
            "<speak xmlns='http://www.w3.org/2001/10/synthesis' xmlns:mstts='http://www.w3.org/2001/mstts' xmlns:emo='http://www.w3.org/2009/10/emotionml' version='1.0' xml:lang='en-US'><voice name='en-US-RogerNeural'><prosody rate='24%' pitch='0%'>#MESSAGE#</prosody></voice></speak>",
            "<speak xmlns='http://www.w3.org/2001/10/synthesis' xmlns:mstts='http://www.w3.org/2001/mstts' xmlns:emo='http://www.w3.org/2009/10/emotionml' version='1.0' xml:lang='en-US'><voice name='en-US-JaneNeural'><prosody rate='24%' pitch='0%'>#MESSAGE#</prosody></voice></speak>",
            "<speak xmlns='http://www.w3.org/2001/10/synthesis' xmlns:mstts='http://www.w3.org/2001/mstts' xmlns:emo='http://www.w3.org/2009/10/emotionml' version='1.0' xml:lang='en-US'><voice name='en-US-MonicaNeural'><prosody rate='9%' pitch='-3%'>#MESSAGE#</prosody></voice></speak>",
        };

        public struct GoogleTTSVoice
        {
            public GoogleTTSVoice(string name, float rate, float pitch)
            {
                Name = name;
                Rate = rate;
                Pitch = pitch;
            }

            public string Name { get; set; }
            public float Rate { get; set; }
            public float Pitch { get; set; }
        }
        public List<GoogleTTSVoice> GoogleVoices { get; set; } = new List<GoogleTTSVoice>()
        {
            new GoogleTTSVoice("en-US-Neural2-A", 1.25f, -4.0f),
            new GoogleTTSVoice("en-US-Neural2-F", 1.25f, -4.0f),
            new GoogleTTSVoice("en-US-Wavenet-E", 1.25f, 0),
            new GoogleTTSVoice("en-US-Wavenet-G", 1.25f, 0),
            new GoogleTTSVoice("en-US-Wavenet-H", 1.05f, 0),
            new GoogleTTSVoice("en-US-Wavenet-I", 1.25f, 0),
            new GoogleTTSVoice("en-US-Wavenet-C", 1.25f, 1.60f),
            new GoogleTTSVoice("en-US-Wavenet-D", 1.25f, 0),
        };

        private readonly List<string> Catchphrases = new()
        {
            "Hey #NAME#! How's it going?",
            "#NAME#! Are you ready to rock!?",
            "unts.unts.unts.unts.",
            "I am having a good time. Really.",
            "I can taste the colors",
            "This is my catchprase. Also I am saying your name, #NAME#, to make it personal",
            "Hey #NAME#, looking for a good time?",
            "Catchphrase!",
            "Yo #NAME#",
        };

        public ConcurrentDictionary<string, CrowdBot> Bots { get; set; } = new ConcurrentDictionary<string, CrowdBot>();
        public ConcurrentStack<CrowdBotConfig.BotConfig?> BotsToAdd { get; set; } = new ConcurrentStack<CrowdBotConfig.BotConfig?>();

        public Entrypoint(string[] args)
        {
            var sanbotPath = Path.Join(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SanBot"
            );

            var configPath = Path.Join(sanbotPath, "CrowdBots.json");
            if (args.Length > 0)
            {
                configPath = Path.Join(sanbotPath, $"CrowdBots_{args[0]}.json");
            }

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

            var BotsToDestroy = new List<CrowdBot>();
            while (Bots.Count > 0)
            {
                foreach (var bot in Bots.Values)
                {
                    if (!bot.IsRunning)
                    {
                        BotsToDestroy.Add(bot);
                    }
                }

                if (BotsToDestroy.Count > 0)
                {
                    foreach (var bot in BotsToDestroy)
                    {
                        bot.Disconnect();
                        Bots.TryRemove(bot.Id, out var removedBot);
                    }

                    BotsToDestroy.Clear();
                }


                while (BotsToAdd.TryPop(out var config))
                {
                    if (config == null)
                    {
                        AddBot();
                    }
                    else
                    {
                        AddBot(config);
                    }
                }

                Thread.Sleep(10);
            }
        }


        public void AddBot()
        {
            var config = BotConfigs.bots[CurrentBotIndex];
            config.SavedAnimation = null;
            config.SavedTransform = null;
            config.SavedControllerInput = null;

            CurrentBotIndex = (CurrentBotIndex + 1) % BotConfigs.bots.Count;

            AddBot(config);
        }

        public void AddBot(CrowdBotConfig.BotConfig config)
        {
            var botId = config.Id;
            var bot = new CrowdBot(botId, config.SavedTransform, config.SavedControllerInput, config.SavedAnimation)
            {
                Voice = Voices[Bots.Count % Voices.Count],
                GoogleTTSVoice = GoogleVoices[Bots.Count % GoogleVoices.Count],
                Catchphrases = Catchphrases
            };

            var i = 1;
            while (!Bots.TryAdd(botId, bot))
            {
                botId = $"{config.Id}_{i}";
                i++;
            }
            bot.Id = botId;

            bot.OnRequestRestartBot += Bot_OnRequestRestartBot;
            bot.OnRequestAddBot += Bot_OnRequestAddBot;

            var credentials = new ConfigFile
            {
                Username = new NetworkCredential("", config.Credentials.Username).SecurePassword,
                Password = new NetworkCredential("", config.Credentials.Password).SecurePassword
            };

            bot.Start(credentials);
        }

        private void Bot_OnRequestAddBot(object? sender, EventArgs e)
        {
            BotsToAdd.Push(null);
        }

        private static void Main(string[] args)
        {
            new Entrypoint(args);
        }

        public void Bot_OnRequestRestartBot(object? sender, EventArgs e)
        {
            if (sender is not CrowdBot bot)
            {
                Console.WriteLine("Bot_OnRequestRestartBot: Bad bot source");
                return;
            }

            if (!Bots.TryGetValue(bot.Id, out var foundBot))
            {
                Console.WriteLine("Bot_OnRequestRestartBot: Unknown bot source");
                bot.Disconnect();
                return;
            }


            var existingConfig = BotConfigs.bots.FirstOrDefault(n => n.Id == foundBot.Id);
            if (existingConfig == null)
            {
                Console.WriteLine("Failed to find existing config?");
                foundBot.Disconnect();
                return;
            }

            existingConfig.SavedAnimation = foundBot.SavedAnimation;
            existingConfig.SavedTransform = foundBot.SavedTransform;
            existingConfig.SavedControllerInput = foundBot.SavedControllerInput;

            foundBot.Disconnect();
            BotsToAdd.Push(existingConfig);
        }
    }
}
