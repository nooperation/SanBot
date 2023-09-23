using System.Collections.Concurrent;
using SanBot.BaseBot;
using SanBot.Core;
using SanProtocol.ClientVoice;

namespace ConversationBot
{
    public class VoiceConversation
    {
        public event EventHandler<SpeechToTextItem>? OnSpeechToText;

        public PersonaData Persona { get; set; }
        public ConversationBot Bot { get; set; }

        public int Id { get; set; }
        public DateTime? TimeWeStartedListeningToTarget { get; set; } = null;
        public DateTime? LastTimeWeListened { get; set; } = null;
        public int LoudSamplesInBuffer { get; set; } = 0;

        public List<byte[]> VoiceBuffer { get; set; } = new List<byte[]>();
        public ConcurrentQueue<VoiceBufferQueueItem> SharedVoiceBufferQueue = new();

        private readonly AzureApi? _azureApi = null;
        public VoiceConversation(PersonaData persona, ConversationBot bot, AzureApi? azureApi)
        {
            Persona = persona;
            Bot = bot;
            _azureApi = azureApi;
        }

        public void AddVoiceData(AudioData data)
        {
            // MAIN THREAD

            if (TimeWeStartedListeningToTarget == null)
            {
                //  Console.WriteLine($"Started buffering voice for {Persona.UserName} ({Persona.Handle})");
                TimeWeStartedListeningToTarget = DateTime.Now;
            }

            if (data.Volume > 300)
            {
                LoudSamplesInBuffer++;
                LastTimeWeListened = DateTime.Now;
            }

            if (data.Volume > 100)
            {
                LastTimeWeListened = DateTime.Now;
            }

            VoiceBuffer.Add(data.Data);
        }

        public class SpeechToTextItem
        {
            public SpeechToTextItem(PersonaData persona, string text)
            {
                Persona = persona;
                Text = text;
                Date = DateTime.Now;
            }

            public PersonaData Persona { get; set; }
            public string Text { get; set; }
            public DateTime Date { get; set; }
        }
        public ConcurrentQueue<SpeechToTextItem> SharedSpeechToTextQueue { get; set; } = new ConcurrentQueue<SpeechToTextItem>();

        public void Poll()
        {
            // MAIN THREAD
            while (SharedSpeechToTextQueue.TryDequeue(out var result))
            {
                OnSpeechToText?.Invoke(this, result);
            }

            if (VoiceBuffer.Count == 0)
            {
                return;
            }

            if (LastTimeWeListened != null)
            {
                if ((DateTime.Now - LastTimeWeListened.Value).TotalMilliseconds > 1000)
                {
                    EnqueueVoicebuffer();
                }
            }

            if (TimeWeStartedListeningToTarget != null)
            {
                if ((DateTime.Now - TimeWeStartedListeningToTarget.Value).TotalMilliseconds > 15000)
                {
                    EnqueueVoicebuffer();
                }
            }
        }

        private void EnqueueVoicebuffer()
        {
            // MAIN THREAD

            SharedVoiceBufferQueue.Enqueue(new VoiceBufferQueueItem()
            {
                VoiceBuffer = new List<byte[]>(VoiceBuffer.AsEnumerable()),
                LoudSamplesInBuffer = LoudSamplesInBuffer
            });

            LoudSamplesInBuffer = 0;
            VoiceBuffer.Clear();

            TimeWeStartedListeningToTarget = null;
            LastTimeWeListened = null;
        }

        public HashSet<string> Blacklist { get; set; } = new HashSet<string>()
        {
            "entity0x",
        };

        public class VoiceBufferQueueItem
        {
            public List<byte[]> VoiceBuffer { get; set; } = default!;
            public int LoudSamplesInBuffer { get; set; }
        }


        public enum SpeechApi
        {
            LocalWhisper = 1,
            Azure = 2,
            OpenApiWhisper = 3,
        }
        public class WhisperSpeechToTextResult
        {
            public class Segment
            {
                public int id { get; set; }
                public int seek { get; set; }
                public float start { get; set; }
                public float end { get; set; }
                public string? text { get; set; }
                public int[]? tokens { get; set; }
                public float temperature { get; set; }
                public float avg_logprob { get; set; }
                public float compression_ratio { get; set; }
                public float no_speech_prob { get; set; }
            }

            public bool Success { get; set; }
            public string? Text { get; set; }
            public Segment[]? Segments { get; set; }
            public string? Language { get; set; }
        }

        public static string? SpeechToTextWhisper(byte[] rawWavBytes)
        {
            using (var client = new HttpClient())
            {
                var result = client.PostAsync("http://127.0.0.1:5000/speech_to_text", new ByteArrayContent(rawWavBytes)).Result;
                var resultString = result.Content.ReadAsStringAsync().Result;

                var jsonResult = System.Text.Json.JsonSerializer.Deserialize<WhisperSpeechToTextResult>(resultString);
                if (jsonResult?.Success == true && jsonResult.Segments != null && jsonResult.Segments.Length > 0)
                {
                    if (jsonResult.Segments[0].no_speech_prob >= 0.35)
                    {
                        Console.WriteLine($"Ignored text, no speech prob = {jsonResult.Segments[0].no_speech_prob}: {jsonResult.Text}");
                        return null;
                    }
                    return jsonResult.Text?.Trim();
                }

                return null;
            }
        }

        public SpeechApi SpeechToTextApi { get; set; } = SpeechApi.LocalWhisper;
        private bool SpeechToText(byte[] rawWavBytes)
        {
            // SECONDARY THREAD

            string? outputText = null;

            if (SpeechToTextApi == SpeechApi.Azure && _azureApi != null)
            {
                outputText = _azureApi.SpeechToTextAzure(rawWavBytes);
            }
            else if (SpeechToTextApi == SpeechApi.LocalWhisper)
            {
                outputText = SpeechToTextWhisper(rawWavBytes);
            }
            else
            {
                throw new Exception("Invalid SpeechToTextApi or SpeechToTextApi not configured");
            }

            if (outputText != null && outputText.Length > 0)
            {
                SharedSpeechToTextQueue.Enqueue(new SpeechToTextItem(Persona, outputText));
                return true;
            }

            return false;
        }

        public bool ProcessVoiceBufferQueue()
        {
            // SECONDARY THREAD

            while (SharedVoiceBufferQueue.TryDequeue(out var voiceBuffer))
            {
                if (Blacklist.Contains(Persona.Handle.ToLower()))
                {
                    return true;
                }

                if (voiceBuffer.LoudSamplesInBuffer < 15)
                {
                    return true;
                }

                var wavBytes = Driver.OpusToRaw(voiceBuffer.VoiceBuffer, 16000, 16, 1);
                SpeechToText(wavBytes);
            }

            return true;
        }
    }
}
