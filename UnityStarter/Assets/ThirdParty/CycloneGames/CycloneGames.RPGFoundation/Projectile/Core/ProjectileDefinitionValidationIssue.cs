namespace CycloneGames.RPGFoundation.Projectile.Core
{
    public readonly struct ProjectileDefinitionValidationIssue
    {
        public readonly ProjectileValidationSeverity Severity;
        public readonly ProjectileDefinitionValidationCode Code;
        public readonly string Message;

        public ProjectileDefinitionValidationIssue(
            ProjectileValidationSeverity severity,
            ProjectileDefinitionValidationCode code,
            string message)
        {
            Severity = severity;
            Code = code;
            Message = message;
        }
    }
}
