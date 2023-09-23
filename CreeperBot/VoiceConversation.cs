using System.Collections.Concurrent;
using Concentus.Structs;
using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using NAudio.Wave;
using SanProtocol.ClientVoice;
using static CreeperBot.CreeperBot;
using static SanBot.Core.Driver;

namespace CreeperBot
{
    public class VoiceConversation
    {
        public class VoicePersona
        {
            public uint? AgentControllerId { get; set; }
        }

        public event EventHandler<SpeechToTextItem>? OnSpeechToText;

        public VoicePersona Persona { get; set; }
        public CreeperBot Bot { get; set; }

        public int Id { get; set; }
        public DateTime? TimeWeStartedListeningToTarget { get; set; } = null;
        public DateTime? LastTimeWeListened { get; set; } = null;
        public int LoudSamplesInBuffer { get; set; } = 0;

        public List<byte[]> VoiceBuffer { get; set; } = new List<byte[]>();
        public ConcurrentQueue<VoiceBufferQueueItem> SharedVoiceBufferQueue = new();

        public VoiceConversation(VoicePersona persona, CreeperBot bot)
        {
            Bot = bot;
            Persona = persona;
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
            public SpeechToTextItem(VoicePersona persona, string text)
            {
                Persona = persona;
                Text = text;
                Date = DateTime.Now;
            }

            public VoicePersona Persona { get; set; }
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
        }
        public SpeechApi SpeechToTextApi { get; set; } = SpeechApi.LocalWhisper;


