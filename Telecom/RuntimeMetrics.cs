using System.Diagnostics;

namespace σκοπός {
  internal class RuntimeMetrics {
    public RuntimeMetrics() { }
    public int num_fixed_update_iterations_ = 0;
    public double fixed_update_runtime_ = 0;

    public double AverageFixedUpdateRuntime => fixed_update_runtime_ / num_fixed_update_iterations_;
  }

  internal class FixedUpdateMetric {
    public FixedUpdateMetric(string name) { 
      this.name = name;
      watch = new Stopwatch();  
    }

    public void StartFixedUpdate() {
      if (ticks_start_last_fixedupdate != watch.ElapsedTicks) fixedupdate_count++;
      ticks_start_last_fixedupdate = watch.ElapsedTicks;
    }

    public void Start() {
      total_calls++;
      calls_this_fixedupdate++;
      watch.Start();
    }

    public void Pause() {
      watch.Stop();
    }

    public void Resume() {
      watch.Start();
    }

    public void StopSuccess() {
      watch.Stop();
      successes++;
    }
    public void StopFailure() {
      watch.Stop();
      failures++;
    }

    public long total_calls = 0;
    public long calls_this_fixedupdate = 0;
    public long successes;
    public long failures;
    Stopwatch watch;
    string name;
    long ticks_start_last_fixedupdate = 0;
    long fixedupdate_count = 0;
    public double average_calls_per_fixedupdate => (double) total_calls / fixedupdate_count;
    public double average_runtime_per_fixedupdate => (double) watch.ElapsedTicks / Stopwatch.Frequency / fixedupdate_count;
    public double average_runtime_per_call => (double) watch.ElapsedTicks / Stopwatch.Frequency / total_calls;
    public double average_runtime_this_fixedupdate => (double) (watch.ElapsedTicks - ticks_start_last_fixedupdate) / Stopwatch.Frequency / calls_this_fixedupdate;
  }
}
