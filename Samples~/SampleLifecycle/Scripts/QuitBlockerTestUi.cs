using System;
using System.Threading;
using System.Threading.Tasks;
using AceLand.Lifecycle;
using UnityEngine;
using Random = UnityEngine.Random;

namespace AceLand.Sample.LifeCycle.Scripts
{
    public class QuitBlockerTestUi : ModuleTester
    {
        private int jobId = 0;
        
        protected override void RunTest()
        {
            var countdown = Random.Range(5, 10);
            var token = LifecycleToken.ApplicationAlive;
            Task.Run(async () => await StartBlockJob(countdown, token), token);
        }
        
        /// <summary>
        /// By using BeginJob() in QuitBlockerModule,
        ///     the blocker will wait for the whole process complete.
        /// If Application Quit Pipeline is already enter WaitingForBlocker phase,
        ///     blocker will not begin any new job.
        /// </summary>
        private async Task StartBlockJob(int countdown, CancellationToken token)
        {
            var id = jobId;
            
            jobId++;
            
            if (!ApplicationQuitPipeline.TryBeginWork($"Test Block Job - {id}", out var scope))
            {
                Debug.LogWarning("Quit in progress — new blocker rejected.");
                return;
            }

            try
            {
                using (scope)
                {
                    Debug.Log($"Fake job ({id}) started — quit is now blocked for {countdown}s");
                    while (countdown > 0)
                    {
                        Debug.Log($"Fake job ({id}) ... {countdown}");
                        await Task.Delay(1000, token);
                        countdown--;
                    }
                    Debug.Log($"Fake job ({id}) finished");
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                throw;
            }
        }
    }
}