        public static void PlayOpusSound(List<byte[]> opusSamplesIn, int sampleRateOut = 16000, int bitsOut = 16, int channelsOut = 1)
        {
            const int kFrameSize = 960;
            const int kFrequency = 48000;

            var outputDirectory = $"Out/{DateTime.Now.Year}-{DateTime.Now.Month}-{DateTime.Now.Day}";

            if (!Directory.Exists(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
            }

            using (var ms = new MemoryStream())
            {
                var decoder = OpusDecoder.Create(kFrequency, 1);
                var decompressedBuffer = new short[kFrameSize * 2];

                foreach (var item in opusSamplesIn)
                {
                    var numSamples = OpusPacketInfo.GetNumSamples(decoder, item, 0, item.Length);
                    var result = decoder.Decode(item, 0, item.Length, decompressedBuffer, 0, numSamples);

                    /*
                    var min = Math.Abs((int)decompressedBuffer.Min());
                    var max = Math.Abs((int)decompressedBuffer.Max());

                    var peak = Math.Max(1, Math.Max(min, max));
                    var maxMultiplier = short.MaxValue / peak;
                    var volume = Math.Min(maxMultiplier, 8);
                     */
                    for (var i = 0; i < decompressedBuffer.Length; i++)
                    {
                        decompressedBuffer[i] *= 2;
                    }


                    var decompressedBufferBytes = new byte[result * 2];
                    Buffer.BlockCopy(decompressedBuffer, 0, decompressedBufferBytes, 0, result * 2);

                    ms.Write(decompressedBufferBytes);
                }

                ms.Seek(0, SeekOrigin.Begin);

                using (var waveOut = new WaveOutEvent())
                {
                    using (WaveStream waveStream = new RawSourceWaveStream(ms, new WaveFormat(kFrequency, 16, 1)))
                    {
                        var volumeWaveProvider = new VolumeWaveProvider16(waveStream)
                        {
                            Volume = 4.0f
                        };

                        waveOut.Init(volumeWaveProvider);

                        Console.WriteLine("Playing decoded audio...");
                        waveOut.Play();

                        while (waveOut.PlaybackState == PlaybackState.Playing)
                        {
                            Thread.Sleep(10);
                        }

                        using (var writer = new WaveFileWriter(outputDirectory + "/" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + ".mp3", waveStream.WaveFormat))
                        {
                            ms.Seek(0, SeekOrigin.Begin);
                            var bytes = ms.ToArray();
                            writer.Write(bytes, 0, bytes.Length);
                        }
                    }
                }
            }
        }

        public void PlayOpusSoundB(List<byte[]> opusSamplesIn, int sampleRateOut = 16000, int bitsOut = 16, int channelsOut = 1)
        {
            const int kFrameSize = 960;
            const int kFrequency = 48000;

            var outputDirectory = $"Out/{DateTime.Now.Year}-{DateTime.Now.Month}-{DateTime.Now.Day}";

            if (!Directory.Exists(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
            }

            var ms = new MemoryStream();

            var decoder = OpusDecoder.Create(kFrequency, 1);
            var decompressedBuffer = new short[kFrameSize * 2];

            foreach (var item in opusSamplesIn)
            {
                var numSamples = OpusPacketInfo.GetNumSamples(decoder, item, 0, item.Length);
                var result = decoder.Decode(item, 0, item.Length, decompressedBuffer, 0, numSamples);

                for (var i = 0; i < decompressedBuffer.Length; i++)
                {
                    decompressedBuffer[i] *= 2;
                }

                var decompressedBufferBytes = new byte[result * 2];
                Buffer.BlockCopy(decompressedBuffer, 0, decompressedBufferBytes, 0, result * 2);

                ms.Write(decompressedBufferBytes);
            }

            ms.Seek(0, SeekOrigin.Begin);

            var waveOut = new WaveOutEvent();
            var waveStream = new RawSourceWaveStream(ms, new WaveFormat(kFrequency, 16, 1));
            var volumeWaveProvider = new VolumeWaveProvider16(waveStream)
            {
                Volume = 4.0f
            };

            waveOut.Init(volumeWaveProvider);

            events.Add(new SoundPlayerData()
            {
                WaveOut = waveOut,
                Stream = ms,
                WaveStream = waveStream
            });

            Console.WriteLine("Playing decoded audio...");
            waveOut.Play();

            using (var writer = new WaveFileWriter(outputDirectory + "/" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + ".mp3", waveStream.WaveFormat))
            {
                ms.Seek(0, SeekOrigin.Begin);
                var bytes = ms.ToArray();
                writer.Write(bytes, 0, bytes.Length);
            }
        }

        public class SoundPlayerData
        {
            public MemoryStream? Stream { get; set; }
            public WaveOutEvent? WaveOut { get; set; }
            public RawSourceWaveStream? WaveStream { get; set; }
        }

        private readonly List<SoundPlayerData> events = new();
        public void CheckSoundQueue()
        {
            var eventsToRemove = events.Where(n => n.WaveOut.PlaybackState == PlaybackState.Stopped).ToList();
            foreach (var item in eventsToRemove)
            {
                item.WaveOut.Stop();
                item.WaveOut.Dispose();
                item.WaveStream.Dispose();
                item.Stream.Dispose();

                events.Remove(item);
            }
        }

        public static byte[] OpusToRaw(List<byte[]> opusSamplesIn, int sampleRateOut = 16000, int bitsOut = 16, int channelsOut = 1)
        {
            const int kFrameSize = 960;
            const int kFrequency = 48000;

            using (var ms = new MemoryStream())
            {
                var decoder = OpusDecoder.Create(kFrequency, 1);
                var decompressedBuffer = new short[kFrameSize * 2];

                foreach (var item in opusSamplesIn)
                {
                    var numSamples = OpusPacketInfo.GetNumSamples(decoder, item, 0, item.Length);
                    var result = decoder.Decode(item, 0, item.Length, decompressedBuffer, 0, numSamples);

                    var decompressedBufferBytes = new byte[result * 2];
                    Buffer.BlockCopy(decompressedBuffer, 0, decompressedBufferBytes, 0, result * 2);

                    ms.Write(decompressedBufferBytes);
                }

                ms.Seek(0, SeekOrigin.Begin);

                using (var rs = new RawSourceWaveStream(ms, new WaveFormat(kFrequency, 16, 1)))
                {
                    using (var wavStream = new MemoryStream())
                    {
                        var outFormat = new WaveFormat(sampleRateOut, bitsOut, channelsOut);
                        using (var resampler = new MediaFoundationResampler(rs, outFormat))
                        {
                            WaveFileWriter.WriteWavFileToStream(wavStream, resampler);
                            return wavStream.ToArray();
                        }
                    }
                }
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

        public static string? SpeechToTextAzure(byte[] rawWavBytes)
        {
            var azureConfigPath = Path.Join(GetSanbotConfigPath(), "azure.json");
            var configFileContents = File.ReadAllText(azureConfigPath);
            var azureConfig = System.Text.Json.JsonSerializer.Deserialize<AzureConfigPayload>(configFileContents);
            if (azureConfig == null || azureConfig.key1.Length == 0 || azureConfig.region.Length == 0)
            {
                throw new Exception("Invalid azure config");
            }

            var pushStream = AudioInputStream.CreatePullStream(new VoiceAudioStream(rawWavBytes));
            var audioConfig = AudioConfig.FromStreamInput(pushStream);

            var speechConfig = SpeechConfig.FromSubscription(azureConfig.key1, azureConfig.region);
            speechConfig.SetProfanity(ProfanityOption.Raw);

            var recognizer = new SpeechRecognizer(speechConfig, audioConfig);
            var results = recognizer.RecognizeOnceAsync().Result;

            if (results.Reason == ResultReason.RecognizedSpeech)
            {
                return results.Text;
            }

            return null;
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
                if (jsonResult?.Success == true && jsonResult.Segments.Length > 0)
                {
                    if (jsonResult.Segments[0].no_speech_prob >= 0.35)
                    {
                        Console.WriteLine($"Ignored text, no speech prob = {jsonResult.Segments[0].no_speech_prob}: {jsonResult.Text}");
                        return null;
                    }
                    return jsonResult.Text.Trim();
                }

                return null;
            }
        }

        private bool SpeechToText(byte[] rawWavBytes)
        {
            // SECONDARY THREAD

            string? outputText = null;

            if (SpeechToTextApi == SpeechApi.Azure)
            {
                outputText = SpeechToTextAzure(rawWavBytes);
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

            CheckSoundQueue();

            while (SharedVoiceBufferQueue.TryDequeue(out var voiceBuffer))
            {
                if (voiceBuffer.LoudSamplesInBuffer < 15)
                {
                    return true;
                }

                //  PlayOpusSound(voiceBuffer.VoiceBuffer, 16000, 16, 1);
                PlayOpusSoundB(voiceBuffer.VoiceBuffer, 16000, 16, 1);

                // var wavBytes = OpusToRaw(voiceBuffer.VoiceBuffer, 16000, 16, 1);
                // SpeechToText(wavBytes);
            }

            return true;
        }
    }
}
