using System;

namespace CycloneGames.GameplayAbilities.Networking
{
    /// <summary>
    /// Reusable, fixed-capacity difference between two completed canonical GAS states.
    /// </summary>
    public sealed class GASNetworkStateDeltaBuffer
    {
        private readonly GASAbilityStateRecord[] abilities;
        private readonly GASAttributeStateRecord[] attributes;
        private readonly GASEffectStateRecord[] effects;
        private readonly GASEffectTagStateRecord[] effectTags;
        private readonly GASEffectMagnitudeStateRecord[] effectMagnitudes;
        private readonly GASLooseTagStateRecord[] looseTags;

        public GASNetworkStateDeltaBuffer(GASNetworkStateCapacity stateCapacity)
        {
            abilities = new GASAbilityStateRecord[GetDeltaCapacity(stateCapacity.Abilities)];
            attributes = new GASAttributeStateRecord[GetDeltaCapacity(stateCapacity.Attributes)];
            effects = new GASEffectStateRecord[GetDeltaCapacity(stateCapacity.Effects)];
            effectTags = new GASEffectTagStateRecord[GetDeltaCapacity(stateCapacity.EffectTags)];
            effectMagnitudes = new GASEffectMagnitudeStateRecord[GetDeltaCapacity(stateCapacity.EffectMagnitudes)];
            looseTags = new GASLooseTagStateRecord[GetDeltaCapacity(stateCapacity.LooseTags)];
        }

        public int AbilityCount { get; private set; }
        public int AttributeCount { get; private set; }
        public int EffectCount { get; private set; }
        public int EffectTagCount { get; private set; }
        public int EffectMagnitudeCount { get; private set; }
        public int LooseTagCount { get; private set; }
        public int TotalRecordCount => AbilityCount + AttributeCount + EffectCount + EffectTagCount + EffectMagnitudeCount + LooseTagCount;
        public bool IsEmpty => TotalRecordCount == 0;
        public ReadOnlySpan<GASAbilityStateRecord> Abilities => abilities.AsSpan(0, AbilityCount);
        public ReadOnlySpan<GASAttributeStateRecord> Attributes => attributes.AsSpan(0, AttributeCount);
        public ReadOnlySpan<GASEffectStateRecord> Effects => effects.AsSpan(0, EffectCount);
        public ReadOnlySpan<GASEffectTagStateRecord> EffectTags => effectTags.AsSpan(0, EffectTagCount);
        public ReadOnlySpan<GASEffectMagnitudeStateRecord> EffectMagnitudes => effectMagnitudes.AsSpan(0, EffectMagnitudeCount);
        public ReadOnlySpan<GASLooseTagStateRecord> LooseTags => looseTags.AsSpan(0, LooseTagCount);

        public bool TryBuild(GASNetworkStateBuffer baseline, GASNetworkStateBuffer current)
        {
            if (baseline == null)
                throw new ArgumentNullException(nameof(baseline));
            if (current == null)
                throw new ArgumentNullException(nameof(current));

            ClearCounts();
            if (!baseline.IsComplete || !current.IsComplete ||
                baseline.Entity != current.Entity ||
                current.StateVersion <= baseline.StateVersion ||
                current.LastProcessedCommandSequence < baseline.LastProcessedCommandSequence)
                return false;

            bool success = BuildAbilities(baseline, current) &&
                           BuildAttributes(baseline, current) &&
                           BuildEffects(baseline, current) &&
                           BuildEffectTags(baseline, current) &&
                           BuildEffectMagnitudes(baseline, current) &&
                           BuildLooseTags(baseline, current);
            if (!success)
                ClearCounts();
            return success;
        }

        public void ClearCounts()
        {
            AbilityCount = 0;
            AttributeCount = 0;
            EffectCount = 0;
            EffectTagCount = 0;
            EffectMagnitudeCount = 0;
            LooseTagCount = 0;
        }

