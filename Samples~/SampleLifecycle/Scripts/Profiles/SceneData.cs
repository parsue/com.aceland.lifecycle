using UnityEngine;

namespace AceLand.Sample.LifeCycle.Scripts.Profiles
{
    [CreateAssetMenu(fileName = "SceneData", menuName = "AceLand/Sample/LifeCycle/SceneData")]
    public sealed class SceneData : ScriptableObject
    {
        [SerializeField] private Canvas[] prefabs;
        
        public Canvas[] Prefabs => prefabs;
    }
}