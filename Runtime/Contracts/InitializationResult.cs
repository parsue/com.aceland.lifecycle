namespace AceLand.Lifecycle
{
    public readonly struct InitializationResult
    {
        public readonly int Total;
        public readonly int Ready;
        public readonly int Failed;
        public readonly int Skipped;
        public readonly double Milliseconds;

        public bool HasErrors => Failed > 0 || Skipped > 0;

        internal InitializationResult(int total, int ready, int failed, int skipped, double ms)
        {
            Total = total; Ready = ready; Failed = failed; Skipped = skipped; Milliseconds = ms;
        }

        public override string ToString() =>
            $"{Ready}/{Total} ready, {Failed} failed, {Skipped} skipped, {Milliseconds:0} ms";
    }
}