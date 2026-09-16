using AceLand.Lifecycle;
using UnityEngine;

namespace AceLand.Sample.LifeCycle.Scripts
{
    public class RunAfterFramesTestUi : ModuleTester
    {
        [SerializeField] private PlayerLoopPointsUi pointUi;

        private const int FRAMES = 1000;
        private int _id;

        protected override void RunTest()
        {
            _id++;
            Debug.Log($"Run After {FRAMES} frames ({_id})  called: {Time.frameCount}");

            LifecycleFrame.RunAfterFrames(() =>
                {
                    Debug.Log($"Run After {FRAMES} Frame ({_id})  finished: {Time.frameCount}");
                },
                point: pointUi.CurrentPoint,
                frames: FRAMES
            );
        }
    }
}