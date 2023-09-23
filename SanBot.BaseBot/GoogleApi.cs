using Google.Cloud.TextToSpeech.V1;
using NAudio.Wave;

namespace SanBot.BaseBot
{
    internal class GoogleApi
    {
        public class GoogleConfigPayload
        {
            public string Key { get; set; } = default!;
        }

        private readonly Action<byte[]> _speakFunction;

        private GoogleConfigPayload? GoogleConfig { get; set; }

        public HashSet<string> PreviousMessages { get; set; } = new HashSet<string>();
        public string TextToSpeechVoice { get; set; } = $"<speak xmlns='http://www.w3.org/2001/10/synthesis' xmlns:mstts='http://www.w3.org/2001/mstts' xmlns:emo='http://www.w3.org/2009/10/emotionml' version='1.0' xml:lang='en-US'><voice name=\"en-US-JennyNeural\"><prosody volume='40'  rate=\'20%\' pitch=\'0%\'>#MESSAGE#</prosody></voice></speak>";
        public string GoogleTTSName { get; set; } = "";
        public float GoogleTTSRate { get; set; } = 1.0f;
        public float GoogleTTSPitch { get; set; } = 0;

        public GoogleApi(string configPath, Action<byte[]> speakFunction)
        {
            _speakFunction = speakFunction;

            try
            {
                var configFileContents = File.ReadAllText(configPath);
                var result = System.Text.Json.JsonSerializer.Deserialize<GoogleConfigPayload>(configFileContents, new System.Text.Json.JsonSerializerOptions()
                {
                    PropertyNameCaseInsensitive = true
                });

                if (result == null || string.IsNullOrWhiteSpace(result.Key))
                {
                    throw new Exception("Invalid google config");
                }

                GoogleConfig = result;
            }
            catch (Exception ex)
            {
                throw new Exception("Missing or invalid google config", ex);
            }
        }

        public void Speak(string message, bool allowRepeating = false)
        {
            if (GoogleConfig == null || string.IsNullOrWhiteSpace(GoogleConfig.Key))
            {
                Console.WriteLine("ERROR: Invalid or missing google config");
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


            var client = TextToSpeechClient.Create();

            var input = new SynthesisInput
            {
                Text = message,

            };
            var voiceSelection = new VoiceSelectionParams
            {
                LanguageCode = "en-US",
                Name = GoogleTTSName,
                SsmlGender = SsmlVoiceGender.Neutral
            };
            var audioConfig = new AudioConfig
            {
                AudioEncoding = AudioEncoding.Mp3,
                SampleRateHertz = 48000,
                SpeakingRate = GoogleTTSRate,
                Pitch = GoogleTTSPitch
            };
            var response = client.SynthesizeSpeech(input, voiceSelection, audioConfig);

            using (var mp3Stream = new MemoryStream())
            {
                response.AudioContent.WriteTo(mp3Stream);
                mp3Stream.Position = 0;

                var pcm = WaveFormatConversionStream.CreatePcmStream(new Mp3FileReader(mp3Stream));
                var bytes = new byte[pcm.Length];
                pcm.Position = 0;
                pcm.Read(bytes, 0, (int)pcm.Length);

                _speakFunction(bytes);
            }
        }
    }
}
