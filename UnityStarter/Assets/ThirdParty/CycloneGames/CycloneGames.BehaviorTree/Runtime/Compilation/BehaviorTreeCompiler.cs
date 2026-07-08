using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using CycloneGames.Hash.Core;
using CycloneGames.BehaviorTree.Runtime.Conditions;
using CycloneGames.BehaviorTree.Runtime.Conditions.BlackBoards;
using CycloneGames.BehaviorTree.Runtime.Core;
using CycloneGames.BehaviorTree.Runtime.Core.Nodes;
using CycloneGames.BehaviorTree.Runtime.Core.Nodes.Actions;
using CycloneGames.BehaviorTree.Runtime.Core.Nodes.Actions.State;
using CycloneGames.BehaviorTree.Runtime.Core.Nodes.Compositors;
using CycloneGames.BehaviorTree.Runtime.Core.Nodes.Conditions;
using CycloneGames.BehaviorTree.Runtime.Core.Nodes.Decorators;
using CycloneGames.BehaviorTree.Runtime.Nodes;
using CycloneGames.BehaviorTree.Runtime.Nodes.Actions;
using CycloneGames.BehaviorTree.Runtime.Nodes.Actions.BlackBoards;
using CycloneGames.BehaviorTree.Runtime.Nodes.Actions.State;
using CycloneGames.BehaviorTree.Runtime.Nodes.Compositors;
using CycloneGames.BehaviorTree.Runtime.Nodes.Decorators;
using UnityEngine;

namespace CycloneGames.BehaviorTree.Runtime.Compilation
{
    public static class BehaviorTreeCompiler
    {
        public static RuntimeBehaviorTree Compile(BehaviorTree source, RuntimeBTContext context = null)
        {
            return Compile(source, context, BehaviorTreeCompileOptions.Default);
        }

        public static RuntimeBehaviorTree Compile(
            BehaviorTree source,
            RuntimeBTContext context,
            BehaviorTreeCompileOptions options)
        {
            if (source == null)
            {
                throw new BehaviorTreeCompileException("Behavior tree asset is null.");
            }

            BehaviorTreeCompileArtifact artifact = Analyze(source, options);
            if (!artifact.IsValid)
            {
                throw new BehaviorTreeCompileException(FormatErrors(source, artifact.Errors));
            }

            context ??= new RuntimeBTContext();

            var blackboard = new RuntimeBlackboard
            {
                Context = context
            };

            RuntimeNode runtimeRoot;
            try
            {
                runtimeRoot = artifact.EmitRuntimeRoot();
            }
            catch (Exception exception)
            {
                throw new BehaviorTreeCompileException(
                    $"Behavior tree runtime graph creation failed for '{source.name}': {exception.Message}",
                    exception);
            }

            if (runtimeRoot == null)
            {
                throw new BehaviorTreeCompileException($"Root node {source.Root.GetType().Name} returned null runtime node.");
            }

            return new RuntimeBehaviorTree(runtimeRoot, blackboard, context);
        }

        public static BehaviorTreeCompileArtifact Analyze(
            BehaviorTree source,
            BehaviorTreeCompileOptions options = null)
        {
            options ??= BehaviorTreeCompileOptions.Default;
            if (source == null)
            {
                return BehaviorTreeCompileArtifact.Invalid(null, 0UL, 0, new List<string> { "Behavior tree asset is null." });
            }

            ulong fingerprint = BehaviorTreeCompileFingerprint.Compute(source, out int nodeCount);
            BehaviorTreeCompileCache cache = options.Cache;
            BehaviorTreeNodeEmitterRegistry emitters = options.Emitters ?? BehaviorTreeNodeEmitterRegistry.BuiltIn;
            if (options.UseCache && cache != null && cache.TryGet(source, fingerprint, emitters, out BehaviorTreeCompileArtifact cached))
            {
                return cached;
            }

            List<string> errors = options.ValidateGraph ? Validate(source) : new List<string>(0);
            var artifact = new BehaviorTreeCompileArtifact(source, fingerprint, nodeCount, emitters, errors);
            if (options.UseCache && cache != null && artifact.IsValid)
            {
                cache.Store(artifact);
            }

            return artifact;
        }

        public static List<string> Validate(BehaviorTree source)
        {
            var errors = new List<string>(4);
            if (source == null)
            {
                errors.Add("Behavior tree asset is null.");
                return errors;
            }

            if (source.Root == null)
            {
                errors.Add("Root is null.");
                return errors;
            }

            var visited = new HashSet<BTNode>();
            var visiting = new HashSet<BTNode>();
            var guids = new HashSet<string>();
            ValidateNode(source.Root, "Root", visited, visiting, guids, errors);
            return errors;
        }

