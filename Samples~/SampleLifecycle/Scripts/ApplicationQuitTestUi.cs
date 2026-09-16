using AceLand.Lifecycle;

namespace AceLand.Sample.LifeCycle.Scripts
{
    public class ApplicationQuitTestUi : ModuleTester
    {
        /// <summary>
        /// Use ApplicationQuitPipeline.Quit() to have the best quit pipeline handling
        /// </summary>
        protected override void RunTest()
        {
            ApplicationQuitPipeline.Quit();
        }
    }
}