using System.ComponentModel.Composition;
using System.Windows;

namespace NINA.Plugin.SeeDrift {
    [Export(typeof(ResourceDictionary))]
    public partial class Resources : ResourceDictionary {
        public Resources() {
            InitializeComponent();
        }
    }
}
