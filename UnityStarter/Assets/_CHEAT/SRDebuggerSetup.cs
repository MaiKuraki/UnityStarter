#define ENABLE_CHEAT
using UnityEngine;

public sealed class SRDebuggerSetup : MonoBehaviour
{
    void Start()
    {
#if ENABLE_CHEAT
        SRDebug.Init();
#endif
    }
}