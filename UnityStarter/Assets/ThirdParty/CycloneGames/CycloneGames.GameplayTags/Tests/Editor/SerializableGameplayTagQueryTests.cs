using System.Collections.Generic;
using CycloneGames.GameplayTags.Core;
using CycloneGames.GameplayTags.Unity.Runtime;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.GameplayTags.Tests.Editor
{
   /// <summary>
   /// Covers the Unity half of query persistence: the arrays Unity actually writes, and the
   /// serialization callbacks that keep the authored payload and the live query in sync.
   /// </summary>
   /// <remarks>
   /// The Core round-trip is covered by <see cref="GameplayTagQueryPersistenceFuzzTests"/>; this fixture
   /// answers a different question - does Unity serialize these arrays, and does a payload that came back
   /// from disk rebuild an equivalent query.
   /// </remarks>
   public sealed class SerializableGameplayTagQueryTests
   {
      private readonly List<string> names = new List<string> { "Bridge.A", "Bridge.B", "Bridge.C" };

      [SetUp]
      public void SetUp()
      {
         GameplayTagManager.ResetForTests();
         GameplayTagManager.RegisterDynamicTags(names);
      }

      [Test]
      public void UnityJson_PersistsTheFlatArrays_AndRebuildsAnEquivalentQuery()
      {
         GameplayTagQuery query = BuildNestedQuery();
         var bridge = new SerializableGameplayTagQuery();
         Assert.That(bridge.TrySetQuery(query), Is.True, "a well-formed query must be capturable");

         // Unity drives this callback before writing; call it explicitly so the test does not depend on
         // when the editor decides to serialize.
         ((ISerializationCallbackReceiver)bridge).OnBeforeSerialize();

         string json = EditorJsonUtility.ToJson(bridge, prettyPrint: false);
         Assert.That(json, Does.Contain("operators"), json);
         Assert.That(json, Does.Contain("childCounts"), json);
         Assert.That(json, Does.Contain("tagCounts"), json);
         Assert.That(json, Does.Contain("Bridge.A"), json);
         Assert.That(json, Does.Contain("Bridge.B"), json);

         var restored = new SerializableGameplayTagQuery();
         EditorJsonUtility.FromJsonOverwrite(json, restored);
         ((ISerializationCallbackReceiver)restored).OnAfterDeserialize();

         Assert.That(restored.IsEmpty, Is.False, "the payload must have survived the round-trip");
         AssertEquivalent(query, restored.Query);
      }

      [Test]
      public void InspectorEditedPayload_ReplacesTheRuntimeQuery()
      {
         GameplayTagQuery query = BuildNestedQuery();
         var bridge = new SerializableGameplayTagQuery();
         Assert.That(bridge.TrySetQuery(query), Is.True);

         // Simulate an inspector edit: the serialized arrays are replaced out from under the live query.
         Assert.That(
            GameplayTagQueryPersisted.TryCreate(
               GameplayTagQuery.BuildQueryAny(SingleTag("Bridge.C")), out GameplayTagQueryPersisted anyC),
            Is.True);

         bridge.LoadPersisted(anyC);
         ((ISerializationCallbackReceiver)bridge).OnBeforeSerialize();

         Assert.That(bridge.Query, Is.Not.Null);
         Assert.That(bridge.Query.Matches(Container("Bridge.C")), Is.True,
            "the edited payload must win over the previously assigned query");
         Assert.That(bridge.Query.Matches(Container("Bridge.A")), Is.False);
      }

      [Test]
      public void UnauthoredBridge_NeverMatches_AndSurvivesSerialization()
      {
         var bridge = new SerializableGameplayTagQuery();
         ((ISerializationCallbackReceiver)bridge).OnBeforeSerialize();

         Assert.That(bridge.IsEmpty, Is.True);
         Assert.That(bridge.Query, Is.Null);
         Assert.That(bridge.Matches(Container("Bridge.A")), Is.False);

         string json = EditorJsonUtility.ToJson(bridge, prettyPrint: false);
         var restored = new SerializableGameplayTagQuery();
         EditorJsonUtility.FromJsonOverwrite(json, restored);
         ((ISerializationCallbackReceiver)restored).OnAfterDeserialize();

         Assert.That(restored.IsEmpty, Is.True, "an empty payload must stay empty");
         Assert.That(restored.Query, Is.Null);
      }

      private GameplayTagQuery BuildNestedQuery()
      {
         return new GameplayTagQuery
         {
            RootExpression = GameplayTagQueryExpression.All(
               GameplayTagQueryExpression.MatchAll(Container("Bridge.A")),
               GameplayTagQueryExpression.Any(
                  GameplayTagQueryExpression.MatchAll(Container("Bridge.B")),
                  GameplayTagQueryExpression.MatchNone(Container("Bridge.C"))))
         };
      }

      private static void AssertEquivalent(GameplayTagQuery expected, GameplayTagQuery actual)
      {
         Assert.That(actual, Is.Not.Null);

         string[][] subjects =
         {
            new[] { "Bridge.A" },
            new[] { "Bridge.A", "Bridge.B" },
            new[] { "Bridge.A", "Bridge.C" },
            new[] { "Bridge.A", "Bridge.B", "Bridge.C" },
            new[] { "Bridge.B" },
            new string[0]
         };

         for (int i = 0; i < subjects.Length; i++)
         {
            GameplayTagContainer container = Container(subjects[i]);
            Assert.That(actual.Matches(container), Is.EqualTo(expected.Matches(container)),
               $"subject {i}: restored query must answer identically");
         }
      }

      private static GameplayTagContainer Container(params string[] tagNames)
      {
         var container = new GameplayTagContainer();
         for (int i = 0; i < tagNames.Length; i++)
            container.AddTag(GameplayTagManager.Request(tagNames[i]));

         return container;
      }

      private GameplayTagContainer SingleTag(string tagName) => Container(tagName);
   }
}
