using System;
using Anatawa12.AvatarOptimizer;
using UnityEditor;
using UnityEngine;

namespace Poyo.CandyBox.AaoMergeBoneHelper.Editor
{
    internal static class AaoMergeBoneType
    {
        private const string MergeBoneTypeName = "Anatawa12.AvatarOptimizer.MergeBone";
        private const string AvoidNameConflictFieldName = "avoidNameConflict";

        private static Type _componentType;
        private static bool _resolved;

        internal static bool IsAvailable
        {
            get { return ComponentType != null; }
        }

        internal static Type ComponentType
        {
            get
            {
                if (!_resolved)
                {
                    _componentType = Resolve();
                    _resolved = true;
                }

                return _componentType;
            }
        }

        internal static Component Get(GameObject gameObject)
        {
            Type type = ComponentType;
            return gameObject != null && type != null
                ? gameObject.GetComponent(type)
                : null;
        }

        internal static bool Has(GameObject gameObject)
        {
            return Get(gameObject) != null;
        }

        internal static Component Add(GameObject gameObject)
        {
            // NOTE: 多重追加を禁止する属性があるため、呼び出し側が先に存在を確認する。
            return gameObject != null && ComponentType != null
                ? Undo.AddComponent(gameObject, ComponentType)
                : null;
        }

        internal static void Remove(Component component)
        {
            if (component != null)
            {
                Undo.DestroyObjectImmediate(component);
            }
        }

        internal static bool GetAvoidNameConflict(Component component)
        {
            if (component == null)
            {
                return true;
            }

            var serializedObject = new SerializedObject(component);
            serializedObject.UpdateIfRequiredOrScript();
            SerializedProperty property =
                serializedObject.FindProperty(AvoidNameConflictFieldName);
            return property == null || property.boolValue;
        }

        private static Type Resolve()
        {
            foreach (Type type in TypeCache.GetTypesDerivedFrom<AvatarTagComponent>())
            {
                if (string.Equals(type.FullName, MergeBoneTypeName, StringComparison.Ordinal))
                {
                    return type;
                }
            }

            return null;
        }
    }
}
