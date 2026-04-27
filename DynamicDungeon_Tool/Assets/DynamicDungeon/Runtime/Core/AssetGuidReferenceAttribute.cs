using System;
using UnityEngine;

namespace DynamicDungeon.Runtime.Core
{
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
    public sealed class AssetGuidReferenceAttribute : PropertyAttribute
    {
        private readonly Type _assetType;

        public Type AssetType
        {
            get
            {
                return _assetType;
            }
        }

        public AssetGuidReferenceAttribute(Type assetType)
        {
            _assetType = assetType;
        }
    }
}
