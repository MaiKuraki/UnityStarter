namespace CycloneGames.GameplayTags.Runtime
{
    /// <summary>
    /// The operator to use when evaluating a list of expressions or tags.
    /// </summary>
    public enum EGameplayTagQueryExprOperator
    {
        // Match if ALL of the expressions/tags in the list match.
        All,
        // Match if ANY of the expressions/tags in the list match.
        Any,
        // Match if NONE of the expressions/tags in the list match.
        None
    }

    /// <summary>
    /// Represents a complex query that can be run against a GameplayTagContainer.
    /// This allows for nested logic like (A AND B) OR (C AND NOT D).
    /// </summary>
    [System.Serializable]
    public class GameplayTagQuery
    {
        // The root expression of the query tree.
        public GameplayTagQueryExpression RootExpression;

        /// <summary>
        /// Evaluates this query against the given tag container.
        /// </summary>
        /// <param name="container">The tag container to check against.</param>
        /// <returns>True if the container matches the query, false otherwise.</returns>
        public bool Matches(GameplayTagContainer container)
        {
            if (RootExpression == null)
            {
                return false;
            }
            return RootExpression.Matches(container);
        }

        public override string ToString()
        {
            if (RootExpression == null)
            {
                return "Empty Query";
            }
            return RootExpression.ToString();
        }
        
        /// <summary>
        /// Creates a simple query that checks if a container has all of the specified tags.
        /// </summary>
        public static GameplayTagQuery BuildQueryAll(GameplayTagContainer tags)
        {
            return new GameplayTagQuery
            {
                RootExpression = new GameplayTagQueryExpression
                {
                    Operator = EGameplayTagQueryExprOperator.All,
                    Tags = tags
                }
            };
        }

        /// <summary>
        /// Creates a simple query that checks if a container has any of the specified tags.
        /// </summary>
        public static GameplayTagQuery BuildQueryAny(GameplayTagContainer tags)
        {
            return new GameplayTagQuery
            {
                RootExpression = new GameplayTagQueryExpression
                {
                    Operator = EGameplayTagQueryExprOperator.Any,
                    Tags = tags
                }
            };
        }
    }
}