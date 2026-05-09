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
    [ExportMetadata("Name", "SeeDrift Start")]
    [ExportMetadata("Description", "Arms SeeDrift — Start time used with Stop to select Saved image to … lines in NINA session logs (arm→disarm) for plate solving and the drift HTML report.")]
    [ExportMetadata("Icon", "SeeDrift_Icon")]
    [ExportMetadata("Category", "SeeDrift")]
    public class SeeDriftStartInstruction : SequenceItem {

        private readonly SeeDriftPlugin _plugin;

        [ImportingConstructor]
        public SeeDriftStartInstruction(SeeDriftPlugin plugin) {
            _plugin = plugin;
            Name = "SeeDrift Start";
            SeeDriftIconRegistration.Register();
            if (Application.Current?.Resources["SeeDrift_Icon"] is GeometryGroup icon)
                Icon = icon;
        }

        private SeeDriftStartInstruction(SeeDriftStartInstruction cloneMe) : this(cloneMe._plugin) { }

        public override Task Execute(IProgress<ApplicationStatus> progress, CancellationToken token) {
            _plugin.DriftTracker.Arm();
            return Task.CompletedTask;
        }

        public override object Clone() => new SeeDriftStartInstruction(this);
    }
}
