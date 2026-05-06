using DynamicDungeon.Runtime.Core;

namespace DynamicDungeon.Runtime.Component
{
    public sealed class GenerationCompletedArgs
    {
        public WorldSnapshot Snapshot;
        public bool IsSuccess;
        public bool WasBakedFallback;
        public string ErrorMessage;
    }
}