        private static void ValidateNode(
            BTNode node,
            string path,
            HashSet<BTNode> visited,
            HashSet<BTNode> visiting,
            HashSet<string> guids,
            List<string> errors)
        {
            if (node == null)
            {
                errors.Add($"{path}: null node reference.");
                return;
            }

            if (!string.IsNullOrEmpty(node.GUID) && !guids.Add(node.GUID))
            {
                errors.Add($"{path}: duplicate node GUID '{node.GUID}'.");
            }

            if (visited.Contains(node))
            {
                return;
            }

            if (!visiting.Add(node))
            {
                errors.Add($"{path}: cycle detected.");
                return;
            }

            ValidateChildren(node, path, visited, visiting, guids, errors);
            visiting.Remove(node);
            visited.Add(node);
        }

        private static void ValidateChildren(
            BTNode node,
            string path,
            HashSet<BTNode> visited,
            HashSet<BTNode> visiting,
            HashSet<string> guids,
            List<string> errors)
        {
            if (node is BTRootNode root)
            {
                if (root.Child == null)
                {
                    errors.Add($"{path}: root child is null.");
                    return;
                }

                ValidateNode(root.Child, $"{path}/{root.Child.GetType().Name}", visited, visiting, guids, errors);
                return;
            }

            if (node is DecoratorNode decorator)
            {
                if (node is SubTreeNode subTree)
                {
                    ValidateSubTreeNode(subTree, path, visited, visiting, guids, errors);
                    return;
                }

                if (decorator.Child == null)
                {
                    errors.Add($"{path}: decorator child is null.");
                    return;
                }

                ValidateNode(decorator.Child, $"{path}/{decorator.Child.GetType().Name}", visited, visiting, guids, errors);
                return;
            }

            if (node is CompositeNode composite)
            {
                for (int i = 0; i < composite.Children.Count; i++)
                {
                    BTNode child = composite.Children[i];
                    if (child == null)
                    {
                        errors.Add($"{path}: child[{i}] is null.");
                        continue;
                    }

                    ValidateNode(child, $"{path}/{child.GetType().Name}[{i}]", visited, visiting, guids, errors);
                }
            }
        }

        private static void ValidateSubTreeNode(
            SubTreeNode subTree,
            string path,
            HashSet<BTNode> visited,
            HashSet<BTNode> visiting,
            HashSet<string> guids,
            List<string> errors)
        {
            if (subTree.Child != null)
            {
                ValidateNode(subTree.Child, $"{path}/{subTree.Child.GetType().Name}", visited, visiting, guids, errors);
                return;
            }

            BehaviorTree subTreeAsset = subTree.SubTreeAsset;
            if (subTreeAsset == null || subTreeAsset.Root == null)
            {
                errors.Add($"{path}: subtree node has neither child nor subtree asset root.");
                return;
            }

            ValidateNode(subTreeAsset.Root, $"{path}/SubTreeAsset/{subTreeAsset.Root.GetType().Name}", visited, visiting, guids, errors);
        }

        private static string FormatErrors(BehaviorTree source, IReadOnlyList<string> errors)
        {
            var builder = new StringBuilder(128 + errors.Count * 48);
            builder.Append("Behavior tree compile failed for '");
            builder.Append(source != null ? source.name : "<null>");
            builder.AppendLine("':");
            for (int i = 0; i < errors.Count; i++)
            {
                builder.Append("- ");
                builder.AppendLine(errors[i]);
            }

            return builder.ToString();
        }
    }

    public sealed class BehaviorTreeCompileOptions
    {
        public static BehaviorTreeCompileOptions Default => new BehaviorTreeCompileOptions();

        public bool ValidateGraph { get; set; } = true;
        public bool UseCache { get; set; } = true;
        public BehaviorTreeCompileCache Cache { get; set; } = BehaviorTreeCompileCache.Shared;
        public BehaviorTreeNodeEmitterRegistry Emitters { get; set; } = BehaviorTreeNodeEmitterRegistry.BuiltIn;
    }

    public sealed class BehaviorTreeCompileCache
    {
        public static readonly BehaviorTreeCompileCache Shared = new BehaviorTreeCompileCache();

        private readonly Dictionary<int, CacheEntry> _entries = new Dictionary<int, CacheEntry>(32);

        public bool TryGet(
            BehaviorTree source,
            ulong fingerprint,
            BehaviorTreeNodeEmitterRegistry emitters,
            out BehaviorTreeCompileArtifact artifact)
        {
            artifact = null;
            if (source == null)
            {
                return false;
            }

            int key = source.GetInstanceID();
            lock (_entries)
            {
                if (_entries.TryGetValue(key, out CacheEntry entry)
                    && entry.Fingerprint == fingerprint
                    && ReferenceEquals(entry.Emitters, emitters))
                {
                    artifact = entry.Artifact;
                    return true;
                }
            }

            return false;
        }

