using System;
using System.Collections.Generic;

namespace CycloneGames.GameplayTags.Core
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

    internal enum GameplayTagQueryOpcode
    {
        PushTrue = 1,
        PushFalse = 2,
        EvalAllTags = 3,
        EvalAnyTags = 4,
        EvalNoTags = 5,
        EvalAllExpr = 6,
        EvalAnyExpr = 7,
        EvalNoExpr = 8
    }

    /// <summary>
    /// Represents a complex query that can be run against a GameplayTagContainer.
    /// This allows for nested logic like (A AND B) OR (C AND NOT D).
    /// </summary>
    [Serializable]
    public sealed class GameplayTagQuery
    {
        public const int MaxExpressionDepth = 32;
        public const int MaxExpressionNodes = 1024;
        public const int MaxReferencedTags = 4096;

        // The root expression of the query tree.
        public GameplayTagQueryExpression RootExpression;

        [NonSerialized]
        private int[] m_TokenStream;

        [NonSerialized]
        private int[] m_CompiledTagIndices;

        [NonSerialized]
        private GameplayTagQueryExpression m_CompiledRootExpression;

        [NonSerialized]
        private int m_CompiledRegistryGeneration;

        /// <summary>
        /// Evaluates this query against the given tag container.
        /// </summary>
        /// <param name="container">The tag container to check against.</param>
        /// <returns>True if the container matches the query, false otherwise.</returns>
        public bool Matches(GameplayTagContainer container)
        {
            if (container == null)
            {
                return false;
            }

            return Matches<GameplayTagContainer>(container);
        }

        public bool Matches<T>(in T container) where T : IReadOnlyGameplayTagContainer
        {
            if (container == null)
            {
                return false;
            }

            GameplayTagContainerUtility.EnsureCurrentRuntimeIndexEpoch(container);
            if (RootExpression == null)
            {
                return false;
            }

            EnsureCompiled();
            return EvaluateNode(container, 0);
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

        private void EnsureCompiled()
        {
            int registryGeneration = GameplayTagManager.CurrentGeneration;
            if (m_TokenStream != null &&
                ReferenceEquals(m_CompiledRootExpression, RootExpression) &&
                m_CompiledRegistryGeneration == registryGeneration)
            {
                return;
            }

            List<int> tokenStream = new(24);
            List<int> tagIndices = new(16);
            HashSet<GameplayTagQueryExpression> activeExpressions = new();
            int nodeCount = 0;
            CompileExpression(RootExpression, tokenStream, tagIndices, activeExpressions, 0, ref nodeCount);

            m_TokenStream = tokenStream.ToArray();
            m_CompiledTagIndices = tagIndices.ToArray();
            m_CompiledRootExpression = RootExpression;
            m_CompiledRegistryGeneration = registryGeneration;
        }

        /// <summary>
        /// Clears the compiled token stream. Call this after mutating <see cref="RootExpression"/>
        /// or any nested expression/tag container in place.
        /// </summary>
        public void InvalidateCompiledCache()
        {
            m_TokenStream = null;
            m_CompiledTagIndices = null;
            m_CompiledRootExpression = null;
            m_CompiledRegistryGeneration = 0;
        }

        private static void CompileExpression(
            GameplayTagQueryExpression expression,
            List<int> tokenStream,
            List<int> tagIndices,
            HashSet<GameplayTagQueryExpression> activeExpressions,
            int depth,
            ref int nodeCount)
        {
            if (++nodeCount > MaxExpressionNodes)
                throw new InvalidOperationException($"Gameplay tag query node count exceeds {MaxExpressionNodes}.");

            if (expression == null)
            {
                tokenStream.Add((int)GameplayTagQueryOpcode.PushFalse);
                return;
            }

            if (depth > MaxExpressionDepth)
                throw new InvalidOperationException($"Gameplay tag query depth exceeds {MaxExpressionDepth}.");
            if (!activeExpressions.Add(expression))
                throw new InvalidOperationException("Gameplay tag query contains an expression cycle.");

            bool hasTags = expression.Tags != null && !expression.Tags.IsEmpty;
            bool hasExpressions = expression.Expressions != null && expression.Expressions.Count > 0;
            if (hasTags && hasExpressions)
                throw new InvalidOperationException("A gameplay tag query expression cannot contain both tags and child expressions.");

            if (hasTags)
            {
                int tagStart = tagIndices.Count;
                foreach (GameplayTag tag in expression.Tags.GetExplicitTags())
                {
                    tagIndices.Add(tag.RuntimeIndex);
                    if (tagIndices.Count > MaxReferencedTags)
                        throw new InvalidOperationException($"Gameplay tag query references more than {MaxReferencedTags} tags.");
                }

                int tagCount = tagIndices.Count - tagStart;
                tokenStream.Add((int)GetTagOpcode(expression.Operator));
                tokenStream.Add(tagStart);
                tokenStream.Add(tagCount);
                activeExpressions.Remove(expression);
                return;
            }

            if (hasExpressions)
            {
                int childCount = expression.Expressions.Count;
                for (int i = 0; i < childCount; i++)
                {
                    CompileExpression(
                        expression.Expressions[i],
                        tokenStream,
                        tagIndices,
                        activeExpressions,
                        depth + 1,
                        ref nodeCount);
                }

                tokenStream.Add((int)GetExprOpcode(expression.Operator));
                tokenStream.Add(childCount);
                activeExpressions.Remove(expression);
                return;
            }

            tokenStream.Add(expression.Operator == EGameplayTagQueryExprOperator.Any
                ? (int)GameplayTagQueryOpcode.PushFalse
                : (int)GameplayTagQueryOpcode.PushTrue);
            activeExpressions.Remove(expression);
        }

        private static GameplayTagQueryOpcode GetTagOpcode(EGameplayTagQueryExprOperator op)
        {
            switch (op)
            {
                case EGameplayTagQueryExprOperator.All: return GameplayTagQueryOpcode.EvalAllTags;
                case EGameplayTagQueryExprOperator.Any: return GameplayTagQueryOpcode.EvalAnyTags;
                default: return GameplayTagQueryOpcode.EvalNoTags;
            }
        }

        private static GameplayTagQueryOpcode GetExprOpcode(EGameplayTagQueryExprOperator op)
        {
            switch (op)
            {
                case EGameplayTagQueryExprOperator.All: return GameplayTagQueryOpcode.EvalAllExpr;
                case EGameplayTagQueryExprOperator.Any: return GameplayTagQueryOpcode.EvalAnyExpr;
                default: return GameplayTagQueryOpcode.EvalNoExpr;
            }
        }

        private bool EvaluateNode<T>(in T container, int nodeIndex) where T : IReadOnlyGameplayTagContainer
        {
            // A compiled expression can push at most one result per expression node.
            // The compile-time MaxExpressionNodes budget therefore bounds this fixed
            // operation-local scratch to 1 KiB without a shared pool or heap lease.
            Span<bool> stack = stackalloc bool[MaxExpressionNodes];
            int stackCount = 0;

            for (int i = 0; i < m_TokenStream.Length;)
            {
                GameplayTagQueryOpcode opcode = (GameplayTagQueryOpcode)m_TokenStream[i++];
                switch (opcode)
                {
                    case GameplayTagQueryOpcode.PushTrue:
                        stack[stackCount++] = true;
                        break;

                    case GameplayTagQueryOpcode.PushFalse:
                        stack[stackCount++] = false;
                        break;

                    case GameplayTagQueryOpcode.EvalAllTags:
                    case GameplayTagQueryOpcode.EvalAnyTags:
                    case GameplayTagQueryOpcode.EvalNoTags:
                    {
                        int tagStart = m_TokenStream[i++];
                        int tagCount = m_TokenStream[i++];
                        stack[stackCount++] = EvaluateTags(container, opcode, tagStart, tagCount);
                        break;
                    }

                    case GameplayTagQueryOpcode.EvalAllExpr:
                    case GameplayTagQueryOpcode.EvalAnyExpr:
                    case GameplayTagQueryOpcode.EvalNoExpr:
                    {
                        int childCount = m_TokenStream[i++];
                        bool result = EvaluateExpression(opcode, stack, ref stackCount, childCount);
                        stack[stackCount++] = result;
                        break;
                    }

                    default:
                        return false;
                }
            }

            return stackCount > 0 && stack[stackCount - 1];
        }

        private bool EvaluateTags<T>(in T container, GameplayTagQueryOpcode opcode, int tagStart, int tagCount) where T : IReadOnlyGameplayTagContainer
        {
            switch (opcode)
            {
                case GameplayTagQueryOpcode.EvalAllTags:
                    for (int i = 0; i < tagCount; i++)
                    {
                        if (!container.ContainsRuntimeIndex(m_CompiledTagIndices[tagStart + i], explicitOnly: false))
                        {
                            return false;
                        }
                    }
                    return true;

                case GameplayTagQueryOpcode.EvalAnyTags:
                    for (int i = 0; i < tagCount; i++)
                    {
                        if (container.ContainsRuntimeIndex(m_CompiledTagIndices[tagStart + i], explicitOnly: false))
                        {
                            return true;
                        }
                    }
                    return false;

                case GameplayTagQueryOpcode.EvalNoTags:
                    for (int i = 0; i < tagCount; i++)
                    {
                        if (container.ContainsRuntimeIndex(m_CompiledTagIndices[tagStart + i], explicitOnly: false))
                        {
                            return false;
                        }
                    }
                    return true;
            }

            return false;
        }

        private static bool EvaluateExpression(GameplayTagQueryOpcode opcode, Span<bool> stack, ref int stackCount, int childCount)
        {
            bool result = opcode != GameplayTagQueryOpcode.EvalAnyExpr;
            int startIndex = stackCount - childCount;
            if (startIndex < 0)
                return false;

            switch (opcode)
            {
                case GameplayTagQueryOpcode.EvalAllExpr:
                    for (int i = startIndex; i < stackCount; i++)
                    {
                        if (!stack[i])
                        {
                            result = false;
                            break;
                        }
                    }
                    break;

                case GameplayTagQueryOpcode.EvalAnyExpr:
                    result = false;
                    for (int i = startIndex; i < stackCount; i++)
                    {
                        if (stack[i])
                        {
                            result = true;
                            break;
                        }
                    }
                    break;

                case GameplayTagQueryOpcode.EvalNoExpr:
                    for (int i = startIndex; i < stackCount; i++)
                    {
                        if (stack[i])
                        {
                            result = false;
                            goto finish;
                        }
                    }
                    result = true;
                    break;
            }

        finish:
            stackCount = startIndex;
            return result;
        }
    }
}
