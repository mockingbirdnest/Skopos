using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ContractConfigurator;
using Contracts;
using RealAntennas;

namespace σκοπός {
  public abstract class ConnectionAvailabilityFactory : ParameterFactory {
    public override bool Load(ConfigNode node) {
      var ok = base.Load(node);
      ok &= ConfigNodeUtil.ParseValue<string>(node, "connection", x => connection_ = x, this);
      ok &= ConfigNodeUtil.ParseValue<double>(node, "availability", x => availability_ = x, this);
      return ok;
    }

    public abstract ConnectionAvailability.Goal Goal();

    public override ContractParameter Generate(Contract contract) {
      return new ConnectionAvailability(connection_, availability_, Goal());
    }

    private string connection_;
    private double availability_;
  }

  public class AchieveConnectionAvailabilityFactory : ConnectionAvailabilityFactory {
    public override ConnectionAvailability.Goal Goal() {
      return ConnectionAvailability.Goal.ACHIEVE;
    }
  }

  public class MaintainConnectionAvailabilityFactory : ConnectionAvailabilityFactory {
    public override ConnectionAvailability.Goal Goal() {
      return ConnectionAvailability.Goal.MAINTAIN;
    }
  }

  public class ConnectionAvailability : ContractParameter {
    public enum Goal {
      ACHIEVE,
      MAINTAIN,
    }

    public ConnectionAvailability() {
      title_tracker_ = new TitleTracker(this);
    }

    public ConnectionAvailability(string connection, double availability, Goal goal) {
      title_tracker_ = new TitleTracker(this);
      connection_ = connection;
      availability_ = availability;
      goal_ = goal;
      disableOnStateChange = false;
    }

    protected override void OnUpdate() {
      base.OnUpdate();
      var connection = Telecom.Instance.network.GetConnection(connection_);
      if (state == ParameterState.Failed) {
        return;
      }
      if (goal_ == Goal.MAINTAIN && state == ParameterState.Complete) {
        connection.Monitor(availability_);
      }
      if (connection.days >= connection.window &&
          connection.availability >= availability_) {
        SetComplete();
      } else {
        switch (goal_) {
          case Goal.ACHIEVE:
            SetIncomplete();
            break;
          case Goal.MAINTAIN:
            // Be lenient for the first day.  This means that for an
            // auto-accepted maintenance contract, we penalize daily.
            if (Telecom.Instance.last_universal_time - Root.DateAccepted >
                KSPUtil.dateTimeFormatter.Day) {
              SetFailed();
            } else {
              SetIncomplete();
            }
            break;
        }
      }
      GetTitle();
    }

    protected override void OnLoad(ConfigNode node) {
      connection_ = node.GetValue("connection");
      availability_ = double.Parse(node.GetValue("availability"));
      Enum.TryParse(node.GetValue("goal"), out goal_);
    }

    protected override void OnSave(ConfigNode node) {
      node.AddValue("connection", connection_);
      node.AddValue("availability", availability_);
      node.AddValue("goal", goal_);
    }

    protected override string GetTitle() {
      var connection = Telecom.Instance.network.GetConnection(connection_);
      var tx = Telecom.Instance.network.GetStation(connection.tx_name);
      var rx = Telecom.Instance.network.GetStation(connection.rx_name);
      string data_rate = RATools.PrettyPrintDataRate(connection.rate_threshold);
      double latency = connection.latency_threshold;
      string pretty_latency = latency >= 1 ? $"{latency} s" : $"{latency * 1000} ms";
      string status = connection.within_sla ? "connected" : "disconnected";
      bool window_full = connection.days == connection.window;
      string window_text = window_full
          ? $"over the last {connection.window} days"
          : $"over {connection.days}/{connection.window} days";
      string title = $"Connect {tx.displaynodeName} to {rx.displaynodeName}:\n" +
             $"At least {data_rate}, with a latency of at most {pretty_latency} (currently {status})\n" +
             $"{connection.availability:P1} availability (target: {availability_:P1}) {window_text}";
      if (connection.days > connection.monitoring_window) {
        title += '\n';
        title += $@"(Availability over the last {
            connection.monitoring_window} days: {
            connection.monitored_availability:P1})";
      }
      title_tracker_.Add(title);
      if (last_title_ != title) {
        title_tracker_.UpdateContractWindow(title);
      }
      last_title_ = title;
      return title;
    }

    private string connection_;
    private double availability_;
    private Goal goal_;
    private string last_title_;
    private TitleTracker title_tracker_;
  }
}
