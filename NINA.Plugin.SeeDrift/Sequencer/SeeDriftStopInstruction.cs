using System;
using System.ComponentModel.Composition;
using System.Threading;
using System.Threading.Tasks;
using NINA.Core.Model;
using NINA.Sequencer.SequenceItem;

namespace NINA.Plugin.SeeDrift.Sequencer {

    [Export(typeof(ISequenceItem))]
    [ExportMetadata("Name", "SeeDrift Stop")]
    [ExportMetadata("Description", "Stops SeeDrift tracking and saves the completed target's drift trace to the nightly HTML report. Place after the imaging loop for each target.")]
    [ExportMetadata("Icon", "SeeDrift_Icon")]
    [ExportMetadata("Category", "SeeDrift")]
    public class SeeDriftStopInstruction : SequenceItem {

        private readonly SeeDriftPlugin _plugin;

        [ImportingConstructor]
        public SeeDriftStopInstruction(SeeDriftPlugin plugin) {
            _plugin = plugin;
            Name = "SeeDrift Stop";
            if (System.Windows.Application.Current?.Resources["SeeDrift_Icon"]
                    is System.Windows.Media.GeometryGroup icon)
                Icon = icon;
        }

        private SeeDriftStopInstruction(SeeDriftStopInstruction cloneMe) : this(cloneMe._plugin) { }

        public override Task Execute(IProgress<ApplicationStatus> progress, CancellationToken token) {
            _plugin.DriftTracker.Disarm();
            return Task.CompletedTask;
        }

        public override object Clone() => new SeeDriftStopInstruction(this);
    }
}
