using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace σκοπός {
  public class Monitoring {
    public void Monitor(int window, double availability) {
      if (alerted_availabilities_.Contains(availability)) {
        return;
      }
      if (monitored_availability < availability) {
        alerted_availabilities_.Add(availability);
        var tx = Telecom.Instance.network.GetStation(tx_name);
        var rx = Telecom.Instance.network.GetStation(rx_name);
        TimeWarp.fetch.CancelAutoWarp();
        TimeWarp.SetRate(
            TimeWarp.fetch.warpRates.IndexOf(1),
            instant: true,
            postScreenMessage: false);
        ScreenMessages.PostScreenMessage(
            $@"WARNING: The availability of the link from {tx.nodeName} to {
                rx.nodeName} is below the target availability of {
                availability:P1} over the past {monitoring_window} days.",
            30, ScreenMessageStyle.UPPER_CENTER, XKCDColors.Orange);
        KSP.UI.Screens.MessageSystem.Instance.AddMessage(
            new KSP.UI.Screens.MessageSystem.Message(
                messageTitle: $"Out of SLA on {tx.nodeName} to {rx.nodeName}",
                message: $@"The availability of the link from {tx.nodeName} to {
                rx.nodeName} went below the target availability of {
                availability:P1} over a {
                monitoring_window}-day period ending on {
                RSS.current_time.Date:s}.",
                KSP.UI.Screens.MessageSystemButton.MessageButtonColor.ORANGE,
                KSP.UI.Screens.MessageSystemButton.ButtonIcons.ALERT));
      }
    }

    private HashSet<double> alerted_availabilities_ = new HashSet<double>();
  }
}
