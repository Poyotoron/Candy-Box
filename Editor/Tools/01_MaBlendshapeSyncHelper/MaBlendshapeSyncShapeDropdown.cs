using System;
using System.Collections.Generic;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace Poyo.CandyBox.MaBlendshapeSyncHelper.Editor
{
    internal sealed class MaBlendshapeSyncShapeDropdown : AdvancedDropdown
    {
        private readonly string _title;
        private readonly IReadOnlyList<string> _names;
        private readonly Action<int> _onSelected;

        internal MaBlendshapeSyncShapeDropdown(
            AdvancedDropdownState state,
            string title,
            IReadOnlyList<string> names,
            Action<int> onSelected)
            : base(state)
        {
            _title = title;
            _names = names;
            _onSelected = onSelected;
            minimumSize = new Vector2(260f, 320f);
        }

        protected override AdvancedDropdownItem BuildRoot()
        {
            var root = new AdvancedDropdownItem(_title);
            for (int nameIndex = 0; nameIndex < _names.Count; nameIndex++)
            {
                var item = new AdvancedDropdownItem(_names[nameIndex])
                {
                    id = nameIndex,
                };
                root.AddChild(item);
            }

            return root;
        }

        protected override void ItemSelected(AdvancedDropdownItem item)
        {
            _onSelected(item.id);
        }
    }
}
