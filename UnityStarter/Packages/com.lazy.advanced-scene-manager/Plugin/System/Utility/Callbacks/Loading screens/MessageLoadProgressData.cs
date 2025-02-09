namespace AdvancedSceneManager.Loading
{

    /// <summary>An implementation of <see cref="ILoadProgressData"/> that provides a string message.</summary>
    public readonly struct MessageLoadProgressData : ILoadProgressData
    {

        public float value { get; }

        /// <summary>The message of this report.</summary>
        public string message { get; }

        public MessageLoadProgressData(string message, float value)
        {
            this.message = message;
            this.value = value;
        }

        public override string ToString()
        {
            return $"{value * 100}%: {message}";
        }

    }

}
