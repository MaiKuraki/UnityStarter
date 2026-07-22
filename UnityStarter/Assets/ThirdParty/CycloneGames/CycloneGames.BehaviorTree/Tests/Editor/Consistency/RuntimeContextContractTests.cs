using System;
using System.Collections.Generic;
using System.Reflection;
using CycloneGames.BehaviorTree.Runtime.Components;
using CycloneGames.BehaviorTree.Runtime.Core;
using NUnit.Framework;
using UnityEngine;

namespace CycloneGames.BehaviorTree.Tests.Editor.Consistency
{
    public sealed class RuntimeContextContractTests
    {
        [Test]
        public void PublicCoreContextContracts_DoNotExposeUnityEngineObjectTypes()
        {
            Type[] contractTypes =
            {
                typeof(IRuntimeBTContext),
                typeof(RuntimeBTContext),
                typeof(RuntimeBehaviorTree),
                typeof(RuntimeBehaviorTreeBuilder),
            };

            var violations = new List<string>();
            for (int i = 0; i < contractTypes.Length; i++)
            {
                CollectUnityTypeViolations(contractTypes[i], violations);
            }

            Assert.That(violations, Is.Empty, string.Join(Environment.NewLine, violations));
        }

        [Test]
        public void RuntimeContext_ResolvesPlainOwnerAndGameObjectComponents()
        {
            var plainOwner = new OwnerToken();
            var context = new RuntimeBTContext(plainOwner);

            Assert.That(context.Owner, Is.SameAs(plainOwner));
            Assert.That(context.GetOwner<OwnerToken>(), Is.SameAs(plainOwner));
            Assert.That(context.GetOwner<BTRunnerComponent>(), Is.Null);

            var ownerGameObject = new GameObject("RuntimeContextContractTests");
            ownerGameObject.SetActive(false);
            try
            {
                var runner = ownerGameObject.AddComponent<BTRunnerComponent>();
                context.Owner = ownerGameObject;

                Assert.That(context.GetOwner<GameObject>(), Is.SameAs(ownerGameObject));
                Assert.That(context.GetOwner<BTRunnerComponent>(), Is.SameAs(runner));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(ownerGameObject);
            }
        }

        [Test]
        public void Runner_SetContextInjectsItsGameObjectOwner()
        {
            var ownerGameObject = new GameObject("RuntimeContextRunnerTests");
            ownerGameObject.SetActive(false);
            try
            {
                var runner = ownerGameObject.AddComponent<BTRunnerComponent>();
                var context = new RuntimeBTContext(new OwnerToken());

                runner.SetContext(context);

                Assert.That(context.Owner, Is.SameAs(ownerGameObject));
                Assert.That(context.GetOwner<BTRunnerComponent>(), Is.SameAs(runner));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(ownerGameObject);
            }
        }

        [Test]
        public void Builder_ForwardsPlainOwnerThroughGenericContract()
        {
            var owner = new OwnerToken();
            using RuntimeBehaviorTree tree = new RuntimeBehaviorTreeBuilder(owner)
                .Action(_ => RuntimeState.Success)
                .Build();

            Assert.That(tree.GetOwner<OwnerToken>(), Is.SameAs(owner));
            Assert.That(tree.Tick(), Is.EqualTo(RuntimeState.Success));
        }

        private static void CollectUnityTypeViolations(Type contractType, List<string> violations)
        {
            const BindingFlags PublicDeclared =
                BindingFlags.Public |
                BindingFlags.Instance |
                BindingFlags.Static |
                BindingFlags.DeclaredOnly;

            ConstructorInfo[] constructors = contractType.GetConstructors(PublicDeclared);
            for (int i = 0; i < constructors.Length; i++)
            {
                CollectParameterViolations(contractType, constructors[i], constructors[i].GetParameters(), violations);
            }

            MethodInfo[] methods = contractType.GetMethods(PublicDeclared);
            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo method = methods[i];
                if (ContainsUnityObjectType(method.ReturnType))
                {
                    violations.Add($"{contractType.FullName}.{method.Name} returns {method.ReturnType.FullName}.");
                }

                CollectParameterViolations(contractType, method, method.GetParameters(), violations);
            }

            PropertyInfo[] properties = contractType.GetProperties(PublicDeclared);
            for (int i = 0; i < properties.Length; i++)
            {
                PropertyInfo property = properties[i];
                if (ContainsUnityObjectType(property.PropertyType))
                {
                    violations.Add($"{contractType.FullName}.{property.Name} exposes {property.PropertyType.FullName}.");
                }
            }

            FieldInfo[] fields = contractType.GetFields(PublicDeclared);
            for (int i = 0; i < fields.Length; i++)
            {
                FieldInfo field = fields[i];
                if (ContainsUnityObjectType(field.FieldType))
                {
                    violations.Add($"{contractType.FullName}.{field.Name} exposes {field.FieldType.FullName}.");
                }
            }

            EventInfo[] events = contractType.GetEvents(PublicDeclared);
            for (int i = 0; i < events.Length; i++)
            {
                EventInfo eventInfo = events[i];
                if (ContainsUnityObjectType(eventInfo.EventHandlerType))
                {
                    violations.Add($"{contractType.FullName}.{eventInfo.Name} exposes {eventInfo.EventHandlerType?.FullName}.");
                }
            }
        }

        private static void CollectParameterViolations(
            Type contractType,
            MethodBase member,
            ParameterInfo[] parameters,
            List<string> violations)
        {
            for (int i = 0; i < parameters.Length; i++)
            {
                ParameterInfo parameter = parameters[i];
                if (ContainsUnityObjectType(parameter.ParameterType))
                {
                    violations.Add(
                        $"{contractType.FullName}.{member.Name} parameter '{parameter.Name}' exposes {parameter.ParameterType.FullName}.");
                }
            }
        }

        private static bool ContainsUnityObjectType(Type type)
        {
            if (type == null)
            {
                return false;
            }

            if (type.IsByRef || type.IsPointer || type.IsArray)
            {
                return ContainsUnityObjectType(type.GetElementType());
            }

            if (typeof(UnityEngine.Object).IsAssignableFrom(type))
            {
                return true;
            }

            if (!type.IsGenericType)
            {
                return false;
            }

            Type[] arguments = type.GetGenericArguments();
            for (int i = 0; i < arguments.Length; i++)
            {
                if (ContainsUnityObjectType(arguments[i]))
                {
                    return true;
                }
            }

            return false;
        }

        private sealed class OwnerToken
        {
        }
    }
}
