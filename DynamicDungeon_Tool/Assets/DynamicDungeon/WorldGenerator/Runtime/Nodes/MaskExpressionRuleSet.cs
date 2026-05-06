using System;

namespace DynamicDungeon.Runtime.Nodes
{
    public enum MaskExpressionOperation
    {
        Replace = 0,
        OR = 1,
        AND = 2,
        XOR = 3,
        Subtract = 4
    }

    [Serializable]
    public sealed class MaskExpressionRule
    {
        public bool Enabled = true;
        public int MaskSlot = 1;
        public MaskExpressionOperation Operation = MaskExpressionOperation.OR;
        public bool Invert;
    }

    [Serializable]
    public sealed class MaskExpressionRuleSet
    {
        public MaskExpressionRule[] Rules = Array.Empty<MaskExpressionRule>();
    }
}
