using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace σκοπός {
// A class that tracks the availability of some service in daily buckets
// Metrics derived from daily availability can be registered.
public class Service {
  public Service(int window_size) {
    this.window_size = window_size;
  }

  public void ReportAvailability(bool available, double t) {
    this.available = available;

    double day = KSPUtil.dateTimeFormatter.Day;
    double t_in_days = t / day;
    double new_day = Math.Floor(t_in_days);

    if (current_day_ == null) {
      current_day_ = new_day;
      return;
    }

    if (new_day > current_day_) {
      if (available) {
        daily_availability_.AddLast(day_fraction_available_ + (1 - day_fraction_));
      } else {
        daily_availability_.AddLast(day_fraction_available_);
      }
      for (int i = 0; i < new_day - current_day_ - 1; ++i) {
        daily_availability_.AddLast(available ? 1 : 0);
      }
      day_fraction_ = t_in_days - new_day;
      day_fraction_available_ = available ? day_fraction_ : 0;
      while (daily_availability_.Count > window_size) {
        daily_availability_.RemoveFirst();
      }
      foreach (var metric in metrics_) {
        metric.UpdateTimeline(daily_availability_.Reverse(), (int)current_day_.Value - 1);
      }
    } else {
      day_fraction_available_ += available ? (t_in_days - new_day) - day_fraction_ : 0;
      day_fraction_ = t_in_days - new_day;
    }
    current_day_ = new_day;
    foreach (var metric in metrics_) {
      metric.UpdateCurrentDay(day_fraction_available_, day_fraction_);
    }
  }

  public void RegisterMetric(AvailabilityMetric metric) {
    Telecom.Log($"Registering metric {metric.GetType().Name}");
    if (!metric.ComputableFrom(window_size)) {
      throw new ArgumentException($"Metric is not computable from {window_size} days");
    }
    metrics_.Add(metric);
    if (current_day_ != null) {
      metric.UpdateTimeline(daily_availability_.Reverse(), (int)current_day_.Value - 1);
      metric.UpdateCurrentDay(day_fraction_available_, day_fraction_);
    }
  }

  public void Save(ConfigNode node) {
    foreach (var availability in daily_availability_) {
      node.AddValue("daily_availability", availability);
    }
    if (current_day_ != null) {
      node.AddValue("current_day", current_day_);
    }
    node.AddValue("day_fraction_connected", day_fraction_available_);
    node.AddValue("day_fraction", day_fraction_);
  }

  public void Load(ConfigNode node) {
    daily_availability_ =
        new LinkedList<double>(node.GetValues("daily_availability").Select(double.Parse));
    if (node.HasValue("current_day")) {
      current_day_ = double.Parse(node.GetValue("current_day"));
    }
    day_fraction_available_ = double.Parse(node.GetValue("day_fraction_connected"));
    day_fraction_ = double.Parse(node.GetValue("day_fraction"));
    foreach (var metric in metrics_) {
      metric.UpdateTimeline(daily_availability_.Reverse(), (int)current_day_.Value - 1);
    }
  }

  public bool available { get; private set; }
  public int window_size { get; private set; }

  private LinkedList<double> daily_availability_ = new LinkedList<double>();
  private double? current_day_;
  private double day_fraction_available_;
  private double day_fraction_;

  private List<AvailabilityMetric> metrics_ = new List<AvailabilityMetric>();
}
}  // namespace σκοπός
