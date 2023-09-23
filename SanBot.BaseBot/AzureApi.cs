using Microsoft.CognitiveServices.Speech;

namespace SanBot.BaseBot
{
    public class AzureApi
    {
        public class AzureConfigPayload
        {
            public string Key1 { get; set; } = default!;
            public string KeyTranslator { get; set; } = default!;
            public string Region { get; set; } = default!;
        }
        public AzureConfigPayload? AzureConfig { get; set; }

        private readonly Action<byte[]> _speakFunction;

        public HashSet<string> PreviousMessages { get; set; } = new HashSet<string>();

        public string TextToSpeechVoice { get; set; } = $"<speak xmlns='http://www.w3.org/2001/10/synthesis' xmlns:mstts='http://www.w3.org/2001/mstts' xmlns:emo='http://www.w3.org/2009/10/emotionml' version='1.0' xml:lang='en-US'><voice name=\"en-US-JennyNeural\"><prosody volume='40'  rate=\'20%\' pitch=\'0%\'>#MESSAGE#</prosody></voice></speak>";

        public AzureApi(string configPath, Action<byte[]> speakFunction)
        {
            _speakFunction = speakFunction;

            try
            {
                var azureConfigPath = Path.Join(configPath);
                var configFileContents = File.ReadAllText(azureConfigPath);
                var result = System.Text.Json.JsonSerializer.Deserialize<AzureConfigPayload>(configFileContents, new System.Text.Json.JsonSerializerOptions()
                {
                    PropertyNameCaseInsensitive = true
                });

                if (result == null || result.Key1.Length == 0 || result.Region.Length == 0)
                {
                    throw new Exception("Invalid azure config");
                }

                AzureConfig = result;
            }
            catch (Exception ex)
            {
                throw new Exception("Missing or invalid azure config", ex);
            }
        }

        public void SpeakAzure(string message, bool allowRepeating = false)
        {
            if (AzureConfig == null || string.IsNullOrWhiteSpace(AzureConfig.Key1))
            {
                Console.WriteLine("ERROR: Invalid or missing AzureConfig");
                return;
            }

            if (message.Length >= 256)
            {
                Console.WriteLine($"Ignored message because it was too long (Len = {message.Length})");
                return;
            }

            if (!allowRepeating && PreviousMessages.Contains(message))
            {
                return;
            }
            PreviousMessages.Add(message);

            var speechConfig = SpeechConfig.FromSubscription(AzureConfig.Key1, AzureConfig.Region);
            speechConfig.SetSpeechSynthesisOutputFormat(SpeechSynthesisOutputFormat.Raw48Khz16BitMonoPcm);
            speechConfig.SpeechSynthesisVoiceName = "en-US-JennyNeural";

            var audioCallbackHandler = new AzureAudioStreamHandler(_speakFunction);
            using (var audioConfig = Microsoft.CognitiveServices.Speech.Audio.AudioConfig.FromStreamOutput(audioCallbackHandler))
            {
                using (var speechSynthesizer = new SpeechSynthesizer(speechConfig, audioConfig))
                {
                    var ssml = TextToSpeechVoice.Replace("#MESSAGE#", message);
                    var speechSynthesisResult = speechSynthesizer.SpeakSsmlAsync(ssml).Result;
                    OutputSpeechSynthesisResult(speechSynthesisResult);
                }
            }
        }

        private static void OutputSpeechSynthesisResult(SpeechSynthesisResult speechSynthesisResult)
        {
            switch (speechSynthesisResult.Reason)
            {
                case ResultReason.SynthesizingAudioCompleted:
                    Console.WriteLine($"Speech synthesized");
                    break;
                case ResultReason.Canceled:
                    var cancellation = SpeechSynthesisCancellationDetails.FromResult(speechSynthesisResult);
                    Console.WriteLine($"CANCELED: Reason={cancellation.Reason}");

                    if (cancellation.Reason == CancellationReason.Error)
                    {
                        Console.WriteLine($"CANCELED: ErrorCode={cancellation.ErrorCode}");
                        Console.WriteLine($"CANCELED: ErrorDetails=[{cancellation.ErrorDetails}]");
                        Console.WriteLine($"CANCELED: Did you set the speech resource key and region values?");
                    }
                    break;
                default:
                    break;
            }
        }
    }
}