        public void Store(BehaviorTreeCompileArtifact artifact)
        {
            if (artifact == null || artifact.Source == null || !artifact.IsValid)
            {
                return;
            }

            int key = artifact.Source.GetInstanceID();
            lock (_entries)
            {
                _entries[key] = new CacheEntry(artifact.Fingerprint, artifact.Emitters, artifact);
            }
        }

        public void Clear()
        {
            lock (_entries)
            {
                _entries.Clear();
            }
        }

        private readonly struct CacheEntry
        {
            public readonly ulong Fingerprint;
            public readonly BehaviorTreeNodeEmitterRegistry Emitters;
            public readonly BehaviorTreeCompileArtifact Artifact;

            public CacheEntry(
                ulong fingerprint,
                BehaviorTreeNodeEmitterRegistry emitters,
                BehaviorTreeCompileArtifact artifact)
            {
                Fingerprint = fingerprint;
                Emitters = emitters;
                Artifact = artifact;
            }
        }
    }

    public sealed class BehaviorTreeCompileArtifact
    {
        private readonly List<string> _errors;
        private readonly IReadOnlyList<string> _readOnlyErrors;

        public BehaviorTreeCompileArtifact(
            BehaviorTree source,
            ulong fingerprint,
            int nodeCount,
            BehaviorTreeNodeEmitterRegistry emitters,
            List<string> errors)
        {
            Source = source;
            Fingerprint = fingerprint;
            NodeCount = nodeCount;
            Emitters = emitters ?? BehaviorTreeNodeEmitterRegistry.BuiltIn;
            _errors = errors ?? new List<string>(0);
            _readOnlyErrors = _errors.AsReadOnly();
        }

        public BehaviorTree Source { get; }
        public ulong Fingerprint { get; }
        public int NodeCount { get; }
        public BehaviorTreeNodeEmitterRegistry Emitters { get; }
        public bool IsValid => _errors.Count == 0;
        public IReadOnlyList<string> Errors => _readOnlyErrors;

        public RuntimeNode EmitRuntimeRoot()
        {
            if (!IsValid)
            {
                throw new BehaviorTreeCompileException("Cannot emit an invalid behavior tree compile artifact.");
            }

            if (Source?.Root == null)
            {
                throw new BehaviorTreeCompileException("Cannot emit a behavior tree without a root node.");
            }

            var context = new BehaviorTreeEmitContext(Emitters);
            return context.EmitRequired(Source.Root, "root node");
        }

        public static BehaviorTreeCompileArtifact Invalid(
            BehaviorTree source,
            ulong fingerprint,
            int nodeCount,
            List<string> errors)
        {
            return new BehaviorTreeCompileArtifact(
                source,
                fingerprint,
                nodeCount,
                BehaviorTreeNodeEmitterRegistry.BuiltIn,
                errors);
        }
    }

    internal static class BehaviorTreeCompileFingerprint
    {
        public static ulong Compute(BehaviorTree source, out int nodeCount)
        {
            nodeCount = 0;
            ulong hash = Fnv1a64.OffsetBasis;
            AppendNode(ref hash, source?.Root, new HashSet<BTNode>(), ref nodeCount);
            return hash;
        }

        private static void AppendNode(ref ulong hash, BTNode node, HashSet<BTNode> visited, ref int nodeCount)
        {
            if (node == null)
            {
                AppendByte(ref hash, 0);
                return;
            }

            if (!visited.Add(node))
            {
                AppendByte(ref hash, 1);
                return;
            }

            nodeCount++;
            Type type = node.GetType();
            AppendString(ref hash, type.FullName);
            AppendSerializedFields(ref hash, node, type);

            if (node is BTRootNode root)
            {
                AppendNode(ref hash, root.Child, visited, ref nodeCount);
                return;
            }

            if (node is SubTreeNode subTree)
            {
                if (subTree.Child != null)
                {
                    AppendNode(ref hash, subTree.Child, visited, ref nodeCount);
                }
                else
                {
                    AppendNode(ref hash, subTree.SubTreeAsset?.Root, visited, ref nodeCount);
                }

                return;
            }

            if (node is DecoratorNode decorator)
            {
                AppendNode(ref hash, decorator.Child, visited, ref nodeCount);
                return;
            }

            if (node is CompositeNode composite)
            {
                int count = composite.Children != null ? composite.Children.Count : 0;
                AppendInt32(ref hash, count);
                for (int i = 0; i < count; i++)
                {
                    AppendNode(ref hash, composite.Children[i], visited, ref nodeCount);
                }
            }
        }

