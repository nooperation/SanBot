using System.Collections.Concurrent;
using Concentus.Structs;
using NAudio.Wave;
using SanProtocol.ClientVoice;

namespace CreeperBot
{
    public class VoiceConversation
    {
        public class VoicePersona
        {
            public uint? AgentControllerId { get; set; }
        }

        public VoicePersona Persona { get; set; }
        public CreeperBot Bot { get; set; }

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

        public void Poll()
        {
            // MAIN THREAD
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

        public class VoiceBufferQueueItem
        {
            public List<byte[]> VoiceBuffer { get; set; } = default!;
            public int LoudSamplesInBuffer { get; set; }
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
            var eventsToRemove = events.Where(n => n.WaveOut != null && n.WaveOut.PlaybackState == PlaybackState.Stopped).ToList();
            foreach (var item in eventsToRemove)
            {
                item.WaveOut?.Stop();
                item.WaveOut?.Dispose();
                item.WaveStream?.Dispose();
                item.Stream?.Dispose();

                events.Remove(item);
            }
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
