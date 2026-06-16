using CycloneGames.GameplayAbilities.Runtime;
using UnityEngine;

namespace CycloneGames.GameplayAbilities.Sample
{
    [CreateAssetMenu(fileName = "Exec_Burn", menuName = "CycloneGames/GameplayAbilities/Samples/Execution Definition/Exec_Burn")]
    public class ExecCalcSO_Burn : GameplayEffectExecutionCalculationSO
    {
        public override GameplayEffectExecutionCalculation CreateExecution()
        {
            return new ExecCalc_Burn();
        }
    }
}
