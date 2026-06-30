namespace CycloneGames.RPGFoundation.Trajectory.Core
{
    public readonly struct TrajectoryQueryValidationIssue
    {
        public readonly TrajectoryValidationSeverity Severity;
        public readonly TrajectoryQueryValidationCode Code;
        public readonly string Message;

        public TrajectoryQueryValidationIssue(
            TrajectoryValidationSeverity severity,
            TrajectoryQueryValidationCode code,
            string message)
        {
            Severity = severity;
            Code = code;
            Message = message;
        }
    }
}
