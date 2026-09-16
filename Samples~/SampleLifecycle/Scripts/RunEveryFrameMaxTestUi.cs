using AceLand.Lifecycle;
using UnityEngine;

namespace AceLand.Sample.LifeCycle.Scripts
{
    public class RunEveryFrameMaxTestUi : ModuleTester
    {
        [SerializeField] private PlayerLoopPointsUi pointUi;
        
        private const int FRAMES = 1000;
        private int _id;
        
        protected override void RunTest()
        {
            _id++;
            Debug.Log($"Run Every Frame max {FRAMES} frames ({_id}) called: {Time.frameCount}");

            LifecycleFrame.RunEveryFrame(() =>
                {
                    Debug.Log($"Run Every Frame max {FRAMES} frames ({_id}): {Time.frameCount}");
                },
                point: pointUi.CurrentPoint,
                maxFrames: FRAMES
            );
        }
    }
}