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

  public class BroadcastRxAvailability : ContractParameter {
    public BroadcastRxAvailability(AvailabilityMetric metric,
                                   string rx_name,
                                   double availability,
                                   ConnectionAvailability.Goal goal) {
      title_tracker_ = new TitleTracker(this);
      metric_ = metric;
      rx_name_ = rx_name;
      availability_ = availability;
      goal_ = goal;
      disableOnStateChange = false;
    }

    protected override void OnUpdate() {
      base.OnUpdate();
      if (state == ParameterState.Failed) {
        return;
      }
      if (metric_.partial) {
        SetIncomplete();
      } else if (metric_.availability < availability_) {
        if (goal_ == ConnectionAvailability.Goal.MAINTAIN) {
          SetFailed();
        } else {
          SetIncomplete();
        }
      } else {
        SetComplete();
      }
      GetTitle();
    }

    protected override string GetTitle() {
      var connection = Telecom.Instance.network.GetConnection(connection_);
      var tx = Telecom.Instance.network.GetStation(connection.tx_name);
      //var rx = Telecom.Instance.network.GetStation(connection.rx_names);
      string data_rate = RATools.PrettyPrintDataRate(connection.data_rate);
      double latency = connection.latency_limit;
      string pretty_latency = latency >= 1 ? $"{latency} s" : $"{latency * 1000} ms";

      return $"{rx_name_}: \nAvailability: {metric_.description} (target: {availability_:P2})";

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

    private AvailabilityMetric metric_;
    private string rx_name_;
    private double availability_;
    private ConnectionAvailability.Goal goal_;
    private string last_title_;
    private TitleTracker title_tracker_;
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
      if (connection is PointToMultipointConnection point_to_multipoint &&
          point_to_multipoint.rx_names.Length > 1 &&
          subparameters_ == null) {
        subparameters_ = new List<ConnectionAvailability>();
        // TODO(egg)
      }
      if (subparameters_ != null) {
        bool any_failed = false;
        bool any_incomplete = false;
        foreach (var subparameter in subparameters_) {
          any_failed |= subparameter.state == ParameterState.Failed;
          any_incomplete |= subparameter.state == ParameterState.Incomplete;
        }
        if (any_failed) {
          SetFailed();
        } else if (any_incomplete) {
          SetIncomplete();
        } else {
          SetComplete();
        }
      } else {
        AvailabilityMetric metric = metric;

        if (state == ParameterState.Failed) {
          return;
        }
        if (metric.partial) {
          SetIncomplete();
        } else if (metric.availability < availability_) {
          if (goal_ == Goal.MAINTAIN) {
            SetFailed();
          } else {
            SetIncomplete();
          }
        } else {
          SetComplete();
        }
      }
      GetTitle();
    }

    protected override void OnLoad(ConfigNode node) {
      connection_ = node.GetValue("connection");
      availability_ = double.Parse(node.GetValue("availability"));
      Enum.TryParse(node.GetValue("goal"), out goal_);
      foreach (ConfigNode subparameter in node.GetNodes("PARAM")) {
        if (subparameter.GetValue("name") ==
            typeof(BroadcastRxAvailability).Name) {
          node.RemoveNode(subparameter);
        }
      }
    }

    protected override void OnSave(ConfigNode node) {
      node.AddValue("connection", connection_);
      node.AddValue("availability", availability_);
      node.AddValue("goal", goal_);
    }

    protected override string GetNotes() {
      var connection = Telecom.Instance.network.GetConnection(connection_);
      if (connection.exclusive) {
        return "\nThis capability must be available simultaneously with " +
               "any others currently in operation; ensure your overall " +
               "network capacity is appropriately sized!";
      } else {
        return "\nThis transmission will be sent over any existing networks; ";
      }
    }

    protected override string GetTitle() {
      var connection = Telecom.Instance.network.GetConnection(connection_);
      var tx = Telecom.Instance.network.GetStation(connection.tx_name);
      //var rx = Telecom.Instance.network.GetStation(connection.rx_names);
      string data_rate = RATools.PrettyPrintDataRate(connection.data_rate);
      double latency = connection.latency_limit;
      string pretty_latency = latency >= 1 ? $"{latency} s" : $"{latency * 1000} ms";

      if (subparameters_ != null) {
        return $"Support broadcast from {tx.displaynodeName} to the " +
            $"following stations, with a data rate of {data_rate} and a " +
            $"latency of at most {pretty_latency}.";
      } else {
        return $"Support transmission from {tx.displaynodeName} to " +
            $"{rx.displaynodeName}, with a data rate of {data_rate} and a " +
            $"latency of at most {pretty_latency}.\nTODO availability";
      } else {
        return $"Support duplex communication between {tx.displaynodeName} " +
            $"and {rx.displaynodeName}, with a one-way data rate of " +
            $"{data_rate} and a round-trip latency of at most " +
            $"{pretty_latency}.\nTODO availability";
      }

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

    private List<BroadcastRxAvailability> subparameters_;
    private string connection_;
    private double availability_;
    private Goal goal_;
    private string last_title_;
    private TitleTracker title_tracker_;
  }
}
