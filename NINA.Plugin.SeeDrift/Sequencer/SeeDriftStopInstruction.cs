using System;
using System.ComponentModel.Composition;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using NINA.Core.Model;
using NINA.Plugin.SeeDrift.Utility;
using NINA.Sequencer.SequenceItem;

namespace NINA.Plugin.SeeDrift.Sequencer {

    [Export(typeof(ISequenceItem))]
    [ExportMetadata("Name", "SeeDrift Stop")]
    [ExportMetadata("Description", "Stops SeeDrift arm window and plate-solves LIGHT frames saved under the NINA image path during that window, then appends to the nightly HTML report.")]
    [ExportMetadata("Icon", "SeeDrift_Icon")]
    [ExportMetadata("Category", "SeeDrift")]
    public class SeeDriftStopInstruction : SequenceItem {

        private readonly SeeDriftPlugin _plugin;

        [ImportingConstructor]
        public SeeDriftStopInstruction(SeeDriftPlugin plugin) {
            _plugin = plugin;
            Name = "SeeDrift Stop";
            SeeDriftIconRegistration.Register();
            if (Application.Current?.Resources["SeeDrift_Icon"] is GeometryGroup icon)
                Icon = icon;
        }

        private SeeDriftStopInstruction(SeeDriftStopInstruction cloneMe) : this(cloneMe._plugin) { }

        public override async Task Execute(IProgress<ApplicationStatus> progress, CancellationToken token) {
            await _plugin.DriftTracker.DisarmAsync(progress, token).ConfigureAwait(false);
        }

        public override object Clone() => new SeeDriftStopInstruction(this);
    }
}
