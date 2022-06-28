using System;
using System.Collections.Generic;
using System.Linq;

namespace σκοπός {

  // A set of properties derived from a timeline of daily availabilities.
  public abstract class AvailabilityMetric {
    // Whether the metric can be computed from a moving window of the given number of days,
    // in addition to availability in the current partial day.
    public abstract bool ComputableFrom(int days);
    // Update the timeline of full days, most recent first.
    public abstract void UpdateTimeline(IEnumerable<double> daily_availabilities, double last_day);
    // Update the current partial day.
    public abstract void UpdateCurrentDay(double day_fraction_available, double day_fraction_elapsed);
  }

  // Availability measured over a moving window of full days, e.g., 98.7% over
  // a period of 14 calendar days ending yesterday (included).
  public class MovingWindowAvailability : AvailabilityMetric {
    public MovingWindowAvailability(int window_size) {
      this.window_size = window_size;
    }

    public override bool ComputableFrom(int days) {
      return days >= window_size;
    }

    public override void UpdateTimeline(IEnumerable<double> daily_availabilities, double last_day) {
      var availabilities = daily_availabilities.Take(window_size).ToArray();
      availability = availabilities.Sum() / availabilities.Count();
      window_filling = availabilities.Count();
    }

    public override void UpdateCurrentDay(double day_fraction_available, double day_fraction_elapsed) {}

    public double availability { get; private set; }
    public int window_size { get; }
    public int window_filling { get; private set; }
  }

  // Availability measured over a moving window that includes the current day,
  // e.g., 98.7% over the last 15 calendar days, including today.
  public class PartialMovingWindowAvailability : AvailabilityMetric {
    public PartialMovingWindowAvailability(int window_size) {
      this.window_size = window_size;
    }

    public override bool ComputableFrom(int days) {
      return days + 1 >= window_size;
    }

    public override void UpdateTimeline(IEnumerable<double> daily_availabilities, double last_day) {
      var availabilities = daily_availabilities.Take(window_size).ToArray();
      full_days_availability_ = availabilities.Sum() / availabilities.Count();
      full_days_accounted_ = availabilities.Count();
    }

    public override void UpdateCurrentDay(double day_fraction_available, double day_fraction_elapsed) {
      throw new NotImplementedException();
    }

    private double full_days_availability_;
    private int full_days_accounted_;
    // Includes the partial day.
    public int window_size { get; }
    public int window_filling => full_days_accounted_ + 1;
  }

  // Availability measured in calendar months, e.g., 98.7% in January.
  // Note that this requires RSS.
  public class MonthlyAvailability : AvailabilityMetric {
    public override bool ComputableFrom(int days) {
      // We need all days from the past month, plus all but the current day of
      // the current month.
      return days >= 61;
    }

    public override void UpdateTimeline(IEnumerable<double> daily_availabilities, double last_day) {
      throw new NotImplementedException();
    }

    public override void UpdateCurrentDay(double day_fraction_available, double day_fraction_elapsed) {
      throw new NotImplementedException();
    }

    // The first day of the currently-measured month.
    public DateTime current_month { get; private set; }
    public double current_month_availability { get; private set; }
    // The first day of the preceding month, for which measurement is complete.
    public DateTime last_month { get; private set; }
    public double last_month_availability { get; private set; }
  }

}
