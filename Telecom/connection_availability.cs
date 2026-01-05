using System;
using System.Collections.Generic;
using ContractConfigurator;
using Contracts;
using RealAntennas;

namespace σκοπός {
  public abstract class ConnectionAvailabilityFactory : ParameterFactory {
    public override bool Load(ConfigNode node) {
      var ok = base.Load(node);
      ok &= ConfigNodeUtil.ParseValue<string>(node, "connection", x => connection_ = x, this);
      ok &= ConfigNodeUtil.ParseValue<double>(node, "availability", x => availability_ = x, this);
      ok &= ConfigNodeUtil.ParseValue<double>(node, "latency", x => latency_ = x, this, defaultValue: double.PositiveInfinity);
      metric_ = ConfigNodeUtil.GetChildNode(node, "metric");
      monitoring_ = ConfigNodeUtil.GetChildNode(node, "monitoring");
      return ok;
    }

    public abstract ConnectionAvailability.Goal Goal();

    public override ContractParameter Generate(Contract contract) { 
      return new ConnectionAvailability(connection_,
                                        availability_,
                                        latency_,
                                        Goal(),
                                        metric_,
                                        monitoring_);
    }

    private string connection_;
    private double availability_;
    private double latency_;
    private ConfigNode metric_;
    private ConfigNode monitoring_;
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
                                   Monitor monitor,
                                   string rx_name,
                                   double availability,
                                   ConnectionAvailability.Goal goal) {
      title_tracker_ = new TitleTracker(this);
      service_ = service;
      metric_ = metric;
      monitor_ = monitor;
      rx_name_ = rx_name;
      availability_ = availability;
      goal_ = goal;
      disableOnStateChange = false;
    }

