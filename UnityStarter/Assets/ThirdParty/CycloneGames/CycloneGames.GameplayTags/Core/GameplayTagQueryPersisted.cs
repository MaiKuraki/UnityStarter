using System;
using System.Collections.Generic;

namespace CycloneGames.GameplayTags.Core
{
   /// <summary>
   /// Engine-neutral persistence form of a <see cref="GameplayTagQuery"/>.
   /// </summary>
   /// <remarks>
   /// <para>
   /// The tree is stored in preorder. Node <c>i</c> owns entry <c>i</c> of <see cref="Operators"/>,
   /// <see cref="ChildCounts"/> and <see cref="TagCounts"/>; its tag names are the next
   /// <see cref="TagCounts"/>[i] entries of <see cref="TagNames"/>, and its children are the nodes that
   /// immediately follow it in preorder.
   /// </para>
   /// <para>
   /// Flat arrays rather than nested serializable classes: Unity serializes nested non-reused types to a
   /// bounded depth while <see cref="GameplayTagQuery.MaxExpressionDepth"/> is 32, so a nested shape would
   /// silently truncate authoring data. A host maps these arrays onto its own serialized fields
   /// (<c>[SerializeField]</c> in Unity, <c>[Export]</c> in Godot) without Core taking a host reference.
   /// </para>
   /// <para>
   /// Only the durable identity of a tag crosses this boundary, never a runtime index: indices are only
   /// meaningful against the registry epoch that produced them. A name the loading registry does not know
   /// is skipped with a diagnostic, so removing a tag from a catalog does not make older data unloadable.
   /// </para>
   /// </remarks>
   public readonly struct GameplayTagQueryPersisted
   {
      /// <summary>Per node, preorder: the <see cref="EGameplayTagQueryExprOperator"/> as an int.</summary>
      public readonly int[] Operators;

      /// <summary>Per node, preorder: number of child expressions that follow this node.</summary>
      public readonly int[] ChildCounts;

      /// <summary>Per node, preorder: number of tag names this node owns.</summary>
      public readonly int[] TagCounts;

      /// <summary>Concatenated tag names, in node preorder.</summary>
      public readonly string[] TagNames;

      public GameplayTagQueryPersisted(
         int[] operators,
         int[] childCounts,
         int[] tagCounts,
         string[] tagNames)
      {
         Operators = operators;
         ChildCounts = childCounts;
         TagCounts = tagCounts;
         TagNames = tagNames;
      }

      /// <summary>An empty payload. It builds no query rather than an always-false one.</summary>
      public static GameplayTagQueryPersisted Empty
         => new GameplayTagQueryPersisted(
            Array.Empty<int>(),
            Array.Empty<int>(),
            Array.Empty<int>(),
            Array.Empty<string>());

      /// <summary>True when this payload carries no node.</summary>
      public bool IsEmpty => Operators == null || Operators.Length == 0;

      /// <summary>
      /// Checks structural invariants without building a query. Returns false with a reason when the
      /// payload is truncated, mutated, or exceeds the query limits.
      /// </summary>
      /// <remarks>
      /// Authoring data is untrusted input: it may have been hand-edited, merged, or produced by an older
      /// build. Validation is therefore a precondition of loading, not an assertion.
      /// </remarks>
      public bool TryValidate(out string error)
      {
         if (IsEmpty)
         {
            error = "no nodes";
            return false;
         }

         if (ChildCounts == null || TagCounts == null || TagNames == null)
         {
            error = "null array";
            return false;
         }

         if (ChildCounts.Length != Operators.Length || TagCounts.Length != Operators.Length)
         {
            error = "array length mismatch";
            return false;
         }

         if (Operators.Length > GameplayTagQuery.MaxExpressionNodes)
         {
            error = "node count over limit";
            return false;
         }

         int totalTags = 0;
         for (int i = 0; i < Operators.Length; i++)
         {
            int value = Operators[i];
            if (value < (int)EGameplayTagQueryExprOperator.All || value > (int)EGameplayTagQueryExprOperator.None)
            {
               error = "operator out of range";
               return false;
            }

            if (ChildCounts[i] < 0 || TagCounts[i] < 0)
            {
               error = "negative count";
               return false;
            }

            totalTags += TagCounts[i];
         }

         if (totalTags > GameplayTagQuery.MaxReferencedTags || totalTags != TagNames.Length)
         {
            error = "tag name count mismatch";
            return false;
         }

         // Walk the preorder stream exactly the way the builder does, so a malformed payload cannot
         // produce a partially built tree. A cycle is impossible here: the payload is a flat array with
         // forward-only child indices.
         int cursor = 0;
         if (!ValidateNode(this, ref cursor, 0, out error))
            return false;

         if (cursor != Operators.Length)
         {
            error = "trailing nodes";
            return false;
         }

         error = null;
         return true;
      }

      /// <summary>
      /// Captures <paramref name="query"/> into a payload.
      /// </summary>
      /// <returns>
      /// False when the query cannot be faithfully captured: no root, a cycle, a depth or node count over
      /// the query limits, or a tag container whose indices are stale against the current registry. The
      /// caller keeps whatever durable copy it already has instead of writing a guessed payload.
      /// </returns>
      public static bool TryCreate(GameplayTagQuery query, out GameplayTagQueryPersisted persisted)
      {
         persisted = Empty;

         GameplayTagQueryExpression root = query?.RootExpression;
         if (root == null)
            return false;

         var operators = new List<int>(16);
         var childCounts = new List<int>(16);
         var tagCounts = new List<int>(16);
         var tagNames = new List<string>(16);
         var active = new HashSet<GameplayTagQueryExpression>();

         if (!Walk(root, 0, operators, childCounts, tagCounts, tagNames, active))
            return false;

         if (operators.Count > GameplayTagQuery.MaxExpressionNodes ||
             tagNames.Count > GameplayTagQuery.MaxReferencedTags)
         {
            return false;
         }

         persisted = new GameplayTagQueryPersisted(
            operators.ToArray(),
            childCounts.ToArray(),
            tagCounts.ToArray(),
            tagNames.ToArray());
         return true;
      }

      /// <summary>
      /// Builds a query from a payload.
      /// </summary>
      /// <param name="persisted">The payload to load.</param>
      /// <param name="registry">Registry the tag names resolve against; null uses the ambient registry.</param>
      /// <param name="query">The built query, or null on failure.</param>
      /// <returns>False when the payload is invalid, leaving <paramref name="query"/> null.</returns>
      public static bool TryBuild(
         in GameplayTagQueryPersisted persisted,
         GameplayTagRegistry registry,
         out GameplayTagQuery query)
      {
         query = null;

         if (!persisted.TryValidate(out _))
            return false;

         int nodeIndex = 0;
         int nameOffset = 0;
         if (!BuildNode(persisted, ref nodeIndex, ref nameOffset, registry, 0, out GameplayTagQueryExpression root))
            return false;

         query = new GameplayTagQuery { RootExpression = root };
         return true;
      }

      private static bool Walk(
         GameplayTagQueryExpression expression,
         int depth,
         List<int> operators,
         List<int> childCounts,
         List<int> tagCounts,
         List<string> tagNames,
         HashSet<GameplayTagQueryExpression> active)
      {
         if (expression == null || depth > GameplayTagQuery.MaxExpressionDepth)
            return false;

         // A cyclic authoring graph cannot be evaluated, so it cannot be persisted either.
         if (!active.Add(expression))
            return false;

         operators.Add((int)expression.Operator);

         GameplayTagContainer tags = expression.Tags;
         if (tags == null || tags.IsEmpty)
         {
            tagCounts.Add(0);
         }
         else
         {
            if (!tags.TryToPersisted(out string[] names))
            {
               active.Remove(expression);
               return false;
            }

            string[] resolved = names ?? Array.Empty<string>();
            tagCounts.Add(resolved.Length);
            for (int i = 0; i < resolved.Length; i++)
               tagNames.Add(resolved[i]);
         }

         int childCount = expression.Expressions?.Count ?? 0;
         childCounts.Add(childCount);

         // Preorder: a node's own tags precede its children's, and the children are contiguous.
         for (int i = 0; i < childCount; i++)
         {
            if (!Walk(expression.Expressions[i], depth + 1, operators, childCounts, tagCounts, tagNames, active))
            {
               active.Remove(expression);
               return false;
            }
         }

         active.Remove(expression);
         return true;
      }

      private static bool BuildNode(
         in GameplayTagQueryPersisted persisted,
         ref int nodeIndex,
         ref int nameOffset,
         GameplayTagRegistry registry,
         int depth,
         out GameplayTagQueryExpression expression)
      {
         expression = null;

         if (depth > GameplayTagQuery.MaxExpressionDepth || nodeIndex >= persisted.Operators.Length)
            return false;

         int index = nodeIndex++;
         int tagCount = persisted.TagCounts[index];
         int childCount = persisted.ChildCounts[index];

         var node = new GameplayTagQueryExpression
         {
            Operator = (EGameplayTagQueryExprOperator)persisted.Operators[index]
         };

         if (tagCount > 0)
         {
            var names = new string[tagCount];
            Array.Copy(persisted.TagNames, nameOffset, names, 0, tagCount);
            nameOffset += tagCount;

            var container = registry != null ? new GameplayTagContainer(registry) : new GameplayTagContainer();
            container.LoadPersisted(names);
            node.Tags = container;
         }

         if (childCount > 0)
         {
            var children = new List<GameplayTagQueryExpression>(childCount);
            for (int i = 0; i < childCount; i++)
            {
               if (!BuildNode(persisted, ref nodeIndex, ref nameOffset, registry, depth + 1, out GameplayTagQueryExpression child))
                  return false;

               children.Add(child);
            }

            node.Expressions = children;
         }

         expression = node;
         return true;
      }
      private static bool ValidateNode(
         in GameplayTagQueryPersisted persisted,
         ref int cursor,
         int depth,
         out string error)
      {
         error = null;
         if (depth > GameplayTagQuery.MaxExpressionDepth)
         {
            error = "depth over limit";
            return false;
         }

         if (cursor >= persisted.Operators.Length)
         {
            error = "missing node";
            return false;
         }

         int index = cursor++;
         int children = persisted.ChildCounts[index];
         for (int i = 0; i < children; i++)
         {
            if (!ValidateNode(persisted, ref cursor, depth + 1, out error))
               return false;
         }

         return true;
      }
   }
}

