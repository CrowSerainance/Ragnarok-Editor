using System.Collections.Generic;
using System.Windows;

namespace ROMapOverlayEditor.Ui
{
    public sealed class SimpleModeController
    {
        private readonly List<UIElement> _advanced = new();

        public void RegisterAdvanced(UIElement el)
        {
            if (el != null) _advanced.Add(el);
        }

        public void Apply(bool advanced)
        {
            var vis = advanced ? Visibility.Visible : Visibility.Collapsed;
            foreach (var el in _advanced)
                el.Visibility = vis;
        }
    }
}
