namespace CycloneGames.BehaviorTree.Runtime.Core.Nodes.Decorators
{
    /// <summary>
    /// Encapsulates a sub-tree with its own scoped blackboard.
    /// The scoped blackboard's parent chain connects to the host tree's blackboard,
    /// enabling hierarchical data isolation with fallback reads.
    /// </summary>
    public class RuntimeSubTreeNode : RuntimeDecoratorNode
    {
        private RuntimeBlackboard _scopedBlackboard;

        // Port remapping: maps child-local key hashes to parent key hashes
        private int[] _localKeys;
        private int[] _parentKeys;
        private byte[] _portTypes; // 0=auto, 1=int, 2=float, 3=bool, 4=Vector3, 5=object
        private int _remapCount;

        public override void OnAwake()
        {
            base.OnAwake();
            _scopedBlackboard = new RuntimeBlackboard(null, 8);
        }

        /// <summary>
        /// Configure port remapping. localKeys[i] in the subtree blackboard
        /// mirrors parentKeys[i] in the host blackboard.
        /// </summary>
        public void SetPortRemapping(int[] localKeys, int[] parentKeys)
        {
            _localKeys = localKeys;
            _parentKeys = parentKeys;
            _remapCount = localKeys != null ? localKeys.Length : 0;
            _portTypes = _remapCount > 0 ? new byte[_remapCount] : null;
        }

        /// <summary>
        /// Configure port remapping with explicit types to avoid boxing.
        /// portTypes: 0=auto, 1=int, 2=float, 3=bool, 4=Vector3, 5=object
        /// </summary>
        public void SetPortRemapping(int[] localKeys, int[] parentKeys, byte[] portTypes)
        {
            _localKeys = localKeys;
            _parentKeys = parentKeys;
            _portTypes = portTypes;
            _remapCount = localKeys != null ? localKeys.Length : 0;
        }

        protected override void OnStart(RuntimeBlackboard blackboard)
        {
            _scopedBlackboard.Parent = blackboard;
            _scopedBlackboard.Context = blackboard.Context;
        }

        protected override RuntimeState OnRun(RuntimeBlackboard blackboard)
        {
            if (Child == null) return RuntimeState.Failure;

            SyncInputs(blackboard);

            var result = Child.Run(_scopedBlackboard);

            if (result != RuntimeState.Running)
            {
                SyncOutputs(blackboard);
            }

            return result;
        }

        protected override void OnStop(RuntimeBlackboard blackboard)
        {
            base.OnStop(blackboard);
            SyncOutputs(blackboard);
            _scopedBlackboard.Clear();
        }

        private void SyncInputs(RuntimeBlackboard parent)
        {
            for (int i = 0; i < _remapCount; i++)
            {
                int pk = _parentKeys[i];
                int lk = _localKeys[i];
                byte type = _portTypes != null ? _portTypes[i] : (byte)0;

                switch (type)
                {
                    case 1: // int
                        _scopedBlackboard.SetInt(lk, parent.GetInt(pk));
                        break;
                    case 2: // float
                        _scopedBlackboard.SetFloat(lk, parent.GetFloat(pk));
                        break;
                    case 3: // bool
                        _scopedBlackboard.SetBool(lk, parent.GetBool(pk));
                        break;
                    case 4: // Vector3
                        _scopedBlackboard.SetVector3(lk, parent.GetVector3(pk));
                        break;
                    case 5: // object
                        if (parent.HasKey(pk))
                        {
                            var obj = parent.GetObject<object>(pk);
                            if (obj != null) _scopedBlackboard.SetObject(lk, obj);
                        }
                        break;
                    default: // auto-detect (fallback, may box)
                        SyncKeyAutoDetect(parent, pk, _scopedBlackboard, lk);
                        break;
                }
            }
        }

        private void SyncOutputs(RuntimeBlackboard parent)
        {
            for (int i = 0; i < _remapCount; i++)
            {
                int pk = _parentKeys[i];
                int lk = _localKeys[i];
                byte type = _portTypes != null ? _portTypes[i] : (byte)0;

                if (!_scopedBlackboard.HasKey(lk)) continue;

                switch (type)
                {
                    case 1:
                        parent.SetInt(pk, _scopedBlackboard.GetInt(lk));
                        break;
                    case 2:
                        parent.SetFloat(pk, _scopedBlackboard.GetFloat(lk));
                        break;
                    case 3:
                        parent.SetBool(pk, _scopedBlackboard.GetBool(lk));
                        break;
                    case 4:
                        parent.SetVector3(pk, _scopedBlackboard.GetVector3(lk));
                        break;
                    case 5:
                    {
                        var obj = _scopedBlackboard.GetObject<object>(lk);
                        if (obj != null) parent.SetObject(pk, obj);
                        break;
                    }
                    default:
                        SyncKeyAutoDetect(_scopedBlackboard, lk, parent, pk);
                        break;
                }
            }
        }

        /// <summary>
        /// Auto-detect value type by probing typed dictionaries via TryGet (precise, 0GC).
        /// </summary>
        private static void SyncKeyAutoDetect(RuntimeBlackboard src, int srcKey, RuntimeBlackboard dst, int dstKey)
        {
            if (!src.HasKey(srcKey)) return;

            if (src.TryGetInt(srcKey, out var intVal))
            {
                dst.SetInt(dstKey, intVal);
                return;
            }

            if (src.TryGetFloat(srcKey, out var floatVal))
            {
                dst.SetFloat(dstKey, floatVal);
                return;
            }

            if (src.TryGetBool(srcKey, out var boolVal))
            {
                dst.SetBool(dstKey, boolVal);
                return;
            }

            if (src.TryGetVector3(srcKey, out var vecVal))
            {
                dst.SetVector3(dstKey, vecVal);
                return;
            }

            if (src.TryGetObject<object>(srcKey, out var obj))
            {
                dst.SetObject(dstKey, obj);
            }
        }
    }
}
