namespace DynamicDungeon.Runtime.Graph
{
    public readonly struct MigrationResult
    {
        public readonly bool Success;
        public readonly int FromVersion;
        public readonly int ToVersion;
        public readonly string ErrorMessage;

        public MigrationResult(bool success, int fromVersion, int toVersion, string errorMessage)
        {
            Success = success;
            FromVersion = fromVersion;
            ToVersion = toVersion;
            ErrorMessage = errorMessage;
        }
    }
}
