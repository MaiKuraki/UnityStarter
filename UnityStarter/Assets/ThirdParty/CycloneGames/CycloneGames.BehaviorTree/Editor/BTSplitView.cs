using UnityEngine.UIElements;

namespace CycloneGames.BehaviorTree.Editor
{
    public class BTSplitView : TwoPaneSplitView
    {
        public new class UxmlFactory : UxmlFactory<BTSplitView, TwoPaneSplitView.UxmlTraits> { }
    }
}