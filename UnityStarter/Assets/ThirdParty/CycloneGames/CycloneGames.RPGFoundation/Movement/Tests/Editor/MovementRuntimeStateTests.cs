using System.Reflection;

using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

using CycloneGames.RPGFoundation.Movement.Core;
using CycloneGames.RPGFoundation.Movement.Runtime;
using CycloneGames.RPGFoundation.Movement.Runtime.Movement2D;
using CycloneGames.RPGFoundation.Movement.Runtime.Movement2D.States;
using Movement3DStateBase = CycloneGames.RPGFoundation.Movement.Runtime.States.MovementStateBase;
using Movement3DRollState = CycloneGames.RPGFoundation.Movement.Runtime.States.RollState;

namespace CycloneGames.RPGFoundation.Movement.Tests.Editor
{
    public sealed class MovementRuntimeStateTests
    {
        [Test]
        public void RunState2D_TopDownOutputsFrameDisplacementAndCurrentVelocity()
        {
            MovementConfig2D config = ScriptableObject.CreateInstance<MovementConfig2D>();
            SetPrivateField(config, "movementType", MovementType2D.TopDown);

            try
            {
                var context = new MovementContext2D
                {
                    Config = config,
                    DeltaTime = 0.02f,
                    InputDirection = new float2(0.5f, -0.25f)
                };

                var state = new RunState2D();
                state.OnUpdate(ref context, out float2 displacement);

                var expectedVelocity = new float2(config.RunSpeed * 0.5f, config.RunSpeed * -0.25f);

                Assert.That(math.distance(context.CurrentVelocity, expectedVelocity), Is.LessThan(0.0001f));
                Assert.That(math.distance(displacement, expectedVelocity * context.DeltaTime), Is.LessThan(0.0001f));
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void RollState2D_KeepsTimerAndDirectionPerMovementContext()
        {
            MovementConfig2D config = ScriptableObject.CreateInstance<MovementConfig2D>();

            try
            {
                var state = StatePool<MovementStateBase2D>.GetState<RollState2D>();

                var first = new MovementContext2D
                {
                    Config = config,
                    DeltaTime = 0.1f,
                    InputDirection = new float2(1f, 0f)
                };
                var second = new MovementContext2D
                {
                    Config = config,
                    DeltaTime = 0.25f,
                    InputDirection = new float2(0f, 1f)
                };

                state.OnEnter(ref first);
                state.OnUpdate(ref first, out _);
                state.OnEnter(ref second);
                state.OnUpdate(ref second, out _);

                Assert.That(first.RollTimer, Is.EqualTo(0.1f).Within(0.0001f));
                Assert.That(second.RollTimer, Is.EqualTo(0.25f).Within(0.0001f));
                Assert.That(math.distance(first.RollDirection, new float2(1f, 0f)), Is.LessThan(0.0001f));
                Assert.That(math.distance(second.RollDirection, new float2(0f, 1f)), Is.LessThan(0.0001f));
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void RollState3D_KeepsTimerAndDirectionPerMovementContext()
        {
            MovementConfig config = ScriptableObject.CreateInstance<MovementConfig>();
            var firstObject = new GameObject("First Roll Context");
            var secondObject = new GameObject("Second Roll Context");

            try
            {
                var state = StatePool<Movement3DStateBase>.GetState<Movement3DRollState>();

                var first = new MovementContext
                {
                    Config = config,
                    DeltaTime = 0.1f,
                    Transform = firstObject.transform,
                    WorldUp = new float3(0f, 1f, 0f),
                    InputDirection = new float3(1f, 0f, 0f)
                };
                var second = new MovementContext
                {
                    Config = config,
                    DeltaTime = 0.25f,
                    Transform = secondObject.transform,
                    WorldUp = new float3(0f, 1f, 0f),
                    InputDirection = new float3(0f, 0f, 1f)
                };

                state.OnEnter(ref first);
                state.OnUpdate(ref first, out _);
                state.OnEnter(ref second);
                state.OnUpdate(ref second, out _);

                Assert.That(first.RollTimer, Is.EqualTo(0.1f).Within(0.0001f));
                Assert.That(second.RollTimer, Is.EqualTo(0.25f).Within(0.0001f));
                Assert.That(math.distance(first.RollDirection, new float3(1f, 0f, 0f)), Is.LessThan(0.0001f));
                Assert.That(math.distance(second.RollDirection, new float3(0f, 0f, 1f)), Is.LessThan(0.0001f));
            }
            finally
            {
                Object.DestroyImmediate(firstObject);
                Object.DestroyImmediate(secondObject);
                Object.DestroyImmediate(config);
            }
        }

        private static void SetPrivateField<TValue>(Object target, string fieldName, TValue value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Field '{fieldName}' was not found on {target.GetType().Name}.");
            field.SetValue(target, value);
        }
    }
}
