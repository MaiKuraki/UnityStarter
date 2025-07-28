using System.Collections.Generic;
using System.Text;

namespace CycloneGames.GameplayTags.Runtime
{
    /// <summary>
    /// A single node in a GameplayTagQuery tree. It can contain a list of tags to check
    /// or a list of sub-expressions to evaluate.
    /// </summary>
    [System.Serializable]
    public class GameplayTagQueryExpression
    {
        // The operator to apply to the Tags or Expressions list.
        public EGameplayTagQueryExprOperator Operator;
        
        // The list of tags to evaluate with the operator. This is used if Expressions is empty.
        public GameplayTagContainer Tags;
        
        // The list of sub-expressions to evaluate. This is used if Tags is empty.
        public List<GameplayTagQueryExpression> Expressions;

        /// <summary>
        /// Evaluates this expression against a tag container.
        /// </summary>
        /// <param name="container">The tag container to check.</param>
        /// <returns>True if the expression matches, false otherwise.</returns>
        public bool Matches(GameplayTagContainer container)
        {
            bool hasTags = Tags != null && !Tags.IsEmpty;
            bool hasExpressions = Expressions != null && Expressions.Count > 0;

            if (hasTags)
            {
                // Evaluate tags
                switch (Operator)
                {
                    case EGameplayTagQueryExprOperator.All:
                        return container.HasAll(Tags);
                    case EGameplayTagQueryExprOperator.Any:
                        return container.HasAny(Tags);
                    case EGameplayTagQueryExprOperator.None:
                        return !container.HasAny(Tags);
                }
            }
            else if (hasExpressions)
            {
                // Evaluate sub-expressions
                switch (Operator)
                {
                    case EGameplayTagQueryExprOperator.All:
                        foreach (var expr in Expressions)
                        {
                            if (!expr.Matches(container)) return false;
                        }
                        return true;
                    case EGameplayTagQueryExprOperator.Any:
                        foreach (var expr in Expressions)
                        {
                            if (expr.Matches(container)) return true;
                        }
                        return false;
                    case EGameplayTagQueryExprOperator.None:
                        foreach (var expr in Expressions)
                        {
                            if (expr.Matches(container)) return false;
                        }
                        return true;
                }
            }

            // If an expression is empty, the behavior matches UE:
            // All/None match (vacuously true), Any does not match.
            return Operator == EGameplayTagQueryExprOperator.All || Operator == EGameplayTagQueryExprOperator.None;
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append(Operator.ToString());
            sb.Append(" (");

            bool hasTags = Tags != null && !Tags.IsEmpty;
            bool hasExpressions = Expressions != null && Expressions.Count > 0;
            
            if (hasTags)
            {
                bool first = true;
                foreach (var tag in Tags.GetExplicitTags())
                {
                    if (!first) sb.Append(", ");
                    sb.Append(tag.Name);
                    first = false;
                }
            }
            else if (hasExpressions)
            {
                bool first = true;
                foreach (var expr in Expressions)
                {
                    if (!first) sb.Append(", ");
                    sb.Append(expr.ToString());
                    first = false;
                }
            }
            
            sb.Append(")");
            return sb.ToString();
        }
    }
}