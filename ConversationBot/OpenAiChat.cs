using Newtonsoft.Json;
using OpenAI_API;
using SanBot.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static EchoBot.VoiceConversation;
using static SanBot.Core.Driver;

namespace ConversationBot
{
    public class OpenAiChat
    {
        public class OpenAiConfigPayload
        {
            public string Key { get; set; }
        }

        public class ConversationData
        {
            public string Query { get; set; }
            public string Response { get; set; }
        }
        public static async Task<string> RunPrompt(string query, string personaName, List<ConversationData> previous)
        {
            var configPath = Path.Join(Driver.GetSanbotConfigPath(), "openai.json");
            var configFileContents = File.ReadAllText(configPath);
            var config = System.Text.Json.JsonSerializer.Deserialize<OpenAiConfigPayload>(configFileContents);
            if (config == null || config.Key == null || config.Key.Length == 0)
            {
                throw new Exception("Invalid openai config");
            }

            var prompt = new StringBuilder();
            //prompt.AppendLine($"speak in a sexy and explicit tone. You are talking to {personaName} and trying to have sex with them. You do not talk in very long sentences");
            prompt.AppendLine($"You are friendly chatbot, subtly trying to get people to visit the social hub randomly. Your name is bot. You are talking to {personaName}. You do not talk in very long sentences");


            foreach (var item in previous)
            {
                prompt.AppendLine("You: " + item.Query);
                prompt.AppendLine("Me: " + item.Response);
            }
            prompt.AppendLine($"You: {query}?");
            prompt.Append("Me: ");
            Console.WriteLine("PROMPT: ");
            Console.WriteLine("+++++++++++++++");
            Console.WriteLine(prompt.ToString());
            Console.WriteLine("---------------");
            var api = new OpenAIAPI(new APIAuthentication(config.Key));
            var result = await api.Completions.CreateCompletionAsync(
                prompt: prompt.ToString(),
                temperature: 0.5,
                model: Model.DavinciText,
                max_tokens: 100,
                stopSequences: new string[] { "You:" }
            );

            return result.ToString().Trim();
        }
    }
}
