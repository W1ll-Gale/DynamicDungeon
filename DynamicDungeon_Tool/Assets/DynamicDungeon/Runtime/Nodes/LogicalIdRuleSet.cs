using System;

namespace DynamicDungeon.Runtime.Nodes
{
    [Serializable]
    public sealed class LogicalIdRule
    {
        public bool Enabled = true;
        public int MaskSlot;
        public int SourceLogicalId = -1;
        public int TargetLogicalId = 1;
    }

    [Serializable]
    public sealed class LogicalIdRuleSet
    {
        public LogicalIdRule[] Rules = Array.Empty<LogicalIdRule>();
    }
}