        private static void AppendSerializedFields(ref ulong hash, object owner, Type type)
        {
            while (type != null && type != typeof(ScriptableObject) && type != typeof(UnityEngine.Object))
            {
                FieldInfo[] fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                Array.Sort(fields, (a, b) => string.CompareOrdinal(a.Name, b.Name));
                for (int i = 0; i < fields.Length; i++)
                {
                    FieldInfo field = fields[i];
                    if (field.IsStatic
                        || field.IsNotSerialized
                        || IsEditorOnlyAuthoringField(field)
                        || !IsSerializedField(field)
                        || IsAuthoringReference(field.FieldType))
                    {
                        continue;
                    }

                    AppendString(ref hash, field.Name);
                    AppendValue(ref hash, field.GetValue(owner));
                }

                type = type.BaseType;
            }
        }

        private static bool IsEditorOnlyAuthoringField(FieldInfo field)
        {
            return field.DeclaringType == typeof(BTNode)
                   && (string.Equals(field.Name, "_position", StringComparison.Ordinal)
                       || string.Equals(field.Name, "GUID", StringComparison.Ordinal));
        }

        private static bool IsSerializedField(FieldInfo field)
        {
            return field.IsPublic || Attribute.IsDefined(field, typeof(SerializeField));
        }

        private static bool IsAuthoringReference(Type type)
        {
            return typeof(BTNode).IsAssignableFrom(type)
                || typeof(BehaviorTree).IsAssignableFrom(type)
                || typeof(IList<BTNode>).IsAssignableFrom(type);
        }

        private static void AppendValue(ref ulong hash, object value)
        {
            if (value == null)
            {
                AppendByte(ref hash, 0);
                return;
            }

            Type type = value.GetType();
            if (type.IsEnum)
            {
                AppendInt64(ref hash, Convert.ToInt64(value));
                return;
            }

            switch (value)
            {
                case bool boolValue:
                    AppendByte(ref hash, boolValue ? (byte)1 : (byte)0);
                    return;
                case byte byteValue:
                    AppendByte(ref hash, byteValue);
                    return;
                case int intValue:
                    AppendInt32(ref hash, intValue);
                    return;
                case uint uintValue:
                    AppendUInt32(ref hash, uintValue);
                    return;
                case long longValue:
                    AppendInt64(ref hash, longValue);
                    return;
                case ulong ulongValue:
                    AppendUInt64(ref hash, ulongValue);
                    return;
                case float floatValue:
                    AppendSingle(ref hash, floatValue);
                    return;
                case double doubleValue:
                    AppendInt64(ref hash, BitConverter.DoubleToInt64Bits(doubleValue));
                    return;
                case string stringValue:
                    AppendString(ref hash, stringValue);
                    return;
                case Vector2 vector2:
                    AppendSingle(ref hash, vector2.x);
                    AppendSingle(ref hash, vector2.y);
                    return;
                case Vector3 vector3:
                    AppendSingle(ref hash, vector3.x);
                    AppendSingle(ref hash, vector3.y);
                    AppendSingle(ref hash, vector3.z);
                    return;
                case IList list:
                    AppendInt32(ref hash, list.Count);
                    for (int i = 0; i < list.Count; i++)
                    {
                        AppendValue(ref hash, list[i]);
                    }
                    return;
            }

            AppendString(ref hash, value.ToString());
        }

        private static void AppendString(ref ulong hash, string value)
        {
            if (value == null)
            {
                AppendInt32(ref hash, -1);
                return;
            }

            AppendInt32(ref hash, value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                AppendByte(ref hash, (byte)c);
                AppendByte(ref hash, (byte)(c >> 8));
            }
        }

        private static void AppendSingle(ref ulong hash, float value)
        {
            var union = new FloatIntUnion
            {
                FloatValue = value
            };
            AppendInt32(ref hash, union.IntValue);
        }

        private static void AppendInt32(ref ulong hash, int value)
        {
            AppendUInt32(ref hash, unchecked((uint)value));
        }

        private static void AppendUInt32(ref ulong hash, uint value)
        {
            AppendByte(ref hash, (byte)value);
            AppendByte(ref hash, (byte)(value >> 8));
            AppendByte(ref hash, (byte)(value >> 16));
            AppendByte(ref hash, (byte)(value >> 24));
        }

        private static void AppendInt64(ref ulong hash, long value)
        {
            AppendUInt64(ref hash, unchecked((ulong)value));
        }

        private static void AppendUInt64(ref ulong hash, ulong value)
        {
            AppendUInt32(ref hash, (uint)value);
            AppendUInt32(ref hash, (uint)(value >> 32));
        }

