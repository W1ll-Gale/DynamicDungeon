using System.Collections.Generic;

namespace DynamicDungeon.ConstraintDungeon
{
    public sealed class ValidationReport
    {
        private readonly List<ValidationIssue> issues = new List<ValidationIssue>();

        public IReadOnlyList<ValidationIssue> Issues => issues;
        public bool IsValid => ErrorCount == 0;

        public IEnumerable<string> Errors
        {
            get
            {
                foreach (ValidationIssue issue in issues)
                {
                    if (issue.IsError)
                    {
                        yield return issue.Message;
                    }
                }
            }
        }

        public IEnumerable<string> Warnings
        {
            get
            {
                foreach (ValidationIssue issue in issues)
                {
                    if (!issue.IsError)
                    {
                        yield return issue.Message;
                    }
                }
            }
        }

        public int ErrorCount
        {
            get
            {
                int count = 0;
                foreach (ValidationIssue issue in issues)
                {
                    if (issue.IsError)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        public void AddError(string message, string nodeId = null, string fromNodeId = null, string toNodeId = null)
        {
            issues.Add(new ValidationIssue(true, message, nodeId, fromNodeId, toNodeId));
        }

        public void AddWarning(string message, string nodeId = null, string fromNodeId = null, string toNodeId = null)
        {
            issues.Add(new ValidationIssue(false, message, nodeId, fromNodeId, toNodeId));
        }
    }

    public readonly struct ValidationIssue
    {
        public readonly bool IsError;
        public readonly string Message;
        public readonly string NodeId;
        public readonly string FromNodeId;
        public readonly string ToNodeId;

        public ValidationIssue(bool isError, string message, string nodeId = null, string fromNodeId = null, string toNodeId = null)
        {
            IsError = isError;
            Message = message;
            NodeId = nodeId;
            FromNodeId = fromNodeId;
            ToNodeId = toNodeId;
        }
    }
}
