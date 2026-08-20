using AceLand.Lifecycle;
using UnityEngine;

namespace AceLand.Sample.LifeCycle.Scripts
{
    public class RunEveryFrameMaxTestUi : ModuleTester
    {
        [SerializeField] private PlayerLoopPointsUi pointUi;
        
        private int _id;
        
        protected override void RunTest()
        {
            _id++;
            Debug.Log($"Run Every Frame max 300 frames ({_id}) called: {Time.frameCount}");

            LifecycleFrame.RunEveryFrame(() =>
                {
                    Debug.Log($"Run Every Frame max 300 frames ({_id}): {Time.frameCount}");
                },
                point: pointUi.CurrentPoint,
                maxFrames: 300
            );
        }
    }
}