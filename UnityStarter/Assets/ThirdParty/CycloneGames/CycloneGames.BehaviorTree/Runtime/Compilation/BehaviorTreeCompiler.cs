using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
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

            options ??= BehaviorTreeCompileOptions.Default;
            BehaviorTreeCompileArtifact artifact = Analyze(source, options);
            if (!artifact.IsValid)
            {
                throw new BehaviorTreeCompileException(FormatErrors(source, artifact.Errors));
            }

            context ??= new RuntimeBTContext();

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

            if (!source.TryGetRuntimeBlackboardSchema(
                    out RuntimeBlackboardSchema runtimeSchema,
                    out string schemaError))
            {
                throw new BehaviorTreeCompileException(
                    $"Behavior tree runtime schema creation failed for '{source.name}': {schemaError}");
            }

            var blackboard = new RuntimeBlackboard(schema: runtimeSchema)
            {
                Context = context
            };

            try
            {
                return new RuntimeBehaviorTree(
                    runtimeRoot,
                    blackboard,
                    context,
                    new RuntimeBehaviorTreeLimits(options.MaxNodeCount, options.MaxDepth));
            }
            catch (Exception exception)
            {
                if (!blackboard.IsDisposed)
                {
                    blackboard.Dispose();
                }

                throw new BehaviorTreeCompileException(
                    $"Behavior tree runtime initialization failed for '{source.name}': {exception.Message}",
                    exception);
            }
        }

        public static BehaviorTreeCompileArtifact Analyze(
            BehaviorTree source,
            BehaviorTreeCompileOptions options = null)
        {
            options ??= BehaviorTreeCompileOptions.Default;
            BehaviorTreeNodeEmitterRegistry emitters =
                options.Emitters ?? BehaviorTreeNodeEmitterRegistry.BuiltIn;
            if (source == null)
            {
                return BehaviorTreeCompileArtifact.Invalid(
                    null,
                    0,
                    new List<string> { "Behavior tree asset is null." },
                    options.MaxNodeCount,
                    options.MaxDepth);
            }

            List<string> errors = Validate(
                source,
                options.MaxNodeCount,
                options.MaxDepth,
                emitters,
                out int nodeCount);

            if (errors.Count > 0)
            {
                return BehaviorTreeCompileArtifact.Invalid(
                    source,
                    nodeCount,
                    errors,
                    options.MaxNodeCount,
                    options.MaxDepth);
            }

            return new BehaviorTreeCompileArtifact(
                source,
                nodeCount,
                emitters,
                errors,
                options.MaxNodeCount,
                options.MaxDepth);
        }

        public static List<string> Validate(BehaviorTree source)
        {
            return Validate(
                source,
                BehaviorTreeCompileOptions.DefaultMaxNodeCount,
                BehaviorTreeCompileOptions.DefaultMaxDepth,
                BehaviorTreeNodeEmitterRegistry.BuiltIn,
                out _);
        }

        private static List<string> Validate(
            BehaviorTree source,
            int maxNodeCount,
            int maxDepth,
            BehaviorTreeNodeEmitterRegistry emitters,
            out int nodeCount)
        {
            nodeCount = 0;
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

            if (maxNodeCount < 1)
            {
                errors.Add("Compile limit MaxNodeCount must be at least 1.");
                return errors;
            }

            if (maxNodeCount > RuntimeBehaviorTreeLimits.HARD_MAX_NODE_COUNT)
            {
                errors.Add(
                    $"Compile limit MaxNodeCount cannot exceed the hard safety limit of {RuntimeBehaviorTreeLimits.HARD_MAX_NODE_COUNT}.");
                return errors;
            }

            if (maxDepth < 1)
            {
                errors.Add("Compile limit MaxDepth must be at least 1.");
                return errors;
            }

            if (maxDepth > RuntimeBehaviorTreeLimits.HARD_MAX_DEPTH)
            {
                errors.Add(
                    $"Compile limit MaxDepth cannot exceed the hard safety limit of {RuntimeBehaviorTreeLimits.HARD_MAX_DEPTH}.");
                return errors;
            }

            RuntimeBlackboardSchema rootSchema = null;
            bool rootSchemaEnabled = source.BlackboardSchemaEnabled;
            if (rootSchemaEnabled &&
                !source.TryGetRuntimeBlackboardSchema(out rootSchema, out string schemaError))
            {
                errors.Add($"Blackboard schema: {schemaError}");
            }

            nodeCount = ValidateGraphIterative(
                source,
                maxNodeCount,
                maxDepth,
                emitters ?? BehaviorTreeNodeEmitterRegistry.BuiltIn,
                rootSchemaEnabled,
                rootSchema,
                errors);
            return errors;
        }

        internal static void ValidateForEmission(
            BehaviorTree source,
            int maxNodeCount,
            int maxDepth,
            BehaviorTreeNodeEmitterRegistry emitters,
            out int nodeCount)
        {
            List<string> errors = Validate(
                source,
                maxNodeCount,
                maxDepth,
                emitters,
                out nodeCount);
            if (errors.Count != 0)
            {
                throw new BehaviorTreeCompileException(FormatErrors(source, errors));
            }
        }

        private static int ValidateGraphIterative(
            BehaviorTree source,
            int maxNodeCount,
            int maxDepth,
            BehaviorTreeNodeEmitterRegistry emitters,
            bool rootSchemaEnabled,
            RuntimeBlackboardSchema rootSchema,
            List<string> errors)
        {
            var statesByOccurrence = new Dictionary<int, Dictionary<BTNode, byte>>
            {
                [0] = new Dictionary<BTNode, byte>(ReferenceComparer<BTNode>.Instance)
            };
            var guidsByOccurrence = new Dictionary<int, HashSet<string>>
            {
                [0] = new HashSet<string>(StringComparer.Ordinal)
            };
            var guidPrefixesByOccurrence = new Dictionary<int, string>
            {
                [0] = null
            };
            var schemasByOccurrence = new Dictionary<int, RuntimeBlackboardSchema>
            {
                [0] = rootSchema
            };
            var strictSchemasByOccurrence = new Dictionary<int, bool>
            {
                [0] = rootSchemaEnabled
            };
            var runtimeGuids = new HashSet<string>(StringComparer.Ordinal);
            var activeAssets = new HashSet<BehaviorTree>(ReferenceComparer<BehaviorTree>.Instance)
            {
                source
            };
            var stack = new Stack<ValidationFrame>(64);
            stack.Push(ValidationFrame.EnterNode(source.Root, "Root", 1, 0));
            int nodeCount = 0;
            int nextOccurrenceId = 0;

            while (stack.Count > 0)
            {
                ValidationFrame frame = stack.Pop();
                if (frame.Kind == ValidationFrameKind.ExitAsset)
                {
                    activeAssets.Remove(frame.Asset);
                    continue;
                }

                if (frame.Kind == ValidationFrameKind.EnterAsset)
                {
                    BehaviorTree asset = frame.Asset;
                    if (asset == null || asset.Root == null)
                    {
                        errors.Add($"{frame.Path}: subtree asset root is null.");
                        continue;
                    }

                    ResolveSubTreeValidationSchema(
                        asset,
                        strictSchemasByOccurrence[frame.OccurrenceId],
                        schemasByOccurrence[frame.OccurrenceId],
                        frame.Path,
                        errors,
                        out bool childStrictSchema,
                        out RuntimeBlackboardSchema childSchema);

                    if (!activeAssets.Add(asset))
                    {
                        errors.Add($"{frame.Path}: recursive subtree asset cycle detected at '{asset.name}'.");
                        continue;
                    }

                    int occurrenceId = ++nextOccurrenceId;
                    statesByOccurrence.Add(
                        occurrenceId,
                        new Dictionary<BTNode, byte>(ReferenceComparer<BTNode>.Instance));
                    guidsByOccurrence.Add(
                        occurrenceId,
                        new HashSet<string>(StringComparer.Ordinal));
                    strictSchemasByOccurrence.Add(occurrenceId, childStrictSchema);
                    schemasByOccurrence.Add(occurrenceId, childSchema);
                    string parentPrefix = guidPrefixesByOccurrence[frame.OccurrenceId];
                    string occurrenceSegment = "bt-subtree-" +
                        occurrenceId.ToString(CultureInfo.InvariantCulture);
                    guidPrefixesByOccurrence.Add(
                        occurrenceId,
                        string.IsNullOrEmpty(parentPrefix)
                            ? occurrenceSegment
                            : parentPrefix + "/" + occurrenceSegment);
                    stack.Push(ValidationFrame.ExitAsset(asset));
                    stack.Push(ValidationFrame.EnterNode(
                        asset.Root,
                        $"{frame.Path}/{asset.Root.GetType().Name}",
                        frame.Depth,
                        occurrenceId));
                    continue;
                }

                BTNode node = frame.Node;
                if (node == null)
                {
                    errors.Add($"{frame.Path}: null node reference.");
                    continue;
                }

                Dictionary<BTNode, byte> states = statesByOccurrence[frame.OccurrenceId];
                if (frame.Kind == ValidationFrameKind.ExitNode)
                {
                    states[node] = 2;
                    continue;
                }

                if (states.TryGetValue(node, out byte state))
                {
                    errors.Add(state == 1
                        ? $"{frame.Path}: cycle detected."
                        : $"{frame.Path}: node is referenced by more than one parent.");
                    continue;
                }

                if (frame.Depth > maxDepth)
                {
                    errors.Add($"{frame.Path}: graph depth exceeds MaxDepth ({maxDepth}).");
                    continue;
                }

                nodeCount++;
                if (nodeCount > maxNodeCount)
                {
                    errors.Add($"Behavior tree node count exceeds MaxNodeCount ({maxNodeCount}).");
                    break;
                }

                states[node] = 1;
                HashSet<string> guids = guidsByOccurrence[frame.OccurrenceId];
                bool occurrenceGuidUnique = true;
                if (!string.IsNullOrEmpty(node.GUID) && !guids.Add(node.GUID))
                {
                    errors.Add($"{frame.Path}: duplicate node GUID '{node.GUID}'.");
                    occurrenceGuidUnique = false;
                }

                if (occurrenceGuidUnique && !string.IsNullOrEmpty(node.GUID))
                {
                    string guidPrefix = guidPrefixesByOccurrence[frame.OccurrenceId];
                    string runtimeGuid = string.IsNullOrEmpty(guidPrefix)
                        ? node.GUID
                        : guidPrefix + "/" + node.GUID;
                    if (!runtimeGuids.Add(runtimeGuid))
                    {
                        errors.Add(
                            $"{frame.Path}: emitted runtime node GUID '{runtimeGuid}' collides with another occurrence.");
                    }
                }

                ValidateNodeSemantics(
                    node,
                    frame.Path,
                    schemasByOccurrence[frame.OccurrenceId],
                    errors);
                if (!emitters.CanEmit(node.GetType()))
                {
                    errors.Add(
                        $"{frame.Path}: no exact runtime emitter is registered for authoring node {node.GetType().FullName}.");
                }

                stack.Push(ValidationFrame.ExitNode(node, frame.Path, frame.Depth, frame.OccurrenceId));
                PushChildren(
                    node,
                    frame.Path,
                    frame.Depth + 1,
                    frame.OccurrenceId,
                    stack,
                    errors);
            }

            return nodeCount;
        }

        private static void ResolveSubTreeValidationSchema(
            BehaviorTree asset,
            bool parentStrictSchema,
            RuntimeBlackboardSchema parentSchema,
            string path,
            List<string> errors,
            out bool childStrictSchema,
            out RuntimeBlackboardSchema childSchema)
        {
            if (asset == null || !asset.BlackboardSchemaEnabled)
            {
                childStrictSchema = parentStrictSchema;
                childSchema = parentSchema;
                return;
            }

            childStrictSchema = true;
            if (!asset.TryGetRuntimeBlackboardSchema(
                    out childSchema,
                    out string childSchemaError))
            {
                errors.Add($"{path}: subtree blackboard schema is invalid: {childSchemaError}");
                return;
            }

            if (!parentStrictSchema)
            {
                errors.Add(
                    $"{path}: strict subtree '{asset.name}' cannot be embedded in a legacy-open root; " +
                    "enable a compatible strict root schema or make the subtree legacy-open.");
                return;
            }

            // Each strict subtree validates its own reusable authoring contract. Runtime storage
            // still uses the compiled root schema as the only externally visible authority.
            if (parentSchema != null &&
                !BehaviorTreeBlackboardSchemaCompiler.IsExactSubset(
                    childSchema,
                    parentSchema,
                    out string compatibilityError))
            {
                errors.Add($"{path}: subtree blackboard schema is incompatible: {compatibilityError}");
            }
        }

        private static void ValidateNodeSemantics(
            BTNode node,
            string path,
            RuntimeBlackboardSchema activeSchema,
            List<string> errors)
        {
            if (node is BTRootNode root && root.Child == null)
            {
                errors.Add($"{path}: root child is null.");
                return;
            }

            if (node is SubTreeNode subTree)
            {
                if (subTree.Child == null && (subTree.SubTreeAsset == null || subTree.SubTreeAsset.Root == null))
                {
                    errors.Add($"{path}: subtree node has neither child nor subtree asset root.");
                }
                else if (subTree.Child != null && subTree.SubTreeAsset != null)
                {
                    errors.Add($"{path}: subtree node cannot use an inline child and a subtree asset at the same time.");
                }
                return;
            }

            if (node is DecoratorNode decorator && decorator.Child == null)
            {
                errors.Add($"{path}: decorator child is null.");
                return;
            }

            if (node is RandomChanceNode randomChance)
            {
                float chance = randomChance.Chance;
                float outOf = randomChance.OutOf;
                if (!IsFinite(outOf) || outOf <= 0f || !IsFinite(chance) || chance < 0f || chance > outOf)
                {
                    errors.Add($"{path}: RandomChance requires finite values with 0 <= chance <= outOf and outOf > 0.");
                }
            }

            if (node is BBComparisonNode comparison)
            {
                string key = comparison.Key;
                float epsilon = comparison.FloatEpsilon;
                BBComparisonOp comparisonOperator = comparison.Operator;
                BBValueType valueType = comparison.ValueType;
                if (string.IsNullOrWhiteSpace(key))
                {
                    errors.Add($"{path}: BBComparison requires a blackboard key.");
                }
                else if (RuntimeBlackboard.DefaultStringHashFunc(key) == 0)
                {
                    errors.Add($"{path}: BBComparison key hashes to the reserved zero value.");
                }
                if ((uint)(int)comparisonOperator > (uint)BBComparisonOp.IsNotSet)
                {
                    errors.Add($"{path}: BBComparison operator value {(int)comparisonOperator} is invalid.");
                }
                if ((uint)(int)valueType > (uint)BBValueType.Object)
                {
                    errors.Add($"{path}: BBComparison value type {(int)valueType} is invalid.");
                }
                if (!IsFinite(epsilon) || epsilon < 0f)
                {
                    errors.Add($"{path}: BBComparison epsilon must be finite and non-negative.");
                }
                if (valueType == BBValueType.Bool
                    && comparisonOperator != BBComparisonOp.Equal
                    && comparisonOperator != BBComparisonOp.NotEqual
                    && comparisonOperator != BBComparisonOp.IsSet
                    && comparisonOperator != BBComparisonOp.IsNotSet)
                {
                    errors.Add($"{path}: bool values only support Equal, NotEqual, IsSet, or IsNotSet.");
                }
                if (valueType == BBValueType.Object
                    && comparisonOperator != BBComparisonOp.IsSet
                    && comparisonOperator != BBComparisonOp.IsNotSet)
                {
                    errors.Add($"{path}: object values only support IsSet or IsNotSet.");
                }
                if (valueType == BBValueType.Float
                    && comparisonOperator != BBComparisonOp.IsSet
                    && comparisonOperator != BBComparisonOp.IsNotSet
                    && string.IsNullOrEmpty(comparison.ReferenceKey)
                    && !IsFinite(comparison.ReferenceFloat))
                {
                    errors.Add($"{path}: BBComparison float reference must be finite.");
                }
                if (!string.IsNullOrEmpty(comparison.ReferenceKey)
                    && string.IsNullOrWhiteSpace(comparison.ReferenceKey))
                {
                    errors.Add($"{path}: BBComparison reference key cannot contain only whitespace.");
                }

                bool isExistenceCheck =
                    comparisonOperator == BBComparisonOp.IsSet ||
                    comparisonOperator == BBComparisonOp.IsNotSet;
                if (!string.IsNullOrWhiteSpace(key) &&
                    RuntimeBlackboard.DefaultStringHashFunc(key) != 0)
                {
                    ValidateSchemaKey(
                        activeSchema,
                        key,
                        path,
                        "BBComparison key",
                        isExistenceCheck ? (RuntimeBlackboardValueType?)null : ToRuntimeValueType(valueType),
                        errors);
                }

                if (!isExistenceCheck &&
                    !string.IsNullOrWhiteSpace(comparison.ReferenceKey))
                {
                    ValidateSchemaKey(
                        activeSchema,
                        comparison.ReferenceKey,
                        path,
                        "BBComparison reference key",
                        ToRuntimeValueType(valueType),
                        errors);
                }
            }

            if (node is WaitNode wait)
            {
                ValidateFiniteNonNegative(wait.Duration, path, "Wait duration", errors);
                ValidateFiniteNonNegativeRange(wait.Range, path, "Wait", errors);
            }

            if (node is WaitSuccessNode waitSuccess)
            {
                ValidateFiniteNonNegative(waitSuccess.WaitTime, path, "WaitSuccess wait time", errors);
                ValidateFiniteNonNegativeRange(waitSuccess.WaitTimeRange, path, "WaitSuccess", errors);
            }

            if (node is DelayNode delay)
            {
                ValidateFiniteNonNegative(delay.DelaySeconds, path, "Delay seconds", errors);
            }

            if (node is TimeoutNode timeout)
            {
                ValidateFiniteNonNegative(timeout.TimeoutSeconds, path, "Timeout seconds", errors);
            }

            if (node is CoolDownNode coolDown)
            {
                ValidateFiniteNonNegative(coolDown.CoolDown, path, "Cooldown", errors);
            }

            if (node is ServiceNode service)
            {
                ValidateFiniteNonNegative(service.Interval, path, "Service interval", errors);
                ValidateFiniteNonNegative(service.RandomDeviation, path, "Service random deviation", errors);
                if (IsFinite(service.Interval)
                    && IsFinite(service.RandomDeviation)
                    && (service.RandomDeviation > float.MaxValue * 0.5f
                        || service.Interval > float.MaxValue - service.RandomDeviation))
                {
                    errors.Add($"{path}: Service interval and random deviation exceed the finite sampling range.");
                }
            }

            if (node is RetryNode retry && retry.MaxAttempts != -1 && retry.MaxAttempts < 1)
            {
                errors.Add($"{path}: Retry MaxAttempts must be -1 or at least 1.");
            }

            if (node is RepeatNode repeat)
            {
                if (repeat.RepeatCount < 1)
                {
                    errors.Add($"{path}: Repeat count must be at least 1.");
                }
                ValidateRepeatRange(repeat.RandomRepeatCountRange, path, errors);
            }

            if (node is MessagePassNode messagePass)
            {
                if (ValidateAuthoringKey(messagePass.Key, path, "MessagePass", errors))
                {
                    ValidateSchemaKey(
                        activeSchema,
                        messagePass.Key,
                        path,
                        "MessagePass key",
                        RuntimeBlackboardValueType.Object,
                        errors);
                }
            }
            else if (node is MessageRemoveNode messageRemove)
            {
                if (ValidateAuthoringKey(messageRemove.Key, path, "MessageRemove", errors))
                {
                    ValidateSchemaKey(
                        activeSchema,
                        messageRemove.Key,
                        path,
                        "MessageRemove key",
                        null,
                        errors);
                }
            }
            else if (node is MessageReceiveNode messageReceive)
            {
                if (ValidateAuthoringKey(messageReceive.Key, path, "MessageReceive", errors))
                {
                    ValidateSchemaKey(
                        activeSchema,
                        messageReceive.Key,
                        path,
                        "MessageReceive key",
                        RuntimeBlackboardValueType.Object,
                        errors);
                }
            }

            if (!(node is CompositeNode composite))
            {
                return;
            }

            if ((uint)(int)composite.AbortType > (uint)ConditionalAbortType.BOTH)
            {
                errors.Add($"{path}: conditional abort value {(int)composite.AbortType} is invalid.");
            }

            int childCount = composite.Children != null ? composite.Children.Count : 0;
            if (node is IfThenElseNode && (childCount < 2 || childCount > 3))
            {
                errors.Add($"{path}: IfThenElse requires two or three children.");
            }
            else if (node is WhileDoElseNode && (childCount < 2 || childCount > 3))
            {
                errors.Add($"{path}: WhileDoElse requires two or three children.");
            }
            else if (childCount == 0)
            {
                errors.Add($"{path}: composite requires at least one child.");
            }

            if (node is ParallelNode parallel
                && (uint)parallel.ModeValue > (uint)RuntimeParallelMode.UntilAnySuccess)
            {
                errors.Add($"{path}: Parallel mode value {parallel.ModeValue} is invalid.");
            }

            if (node is SwitchNode switchNode)
            {
                if (ValidateAuthoringKey(switchNode.VariableKey, path, "Switch", errors))
                {
                    ValidateSchemaKey(
                        activeSchema,
                        switchNode.VariableKey,
                        path,
                        "Switch key",
                        RuntimeBlackboardValueType.Int,
                        errors);
                }
            }

            if (node is UtilitySelectorNode utility)
            {
                IReadOnlyList<string> scoreKeys = utility.ScoreKeys;
                int scoreKeyCount = scoreKeys != null ? scoreKeys.Count : 0;
                if (scoreKeyCount != childCount)
                {
                    errors.Add(
                        $"{path}: UtilitySelector score-key count ({scoreKeyCount}) must match child count ({childCount}).");
                }

                for (int i = 0; i < scoreKeyCount; i++)
                {
                    if (string.IsNullOrWhiteSpace(scoreKeys[i]))
                    {
                        errors.Add($"{path}: UtilitySelector score key[{i}] is required.");
                    }
                    else if (RuntimeBlackboard.DefaultStringHashFunc(scoreKeys[i]) == 0)
                    {
                        errors.Add($"{path}: UtilitySelector score key[{i}] hashes to the reserved zero value.");
                    }
                    else
                    {
                        ValidateSchemaKey(
                            activeSchema,
                            scoreKeys[i],
                            path,
                            $"UtilitySelector score key[{i}]",
                            RuntimeBlackboardValueType.Float,
                            errors);
                    }
                }
            }

            if (node is ParallelAllNode parallelAll && childCount > 0)
            {
                int successThreshold = parallelAll.SuccessThreshold;
                int failureThreshold = parallelAll.FailureThreshold;
                if (!IsValidParallelThreshold(successThreshold, childCount)
                    || !IsValidParallelThreshold(failureThreshold, childCount))
                {
                    errors.Add($"{path}: ParallelAll thresholds must be -1 or between 1 and child count ({childCount}).");
                }
                else
                {
                    int effectiveSuccess = successThreshold == -1 ? childCount : successThreshold;
                    int effectiveFailure = failureThreshold == -1 ? childCount : failureThreshold;
                    if (effectiveSuccess + effectiveFailure > childCount + 1)
                    {
                        errors.Add($"{path}: ParallelAll thresholds can leave a fully completed node without a terminal result.");
                    }
                }
            }

            if (node is ProbabilityBranch probability)
            {
                IReadOnlyList<float> weights = probability.Probabilities;
                if (weights != null && weights.Count > 0)
                {
                    if (weights.Count != childCount)
                    {
                        errors.Add($"{path}: probability weight count ({weights.Count}) must match child count ({childCount}).");
                    }

                    double total = 0d;
                    for (int i = 0; i < weights.Count; i++)
                    {
                        float weight = weights[i];
                        if (!IsFinite(weight) || weight < 0f)
                        {
                            errors.Add($"{path}: probability weight[{i}] must be finite and non-negative.");
                            continue;
                        }
                        total += weight;
                    }

                    if (total <= 0d || double.IsInfinity(total) || total > float.MaxValue)
                    {
                        errors.Add($"{path}: probability weights require a positive finite total no greater than float.MaxValue.");
                    }
                }
            }
        }

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private static void ValidateFiniteNonNegative(
            float value,
            string path,
            string label,
            List<string> errors)
        {
            if (!IsFinite(value) || value < 0f)
            {
                errors.Add($"{path}: {label} must be finite and non-negative.");
            }
        }

        private static void ValidateFiniteNonNegativeRange(
            Vector2 range,
            string path,
            string label,
            List<string> errors)
        {
            if (!IsFinite(range.x) || !IsFinite(range.y)
                || range.x < 0f
                || range.y < range.x)
            {
                errors.Add($"{path}: {label} range must be finite, non-negative, and ordered min <= max.");
            }
        }

        private static void ValidateRepeatRange(Vector2 range, string path, List<string> errors)
        {
            const float MaxExactFloatInteger = 16777215f;
            if (!IsFinite(range.x)
                || !IsFinite(range.y)
                || range.x < 1f
                || range.y < range.x
                || range.y > MaxExactFloatInteger
                || range.x != (float)Math.Truncate(range.x)
                || range.y != (float)Math.Truncate(range.y))
            {
                errors.Add(
                    $"{path}: Repeat random range must contain ordered whole counts from 1 through {MaxExactFloatInteger}.");
            }
        }

        private static bool ValidateAuthoringKey(
            string key,
            string path,
            string nodeName,
            List<string> errors)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                errors.Add($"{path}: {nodeName} requires a blackboard key.");
                return false;
            }

            if (RuntimeBlackboard.DefaultStringHashFunc(key) == 0)
            {
                errors.Add($"{path}: {nodeName} key hashes to the reserved zero value.");
                return false;
            }

            return true;
        }

        private static void ValidateSchemaKey(
            RuntimeBlackboardSchema schema,
            string key,
            string path,
            string label,
            RuntimeBlackboardValueType? expectedType,
            List<string> errors)
        {
            if (schema == null)
            {
                return;
            }

            int keyHash = RuntimeBlackboard.DefaultStringHashFunc(key);
            if (!schema.TryGetDefinition(keyHash, out RuntimeBlackboardKeyDefinition definition))
            {
                errors.Add($"{path}: {label} '{key}' is not declared by the active strict schema.");
                return;
            }

            if (!string.Equals(definition.Name, key, StringComparison.Ordinal))
            {
                errors.Add(
                    $"{path}: {label} '{key}' collides with declared key '{definition.Name}' at hash {keyHash}.");
                return;
            }

            if (expectedType.HasValue && definition.ValueType != expectedType.Value)
            {
                errors.Add(
                    $"{path}: {label} '{key}' requires {expectedType.Value}, but the schema declares {definition.ValueType}.");
            }
        }

        private static RuntimeBlackboardValueType? ToRuntimeValueType(BBValueType valueType)
        {
            switch (valueType)
            {
                case BBValueType.Int:
                    return RuntimeBlackboardValueType.Int;
                case BBValueType.Float:
                    return RuntimeBlackboardValueType.Float;
                case BBValueType.Bool:
                    return RuntimeBlackboardValueType.Bool;
                case BBValueType.Object:
                    return RuntimeBlackboardValueType.Object;
                default:
                    return null;
            }
        }

        private static bool IsValidParallelThreshold(int threshold, int childCount)
        {
            return threshold == -1 || (threshold >= 1 && threshold <= childCount);
        }

        private static void PushChildren(
            BTNode node,
            string path,
            int depth,
            int occurrenceId,
            Stack<ValidationFrame> stack,
            List<string> errors)
        {
            if (node is BTRootNode root)
            {
                if (root.Child != null)
                {
                    stack.Push(ValidationFrame.EnterNode(
                        root.Child,
                        $"{path}/{root.Child.GetType().Name}",
                        depth,
                        occurrenceId));
                }
                return;
            }

            if (node is SubTreeNode subTree)
            {
                if (subTree.Child != null)
                {
                    stack.Push(ValidationFrame.EnterNode(
                        subTree.Child,
                        $"{path}/{subTree.Child.GetType().Name}",
                        depth,
                        occurrenceId));
                }
                else if (subTree.SubTreeAsset != null)
                {
                    stack.Push(ValidationFrame.EnterAsset(
                        subTree.SubTreeAsset,
                        $"{path}/SubTreeAsset({subTree.SubTreeAsset.name})",
                        depth,
                        occurrenceId));
                }
                return;
            }

            if (node is DecoratorNode decorator)
            {
                if (decorator.Child != null)
                {
                    stack.Push(ValidationFrame.EnterNode(
                        decorator.Child,
                        $"{path}/{decorator.Child.GetType().Name}",
                        depth,
                        occurrenceId));
                }
                return;
            }

            if (!(node is CompositeNode composite) || composite.Children == null)
            {
                return;
            }

            for (int i = composite.Children.Count - 1; i >= 0; i--)
            {
                BTNode child = composite.Children[i];
                if (child == null)
                {
                    errors.Add($"{path}: child[{i}] is null.");
                    continue;
                }

                stack.Push(ValidationFrame.EnterNode(
                    child,
                    $"{path}/{child.GetType().Name}[{i}]",
                    depth,
                    occurrenceId));
            }
        }

        private enum ValidationFrameKind : byte
        {
            EnterNode,
            ExitNode,
            EnterAsset,
            ExitAsset
        }

        private readonly struct ValidationFrame
        {
            public readonly BTNode Node;
            public readonly BehaviorTree Asset;
            public readonly string Path;
            public readonly int Depth;
            public readonly int OccurrenceId;
            public readonly ValidationFrameKind Kind;

            private ValidationFrame(
                BTNode node,
                BehaviorTree asset,
                string path,
                int depth,
                int occurrenceId,
                ValidationFrameKind kind)
            {
                Node = node;
                Asset = asset;
                Path = path;
                Depth = depth;
                OccurrenceId = occurrenceId;
                Kind = kind;
            }

            public static ValidationFrame EnterNode(BTNode node, string path, int depth, int occurrenceId) =>
                new ValidationFrame(node, null, path, depth, occurrenceId, ValidationFrameKind.EnterNode);

            public static ValidationFrame ExitNode(BTNode node, string path, int depth, int occurrenceId) =>
                new ValidationFrame(node, null, path, depth, occurrenceId, ValidationFrameKind.ExitNode);

            public static ValidationFrame EnterAsset(
                BehaviorTree asset,
                string path,
                int depth,
                int parentOccurrenceId) =>
                new ValidationFrame(
                    null,
                    asset,
                    path,
                    depth,
                    parentOccurrenceId,
                    ValidationFrameKind.EnterAsset);

            public static ValidationFrame ExitAsset(BehaviorTree asset) =>
                new ValidationFrame(null, asset, null, 0, 0, ValidationFrameKind.ExitAsset);
        }

        internal sealed class ReferenceComparer<T> : IEqualityComparer<T>
            where T : class
        {
            public static readonly ReferenceComparer<T> Instance = new ReferenceComparer<T>();

            public bool Equals(T x, T y) => ReferenceEquals(x, y);

            public int GetHashCode(T obj) => RuntimeHelpers.GetHashCode(obj);
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
        public const int DefaultMaxNodeCount = RuntimeBehaviorTreeLimits.DEFAULT_MAX_NODE_COUNT;
        public const int DefaultMaxDepth = RuntimeBehaviorTreeLimits.DEFAULT_MAX_DEPTH;

        public static BehaviorTreeCompileOptions Default => new BehaviorTreeCompileOptions();

        public BehaviorTreeNodeEmitterRegistry Emitters { get; set; } = BehaviorTreeNodeEmitterRegistry.BuiltIn;
        public int MaxNodeCount { get; set; } = DefaultMaxNodeCount;
        public int MaxDepth { get; set; } = DefaultMaxDepth;
    }

    public sealed class BehaviorTreeCompileArtifact
    {
        private readonly IReadOnlyList<string> _errors;
        private readonly int _maxNodeCount;
        private readonly int _maxDepth;

        internal BehaviorTreeCompileArtifact(
            BehaviorTree source,
            int nodeCount,
            BehaviorTreeNodeEmitterRegistry emitters,
            List<string> errors,
            int maxNodeCount,
            int maxDepth)
        {
            Source = source;
            NodeCount = nodeCount;
            Emitters = emitters ?? BehaviorTreeNodeEmitterRegistry.BuiltIn;
            _errors = (errors != null ? new List<string>(errors) : new List<string>(0)).AsReadOnly();
            _maxNodeCount = maxNodeCount;
            _maxDepth = maxDepth;
        }

        public BehaviorTree Source { get; }
        public int NodeCount { get; private set; }
        public BehaviorTreeNodeEmitterRegistry Emitters { get; }
        public bool IsValid => _errors.Count == 0;
        public IReadOnlyList<string> Errors => _errors;

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

            BehaviorTreeCompiler.ValidateForEmission(
                Source,
                _maxNodeCount,
                _maxDepth,
                Emitters,
                out int currentNodeCount);
            NodeCount = currentNodeCount;

            var context = new BehaviorTreeEmitContext(Emitters, Source);
            return context.EmitRequired(Source.Root, "root node");
        }

        internal static BehaviorTreeCompileArtifact Invalid(
            BehaviorTree source,
            int nodeCount,
            List<string> errors,
            int maxNodeCount,
            int maxDepth)
        {
            return new BehaviorTreeCompileArtifact(
                source,
                nodeCount,
                BehaviorTreeNodeEmitterRegistry.BuiltIn,
                errors,
                maxNodeCount,
                maxDepth);
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

            if (_emitters.TryGetValue(descriptor.AuthoringType, out BehaviorTreeNodeEmitter emitter))
            {
                runtimeNode = emitter(descriptor, context);
                return true;
            }

            if (_fallback != null)
            {
                return _fallback.TryEmit(descriptor, context, out runtimeNode);
            }

            return false;
        }

        internal bool CanEmit(Type authoringType)
        {
            if (authoringType == null)
            {
                return false;
            }

            return _emitters.ContainsKey(authoringType)
                || (_fallback != null && _fallback.CanEmit(authoringType));
        }

        private static BehaviorTreeNodeEmitterRegistry CreateBuiltIn()
        {
            var registry = new BehaviorTreeNodeEmitterRegistry();

            registry.RegisterCore<BTRootNode>(EmitRoot);

            registry.RegisterCore<DebugLogNode>((source, context) =>
                context.WithGuid(source, new RuntimeDebugLogNode
                {
                    Message = source.Message
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
                    KeyHash = HashKey(source.Key),
                    Message = source.Message
                }));

            registry.RegisterCore<MessageRemoveNode>((source, context) =>
                context.WithGuid(source, new RuntimeMessageRemoveNode
                {
                    KeyHash = HashKey(source.Key)
                }));

            registry.RegisterCore<BTChangeNode>((source, context) =>
                context.WithGuid(source, new RuntimeBTChangeNode
                {
                    StateId = source.StateId
                }));

            registry.RegisterCore<OnOffNode>((source, context) =>
                context.WithGuid(source, new RuntimeOnOffNode
                {
                    IsOn = source.IsOn
                }));

            registry.RegisterCore<RandomChanceNode>((source, context) =>
                context.WithGuid(source, new RuntimeRandomChanceNode(
                    source.Chance,
                    source.OutOf,
                    (uint)source.Seed)));

            registry.RegisterCore<MessageReceiveNode>((source, context) =>
                context.WithGuid(source, new RuntimeMessageReceiveNode
                {
                    KeyHash = HashKey(source.Key),
                    ExpectedMessage = source.Message
                }));

            registry.RegisterCore<SequencerNode>((source, context) => context.EmitComposite(source, new RuntimeSequencer()));
            registry.RegisterCore<SequenceWithMemoryNode>((source, context) => context.EmitComposite(source, new RuntimeSequenceWithMemory()));
            registry.RegisterCore<SelectorNode>((source, context) => context.EmitComposite(source, new RuntimeSelector()));
            registry.RegisterCore<ReactiveSequenceNode>((source, context) => context.EmitComposite(source, new RuntimeReactiveSequence()));
            registry.RegisterCore<ReactiveFallbackNode>((source, context) => context.EmitComposite(source, new RuntimeReactiveFallback()));
            registry.RegisterCore<IfThenElseNode>((source, context) => context.EmitComposite(source, new RuntimeIfThenElseNode()));
            registry.RegisterCore<WhileDoElseNode>((source, context) => context.EmitComposite(source, new RuntimeWhileDoElseNode()));

            registry.RegisterCore<SelectorRandomNode>((source, context) =>
                context.EmitComposite(source, new RuntimeSelectorRandom((uint)source.Seed)));

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
                    SuccessThreshold = source.SuccessThreshold,
                    FailureThreshold = source.FailureThreshold
                }));

            registry.RegisterCore<ProbabilityBranch>((source, context) =>
            {
                var runtime = new RuntimeProbabilityBranch();
                IReadOnlyList<float> probabilities = source.Probabilities;
                int count = probabilities != null ? probabilities.Count : 0;
                var weights = new float[count];
                for (int i = 0; i < count; i++)
                {
                    weights[i] = probabilities[i];
                }

                runtime.SetWeights(weights);
                return context.EmitComposite(source, runtime);
            });

            registry.RegisterCore<SwitchNode>((source, context) =>
                context.EmitComposite(source, new RuntimeSwitchNode
                {
                    VariableKeyHash = HashKey(source.VariableKey)
                }));

            registry.RegisterCore<UtilitySelectorNode>((source, context) =>
            {
                var runtime = new RuntimeUtilitySelector();
                IReadOnlyList<string> scoreKeys = source.ScoreKeys;
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
                    CoolDown = source.CoolDown,
                    ResetOnSuccess = source.ResetOnSuccess
                }));

            registry.RegisterCore<DelayNode>((source, context) =>
                context.EmitDecorator(source, new RuntimeDelayNode
                {
                    DelaySeconds = source.DelaySeconds,
                    UseUnscaledTime = source.UseUnscaledTime
                }));

            registry.RegisterCore<RepeatNode>((source, context) =>
            {
                Vector2 range = source.RandomRepeatCountRange;
                return context.EmitDecorator(source, new RuntimeRepeatNode
                {
                    RepeatForever = source.RepeatForever,
                    RepeatCount = source.RepeatCount,
                    UseRandomRepeatCount = source.UseRandomRepeatCount,
                    RandomRangeMin = (int)range.x,
                    RandomRangeMax = (int)range.y
                });
            });

            registry.RegisterCore<RetryNode>((source, context) =>
                context.EmitDecorator(source, new RuntimeRetryNode
                {
                    MaxAttempts = source.MaxAttempts
                }));

            registry.RegisterCore<ServiceNode>((source, context) =>
                context.EmitDecorator(source, new RuntimeServiceNode
                {
                    Interval = source.Interval,
                    RandomDeviation = source.RandomDeviation,
                    UseUnscaledTime = source.UseUnscaledTime
                }));

            registry.RegisterCore<TimeoutNode>((source, context) =>
                context.EmitDecorator(source, new RuntimeTimeoutNode
                {
                    TimeoutSeconds = source.TimeoutSeconds,
                    UseUnscaledTime = source.UseUnscaledTime
                }));

            registry.RegisterCore<WaitSuccessNode>((source, context) =>
            {
                Vector2 range = source.WaitTimeRange;
                return context.EmitDecorator(source, new RuntimeWaitSuccessNode
                {
                    WaitTime = source.WaitTime,
                    UseRandomRange = source.UseRandomBetweenTwoConstants,
                    RangeMin = range.x,
                    RangeMax = range.y,
                    UseUnscaledTime = source.UseUnscaledTime
                });
            });

            registry.RegisterCore<BBComparisonNode>((source, context) =>
            {
                string referenceKey = source.ReferenceKey;
                return context.EmitDecorator(source, new RuntimeBBComparisonNode
                {
                    KeyHash = HashKey(source.Key),
                    Operator = source.Operator,
                    ValueType = source.ValueType,
                    RefInt = source.ReferenceInt,
                    RefFloat = source.ReferenceFloat,
                    RefBool = source.ReferenceBool,
                    RefKeyHash = HashKey(referenceKey),
                    UseRefKey = !string.IsNullOrEmpty(referenceKey),
                    FloatEpsilon = source.FloatEpsilon
                });
            });

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
                runtime.Child = context.EmitSubTreeAssetRoot(
                    source,
                    source.SubTreeAsset,
                    "subtree asset root");
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
        private readonly HashSet<BehaviorTree> _activeSubTreeAssets;
        private readonly HashSet<string> _emittedRuntimeGuids = new HashSet<string>(StringComparer.Ordinal);
        private string _guidPrefix;
        private int _nextSubTreeOccurrenceId;

        internal BehaviorTreeEmitContext(BehaviorTreeNodeEmitterRegistry registry)
            : this(registry, null)
        {
        }

        internal BehaviorTreeEmitContext(
            BehaviorTreeNodeEmitterRegistry registry,
            BehaviorTree rootAsset)
        {
            Registry = registry ?? BehaviorTreeNodeEmitterRegistry.BuiltIn;
            _activeSubTreeAssets = new HashSet<BehaviorTree>(
                BehaviorTreeCompiler.ReferenceComparer<BehaviorTree>.Instance);
            if (rootAsset != null)
            {
                _activeSubTreeAssets.Add(rootAsset);
            }
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

            string sourceGuid = source != null ? source.GUID : null;
            string runtimeGuid = string.IsNullOrEmpty(sourceGuid) || string.IsNullOrEmpty(_guidPrefix)
                ? sourceGuid
                : _guidPrefix + "/" + sourceGuid;
            if (!string.IsNullOrEmpty(runtimeGuid) && !_emittedRuntimeGuids.Add(runtimeGuid))
            {
                throw new InvalidOperationException(
                    $"Emitted runtime node GUID '{runtimeGuid}' is not unique.");
            }

            runtimeNode.GUID = runtimeGuid;
            return runtimeNode;
        }

        internal RuntimeNode EmitSubTreeAssetRoot(
            SubTreeNode source,
            BehaviorTree asset,
            string role)
        {
            if (asset == null || asset.Root == null)
            {
                throw new InvalidOperationException("Subtree asset root is required.");
            }

            if (!_activeSubTreeAssets.Add(asset))
            {
                throw new InvalidOperationException(
                    $"Recursive subtree asset cycle detected at '{asset.name}'.");
            }

            string previousPrefix = _guidPrefix;
            int occurrenceId = checked(++_nextSubTreeOccurrenceId);
            string occurrenceSegment = "bt-subtree-" +
                occurrenceId.ToString(CultureInfo.InvariantCulture);
            _guidPrefix = string.IsNullOrEmpty(previousPrefix)
                ? occurrenceSegment
                : previousPrefix + "/" + occurrenceSegment;

            try
            {
                return EmitRequired(asset.Root, role);
            }
            finally
            {
                _guidPrefix = previousPrefix;
                _activeSubTreeAssets.Remove(asset);
            }
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

            if (node is SubTreeNode subTree)
            {
                return subTree.Child != null
                    || (subTree.SubTreeAsset != null && subTree.SubTreeAsset.Root != null)
                    ? 1
                    : 0;
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

}
