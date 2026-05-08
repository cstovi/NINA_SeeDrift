using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace NINA.Plugin.SeeDrift.Models {

    /// <summary>
    /// Lets folder replay add hundreds of samples with one <see cref="NotifyCollectionChangedAction.Reset"/>
    /// instead of hundreds of Add notifications (WPF/OxyPlot struggled with per-frame updates).
    /// </summary>
    public sealed class ObservableDriftSamples : ObservableCollection<DriftSample> {
        private bool _suppress;

        public void ReplaceAll(IReadOnlyList<DriftSample> items) {
            _suppress = true;
            try {
                Items.Clear();
                foreach (var s in items)
                    Items.Add(s);
            } finally {
                _suppress = false;
                OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
                OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
                OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
            }
        }

        protected override void OnCollectionChanged(NotifyCollectionChangedEventArgs e) {
            if (!_suppress)
                base.OnCollectionChanged(e);
        }
    }
}
