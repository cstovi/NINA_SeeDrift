using OxyPlot;

namespace NINA.Plugin.SeeDrift.Utility {

    /// <summary>
    /// OxyPlot's <see cref="TrackerManipulator"/> can leave the tracker visible after the pointer
    /// leaves the snap radius (default locks to the first series and does not hide on a miss).
    /// Hide the tracker whenever no data point is within <see cref="TrackerManipulator.FiresDistance"/>.
    /// </summary>
    internal sealed class DismissWhenAwayTrackerManipulator : TrackerManipulator {

        public DismissWhenAwayTrackerManipulator(IPlotView plotView) : base(plotView) {
            LockToInitialSeries = false;
        }

        public override void Delta(OxyMouseEventArgs e) {
            base.Delta(e);
            e.Handled = true;

            var model = PlotView.ActualModel;
            if (model == null)
                return;

            if (!model.PlotArea.Contains(e.Position.X, e.Position.Y)) {
                PlotView.HideTracker();
                model.RaiseTrackerChanged(null);
                return;
            }

            for (var i = model.Series.Count - 1; i >= 0; i--) {
                var series = model.Series[i];
                if (!series.IsVisible)
                    continue;
                var hit = series.GetNearestPoint(e.Position, interpolate: false);
                if (hit != null && hit.Position.DistanceTo(e.Position) < FiresDistance)
                    return;
            }

            PlotView.HideTracker();
            model.RaiseTrackerChanged(null);
        }
    }
}
