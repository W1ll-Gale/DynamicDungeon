namespace DynamicDungeon.Runtime.Core
{
    public readonly struct ExecutionResult
    {
        public readonly bool IsSuccess;
        public readonly string ErrorMessage;
        public readonly WorldSnapshot Snapshot;
        public readonly bool WasCancelled;

        public ExecutionResult(bool isSuccess, string errorMessage, WorldSnapshot snapshot, bool wasCancelled)
        {
            IsSuccess = isSuccess;
            ErrorMessage = errorMessage;
            Snapshot = snapshot;
            WasCancelled = wasCancelled;
        }
    }
}
