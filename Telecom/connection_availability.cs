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

    #if IT_COMPILES
    protected override void OnUpdate() {
      base.OnUpdate();
      var connection = Telecom.Instance.network.GetConnection(connection_);
      if (state == ParameterState.Failed) {
        return;
      }
      switch (goal_) {
        case Goal.ACHIEVE:
          if (connection.days >= connection.window_size &&
              connection.availability >= availability_) {
            SetComplete();
          } else {
            SetIncomplete();
          }
          break;
        case Goal.MAINTAIN:
          DateTime date_accepted = RSS.epoch.AddSeconds(Root.DateAccepted);
          var start_of_month = new DateTime(date_accepted.Year, date_accepted.Month, 1);
          var end_of_month = start_of_month.AddMonths(1);
          if (RSS.current_time < end_of_month) {
            SetIncomplete();
          } else {
            connection
          }
          break;
      }
      GetTitle();
    }
    #endif

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
      //var rx = Telecom.Instance.network.GetStation(connection.rx_names);
      string data_rate = RATools.PrettyPrintDataRate(connection.data_rate);
      double latency = connection.latency_limit;
      string pretty_latency = latency >= 1 ? $"{latency} s" : $"{latency * 1000} ms";

      #if IT_COMPILES
      string status = connection.within_sla ? "connected" : "disconnected";
      bool window_full = connection.days == connection.window_size;
      string window_text = window_full
          ? $"over the last {connection.window_size} days"
          : $"over {connection.days}/{connection.window_size} days";
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
      #else
      return null;
      #endif
    }

    private string connection_;
    private double availability_;
    private Goal goal_;
    private string last_title_;
    private TitleTracker title_tracker_;
  }
}