        private bool BuildAbilities(GASNetworkStateBuffer baseline, GASNetworkStateBuffer current)
        {
            int left = 0;
            int right = 0;
            while (left < baseline.AbilityCount || right < current.AbilityCount)
            {
                GASAbilityStateRecord previous = left < baseline.AbilityCount ? baseline.GetAbility(left) : default;
                GASAbilityStateRecord next = right < current.AbilityCount ? current.GetAbility(right) : default;
                int comparison = CompareOptional(previous.Grant.Value, left < baseline.AbilityCount, next.Grant.Value, right < current.AbilityCount);
                if (comparison < 0)
                {
                    if (!TryAdd(new GASAbilityStateRecord(
                            GASStateRecordOperation.Remove,
                            previous.Grant,
                            default,
                            default,
                            0,
                            GASAbilityStateFlags.None))) return false;
                    left++;
                }
                else if (comparison > 0)
                {
                    if (!TryAdd(next)) return false;
                    right++;
                }
                else
                {
                    if (!AbilityEquals(in previous, in next) && !TryAdd(next)) return false;
                    left++;
                    right++;
                }
            }
            return true;
        }

        private bool BuildAttributes(GASNetworkStateBuffer baseline, GASNetworkStateBuffer current)
        {
            int left = 0;
            int right = 0;
            while (left < baseline.AttributeCount || right < current.AttributeCount)
            {
                GASAttributeStateRecord previous = left < baseline.AttributeCount ? baseline.GetAttribute(left) : default;
                GASAttributeStateRecord next = right < current.AttributeCount ? current.GetAttribute(right) : default;
                int comparison = CompareOptional(previous.Attribute.Value, left < baseline.AttributeCount, next.Attribute.Value, right < current.AttributeCount);
                if (comparison < 0)
                {
                    if (!TryAdd(new GASAttributeStateRecord(GASStateRecordOperation.Remove, previous.Attribute, 0L, 0L))) return false;
                    left++;
                }
                else if (comparison > 0)
                {
                    if (!TryAdd(next)) return false;
                    right++;
                }
                else
                {
                    if (!AttributeEquals(in previous, in next) && !TryAdd(next)) return false;
                    left++;
                    right++;
                }
            }
            return true;
        }

        private bool BuildEffects(GASNetworkStateBuffer baseline, GASNetworkStateBuffer current)
        {
            int left = 0;
            int right = 0;
            while (left < baseline.EffectCount || right < current.EffectCount)
            {
                GASEffectStateRecord previous = left < baseline.EffectCount ? baseline.GetEffect(left) : default;
                GASEffectStateRecord next = right < current.EffectCount ? current.GetEffect(right) : default;
                int comparison = CompareOptional(previous.Effect.Value, left < baseline.EffectCount, next.Effect.Value, right < current.EffectCount);
                if (comparison < 0)
                {
                    if (!TryAdd(new GASEffectStateRecord(
                            GASStateRecordOperation.Remove,
                            previous.Effect,
                            default,
                            default,
                            0u,
                            default,
                            0,
                            0,
                            0L,
                            0L,
                            0L,
                            0u,
                            GASEffectStateFlags.None))) return false;
                    left++;
                }
                else if (comparison > 0)
                {
                    if (!TryAdd(next)) return false;
                    right++;
                }
                else
                {
                    if (!EffectEquals(in previous, in next) && !TryAdd(next)) return false;
                    left++;
                    right++;
                }
            }
            return true;
        }

        private bool BuildEffectTags(GASNetworkStateBuffer baseline, GASNetworkStateBuffer current)
        {
            int left = 0;
            int right = 0;
            while (left < baseline.EffectTagCount || right < current.EffectTagCount)
            {
                GASEffectTagStateRecord previous = left < baseline.EffectTagCount ? baseline.GetEffectTag(left) : default;
                GASEffectTagStateRecord next = right < current.EffectTagCount ? current.GetEffectTag(right) : default;
                int comparison = CompareEffectTag(in previous, left < baseline.EffectTagCount, in next, right < current.EffectTagCount);
                if (comparison < 0)
                {
                    if (ContainsEffect(current, previous.Effect) &&
                        !TryAdd(new GASEffectTagStateRecord(
                            GASStateRecordOperation.Remove,
                            previous.Effect,
                            previous.Tag,
                            previous.Kind))) return false;
                    left++;
                }
                else if (comparison > 0)
                {
                    if (!TryAdd(next)) return false;
                    right++;
                }
                else
                {
                    left++;
                    right++;
                }
            }
            return true;
        }

