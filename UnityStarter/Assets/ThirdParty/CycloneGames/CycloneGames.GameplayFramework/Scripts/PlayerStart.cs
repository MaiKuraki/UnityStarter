using UnityEngine;

namespace CycloneGames.GameplayFramework
{
    public class PlayerStart : Actor
    {
        [SerializeField] private Transform Arrow;

        protected override void Awake()
        {
            base.Awake();

            if (Arrow && Arrow.gameObject && Arrow.gameObject.activeInHierarchy) Arrow.gameObject.SetActive(false);
        }
    }
}