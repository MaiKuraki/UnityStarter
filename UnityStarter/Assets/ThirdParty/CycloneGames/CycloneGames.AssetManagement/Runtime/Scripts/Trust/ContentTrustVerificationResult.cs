namespace CycloneGames.AssetManagement.Runtime.Trust
{
    public readonly struct ContentTrustVerificationResult
    {
        public readonly bool Succeeded;
        public readonly ContentTrustFailure Failure;
        public readonly string Location;
        public readonly string Expected;
        public readonly string Actual;
        public readonly string Message;

        private ContentTrustVerificationResult(
            bool succeeded,
            ContentTrustFailure failure,
            string location,
            string expected,
            string actual,
            string message)
        {
            Succeeded = succeeded;
            Failure = failure;
            Location = location;
            Expected = expected;
            Actual = actual;
            Message = message;
        }

        public static ContentTrustVerificationResult Passed(string location)
        {
            return new ContentTrustVerificationResult(true, ContentTrustFailure.None, location, null, null, null);
        }

        public static ContentTrustVerificationResult Failed(
            ContentTrustFailure failure,
            string location,
            string expected = null,
            string actual = null,
            string message = null)
        {
            return new ContentTrustVerificationResult(false, failure, location, expected, actual, message);
        }
    }
}
