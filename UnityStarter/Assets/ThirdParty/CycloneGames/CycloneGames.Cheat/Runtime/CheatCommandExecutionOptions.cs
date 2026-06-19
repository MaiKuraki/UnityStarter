using CycloneGames.Cheat.Core;
using VitalRouter;

namespace CycloneGames.Cheat.Runtime
{
    public readonly struct CheatCommandExecutionOptions
    {
        public readonly Router Router;
        public readonly CheatDuplicatePolicy DuplicatePolicy;
        public readonly string Source;

        public CheatCommandExecutionOptions(
            Router router,
            CheatDuplicatePolicy duplicatePolicy = CheatDuplicatePolicy.Drop,
            string source = null)
        {
            Router = router;
            DuplicatePolicy = duplicatePolicy;
            Source = source;
        }

        public CheatCommandExecutionOptions WithRouter(Router router)
        {
            return new CheatCommandExecutionOptions(router, DuplicatePolicy, Source);
        }

        public CheatCommandExecutionOptions WithDuplicatePolicy(CheatDuplicatePolicy duplicatePolicy)
        {
            return new CheatCommandExecutionOptions(Router, duplicatePolicy, Source);
        }

        public CheatCommandExecutionOptions WithSource(string source)
        {
            return new CheatCommandExecutionOptions(Router, DuplicatePolicy, source);
        }
    }
}
