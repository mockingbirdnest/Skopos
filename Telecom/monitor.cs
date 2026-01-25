namespace σκοπός {
  public class Monitor {
    public Monitor(string service_name,
                   AvailabilityMetric metric,
                   double availability_threshold) {
      service_name_ = service_name;
      metric_ = metric;
      availability_threshold_ = availability_threshold;
    }

    public void AlertIfNeeded() {
      var telecom = Telecom.Instance;
      if (metric_.availability >= availability_threshold_) {
        if (alerted_ && (telecom.last_universal_time > last_restore_time_ + telecom.max_alert_rate_in_days_ * 86400)) {
          alerted_ = false;
          last_restore_time_ = telecom.last_universal_time;
          ScreenMessages.PostScreenMessage(
              $@"{service_name_}: availability is back to normal",
              30, ScreenMessageStyle.UPPER_CENTER, XKCDColors.Pear);
          KSP.UI.Screens.MessageSystem.Instance.AddMessage(
              new KSP.UI.Screens.MessageSystem.Message(
                  messageTitle: $"{service_name_} is back to normal",
                  message: $@"The availability is {metric_.description}, back above the target of {availability_threshold_:P2}.",
                  KSP.UI.Screens.MessageSystemButton.MessageButtonColor.GREEN,
                  KSP.UI.Screens.MessageSystemButton.ButtonIcons.COMPLETE));
        }
      } else {
        if (!alerted_ && (telecom.last_universal_time > last_alert_time_ + telecom.max_alert_rate_in_days_ * 86400)) {
          alerted_ = true;
          last_alert_time_ = telecom.last_universal_time;
          if (telecom.stop_warp_in_sim_ || !RP0.SpaceCenterManagement.Instance.IsSimulatedFlight) {
            TimeWarp.fetch.CancelAutoWarp();
            TimeWarp.SetRate(
                TimeWarp.fetch.warpRates.IndexOf(1),
                instant: true,
                postScreenMessage: false);
          }
          ScreenMessages.PostScreenMessage(
                $@"WARNING: {service_name_}: availability is below {availability_threshold_:P2}.",
                30, ScreenMessageStyle.UPPER_CENTER, XKCDColors.Orange);
          KSP.UI.Screens.MessageSystem.Instance.AddMessage(
                new KSP.UI.Screens.MessageSystem.Message(
                    messageTitle: $"Out of SLA on {service_name_}",
                    message: $@"The availability is {metric_.description}, below the target of {availability_threshold_:P2}.",
                    KSP.UI.Screens.MessageSystemButton.MessageButtonColor.ORANGE,
                    KSP.UI.Screens.MessageSystemButton.ButtonIcons.ALERT));
        }
      }
    }

    public string description => metric_.description;

    private string service_name_;
    private AvailabilityMetric metric_;
    private double availability_threshold_;
    private bool alerted_ = false;
    private double last_alert_time_ = 0;
    private double last_restore_time_ = 0;
  }
}
