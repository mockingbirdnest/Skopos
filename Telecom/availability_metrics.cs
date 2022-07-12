using System;
using System.Collections.Generic;
using System.Linq;

namespace σκοπός {

// A set of properties derived from a timeline of daily availabilities.
public interface AvailabilityMetric {
  // Whether the metric can be computed from a moving window of the given number of days,
  // in addition to availability in the current partial day.
  bool ComputableFrom(int days);
  // Update the timeline of full days, most recent first.
  void UpdateTimeline(IEnumerable<double> daily_availabilities, int last_day);
  // Update the current partial day.
  void UpdateCurrentDay(double day_fraction_available, double day_fraction_elapsed);

  string description { get; }

  double availability { get;}
  bool partial { get; }
}

// Availability measured over a moving window of full days, e.g., 98.7% over
// a period of 14 calendar days ending yesterday (included).
public class MovingWindowAvailability
    : AvailabilityMetric, IEquatable<MovingWindowAvailability> {
  public MovingWindowAvailability(int window_size) {
    this.window_size = window_size;
  }

  public override int GetHashCode() {
    return (GetType().Name, window_size).GetHashCode();
  }

  public override bool Equals(object other) {
    if (other is MovingWindowAvailability right) {
      return Equals(right);
    } else {
      return false;
    }
   }

  public bool Equals(MovingWindowAvailability other) {
    return window_size == other?.window_size;
  }

  public bool ComputableFrom(int days) {
    return days >= window_size;
  }

  public void UpdateTimeline(IEnumerable<double> daily_availabilities,
                             int last_day) {
    var availabilities = daily_availabilities.Take(window_size).ToArray();
    availability = availabilities.Sum() / window_size;
  }

  public void UpdateCurrentDay(double day_fraction_available,
                               double day_fraction_elapsed) {}

  public string description =>
      $"{availability:P2} over a {window_size}-day period ending yesterday";

  public double availability { get; private set; }
  public bool partial => false;

  public int window_size { get; }
}

// Availability measured over a moving window that includes the current day,
// e.g., 98.7% over the last 15 calendar days, including today.
public class PartialMovingWindowAvailability
    : AvailabilityMetric, IEquatable<PartialMovingWindowAvailability> {
  public PartialMovingWindowAvailability(int window_size) {
    this.window_size = window_size;
  }

  public override int GetHashCode() {
    return (GetType().Name, window_size).GetHashCode();
  }

  public override bool Equals(object other) {
    if (other is PartialMovingWindowAvailability right) {
      return Equals(right);
    } else {
      return false;
    }
   }

  public bool Equals(PartialMovingWindowAvailability other) {
    return window_size == other?.window_size;
  }

  public bool ComputableFrom(int days) {
    return days + 1 >= window_size;
  }

  public void UpdateTimeline(IEnumerable<double> daily_availabilities,
                             int last_day) {
    var availabilities = daily_availabilities.Take(window_size - 1).ToArray();
    full_days_total_availability_ = availabilities.Sum();
  }

  public void UpdateCurrentDay(double day_fraction_available,
                               double day_fraction_elapsed) {
    day_fraction_available_ = day_fraction_available;
    day_fraction_elapsed_ = day_fraction_elapsed;
  }

  public string description =>
    $"{availability:P2} over the last {window_size} days";

  public double availability =>
      (full_days_total_availability_ + day_fraction_available_) /
      (window_size + day_fraction_elapsed_);
  public bool partial => false;

  // Includes the partial day.
  public int window_size { get; }

  private double full_days_total_availability_;
  private double day_fraction_available_;
  private double day_fraction_elapsed_;
}

// Availability measured over a fixed period, e.g., 98.7% in January.
public class PeriodAvailability
    : AvailabilityMetric, IEquatable<PeriodAvailability> {
  public PeriodAvailability(int first_day, int last_day) {
    first_day_ = first_day;
    last_day_ = last_day;
  }

  public override int GetHashCode() {
    return (GetType().Name, first_day_, last_day_).GetHashCode();
  }

  public override bool Equals(object other) {
    if (other is PeriodAvailability right) {
      return Equals(right);
    } else {
      return false;
    }
   }

  public bool Equals(PeriodAvailability other) {
    return other != null &&
        first_day_ == other.first_day_ && last_day_ == other.last_day_;
  }

  public bool ComputableFrom(int days) {
    return days >= 31;
  }

  public void UpdateTimeline(IEnumerable<double> daily_availabilities,
                             int last_day) {
    partial = last_day < this.last_day_;
    availability = daily_availabilities
        .Skip(Math.Max(0, last_day_ - last_day))
        .Take(last_day_ - first_day_ + 1)
        .Average();
  }

  public void UpdateCurrentDay(double day_fraction_available,
                               double day_fraction_elapsed) {
    // TODO(egg): take the partial day into account?
  }

  public string description {
    get {
      DateTime start = RSS.epoch.AddDays(first_day_);
      DateTime end = RSS.epoch.AddDays(last_day_);
      if (start.Day == 1 && start.AddMonths(1).AddDays(-1) == end) {
        return $"{availability:P2} in {start:MMMM yyyy}";
      } else if (start == end) {
        return $"{availability:P2} on {start:yyyy-MM-dd}";
      } else {
        return $@"{availability:P2} between {start:yyyy-MM-dd} and {
            end:yyyy-MM-dd}";
      }
    }
  }

  public double availability { get; private set; }
  public bool partial { get; private set; }

  private int first_day_;
  private int last_day_;
}

}
