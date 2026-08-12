using UnityEngine;

namespace AceLand.Sample.LifeCycle.Scripts.Profiles
{
    [CreateAssetMenu(fileName = "SceneData", menuName = "AceLand/Sample/LifeCycle/SceneData")]
    public sealed class SceneData : ScriptableObject
    {
        [SerializeField] private Canvas sceneInfoUiPrefab;
        
        public Canvas SceneInfoUiPrefab => sceneInfoUiPrefab;
    }
}