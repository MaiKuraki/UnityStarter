namespace CycloneGames.Networking.Simulation
{
    public readonly struct NetworkActionResult
    {
        public readonly NetworkActionResultCode Code;
        public readonly NetworkActionPhase Phase;
        public readonly NetworkTickId AuthoritativeTick;
        public readonly ushort Sequence;
        public readonly int PredictionKey;
        public readonly NetworkActionCorrectionFlags CorrectionFlags;
        public readonly ulong AuthoritativeStateHash;
        public readonly ulong PayloadHash;

        public NetworkActionResult(
            NetworkActionResultCode code,
            NetworkActionPhase phase,
            NetworkTickId authoritativeTick,
            ushort sequence,
            int predictionKey = 0,
            NetworkActionCorrectionFlags correctionFlags = NetworkActionCorrectionFlags.None,
            ulong authoritativeStateHash = 0UL,
            ulong payloadHash = 0UL)
        {
            Code = code;
            Phase = phase;
            AuthoritativeTick = authoritativeTick;
            Sequence = sequence;
            PredictionKey = predictionKey;
            CorrectionFlags = correctionFlags;
            AuthoritativeStateHash = authoritativeStateHash;
            PayloadHash = payloadHash;
        }

        public bool IsAccepted
        {
            get
            {
                return Code == NetworkActionResultCode.Accepted || Code == NetworkActionResultCode.Corrected;
            }
        }

        public bool RequiresCorrection
        {
            get
            {
                return CorrectionFlags != NetworkActionCorrectionFlags.None || Code == NetworkActionResultCode.Corrected;
            }
        }

        public static NetworkActionResult Accept(
            NetworkTickId authoritativeTick,
            ushort sequence,
            int predictionKey = 0,
            ulong authoritativeStateHash = 0UL,
            ulong payloadHash = 0UL)
        {
            return new NetworkActionResult(
                NetworkActionResultCode.Accepted,
                NetworkActionPhase.Confirmed,
                authoritativeTick,
                sequence,
                predictionKey,
                NetworkActionCorrectionFlags.None,
                authoritativeStateHash,
                payloadHash);
        }

        public static NetworkActionResult Correct(
            NetworkTickId authoritativeTick,
            ushort sequence,
            NetworkActionCorrectionFlags correctionFlags,
            int predictionKey = 0,
            ulong authoritativeStateHash = 0UL,
            ulong payloadHash = 0UL)
        {
            return new NetworkActionResult(
                NetworkActionResultCode.Corrected,
                NetworkActionPhase.Corrected,
                authoritativeTick,
                sequence,
                predictionKey,
                correctionFlags,
                authoritativeStateHash,
                payloadHash);
        }

        public static NetworkActionResult Reject(
            NetworkActionResultCode code,
            NetworkTickId authoritativeTick,
            ushort sequence,
            int predictionKey = 0)
        {
            return new NetworkActionResult(
                code,
                NetworkActionPhase.Rejected,
                authoritativeTick,
                sequence,
                predictionKey);
        }
    }
}
