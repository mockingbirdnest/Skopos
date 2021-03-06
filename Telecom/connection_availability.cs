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
      var metric = ConfigNodeUtil.GetChildNode(node, "metric");
      metric_ = metric;
      return ok;
    }

    public abstract ConnectionAvailability.Goal Goal();

    public override ContractParameter Generate(Contract contract) { 
      return new ConnectionAvailability(connection_, availability_, Goal(), metric_);
    }

    private string connection_;
    private double availability_;
    private ConfigNode metric_;
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
    public BroadcastRxAvailability(Service service,
                                   AvailabilityMetric metric,
                                   string rx_name,
                                   double availability,
                                   ConnectionAvailability.Goal goal) {
      title_tracker_ = new TitleTracker(this);
      service_ = service;
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
      string status = service_.available
          ? "Currently connected"
          : "Currently disconnected";
      var rx = Telecom.Instance.network.GetStation(rx_name_);
      string title = $"{rx.displaynodeName}: {status}.\n" +
          $"Availability: {metric_.description}\nTarget: {availability_:P2}";
      title_tracker_.Add(title);
      if (last_title_ != title) {
        title_tracker_.UpdateContractWindow(title);
      }
      last_title_ = title;
      return title;
    }

    private Service service_;
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

    public ConnectionAvailability(string connection, double availability, Goal goal,
                                  ConfigNode metric_definition) {
      title_tracker_ = new TitleTracker(this);
      connection_ = connection;
      availability_ = availability;
      goal_ = goal;
      disableOnStateChange = false;
      metric_definition_ = metric_definition;
    }

    protected override void OnUpdate() {
      base.OnUpdate();
      if (subparameters != null) {
        bool any_failed = false;
        bool any_incomplete = false;
        foreach (var subparameter in subparameters) {
          any_failed |= subparameter.State == ParameterState.Failed;
          any_incomplete |= subparameter.State == ParameterState.Incomplete;
        }
        if (any_failed) {
          SetFailed();
        } else if (any_incomplete) {
          SetIncomplete();
        } else {
          SetComplete();
        }
      } else {
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
      metric_definition_ = node.GetNode("metric");
    }

    protected override void OnSave(ConfigNode node) {
      node.AddValue("connection", connection_);
      node.AddValue("availability", availability_);
      node.AddValue("goal", goal_);
      node.AddNode("metric", metric_definition_);
    }

    protected override string GetTitle() {
      Telecom.Log($"GetTitle for {connection_}");
      var connection = Telecom.Instance.network.GetConnection(connection_);
      string data_rate = RATools.PrettyPrintDataRate(connection.data_rate);
      double latency = connection.latency_limit;
      string pretty_latency = latency >= 1 ? $"{latency} s" : $"{latency * 1000} ms";
      Telecom.Log($"{data_rate}, at most {pretty_latency}");

      string title;

      if (connection is PointToMultipointConnection point_to_multipoint) {
        Telecom.Log($"From {point_to_multipoint.tx_name}");
        var tx = Telecom.Instance.network.GetStation(point_to_multipoint.tx_name);
        Telecom.Log($"aka {tx.displaynodeName}");
        if (subparameters != null) {
          title = $"Support broadcast from {tx.displaynodeName} to the " +
              $"following stations, with a data rate of {data_rate} and a " +
              $"latency of at most {pretty_latency}";
        } else {
          Telecom.Log($"To {point_to_multipoint.rx_names[0]}");
          var rx = Telecom.Instance.network.GetStation(
            point_to_multipoint.rx_names[0]);
          Telecom.Log($"aka {rx.displaynodeName}");
          string status = point_to_multipoint.channel_services[0].basic.available
              ? "Currently connected"
              : "Currently disconnected";
          title = $"Support transmission from {tx.displaynodeName} to " +
              $"{rx.displaynodeName}, with a data rate of {data_rate} and a " +
              $"latency of at most {pretty_latency}.\n{status}.\n" +
              $"Availability: {metric.description}\nTarget: {availability_:P2}";
        }
      } else {
        Telecom.Log("Duplex");
        var duplex = (DuplexConnection)connection;
        Telecom.Log($"between {duplex.trx_names[0]} and {duplex.trx_names[1]}");
        var trx0 = Telecom.Instance.network.GetStation(duplex.trx_names[0]);
        var trx1 = Telecom.Instance.network.GetStation(duplex.trx_names[1]);
        Telecom.Log($"aka {trx0.displaynodeName} and {trx1.displaynodeName}");
        string status = duplex.basic_service.available
            ? "Currently connected"
            : "Currently disconnected";
        title = $"Support duplex communication between {trx0.displaynodeName} " +
            $"and {trx1.displaynodeName}, with a one-way data rate of " +
            $"{data_rate} and a round-trip latency of at most " +
            $"{pretty_latency}.\n{status}.\n" +
            $"Availability: {metric.description}\nTarget: {availability_:P2}";
      }
      title_tracker_.Add(title);
      if (last_title_ != title) {
        title_tracker_.UpdateContractWindow(title);
      }
      last_title_ = title;
      return title;
    }

    private AvailabilityMetric MakeMetric() {
      string type = metric_definition_.GetValue("type");
      if (type == "monthly") {
        DateTime date = Root.ContractState == Contract.State.Active
            ? RSS.epoch.AddSeconds(Root.DateAccepted)
            : RSS.current_time;
        var effective_date =
            new DateTime(date.Year, date.Month, 1).AddMonths(1);
        int offset = int.Parse(metric_definition_.GetValue("month"));
        return new PeriodAvailability(
            (effective_date.AddMonths(offset) - RSS.epoch).Days,
            (effective_date.AddMonths(offset + 1).AddDays(-1) - RSS.epoch).Days);
      } else if (type == "fixed") {
        return new PeriodAvailability(
            (DateTime.Parse(metric_definition_.GetValue("first")) - RSS.epoch).Days,
            (DateTime.Parse(metric_definition_.GetValue("last")) - RSS.epoch).Days);
      } else if (type == "moving") {
        return new MovingWindowAvailability(
            int.Parse(metric_definition_.GetValue("window")));
      } else {
        throw new ArgumentException($"Unexpected metric type {type}");
      }
    }

    private AvailabilityMetric metric {
      get {
        if (metric_ == null) {
          var connection = Telecom.Instance.network.GetConnection(connection_);
          if (connection is PointToMultipointConnection point_to_multipoint &&
              point_to_multipoint.rx_names.Length == 1) {
            metric_ = MakeMetric();
            point_to_multipoint.channel_services[0].basic.RegisterMetric(metric_);
          } else if (connection is DuplexConnection duplex) {
            metric_ = MakeMetric();
            duplex.basic_service.RegisterMetric(metric_);
          }
        }
        return metric_;
      }
    }

    private List<BroadcastRxAvailability> subparameters {
      get {
        if (subparameters_ == null) {
          var connection = Telecom.Instance.network.GetConnection(connection_);
          if (connection is PointToMultipointConnection point_to_multipoint &&
          point_to_multipoint.rx_names.Length > 1) {
            subparameters_ = new List<BroadcastRxAvailability>();
            for (int i = 0; i < point_to_multipoint.rx_names.Length; ++i) {
              var metric = MakeMetric();
              point_to_multipoint.channel_services[i].basic.RegisterMetric(
                  metric);
              var subparameter = new BroadcastRxAvailability(
                  point_to_multipoint.channel_services[i].basic,
                  metric,
                  point_to_multipoint.rx_names[i],
                  availability_,
                  goal_);
              AddParameter(subparameter);
              subparameters_.Add(subparameter);
            }
          }
        }
        return subparameters_;
      }
    }

    private List<BroadcastRxAvailability> subparameters_;
    private ConfigNode metric_definition_;
    private AvailabilityMetric metric_;
    private string connection_;
    private double availability_;
    private Goal goal_;
    private string last_title_;
    private TitleTracker title_tracker_;
  }
}
