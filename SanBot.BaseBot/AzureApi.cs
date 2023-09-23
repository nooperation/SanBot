using System.Text;
using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;

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



        private static readonly string endpoint = "https://api.cognitive.microsofttranslator.com";

        public class TranslationResult
        {
            public Detectedlanguage? detectedLanguage { get; set; }
            public Translation[]? translations { get; set; }
        }

        public class Detectedlanguage
        {
            public string? language { get; set; }
            public float score { get; set; }
        }

        public class Translation
        {
            public string? text { get; set; }
            public string? to { get; set; }
        }

        public record RealTranslationResult(string sourceLanguage, string translatedText);
        public RealTranslationResult? ToEnglishAzure(string textToTranslate)
        {
            if (AzureConfig == null || string.IsNullOrWhiteSpace(AzureConfig.Key1))
            {
                Console.WriteLine("ERROR: Invalid or missing AzureConfig");
                return new RealTranslationResult("", "");
            }

            // Input and output languages are defined as parameters.
            var route = "/translate?api-version=3.0&to=en";
            var body = new object[] { new { Text = textToTranslate } };
            var requestBody = System.Text.Json.JsonSerializer.Serialize(body);

            using (var client = new HttpClient())
            using (var request = new HttpRequestMessage())
            {
                // Build the request.
                request.Method = HttpMethod.Post;
                request.RequestUri = new Uri(endpoint + route);
                request.Content = new StringContent(requestBody, Encoding.UTF8, "application/json");
                request.Headers.Add("Ocp-Apim-Subscription-Key", AzureConfig.KeyTranslator);
                // location required if you're using a multi-service or regional (not global) resource.
                request.Headers.Add("Ocp-Apim-Subscription-Region", AzureConfig.Region);

                // Send the request and get response.
                var response = client.SendAsync(request).Result;
                // Read response as a string.
                var result = response.Content.ReadAsStringAsync().Result;

                var resulResult = System.Text.Json.JsonSerializer.Deserialize<TranslationResult[]>(result);
                if (resulResult == null)
                {
                    return null;
                }

                var x = resulResult.FirstOrDefault();
                if (x == null)
                {
                    return null;
                }

                var y = x.translations?.FirstOrDefault();
                if (y == null)
                {
                    return null;
                }

                return new RealTranslationResult(x.detectedLanguage?.language ?? "", y.text ?? "");
            }
        }

        public class VoiceAudioStream : PullAudioInputStreamCallback
        {
            private readonly MemoryStream ms;

            public VoiceAudioStream(byte[] data)
            {
                ms = new MemoryStream(data);
            }

            public override void Close()
            {
                ms.Close();
            }

            public override int Read(byte[] dataBuffer, uint size)
            {
                return ms.Read(dataBuffer, 0, (int)size);
            }
        }
        public string? SpeechToTextAzure(byte[] rawWavBytes)
        {
            if (AzureConfig == null || string.IsNullOrWhiteSpace(AzureConfig.Key1))
            {
                Console.WriteLine("ERROR: Invalid or missing AzureConfig");
                return null;
            }

            var pushStream = AudioInputStream.CreatePullStream(new VoiceAudioStream(rawWavBytes));
            var audioConfig = AudioConfig.FromStreamInput(pushStream);

            var speechConfig = SpeechConfig.FromSubscription(AzureConfig.Key1, AzureConfig.Region);
            speechConfig.SetProfanity(ProfanityOption.Raw);

            var recognizer = new SpeechRecognizer(speechConfig, audioConfig);
            var results = recognizer.RecognizeOnceAsync().Result;

            if (results.Reason == ResultReason.RecognizedSpeech)
            {
                return results.Text;
            }

            return null;
        }

    }
}
