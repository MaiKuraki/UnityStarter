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

      // Published as one immutable object so a reader can never observe a token stream paired with
      // tag indices from a different compilation.
      private sealed class CompiledQuery
      {
         internal readonly int[] Tokens;
         internal readonly int[] TagIndices;
         internal readonly GameplayTagQueryExpression Root;
         internal readonly GameplayTagRegistry Registry;
         internal readonly int RegistryGeneration;
         internal readonly int MaxStackDepth;

         internal CompiledQuery(
            int[] tokens,
            int[] tagIndices,
            GameplayTagQueryExpression root,
            GameplayTagRegistry registry,
            int registryGeneration,
            int maxStackDepth)
         {
            Tokens = tokens;
            TagIndices = tagIndices;
            Root = root;
            Registry = registry;
            RegistryGeneration = registryGeneration;
            MaxStackDepth = maxStackDepth;
         }
      }

      [NonSerialized]
      private volatile CompiledQuery m_Compiled;

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

         if (RootExpression == null)
         {
            return false;
         }

         GameplayTagRegistry registry = GameplayTagRegistryBinding.Resolve(container);
         EnsureCompiled(registry);
         return Evaluate(container);
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

      private void EnsureCompiled(GameplayTagRegistry registry)
      {
         int registryGeneration = registry.Generation;
         CompiledQuery cached = m_Compiled;
         if (cached != null &&
            ReferenceEquals(cached.Root, RootExpression) &&
            ReferenceEquals(cached.Registry, registry) &&
            cached.RegistryGeneration == registryGeneration)
         {
            return;
         }

         List<int> tokenStream = new(24);
         List<int> tagIndices = new(16);
         HashSet<GameplayTagQueryExpression> activeExpressions = new();
         int nodeCount = 0;
         int stackDepth = 0;
         int maxStackDepth = 0;
         CompileExpression(RootExpression, tokenStream, tagIndices, activeExpressions, 0, ref nodeCount, ref stackDepth, ref maxStackDepth);

         int[] compiledTokens = tokenStream.ToArray();

         // Replaying the emitted stream is the only way to size the value stack correctly: the tree walk
         // measures depth, but a wide node keeps N child results live, which is width.
         int compiledDepth = SimulateStackDepth(compiledTokens);

         m_Compiled = new CompiledQuery(
            compiledTokens,
            tagIndices.ToArray(),
            RootExpression,
            registry,
            registryGeneration,
            compiledDepth);
      }

      /// <summary>
      /// Clears the compiled token stream. Call this after mutating <see cref="RootExpression"/>
      /// or any nested expression/tag container in place.
      /// </summary>
      public void InvalidateCompiledCache()
      {
         m_Compiled = null;
      }

      private static void CompileExpression(
         GameplayTagQueryExpression expression,
         List<int> tokenStream,
         List<int> tagIndices,
         HashSet<GameplayTagQueryExpression> activeExpressions,
         int depth,
         ref int nodeCount,
         ref int stackDepth,
         ref int maxStackDepth)
      {
         if (++nodeCount > MaxExpressionNodes)
            throw new InvalidOperationException($"Gameplay tag query node count exceeds {MaxExpressionNodes}.");

         if (expression == null)
         {
            tokenStream.Add((int)GameplayTagQueryOpcode.PushFalse);
            PushResult(ref stackDepth, ref maxStackDepth);
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
            PushResult(ref stackDepth, ref maxStackDepth);
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
                  ref nodeCount,
                  ref stackDepth,
                  ref maxStackDepth);
            }

            tokenStream.Add((int)GetExprOpcode(expression.Operator));
            tokenStream.Add(childCount);
            // The node pops its children and pushes one result. Recording the depth after the pops
            // keeps maxStackDepth an upper bound on how deep the value stack ever gets.
            stackDepth -= childCount - 1;
            PushResult(ref stackDepth, ref maxStackDepth);
            activeExpressions.Remove(expression);
            return;
         }

         tokenStream.Add(expression.Operator == EGameplayTagQueryExprOperator.Any
            ? (int)GameplayTagQueryOpcode.PushFalse
            : (int)GameplayTagQueryOpcode.PushTrue);
         PushResult(ref stackDepth, ref maxStackDepth);
         activeExpressions.Remove(expression);
      }

      private static void PushResult(ref int stackDepth, ref int maxStackDepth)
      {
         stackDepth++;
         if (stackDepth > maxStackDepth)
            maxStackDepth = stackDepth;
      }

      /// <summary>
      /// Replays the token stream with the evaluator's push/pop arithmetic and reports the highest the
      /// value stack ever gets. The result is exact for the emitted program, so it is both a safe
      /// capacity and a check that the program is well formed (it never pops what it did not push).
      /// </summary>
      private static int SimulateStackDepth(int[] tokenStream)
      {
         int depth = 0;
         int maxDepth = 0;

         for (int i = 0; i < tokenStream.Length;)
         {
            GameplayTagQueryOpcode opcode = (GameplayTagQueryOpcode)tokenStream[i++];
            switch (opcode)
            {
               case GameplayTagQueryOpcode.PushTrue:
               case GameplayTagQueryOpcode.PushFalse:
                  depth++;
                  if (depth > maxDepth)
                     maxDepth = depth;
                  break;

               case GameplayTagQueryOpcode.EvalAllTags:
               case GameplayTagQueryOpcode.EvalAnyTags:
               case GameplayTagQueryOpcode.EvalNoTags:
                  i += 2; // tagStart, tagCount - the tag list is evaluated inside the opcode
                  depth++;
                  if (depth > maxDepth)
                     maxDepth = depth;
                  break;

               case GameplayTagQueryOpcode.EvalAllExpr:
               case GameplayTagQueryOpcode.EvalAnyExpr:
               case GameplayTagQueryOpcode.EvalNoExpr:
               {
                  int childCount = tokenStream[i++];
                  depth -= childCount; // consume the children's results
                  depth++;             // leave this node's own result
                  if (depth < 0)
                     throw new InvalidOperationException("Gameplay tag query is malformed: it pops more results than it pushes.");
                  if (depth > maxDepth)
                     maxDepth = depth;
                  break;
               }

               default:
                  return maxDepth;
            }
         }

         return maxDepth;
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

      /// <summary>
      /// The largest value-stack depth this evaluator keeps on the machine stack. At or below this the
      /// stack lives in a single <see cref="ulong"/> word - no stack reservation, no zeroing, no heap.
      /// Past it a span sized to the exact compiled depth is used instead of a fixed budget.
      /// </summary>
      internal const int BitmaskStackCapacity = 64;

      private bool Evaluate<T>(in T container) where T : IReadOnlyGameplayTagContainer
      {
         CompiledQuery program = m_Compiled;
         if (program == null)
            return false;

         if (program.MaxStackDepth <= BitmaskStackCapacity)
            return EvaluateWithBitmaskStack(container, program);

         return EvaluateWithSpanStack(container, program);
      }

      /// <summary>
      /// Evaluation with the value stack packed into one <see cref="ulong"/>.
      /// </summary>
      private bool EvaluateWithBitmaskStack<T>(in T container, CompiledQuery program) where T : IReadOnlyGameplayTagContainer
      {
         ulong stack = 0;
         int stackCount = 0;
         int[] tokenStream = program.Tokens;

         for (int i = 0; i < tokenStream.Length;)
         {
            GameplayTagQueryOpcode opcode = (GameplayTagQueryOpcode)tokenStream[i++];
            switch (opcode)
            {
               case GameplayTagQueryOpcode.PushTrue:
                  stack |= 1UL << stackCount++;
                  break;

               case GameplayTagQueryOpcode.PushFalse:
                  stack &= ~(1UL << stackCount++);
                  break;

               case GameplayTagQueryOpcode.EvalAllTags:
               case GameplayTagQueryOpcode.EvalAnyTags:
               case GameplayTagQueryOpcode.EvalNoTags:
               {
                  int tagStart = tokenStream[i++];
                  int tagCount = tokenStream[i++];
                  bool result = EvaluateTags(container, opcode, tagStart, tagCount, program.TagIndices);
                  if (result)
                     stack |= 1UL << stackCount++;
                  else
                     stack &= ~(1UL << stackCount++);
                  break;
               }

               case GameplayTagQueryOpcode.EvalAllExpr:
               case GameplayTagQueryOpcode.EvalAnyExpr:
               case GameplayTagQueryOpcode.EvalNoExpr:
               {
                  int childCount = tokenStream[i++];
                  bool result = EvaluateExpressionBitmask(opcode, ref stack, ref stackCount, childCount);

                  // Pop the children and reuse the first child's slot for this node's result, so the
                  // bitmask path's stack trajectory matches the span path's and the capacity simulation's.
                  // Advancing stackCount instead of rewinding made the depth grow with the total number of
                  // pushes rather than the live peak; past index 63 C# masks the shift count, which set the
                  // wrong bit and returned a wrong answer with no error. The assignment is unconditional in
                  // both directions because a reused slot still holds its previous value.
                  int startIndex = stackCount - childCount;
                  if (startIndex < 0)
                     return false;

                  if (result)
                     stack |= 1UL << startIndex;
                  else
                     stack &= ~(1UL << startIndex);

                  stackCount = startIndex + 1;
                  break;
               }

               default:
                  return false;
            }
         }

         return stackCount > 0 && (stack >> (stackCount - 1) & 1UL) != 0UL;
      }

      private bool EvaluateWithSpanStack<T>(in T container, CompiledQuery program) where T : IReadOnlyGameplayTagContainer
      {
         Span<bool> stack = stackalloc bool[program.MaxStackDepth];
         int stackCount = 0;
         int[] tokenStream = program.Tokens;

         for (int i = 0; i < tokenStream.Length;)
         {
            GameplayTagQueryOpcode opcode = (GameplayTagQueryOpcode)tokenStream[i++];
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
                  int tagStart = tokenStream[i++];
                  int tagCount = tokenStream[i++];
                  stack[stackCount++] = EvaluateTags(container, opcode, tagStart, tagCount, program.TagIndices);
                  break;
               }

               case GameplayTagQueryOpcode.EvalAllExpr:
               case GameplayTagQueryOpcode.EvalAnyExpr:
               case GameplayTagQueryOpcode.EvalNoExpr:
               {
                  int childCount = tokenStream[i++];

                  // Evaluate first, then store. C# evaluates the target's index - including the
                  // post-increment - before the right-hand side, so `stack[stackCount++] = Evaluate...`
                  // handed the callee a stackCount that was already one too high: it popped the children
                  // from the wrong position and then stored the result above the stack's top. That both
                  // corrupted the result and, whenever the peak reached the capacity, indexed past the end.
                  bool expressionResult = EvaluateExpression(opcode, stack, ref stackCount, childCount);
                  stack[stackCount++] = expressionResult;
                  break;
               }

               default:
                  return false;
            }
         }

         return stackCount > 0 && stack[stackCount - 1];
      }

      private bool EvaluateTags<T>(in T container, GameplayTagQueryOpcode opcode, int tagStart, int tagCount, int[] tagIndices) where T : IReadOnlyGameplayTagContainer
      {
         switch (opcode)
         {
            case GameplayTagQueryOpcode.EvalAllTags:
               for (int i = 0; i < tagCount; i++)
               {
                  if (!container.ContainsRuntimeIndex(tagIndices[tagStart + i], explicitOnly: false))
                  {
                     return false;
                  }
               }
               return true;

            case GameplayTagQueryOpcode.EvalAnyTags:
               for (int i = 0; i < tagCount; i++)
               {
                  if (container.ContainsRuntimeIndex(tagIndices[tagStart + i], explicitOnly: false))
                  {
                     return true;
                  }
               }
               return false;

            case GameplayTagQueryOpcode.EvalNoTags:
               for (int i = 0; i < tagCount; i++)
               {
                  if (container.ContainsRuntimeIndex(tagIndices[tagStart + i], explicitOnly: false))
                  {
                     return false;
                  }
               }
               return true;
         }

         return false;
      }

      private static bool EvaluateExpressionBitmask(
         GameplayTagQueryOpcode opcode,
         ref ulong stack,
         ref int stackCount,
         int childCount)
      {
         switch (opcode)
         {
            case GameplayTagQueryOpcode.EvalAllExpr:
            {
               for (int i = 0; i < childCount; i++)
               {
                  if (((stack >> (stackCount - 1 - i)) & 1UL) == 0UL)
                     return false;
               }

               return true;
            }

            case GameplayTagQueryOpcode.EvalAnyExpr:
            {
               for (int i = 0; i < childCount; i++)
               {
                  if (((stack >> (stackCount - 1 - i)) & 1UL) != 0UL)
                     return true;
               }

               return false;
            }

            case GameplayTagQueryOpcode.EvalNoExpr:
            {
               for (int i = 0; i < childCount; i++)
               {
                  if (((stack >> (stackCount - 1 - i)) & 1UL) != 0UL)
                     return false;
               }

               return true;
            }

            default:
               return false;
         }
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