        private bool BuildEffectMagnitudes(GASNetworkStateBuffer baseline, GASNetworkStateBuffer current)
        {
            int left = 0;
            int right = 0;
            while (left < baseline.EffectMagnitudeCount || right < current.EffectMagnitudeCount)
            {
                GASEffectMagnitudeStateRecord previous = left < baseline.EffectMagnitudeCount ? baseline.GetEffectMagnitude(left) : default;
                GASEffectMagnitudeStateRecord next = right < current.EffectMagnitudeCount ? current.GetEffectMagnitude(right) : default;
                int comparison = CompareEffectMagnitude(in previous, left < baseline.EffectMagnitudeCount, in next, right < current.EffectMagnitudeCount);
                if (comparison < 0)
                {
                    if (ContainsEffect(current, previous.Effect) &&
                        !TryAdd(new GASEffectMagnitudeStateRecord(
                            GASStateRecordOperation.Remove,
                            previous.Effect,
                            previous.Key,
                            0L))) return false;
                    left++;
                }
                else if (comparison > 0)
                {
                    if (!TryAdd(next)) return false;
                    right++;
                }
                else
                {
                    if (previous.ValueRaw != next.ValueRaw && !TryAdd(next)) return false;
                    left++;
                    right++;
                }
            }
            return true;
        }

        private bool BuildLooseTags(GASNetworkStateBuffer baseline, GASNetworkStateBuffer current)
        {
            int left = 0;
            int right = 0;
            while (left < baseline.LooseTagCount || right < current.LooseTagCount)
            {
                GASLooseTagStateRecord previous = left < baseline.LooseTagCount ? baseline.GetLooseTag(left) : default;
                GASLooseTagStateRecord next = right < current.LooseTagCount ? current.GetLooseTag(right) : default;
                int comparison = CompareOptional(previous.Tag.Value, left < baseline.LooseTagCount, next.Tag.Value, right < current.LooseTagCount);
                if (comparison < 0)
                {
                    if (!TryAdd(new GASLooseTagStateRecord(GASStateRecordOperation.Remove, previous.Tag, 0))) return false;
                    left++;
                }
                else if (comparison > 0)
                {
                    if (!TryAdd(next)) return false;
                    right++;
                }
                else
                {
                    if (previous.Count != next.Count && !TryAdd(next)) return false;
                    left++;
                    right++;
                }
            }
            return true;
        }

        private bool TryAdd(in GASAbilityStateRecord value)
        {
            if (AbilityCount >= abilities.Length || !CanAddRecord()) return false;
            abilities[AbilityCount++] = value;
            return true;
        }

        private bool TryAdd(in GASAttributeStateRecord value)
        {
            if (AttributeCount >= attributes.Length || !CanAddRecord()) return false;
            attributes[AttributeCount++] = value;
            return true;
        }

        private bool TryAdd(in GASEffectStateRecord value)
        {
            if (EffectCount >= effects.Length || !CanAddRecord()) return false;
            effects[EffectCount++] = value;
            return true;
        }

        private bool TryAdd(in GASEffectTagStateRecord value)
        {
            if (EffectTagCount >= effectTags.Length || !CanAddRecord()) return false;
            effectTags[EffectTagCount++] = value;
            return true;
        }

        private bool TryAdd(in GASEffectMagnitudeStateRecord value)
        {
            if (EffectMagnitudeCount >= effectMagnitudes.Length || !CanAddRecord()) return false;
            effectMagnitudes[EffectMagnitudeCount++] = value;
            return true;
        }

