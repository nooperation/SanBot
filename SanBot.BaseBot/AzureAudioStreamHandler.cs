using Microsoft.CognitiveServices.Speech.Audio;

namespace SanBot.BaseBot
{
    public class AzureAudioStreamHandler : PushAudioOutputStreamCallback
    {
        private readonly List<byte[]> _collectedBytes = new();

        private readonly Action<byte[]> _speakFunction;

        public AzureAudioStreamHandler(Action<byte[]> speakFunction)
        {
            _speakFunction = speakFunction;
        }

        public override uint Write(byte[] dataBuffer)
        {
            _collectedBytes.Add(dataBuffer);

            return (uint)dataBuffer.Length;
        }

        public override void Close()
        {
            long totalSize = 0;
            foreach (var item in _collectedBytes)
            {
                totalSize += item.Length;
            }

            var buffer = new byte[totalSize];
            var bufferOffset = 0;

            foreach (var item in _collectedBytes)
            {
                item.CopyTo(buffer, bufferOffset);
                bufferOffset += item.Length;
            }

            _speakFunction(buffer);
            base.Close();
        }
    }
}
