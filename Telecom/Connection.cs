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
      daily_availability_ = new LinkedList<double>();
    }

    public void AddMeasurement(double latency, double rate, double t) {
      double day = KSPUtil.dateTimeFormatter.Day;
      double t_in_days = t / day;
      double new_day = Math.Floor(t_in_days);
      if (current_day_ == null) {
        current_day_ = new_day;
        return;
      }
      within_sla = latency <= latency_threshold && rate >= rate_threshold;

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

    public void Serialize(ConfigNode node) {
      foreach (var availability in daily_availability_) {
        node.AddValue("daily_availability", availability);
      }
      node.AddValue("current_day", current_day_);
      node.AddValue("day_fraction_within_sla", day_fraction_within_sla_);
      node.AddValue("day_fraction", day_fraction_);
    }

    public void Load(ConfigNode node) {
      daily_availability_ =
        new LinkedList<double>(node.GetValues("daily_availability").Select(double.Parse));
      current_day_ = double.Parse(node.GetValue("current_day"));
      day_fraction_within_sla_ = double.Parse(node.GetValue("day_fraction_within_sla"));
      day_fraction_ = double.Parse(node.GetValue("day_fraction"));
      UpdateAvailability();
    }

    private void UpdateAvailability() {
      availability = daily_availability_.Sum() / daily_availability_.Count;
    }

    public double latency_threshold { get; }
    public double rate_threshold { get; }
    public double availability { get; private set; }
    public double availability_yesterday => daily_availability_.Count == 0 ? 0 : daily_availability_.Last();

    public string tx_name { get; }
    public string rx_name { get; }

    public bool within_sla { get; private set; }
    public int window { get; private set; }
    public int days => daily_availability_.Count;

    private LinkedList<double> daily_availability_;
    private double? current_day_;
    private double day_fraction_within_sla_;
    private double day_fraction_;
  }
}
