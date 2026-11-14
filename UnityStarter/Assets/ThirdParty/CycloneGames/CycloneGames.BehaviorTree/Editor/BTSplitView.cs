using UnityEngine.UIElements;

namespace CycloneGames.BehaviorTree.Editor
{
#if UNITY_6000_0_OR_NEWER
    [UxmlElement]
    public partial class BTSplitView : TwoPaneSplitView
#else
    public class BTSplitView : TwoPaneSplitView
#endif
    {
#if !UNITY_6000_0_OR_NEWER
        public new class UxmlFactory : UxmlFactory<BTSplitView, TwoPaneSplitView.UxmlTraits> { }
#endif
    }
}