        private bool TryAdd(in GASLooseTagStateRecord value)
        {
            if (LooseTagCount >= looseTags.Length || !CanAddRecord()) return false;
            looseTags[LooseTagCount++] = value;
            return true;
        }

        private bool CanAddRecord()
        {
            return TotalRecordCount < GameplayAbilitiesNetworkProtocol.MaxChunksPerBatch *
                                      GameplayAbilitiesNetworkProtocol.MaxRecordsPerChunk;
        }

        private static int CompareOptional(ulong left, bool hasLeft, ulong right, bool hasRight)
        {
            if (!hasLeft) return 1;
            if (!hasRight) return -1;
            return left.CompareTo(right);
        }

        private static int CompareEffectTag(
            in GASEffectTagStateRecord left,
            bool hasLeft,
            in GASEffectTagStateRecord right,
            bool hasRight)
        {
            int comparison = CompareOptional(left.Effect.Value, hasLeft, right.Effect.Value, hasRight);
            if (comparison != 0 || !hasLeft || !hasRight) return comparison;
            comparison = ((byte)left.Kind).CompareTo((byte)right.Kind);
            return comparison != 0 ? comparison : left.Tag.Value.CompareTo(right.Tag.Value);
        }

        private static int CompareEffectMagnitude(
            in GASEffectMagnitudeStateRecord left,
            bool hasLeft,
            in GASEffectMagnitudeStateRecord right,
            bool hasRight)
        {
            int comparison = CompareOptional(left.Effect.Value, hasLeft, right.Effect.Value, hasRight);
            if (comparison != 0 || !hasLeft || !hasRight) return comparison;
            comparison = ((byte)left.Key.Kind).CompareTo((byte)right.Key.Kind);
            return comparison != 0 ? comparison : left.Key.Value.CompareTo(right.Key.Value);
        }

        private static bool AbilityEquals(in GASAbilityStateRecord left, in GASAbilityStateRecord right)
        {
            return left.Definition == right.Definition &&
                   left.GrantingEffect == right.GrantingEffect &&
                   left.Level == right.Level &&
                   left.Flags == right.Flags;
        }

        private static bool AttributeEquals(in GASAttributeStateRecord left, in GASAttributeStateRecord right)
        {
            return left.BaseValueRaw == right.BaseValueRaw && left.CurrentValueRaw == right.CurrentValueRaw;
        }

        private static bool EffectEquals(in GASEffectStateRecord left, in GASEffectStateRecord right)
        {
            return left.Definition == right.Definition &&
                   left.SourceEntity == right.SourceEntity &&
                   left.SourceStreamEpoch == right.SourceStreamEpoch &&
                   left.SourceGrant == right.SourceGrant &&
                   left.Level == right.Level &&
                   left.StackCount == right.StackCount &&
                   left.DurationRaw == right.DurationRaw &&
                   left.RemainingRaw == right.RemainingRaw &&
                   left.PeriodRaw == right.PeriodRaw &&
                   left.SourceCommandSequence == right.SourceCommandSequence &&
                   left.Flags == right.Flags;
        }

        private static bool ContainsEffect(GASNetworkStateBuffer state, GASNetworkEffectId effect)
        {
            int low = 0;
            int high = state.EffectCount - 1;
            while (low <= high)
            {
                int middle = low + ((high - low) >> 1);
                ulong value = state.GetEffect(middle).Effect.Value;
                if (value == effect.Value)
                    return true;
                if (value < effect.Value)
                    low = middle + 1;
                else
                    high = middle - 1;
            }
            return false;
        }

        private static int GetDeltaCapacity(int stateCapacity)
        {
            int protocolMaximum = GameplayAbilitiesNetworkProtocol.MaxChunksPerBatch *
                                  GameplayAbilitiesNetworkProtocol.MaxRecordsPerChunk;
            return (int)Math.Min(protocolMaximum, (long)stateCapacity * 2L);
        }
    }
}
