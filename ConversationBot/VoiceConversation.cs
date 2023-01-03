using Concentus.Structs;
using Microsoft.CognitiveServices.Speech.Audio;
using Microsoft.CognitiveServices.Speech;
using NAudio.Wave;
using SanBot.Core;
using SanProtocol.ClientVoice;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static EchoBot.ConversationBot;
using static SanBot.Core.Driver;

namespace EchoBot
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
        public ConcurrentQueue<VoiceBufferQueueItem> SharedVoiceBufferQueue = new ConcurrentQueue<VoiceBufferQueueItem>();

        public VoiceConversation(PersonaData persona, ConversationBot bot)
        {
            this.Persona = persona;
            this.Bot = bot;
        }

        public void AddVoiceData(AudioData data)
        {
            // MAIN THREAD

            if (TimeWeStartedListeningToTarget == null)
            {
                //  Console.WriteLine($"Started buffering voice for {Persona.UserName} ({Persona.Handle})");
                TimeWeStartedListeningToTarget = DateTime.Now;
            }

            if (data.Volume > 500)
            {
                LoudSamplesInBuffer++;
                LastTimeWeListened = DateTime.Now;
            }

            if (data.Volume > 200)
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
            while (SharedSpeechToTextQueue.TryDequeue(out SpeechToTextItem? result))
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
            "MetaverseKing-3934",
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

        

        public static byte[] OpusToRaw(List<byte[]> opusSamplesIn, int sampleRateOut = 16000, int bitsOut = 16, int channelsOut = 1)
        {
            const int kFrameSize = 960;
            const int kFrequency = 48000;

            using (MemoryStream ms = new MemoryStream())
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


        public static string? SpeechToTextAzure(byte[] rawWavBytes)
        {
            var azureConfigPath = Path.Join(Driver.GetSanbotConfigPath(), "azure.json");
            var configFileContents = File.ReadAllText(azureConfigPath);
            var azureConfig = System.Text.Json.JsonSerializer.Deserialize<AzureConfigPayload>(configFileContents);
            if (azureConfig == null || azureConfig.key1.Length == 0 || azureConfig.region.Length == 0)
            {
                throw new Exception("Invalid azure config");
            }

            var pushStream = AudioInputStream.CreatePullStream(new VoiceAudioStream(rawWavBytes));
            var audioConfig = AudioConfig.FromStreamInput(pushStream);

            var speechConfig = SpeechConfig.FromSubscription(azureConfig.key1, azureConfig.region);
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
            public bool Success { get; set; }
            public string Text { get; set; }
        }
        public static string? SpeechToTextWhisper(byte[] rawWavBytes)
        {
            using (var client = new HttpClient())
            {
                var result = client.PostAsync("http://127.0.0.1:5000/speech_to_text", new ByteArrayContent(rawWavBytes)).Result;
                var resultString = result.Content.ReadAsStringAsync().Result;

                var jsonResult = System.Text.Json.JsonSerializer.Deserialize<WhisperSpeechToTextResult>(resultString);
                if (jsonResult?.Success == true)
                {
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

            while (SharedVoiceBufferQueue.TryDequeue(out VoiceBufferQueueItem voiceBuffer))
            {
                if (Blacklist.Contains(Persona.Handle.ToLower()))
                {
                    return true;
                }

                if (voiceBuffer.LoudSamplesInBuffer < 15)
                {
                    return true;
                }

                var wavBytes = OpusToRaw(voiceBuffer.VoiceBuffer, 16000, 16, 1);
                SpeechToText(wavBytes);
            }

            return true;
        }
    }
}
