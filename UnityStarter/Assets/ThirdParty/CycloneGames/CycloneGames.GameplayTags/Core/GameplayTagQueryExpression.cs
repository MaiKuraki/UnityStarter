using System.Collections.Generic;
using System.Text;

namespace CycloneGames.GameplayTags.Core
{
    /// <summary>
    /// A single node in a GameplayTagQuery tree. It can contain a list of tags to check
    /// or a list of sub-expressions to evaluate.
    /// </summary>
    [System.Serializable]
    public sealed class GameplayTagQueryExpression
    {
        // The operator to apply to the Tags or Expressions list.
        public EGameplayTagQueryExprOperator Operator;
        
        // The list of tags to evaluate with the operator. This is used if Expressions is empty.
        public GameplayTagContainer Tags;
        
        // The list of sub-expressions to evaluate. This is used if Tags is empty.
        public List<GameplayTagQueryExpression> Expressions;

        public static GameplayTagQueryExpression MatchAll(GameplayTagContainer tags)
            => new() { Operator = EGameplayTagQueryExprOperator.All, Tags = tags };

        public static GameplayTagQueryExpression MatchAny(GameplayTagContainer tags)
            => new() { Operator = EGameplayTagQueryExprOperator.Any, Tags = tags };

        public static GameplayTagQueryExpression MatchNone(GameplayTagContainer tags)
            => new() { Operator = EGameplayTagQueryExprOperator.None, Tags = tags };

        public static GameplayTagQueryExpression All(params GameplayTagQueryExpression[] expressions)
            => FromExpressions(EGameplayTagQueryExprOperator.All, expressions);

        public static GameplayTagQueryExpression Any(params GameplayTagQueryExpression[] expressions)
            => FromExpressions(EGameplayTagQueryExprOperator.Any, expressions);

        public static GameplayTagQueryExpression None(params GameplayTagQueryExpression[] expressions)
            => FromExpressions(EGameplayTagQueryExprOperator.None, expressions);

        private static GameplayTagQueryExpression FromExpressions(
            EGameplayTagQueryExprOperator queryOperator,
            GameplayTagQueryExpression[] expressions)
        {
            return new GameplayTagQueryExpression
            {
                Operator = queryOperator,
                Expressions = expressions == null
                    ? new List<GameplayTagQueryExpression>()
                    : new List<GameplayTagQueryExpression>(expressions)
            };
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            AppendTo(sb, new HashSet<GameplayTagQueryExpression>(), 0);
            return sb.ToString();
        }

        private void AppendTo(StringBuilder sb, HashSet<GameplayTagQueryExpression> active, int depth)
        {
            if (depth > GameplayTagQuery.MaxExpressionDepth)
            {
                sb.Append("<depth-limit>");
                return;
            }
            if (!active.Add(this))
            {
                sb.Append("<cycle>");
                return;
            }

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
                    if (expr == null) sb.Append("<null>");
                    else expr.AppendTo(sb, active, depth + 1);
                    first = false;
                }
            }
            
            sb.Append(")");
            active.Remove(this);
        }
    }
}
