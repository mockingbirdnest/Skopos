using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using RealAntennas;

namespace σκοπός {

  // A Connection represents a directed link between two earth stations with a
  // minimal data rate and a maximal latency. It keeps track of the availability
  // of that link.
  public class Connection {
    public Connection(ConfigNode definition) {
      tx_name = definition.GetValue("tx");
      rx_name = definition.GetValue("rx");
      latency_threshold = double.Parse(definition.GetValue("latency"));
      data_rate = double.Parse(definition.GetValue("rate"));
      window_size = int.Parse(definition.GetValue("window"));
      monitoring_window =  definition.HasValue("monitoring_window")
          ? int.Parse(definition.GetValue("monitoring_window"))
          : 2;
    }

    public void AddMeasurement(double latency, double rate, double t) {
      double day = KSPUtil.dateTimeFormatter.Day;
      double t_in_days = t / day;
      double new_day = Math.Floor(t_in_days);
      within_sla = latency <= latency_threshold && rate >= data_rate;
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
      while (daily_availability_.Count > window_size) {
        daily_availability_.RemoveFirst();
      }
      if (new_day > current_day_) {
        foreach (var metric in metrics_) {
          metric.UpdateTimeline(daily_availability_.Reverse(), current_day_.Value - 1);
        }
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
      foreach (var metric in metrics_) {
        metric.UpdateTimeline(daily_availability_.Reverse(), current_day_.Value - 1);
      }
    }

    public void RegisterMetric(AvailabilityMetric metric) {
      if (!metric.ComputableFrom(window_size)) {
        throw new ArgumentException($"Metric is not computable from {window_size} days");
      }
      metrics_.Add(metric);
      if (current_day_ != null) {
        metric.UpdateTimeline(daily_availability_.Reverse(), current_day_.Value - 1);
        metric.UpdateCurrentDay(day_fraction_within_sla_, day_fraction_);
      }
    }

    // TODO(egg): Initialize.
    public RACommNode tx { get; }
    public RACommNode rx { get; }

    public double latency_threshold { get; }
    public double data_rate { get; }

    public string tx_name { get; }
    public string rx_name { get; }

    public int window_size { get; private set; }

    private LinkedList<double> daily_availability_ = new LinkedList<double>();
    private double? current_day_;
    private double day_fraction_within_sla_;
    private double day_fraction_;

    private List<AvailabilityMetric> metrics_;

    private bool active_;
  }
}
