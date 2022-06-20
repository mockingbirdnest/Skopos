using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace σκοπός {
  public class Connection {
    public Connection(ConfigNode definition) {
      tx_name = definition.GetValue("tx");
      rx_name = definition.GetValue("rx");
      latency_threshold = double.Parse(definition.GetValue("latency"));
      rate_threshold = double.Parse(definition.GetValue("rate"));
      window = int.Parse(definition.GetValue("window"));
      monitoring_window =  definition.HasValue("monitoring_window")
          ? int.Parse(definition.GetValue("monitoring_window"))
          : 2;
    }

    public void AddMeasurement(double latency, double rate, double t) {
      double day = KSPUtil.dateTimeFormatter.Day;
      double t_in_days = t / day;
      double new_day = Math.Floor(t_in_days);
      within_sla = latency <= latency_threshold && rate >= rate_threshold;
      if (!active_) {
        return;
      }
      if (current_day_ == null) {
        current_day_ = new_day;
        return;
      }

      if (new_day > current_day_) {
        if (within_sla) {
          daily_availability_.AddLast(day_fraction_within_sla_ + (1 - day_fraction_));
        } else {
          daily_availability_.AddLast(day_fraction_within_sla_);
        }
        for (int i = 0; i < new_day - current_day_ - 1; ++i) {
          daily_availability_.AddLast(within_sla ? 1 : 0);
        }
        day_fraction_ = t_in_days - new_day;
        day_fraction_within_sla_ = within_sla ? day_fraction_ : 0;
      } else {
        day_fraction_within_sla_ += within_sla ? (t_in_days - new_day) - day_fraction_ : 0;
        day_fraction_ = t_in_days - new_day;
      }
      while (daily_availability_.Count > window) {
        daily_availability_.RemoveFirst();
      }
      if (new_day > current_day_) {
        UpdateAvailability();
      }
      current_day_ = new_day;
    }

    public void Activate() {
      active_ = true;
    }

    public void Deactivate() {
      daily_availability_.Clear();
      current_day_ = null;
      active_ = false;
    }

    public void Serialize(ConfigNode node) {
      foreach (var availability in daily_availability_) {
        node.AddValue("daily_availability", availability);
      }
      if (current_day_ != null) {
        node.AddValue("current_day", current_day_);
      }
      node.AddValue("day_fraction_within_sla", day_fraction_within_sla_);
      node.AddValue("day_fraction", day_fraction_);
      node.AddValue("active", active_);
      foreach (double availability in alerted_availabilities_) {
        node.AddValue("alerted_availability", availability);
      }
    }

    public void Load(ConfigNode node) {
      daily_availability_ =
          new LinkedList<double>(node.GetValues("daily_availability").Select(double.Parse));
      alerted_availabilities_ = new HashSet<double>(
          node.GetValues("alerted_availability").Select(double.Parse));
      if (node.HasValue("current_day")) {
        current_day_ = double.Parse(node.GetValue("current_day"));
      }
      day_fraction_within_sla_ = double.Parse(node.GetValue("day_fraction_within_sla"));
      day_fraction_ = double.Parse(node.GetValue("day_fraction"));
      active_ = bool.Parse(node.GetValue("active"));
      UpdateAvailability();
    }

    private void UpdateAvailability() {
      availability = daily_availability_.Count == 0
          ? 0
          : daily_availability_.Sum() / daily_availability_.Count;
      monitored_availability_base = daily_availability_.Count == 0
          ? 0
          : daily_availability_.TakeLast(monitoring_base_window).Sum() /
            monitoring_base_window;
      alerted_availabilities_ =
          new HashSet<double>(alerted_availabilities_.Where(a => a > monitored_availability));
    }

    public void Monitor(double availability) {
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

    public double latency_threshold { get; }
    public double rate_threshold { get; }
    public double availability { get; private set; }
    private double monitored_availability_base;
    public double monitored_availability =>
        (monitored_availability_base * monitoring_base_window + day_fraction_within_sla_) /
        (monitoring_base_window + day_fraction_);

    public string tx_name { get; }
    public string rx_name { get; }

    public bool within_sla { get; private set; }
    public int window { get; private set; }
    public int monitoring_window { get; private set; }
    private int monitoring_base_window => Math.Min(days, monitoring_window - 1);
    public int days => daily_availability_.Count;

    private LinkedList<double> daily_availability_ = new LinkedList<double>();
    private double? current_day_;
    private double day_fraction_within_sla_;
    private double day_fraction_;

    private HashSet<double> alerted_availabilities_ = new HashSet<double>();

    private bool active_;
  }
}