    protected override void OnUpdate() {
      base.OnUpdate();
      if (goal_ == ConnectionAvailability.Goal.MAINTAIN) {
        monitor_.AlertIfNeeded();
      }
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
      string title = $"{rx.displaynodeName}: {status}, {monitor_.description}.\n" +
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
    private Monitor monitor_;
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

    public ConnectionAvailability(string connection,
                                  double availability,
                                  double latency,
                                  Goal goal,
                                  ConfigNode metric_definition,
                                  ConfigNode monitoring_definition) {
      title_tracker_ = new TitleTracker(this);
      connection_ = connection;
      availability_ = availability;
      latency_ = latency == double.PositiveInfinity ? (double?)null : latency;
      goal_ = goal;
      disableOnStateChange = false;
      metric_definition_ = metric_definition;
      monitoring_definition_ = monitoring_definition;
    }

    protected override void OnUpdate() {
      base.OnUpdate();
      if (goal_ == Goal.MAINTAIN) {
        monitor?.AlertIfNeeded();
      }
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
      latency_ = node.HasValue("latency")
          ? double.Parse(node.GetValue("latency"))
          : (double?)null;
      Enum.TryParse(node.GetValue("goal"), out goal_);
      foreach (ConfigNode subparameter in node.GetNodes("PARAM")) {
        if (subparameter.GetValue("name") ==
            typeof(BroadcastRxAvailability).Name) {
          node.RemoveNode(subparameter);
        }
      }
      metric_definition_ = node.GetNode("metric");
      monitoring_definition_ = node.GetNode("monitoring");
    }

    protected override void OnSave(ConfigNode node) {
      node.AddValue("connection", connection_);
      node.AddValue("availability", availability_);
      if (latency_ != null) {
        node.AddValue("latency", latency_.Value);
      }
      node.AddValue("goal", goal_);
      node.AddNode("metric", metric_definition_);
      node.AddNode("monitoring", monitoring_definition_);
    }

    protected override string GetTitle() {
      var connection = Telecom.Instance.network.GetConnection(connection_);
      string data_rate = RATools.PrettyPrintDataRate(connection.data_rate);
      double latency = latency_ ?? connection.latency_limit;
      string pretty_latency = latency >= 1 ? $"{latency} s" : $"{latency * 1000} ms";

      string title;

      if (connection is PointToMultipointConnection point_to_multipoint) {
        var tx = Telecom.Instance.network.GetStation(point_to_multipoint.tx_name);
        if (subparameters != null) {
          title = $"Support broadcast from {tx.displaynodeName} to the " +
              $"following stations, with a data rate of {data_rate} and a " +
              $"latency of at most {pretty_latency}";
        } else {
          var rx = Telecom.Instance.network.GetStation(
            point_to_multipoint.rx_names[0]);
          var services = point_to_multipoint.channel_services[0];
          var service = latency_ == null
              ? services.basic
              : services.improved_by_latency[latency_.Value];
          string status = service.available
              ? "Currently connected"
              : "Currently disconnected";
          title = $"Support transmission from {tx.displaynodeName} to " +
              $"{rx.displaynodeName}, with a data rate of {data_rate} and a " +
              $"latency of at most {pretty_latency}.\n{status}, {monitor.description}.\n" +
              $"Availability: {metric.description}\nTarget: {availability_:P2}";
        }
      } else {
        var duplex = (DuplexConnection)connection;
        var trx0 = Telecom.Instance.network.GetStation(duplex.trx_names[0]);
        var trx1 = Telecom.Instance.network.GetStation(duplex.trx_names[1]);
        var service = latency_ == null
            ? duplex.basic_service
            : duplex.improved_service_by_latency[latency_.Value];
        string status = service.available
            ? "Currently connected"
            : "Currently disconnected";
        title = $"Support duplex communication between {trx0.displaynodeName} " +
            $"and {trx1.displaynodeName}, with a one-way data rate of " +
            $"{data_rate} and a round-trip latency of at most " +
            $"{pretty_latency}.\n{status}, {monitor.description}.\n" +
            $"Availability: {metric.description}\nTarget: {availability_:P2}";
        
      }
      title_tracker_.Add(title);
      if (last_title_ != title) {
        title_tracker_.UpdateContractWindow(title);
      }
      last_title_ = title;
      return title;
    }

    private AvailabilityMetric MakeMetric(ConfigNode definition) {
      string type = definition.GetValue("type");
      if (type == "monthly") {
        DateTime date =
            (Root.ContractState == Contract.State.Active ||
             Root.ContractState == Contract.State.Completed ||
             Root.ContractState == Contract.State.Failed)
                ? RSS.epoch.AddSeconds(Root.DateAccepted)
                : RSS.current_time;
        // A contract can pertain to the current month if it is accepted within
        // the first week, otherwise it starts on the next month.  This prevents
        // repeating monthly contracts, which finish early in the month after
        // the one to which they pertain, from skipping every other month.
        var effective_date = date.Day <= 7
            ? new DateTime(date.Year, date.Month, 1)
            : new DateTime(date.Year, date.Month, 1).AddMonths(1);
        int offset = int.Parse(definition.GetValue("month"));
        return new PeriodAvailability(
            (effective_date.AddMonths(offset) - RSS.epoch).Days,
            (effective_date.AddMonths(offset + 1).AddDays(-1) - RSS.epoch).Days);
      } else if (type == "fixed") {
        return new PeriodAvailability(
            (DateTime.Parse(definition.GetValue("first")) - RSS.epoch).Days,
            (DateTime.Parse(definition.GetValue("last")) - RSS.epoch).Days);
      } else if (type == "moving") {
        return new MovingWindowAvailability(
            int.Parse(definition.GetValue("window")));
      } else if (type == "partial_moving") {
        return new PartialMovingWindowAvailability(
            int.Parse(definition.GetValue("window")));
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
            metric_ = MakeMetric(metric_definition_);
            var services = point_to_multipoint.channel_services[0];
            var service = latency_ == null
                ? services.basic
                : services.improved_by_latency[latency_.Value];
            service.RegisterMetric(metric_);
          } else if (connection is DuplexConnection duplex) {
            metric_ = MakeMetric(metric_definition_);
            var service = latency_ == null
                ? duplex.basic_service
                : duplex.improved_service_by_latency[latency_.Value];
            service.RegisterMetric(metric_);
          }
        }
        return metric_;
      }
    }

    private Monitor monitor {
      get {
        if (monitor_ == null) {
          var connection = Telecom.Instance.network.GetConnection(connection_);
          string data_rate = RATools.PrettyPrintDataRate(connection.data_rate);
          double latency = latency_ ?? connection.latency_limit;
          string pretty_latency =
              latency >= 1 ? $"{latency} s" : $"{latency * 1000} ms";
          if (connection is PointToMultipointConnection point_to_multipoint &&
              point_to_multipoint.rx_names.Length == 1) {
            var monitored_metric = MakeMetric(monitoring_definition_);
            var services = point_to_multipoint.channel_services[0];
            var service = latency_ == null
                ? services.basic
                : services.improved_by_latency[latency_.Value];
            service.RegisterMetric(monitored_metric);
            var tx = Telecom.Instance.network.GetStation(point_to_multipoint.tx_name);
            var rx = Telecom.Instance.network.GetStation(
              point_to_multipoint.rx_names[0]);
            monitor_ = new Monitor(
                $@"{data_rate} {pretty_latency} connection from {
                    tx.displaynodeName} to {rx.displaynodeName}",
                monitored_metric,
                availability_);
          } else if (connection is DuplexConnection duplex) {
            var monitored_metric = MakeMetric(monitoring_definition_);
            var service = latency_ == null
                ? duplex.basic_service
                : duplex.improved_service_by_latency[latency_.Value];
            service.RegisterMetric(monitored_metric);
            var trx0 = Telecom.Instance.network.GetStation(duplex.trx_names[0]);
            var trx1 = Telecom.Instance.network.GetStation(duplex.trx_names[1]);
            monitor_ = new Monitor(
                $@"{data_rate} {pretty_latency} duplex connection between {
                    trx0.displaynodeName} and {trx1.displaynodeName}",
                monitored_metric,
                availability_);
          }
        }
        return monitor_;
      }
    }

    private List<BroadcastRxAvailability> subparameters {
      get {
        if (subparameters_ == null) {
          var connection = Telecom.Instance.network.GetConnection(connection_);
          string data_rate = RATools.PrettyPrintDataRate(connection.data_rate);
          double latency = latency_ ?? connection.latency_limit;
          string pretty_latency =
              latency >= 1 ? $"{latency} s" : $"{latency * 1000} ms";
          if (connection is PointToMultipointConnection point_to_multipoint &&
          point_to_multipoint.rx_names.Length > 1) {
            subparameters_ = new List<BroadcastRxAvailability>();
            var tx = Telecom.Instance.network.GetStation(
                point_to_multipoint.tx_name);
            for (int i = 0; i < point_to_multipoint.rx_names.Length; ++i) {
              var metric = MakeMetric(metric_definition_);
              var services = point_to_multipoint.channel_services[i];
              var service = latency_ == null
                  ? services.basic
                  : services.improved_by_latency[latency_.Value];
              service.RegisterMetric(metric);
              var monitored_metric = MakeMetric(monitoring_definition_);
              service.RegisterMetric(monitored_metric);
              var rx = Telecom.Instance.network.GetStation(
                point_to_multipoint.rx_names[i]);
              var monitor = new Monitor(
                $@"{data_rate} {pretty_latency} connection from {
                    tx.displaynodeName} to {rx.displaynodeName}",
                monitored_metric,
                availability_);
              var subparameter = new BroadcastRxAvailability(
                  service,
                  metric,
                  monitor,
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

    public string connection_name => connection_;

    private List<BroadcastRxAvailability> subparameters_;
    private ConfigNode metric_definition_;
    private ConfigNode monitoring_definition_;
    private AvailabilityMetric metric_;
    private Monitor monitor_;
    private string connection_;
    private double availability_;
    private double? latency_;
    private Goal goal_;
    private string last_title_;
    private TitleTracker title_tracker_;
  }
}
