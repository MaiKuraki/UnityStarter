using System;

namespace CycloneGames.Networking.Replication
{
    public struct NetworkSendBudget
    {
        public readonly int MaxBytes;
        public readonly int MaxMessages;

        public int RemainingBytes { get; private set; }
        public int RemainingMessages { get; private set; }

        public NetworkSendBudget(int maxBytes, int maxMessages)
        {
            if (maxBytes < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxBytes));
            }

            if (maxMessages < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxMessages));
            }

            MaxBytes = maxBytes;
            MaxMessages = maxMessages;
            RemainingBytes = maxBytes;
            RemainingMessages = maxMessages;
        }

        public bool CanConsume(int byteCount, int messageCount = 1)
        {
            if (byteCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(byteCount));
            }

            if (messageCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(messageCount));
            }

            return byteCount <= RemainingBytes && messageCount <= RemainingMessages;
        }

        public bool TryConsume(int byteCount, int messageCount = 1)
        {
            if (!CanConsume(byteCount, messageCount))
            {
                return false;
            }

            RemainingBytes -= byteCount;
            RemainingMessages -= messageCount;
            return true;
        }

        public void Reset()
        {
            RemainingBytes = MaxBytes;
            RemainingMessages = MaxMessages;
        }
    }
}