        private static void AppendByte(ref ulong hash, byte value)
        {
            hash ^= value;
            hash *= Fnv1a64.Prime;
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Explicit)]
        private struct FloatIntUnion
        {
            [System.Runtime.InteropServices.FieldOffset(0)] public int IntValue;
            [System.Runtime.InteropServices.FieldOffset(0)] public float FloatValue;
        }
    }

    public readonly struct BehaviorTreeNodeDescriptor
    {
        public BehaviorTreeNodeDescriptor(BTNode source, int childCount)
        {
            Source = source;
            AuthoringType = source != null ? source.GetType() : null;
            Guid = source != null ? source.GUID : null;
            ChildCount = childCount;
        }

        public BTNode Source { get; }
        public Type AuthoringType { get; }
        public string Guid { get; }
        public int ChildCount { get; }
    }

    public delegate RuntimeNode BehaviorTreeNodeEmitter(
        BehaviorTreeNodeDescriptor descriptor,
        BehaviorTreeEmitContext context);

    public sealed class BehaviorTreeNodeEmitterRegistry
    {
        private readonly Dictionary<Type, BehaviorTreeNodeEmitter> _emitters = new Dictionary<Type, BehaviorTreeNodeEmitter>(64);
        private readonly BehaviorTreeNodeEmitterRegistry _fallback;
        private bool _isReadOnly;

        public static BehaviorTreeNodeEmitterRegistry BuiltIn { get; } = CreateBuiltIn();

        public BehaviorTreeNodeEmitterRegistry(BehaviorTreeNodeEmitterRegistry fallback = null)
        {
            _fallback = fallback;
        }

        public static BehaviorTreeNodeEmitterRegistry CreateWithBuiltInFallback()
        {
            return new BehaviorTreeNodeEmitterRegistry(BuiltIn);
        }

        public void Register<TNode>(Func<TNode, BehaviorTreeEmitContext, RuntimeNode> emitter) where TNode : BTNode
        {
            if (_isReadOnly)
            {
                throw new InvalidOperationException("Cannot modify a read-only behavior tree node emitter registry.");
            }

            RegisterCore(emitter);
        }

        private void RegisterCore<TNode>(Func<TNode, BehaviorTreeEmitContext, RuntimeNode> emitter) where TNode : BTNode
        {
            if (emitter == null)
            {
                throw new ArgumentNullException(nameof(emitter));
            }

            _emitters[typeof(TNode)] = (descriptor, context) => emitter((TNode)descriptor.Source, context);
        }

        public bool TryEmit(
            BehaviorTreeNodeDescriptor descriptor,
            BehaviorTreeEmitContext context,
            out RuntimeNode runtimeNode)
        {
            runtimeNode = null;
            if (descriptor.Source == null || descriptor.AuthoringType == null)
            {
                return false;
            }

            Type type = descriptor.AuthoringType;
            while (type != null && type != typeof(ScriptableObject))
            {
                if (_emitters.TryGetValue(type, out BehaviorTreeNodeEmitter emitter))
                {
                    runtimeNode = emitter(descriptor, context);
                    return true;
                }

                type = type.BaseType;
            }

            if (_fallback != null)
            {
                return _fallback.TryEmit(descriptor, context, out runtimeNode);
            }

            return false;
        }

        private static BehaviorTreeNodeEmitterRegistry CreateBuiltIn()
        {
            var registry = new BehaviorTreeNodeEmitterRegistry();

            registry.RegisterCore<BTRootNode>(EmitRoot);

            registry.RegisterCore<DebugLogNode>((source, context) =>
                context.WithGuid(source, new RuntimeDebugLogNode
                {
                    Message = AuthoringField.Read<string>(source, "_message")
                }));

            registry.RegisterCore<WaitNode>((source, context) =>
                context.WithGuid(source, new RuntimeWaitNode
                {
                    Duration = source.Duration,
                    UseUnscaledTime = source.UseUnscaledTime,
                    UseRandomRange = source.UseRandomBetweenTwoConstants,
                    RangeMin = source.Range.x,
                    RangeMax = source.Range.y
                }));

            registry.RegisterCore<MessagePassNode>((source, context) =>
                context.WithGuid(source, new RuntimeMessagePassNode
                {
                    KeyHash = HashKey(AuthoringField.Read<string>(source, "_key")),
                    Message = AuthoringField.Read<string>(source, "_message")
                }));

            registry.RegisterCore<MessageRemoveNode>((source, context) =>
                context.WithGuid(source, new RuntimeMessageRemoveNode
                {
                    KeyHash = HashKey(AuthoringField.Read<string>(source, "_key"))
                }));

            registry.RegisterCore<BTChangeNode>((source, context) =>
                context.WithGuid(source, new RuntimeBTChangeNode
                {
                    StateId = AuthoringField.Read<string>(source, "_stateId")
                }));

            registry.RegisterCore<OnOffNode>((source, context) =>
                context.WithGuid(source, new RuntimeOnOffNode
                {
                    IsOn = AuthoringField.Read<bool>(source, "_on")
                }));

            registry.RegisterCore<RandomChanceNode>((source, context) =>
                context.WithGuid(source, new RuntimeRandomChanceNode(
                    AuthoringField.Read<float>(source, "_chance"),
                    AuthoringField.Read<float>(source, "_outOf"),
                    (uint)AuthoringField.Read<int>(source, "_seed"))));

            registry.RegisterCore<MessageReceiveNode>((source, context) =>
                context.WithGuid(source, new RuntimeMessageReceiveNode
                {
                    KeyHash = HashKey(AuthoringField.Read<string>(source, "_key")),
                    ExpectedMessage = AuthoringField.Read<string>(source, "_message")
                }));

            registry.RegisterCore<SequencerNode>((source, context) => context.EmitComposite(source, new RuntimeSequencer()));
            registry.RegisterCore<SequenceWithMemoryNode>((source, context) => context.EmitComposite(source, new RuntimeSequenceWithMemory()));
            registry.RegisterCore<SelectorNode>((source, context) => context.EmitComposite(source, new RuntimeSelector()));
            registry.RegisterCore<ReactiveSequenceNode>((source, context) => context.EmitComposite(source, new RuntimeReactiveSequence()));
            registry.RegisterCore<ReactiveFallbackNode>((source, context) => context.EmitComposite(source, new RuntimeReactiveFallback()));
            registry.RegisterCore<IfThenElseNode>((source, context) => context.EmitComposite(source, new RuntimeIfThenElseNode()));
            registry.RegisterCore<WhileDoElseNode>((source, context) => context.EmitComposite(source, new RuntimeWhileDoElseNode()));

            registry.RegisterCore<SelectorRandomNode>((source, context) =>
                context.EmitComposite(source, new RuntimeSelectorRandom((uint)AuthoringField.Read<int>(source, "_seed"))));

            registry.RegisterCore<ParallelNode>((source, context) =>
            {
                var runtime = new RuntimeParallelNode
                {
                    Mode = (RuntimeParallelMode)source.ModeValue
                };
                return context.EmitComposite(source, runtime);
            });

            registry.RegisterCore<SimpleParallelNode>((source, context) =>
                context.EmitComposite(source, new RuntimeParallelNode
                {
                    Mode = RuntimeParallelMode.Default
                }));

            registry.RegisterCore<ParallelAllNode>((source, context) =>
                context.EmitComposite(source, new RuntimeParallelAllNode
                {
                    SuccessThreshold = AuthoringField.Read<int>(source, "_successThreshold"),
                    FailureThreshold = AuthoringField.Read<int>(source, "_failureThreshold")
                }));

            registry.RegisterCore<ProbabilityBranch>((source, context) =>
            {
                var runtime = new RuntimeProbabilityBranch();
                List<float> probabilities = AuthoringField.Read<List<float>>(source, "_probabilities");
                runtime.SetWeights(probabilities != null ? probabilities.ToArray() : Array.Empty<float>());
                return context.EmitComposite(source, runtime);
            });

            registry.RegisterCore<SwitchNode>((source, context) =>
                context.EmitComposite(source, new RuntimeSwitchNode
                {
                    VariableKeyHash = HashKey(AuthoringField.Read<string>(source, "_variableKey"))
                }));

            registry.RegisterCore<UtilitySelectorNode>((source, context) =>
            {
                var runtime = new RuntimeUtilitySelector();
                List<string> scoreKeys = AuthoringField.Read<List<string>>(source, "_scoreKeys");
                int count = scoreKeys != null ? scoreKeys.Count : 0;
                var hashes = new int[count];
                for (int i = 0; i < count; i++)
                {
                    hashes[i] = HashKey(scoreKeys[i]);
                }

                runtime.SetScoreKeys(hashes);
                return context.EmitComposite(source, runtime);
            });

            registry.RegisterCore<BlackBoardNode>((source, context) =>
                context.EmitDecorator(source, new RuntimeBlackboardNode()));

            registry.RegisterCore<InvertNode>((source, context) =>
                context.EmitDecorator(source, new RuntimeInverterNode()));

            registry.RegisterCore<SucceederNode>((source, context) =>
                context.EmitDecorator(source, new RuntimeSucceederNode()));

            registry.RegisterCore<ForceFailureNode>((source, context) =>
                context.EmitDecorator(source, new RuntimeForceFailureNode()));

            registry.RegisterCore<RunOnceNode>((source, context) =>
                context.EmitDecorator(source, new RuntimeRunOnceNode()));

            registry.RegisterCore<KeepRunningUntilFailureNode>((source, context) =>
                context.EmitDecorator(source, new RuntimeKeepRunningUntilFailureNode()));

            registry.RegisterCore<CoolDownNode>((source, context) =>
                context.EmitDecorator(source, new RuntimeCoolDownNode
                {
                    CoolDown = AuthoringField.Read<float>(source, "_coolDown"),
                    ResetOnSuccess = AuthoringField.Read<bool>(source, "_resetOnSuccess")
                }));

            registry.RegisterCore<DelayNode>((source, context) =>
                context.EmitDecorator(source, new RuntimeDelayNode
                {
                    DelaySeconds = AuthoringField.Read<float>(source, "_delaySeconds"),
                    UseUnscaledTime = AuthoringField.Read<bool>(source, "_useUnscaledTime")
                }));

            registry.RegisterCore<RepeatNode>((source, context) =>
            {
                Vector2 range = AuthoringField.Read<Vector2>(source, "_randomRepeatCountRange");
                return context.EmitDecorator(source, new RuntimeRepeatNode
                {
                    RepeatForever = AuthoringField.Read<bool>(source, "_repeatForever"),
                    RepeatCount = AuthoringField.Read<int>(source, "_repeatCount"),
                    UseRandomRepeatCount = AuthoringField.Read<bool>(source, "_useRandomRepeatCount"),
                    RandomRangeMin = (int)range.x,
                    RandomRangeMax = (int)range.y
                });
            });

            registry.RegisterCore<RetryNode>((source, context) =>
                context.EmitDecorator(source, new RuntimeRetryNode
                {
                    MaxAttempts = AuthoringField.Read<int>(source, "_maxAttempts")
                }));

            registry.RegisterCore<ServiceNode>((source, context) =>
                context.EmitDecorator(source, new RuntimeServiceNode
                {
                    Interval = AuthoringField.Read<float>(source, "_interval"),
                    RandomDeviation = AuthoringField.Read<float>(source, "_randomDeviation"),
                    UseUnscaledTime = AuthoringField.Read<bool>(source, "_useUnscaledTime")
                }));

            registry.RegisterCore<TimeoutNode>((source, context) =>
                context.EmitDecorator(source, new RuntimeTimeoutNode
                {
                    TimeoutSeconds = AuthoringField.Read<float>(source, "_timeoutSeconds"),
                    UseUnscaledTime = AuthoringField.Read<bool>(source, "_useUnscaledTime")
                }));

            registry.RegisterCore<WaitSuccessNode>((source, context) =>
            {
                Vector2 range = AuthoringField.Read<Vector2>(source, "_waitTimeRange");
                return context.EmitDecorator(source, new RuntimeWaitSuccessNode
                {
                    WaitTime = AuthoringField.Read<float>(source, "_waitTime"),
                    UseRandomRange = AuthoringField.Read<bool>(source, "_useRandomBetweenTwoConstants"),
                    RangeMin = range.x,
                    RangeMax = range.y,
                    UseUnscaledTime = AuthoringField.Read<bool>(source, "_useUnscaledTime")
                });
            });

            registry.RegisterCore<BBComparisonNode>((source, context) =>
                context.EmitDecorator(source, new RuntimeBBComparisonNode
                {
                    KeyHash = HashKey(AuthoringField.Read<string>(source, "_key")),
                    Operator = AuthoringField.Read<BBComparisonOp>(source, "_operator"),
                    ValueType = AuthoringField.Read<BBValueType>(source, "_valueType"),
                    RefInt = AuthoringField.Read<int>(source, "_refInt"),
                    RefFloat = AuthoringField.Read<float>(source, "_refFloat"),
                    RefBool = AuthoringField.Read<bool>(source, "_refBool"),
                    RefKeyHash = HashKey(AuthoringField.Read<string>(source, "_refKey")),
                    FloatEpsilon = AuthoringField.Read<float>(source, "_floatEpsilon")
                }));

            registry.RegisterCore<SubTreeNode>(EmitSubTree);
            registry._isReadOnly = true;

            return registry;
        }

        private static RuntimeNode EmitRoot(BTRootNode source, BehaviorTreeEmitContext context)
        {
            return context.WithGuid(source, new RuntimeRootNode
            {
                Child = context.EmitRequired(source.Child, "root child")
            });
        }

        private static RuntimeNode EmitSubTree(SubTreeNode source, BehaviorTreeEmitContext context)
        {
            var runtime = context.WithGuid(source, new RuntimeSubTreeNode());
            if (source.Child != null)
            {
                runtime.Child = context.EmitRequired(source.Child, "inline subtree child");
                return runtime;
            }

            if (source.SubTreeAsset != null && source.SubTreeAsset.Root != null)
            {
                runtime.Child = context.EmitRequired(source.SubTreeAsset.Root, "subtree asset root");
                return runtime;
            }

            throw new InvalidOperationException("SubTreeNode requires an inline child or a subtree asset root.");
        }

        private static int HashKey(string key)
        {
            return string.IsNullOrEmpty(key) ? 0 : RuntimeBlackboard.DefaultStringHashFunc(key);
        }
    }

    public sealed class BehaviorTreeEmitContext
    {
        public BehaviorTreeEmitContext(BehaviorTreeNodeEmitterRegistry registry)
        {
            Registry = registry ?? BehaviorTreeNodeEmitterRegistry.BuiltIn;
        }

        public BehaviorTreeNodeEmitterRegistry Registry { get; }

        public RuntimeNode EmitRequired(BTNode node, string role)
        {
            if (node == null)
            {
                throw new InvalidOperationException($"Behavior tree compile requires {role}.");
            }

            var descriptor = new BehaviorTreeNodeDescriptor(node, GetChildCount(node));
            if (!Registry.TryEmit(descriptor, this, out RuntimeNode runtimeNode) || runtimeNode == null)
            {
                throw new InvalidOperationException(
                    $"No behavior tree runtime emitter is registered for authoring node {node.GetType().Name}.");
            }

            return runtimeNode;
        }

        public TNode WithGuid<TNode>(BTNode source, TNode runtimeNode) where TNode : RuntimeNode
        {
            if (runtimeNode == null)
            {
                throw new ArgumentNullException(nameof(runtimeNode));
            }

            runtimeNode.GUID = source != null ? source.GUID : null;
            return runtimeNode;
        }

        public TNode EmitComposite<TNode>(CompositeNode source, TNode runtimeNode) where TNode : RuntimeCompositeNode
        {
            WithGuid(source, runtimeNode);
            runtimeNode.AbortType = (RuntimeAbortType)(int)source.AbortType;
            List<BTNode> children = source.Children;
            int count = children != null ? children.Count : 0;
            for (int i = 0; i < count; i++)
            {
                runtimeNode.AddChild(EmitRequired(children[i], $"child[{i}]"));
            }

            return runtimeNode;
        }

        public TNode EmitDecorator<TNode>(DecoratorNode source, TNode runtimeNode) where TNode : RuntimeDecoratorNode
        {
            WithGuid(source, runtimeNode);
            runtimeNode.Child = EmitRequired(source.Child, "decorator child");
            return runtimeNode;
        }

        private static int GetChildCount(BTNode node)
        {
            if (node is BTRootNode root)
            {
                return root.Child != null ? 1 : 0;
            }

            if (node is DecoratorNode decorator)
            {
                return decorator.Child != null ? 1 : 0;
            }

            if (node is CompositeNode composite)
            {
                return composite.Children != null ? composite.Children.Count : 0;
            }

            return 0;
        }
    }

    internal static class AuthoringField
    {
        private static readonly Dictionary<string, FieldInfo> Fields = new Dictionary<string, FieldInfo>(128);

        public static T Read<T>(BTNode source, string fieldName)
        {
            if (source == null)
            {
                return default;
            }

            FieldInfo field = GetField(source.GetType(), fieldName);
            object value = field.GetValue(source);
            if (value == null)
            {
                return default;
            }

            if (value is T typedValue)
            {
                return typedValue;
            }

            return (T)Convert.ChangeType(value, typeof(T));
        }

        public static int ReadEnumInt(BTNode source, string fieldName)
        {
            if (source == null)
            {
                return 0;
            }

            object value = GetField(source.GetType(), fieldName).GetValue(source);
            return value != null ? Convert.ToInt32(value) : 0;
        }

        private static FieldInfo GetField(Type sourceType, string fieldName)
        {
            string key = sourceType.FullName + "." + fieldName;
            lock (Fields)
            {
                if (Fields.TryGetValue(key, out FieldInfo cached))
                {
                    return cached;
                }

                Type current = sourceType;
                while (current != null && current != typeof(ScriptableObject))
                {
                    FieldInfo field = current.GetField(
                        fieldName,
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                    if (field != null)
                    {
                        Fields[key] = field;
                        return field;
                    }

                    current = current.BaseType;
                }
            }

            throw new MissingFieldException(sourceType.FullName, fieldName);
        }
    }
}
